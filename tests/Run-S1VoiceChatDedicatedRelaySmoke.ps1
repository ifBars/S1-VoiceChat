#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs a two-client S1VoiceChat synthetic packet smoke test through DedicatedServerMod.

.DESCRIPTION
    Prepares clean isolated dedicated-server installs, starts a Schedule I
    dedicated server, auto-connects two clients through DedicatedServerMod, and
    requires the receiver to observe a SteamNetworkLib-delivered S1VoiceChat
    VoicePacket sent by the sender through the dedicated relay path.
#>

param(
    [ValidateSet("Mono", "Il2Cpp")]
    [string]$Runtime = "Mono",
    [string]$MonoClientPath = "D:\SteamLibrary\steamapps\common\Schedule I_alternate",
    [string]$MonoServerPath = "D:\SteamLibrary\steamapps\common\Schedule I_server",
    [string]$Il2CppClientPath = "D:\SteamLibrary\steamapps\common\Schedule I_public",
    [string]$Il2CppServerPath = "D:\SteamLibrary\steamapps\common\Schedule I_public_server",
    [string]$ServerIp = "127.0.0.1",
    [int]$Port = 38465,
    [string]$OutputRoot = "",
    [string]$SenderSteamId = "76561198000000009",
    [string]$ReceiverSteamId = "76561198000000019",
    [string]$SenderName = "S1VCDedicatedSender",
    [string]$ReceiverName = "S1VCDedicatedReceiver",
    [ValidateRange(0,120)]
    [int]$ClientStartDelaySeconds = 20,
    [int]$ServerReadyTimeoutSeconds = 180,
    [int]$TimeoutSeconds = 120,
    [switch]$KeepGameRunning,
    [switch]$KeepIsolatedInstalls
)

$ErrorActionPreference = "Stop"
$script:RunStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$script:RunStartedAt = Get-Date

function Write-Step([string]$Message) {
    Write-Host ("[{0:n1}s] {1}" -f $script:RunStopwatch.Elapsed.TotalSeconds, $Message) -ForegroundColor Yellow
}

function Write-Status([string]$Message) {
    Write-Host ("[{0:n1}s] {1}" -f $script:RunStopwatch.Elapsed.TotalSeconds, $Message) -ForegroundColor Gray
}

function Assert-Path([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Description not found: $Path"
    }
}

function Remove-TestRoot {
    param(
        [string]$RootPath,
        [string]$AllowedBasePath
    )

    $resolvedRoot = [System.IO.Path]::GetFullPath($RootPath)
    $resolvedBase = [System.IO.Path]::GetFullPath($AllowedBasePath)
    if (-not $resolvedRoot.StartsWith($resolvedBase, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean test directory outside expected base: $resolvedRoot"
    }

    Get-ChildItem -LiteralPath $resolvedRoot -Recurse -Force -Attributes ReparsePoint -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        ForEach-Object {
            try {
                [System.IO.Directory]::Delete($_.FullName, $false)
            }
            catch {
                Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue
            }
        }

    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force -ErrorAction SilentlyContinue
}

function Stop-ScheduleOneProcesses {
    param([string[]]$Roots)

    $fullRoots = $Roots | ForEach-Object { [System.IO.Path]::GetFullPath($_) }
    Get-Process -ErrorAction SilentlyContinue |
        Where-Object {
            if ([string]::IsNullOrWhiteSpace($_.Path)) {
                return $false
            }

            $path = [System.IO.Path]::GetFullPath($_.Path)
            foreach ($root in $fullRoots) {
                if ($path.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
                    return $true
                }
            }

            return $false
        } |
        ForEach-Object {
            Write-Status "Stopping existing process $($_.Id): $($_.Path)"
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        }
}

function Write-GoldbergConfig {
    param(
        [Parameter(Mandatory=$true)][string]$TargetGameDir,
        [Parameter(Mandatory=$true)][string]$AccountName,
        [Parameter(Mandatory=$true)][string]$AccountSteamId
    )

    $settingsDir = Join-Path $TargetGameDir "Schedule I_Data\Plugins\x86_64\steam_settings"
    $configFile = Join-Path $settingsDir "configs.user.ini"
    New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null

    $ini = @"
[user::general]
account_name=$AccountName
account_steamid=$AccountSteamId
language=english
"@
    Set-Content -Path $configFile -Value $ini -Encoding UTF8
    Write-Status "Goldberg config written for $AccountName ($AccountSteamId)"
}

function Wait-ForLogPattern {
    param(
        [string]$Path,
        [string]$Pattern,
        [int]$TimeoutSeconds,
        [string]$Description
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $nextStatus = Get-Date
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $Path) {
            $match = Select-String -LiteralPath $Path -Pattern $Pattern -SimpleMatch -Quiet
            if ($match) {
                return $true
            }
        }

        if ((Get-Date) -ge $nextStatus) {
            Write-Status "Waiting for $Description..."
            $nextStatus = (Get-Date).AddSeconds(5)
        }

        Start-Sleep -Milliseconds 500
    }

    $tail = if (Test-Path -LiteralPath $Path) {
        (Get-Content -LiteralPath $Path -Tail 80) -join [Environment]::NewLine
    }
    else {
        "<log file not found>"
    }

    throw "Timed out waiting for $Description in $Path. Tail:$([Environment]::NewLine)$tail"
}

function Copy-Logs {
    param(
        [string]$GamePath,
        [string]$DestinationPath,
        [string]$Prefix
    )

    New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null

    foreach ($candidate in @(
        (Join-Path $GamePath "MelonLoader\Latest.log"),
        (Join-Path $GamePath "UserData\MelonLoader\Latest.log")
    )) {
        if (Test-Path -LiteralPath $candidate) {
            $name = Split-Path -Leaf (Split-Path -Parent $candidate)
            Copy-Item -LiteralPath $candidate -Destination (Join-Path $DestinationPath "$Prefix-$name-Latest.log") -Force
        }
    }

    foreach ($logsRoot in @(
        (Join-Path $GamePath "MelonLoader\Logs"),
        (Join-Path $GamePath "UserData\MelonLoader\Logs")
    )) {
        if (-not (Test-Path -LiteralPath $logsRoot)) {
            continue
        }

        Get-ChildItem -LiteralPath $logsRoot -File -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTime -ge $script:RunStartedAt.AddSeconds(-5) } |
            Sort-Object LastWriteTime |
            ForEach-Object {
                $safeName = $_.Name -replace '[^\w\.\-]', '_'
                Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $DestinationPath "$Prefix-rotated-$safeName") -Force
            }
    }
}

function Get-RelevantTimeline {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return @()
    }

    $patterns = @(
        "DEDICATED SERVER READY",
        "Player joined:",
        "Snapshot updated",
        "Session mode updated",
        "DedicatedRelay",
        "snl_dedicated_p2p_send",
        "SnlDedicatedP2PMessage",
        "S1 VoiceChat smoke",
        "[S1VoiceChatSmoke]",
        "PacketId of 65535",
        "Connection will be kicked",
        "Dedicated server connection stopped unexpectedly",
        "TypeLoadException",
        "Exception",
        "NullReference"
    )

    Get-Content -LiteralPath $Path -ErrorAction SilentlyContinue |
        Where-Object {
            $line = $_
            foreach ($pattern in $patterns) {
                if ($line.IndexOf($pattern, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                    return $true
                }
            }

            return $false
        }
}

function Assert-DedicatedSmokeInstall {
    param(
        [Parameter(Mandatory=$true)][string]$GamePath,
        [Parameter(Mandatory=$true)][string]$RuntimeName,
        [Parameter(Mandatory=$true)][ValidateSet("Client", "Server")][string]$Side
    )

    $modsPath = Join-Path $GamePath "Mods"
    $userLibsPath = Join-Path $GamePath "UserLibs"
    $expectedVoiceChat = if ($RuntimeName -eq "Mono") { "S1VoiceChat.MonoMelon.dll" } else { "S1VoiceChat.Il2CppMelon.dll" }
    $expectedS1Api = if ($RuntimeName -eq "Mono") { "S1API.Mono.MelonLoader.dll" } else { "S1API.Il2Cpp.MelonLoader.dll" }
    if ($RuntimeName -eq "Mono") {
        $expectedFramework = if ($Side -eq "Client") { "DedicatedServerMod_Mono_Client.dll" } else { "DedicatedServerMod_Mono_Server.dll" }
    }
    else {
        $expectedFramework = if ($Side -eq "Client") { "DedicatedServerMod_Il2cpp_Client.dll" } else { "DedicatedServerMod_Il2cpp_Server.dll" }
    }

    $expectedMods = @($expectedFramework, $expectedS1Api, $expectedVoiceChat) | Sort-Object
    $mods = @(Get-ChildItem -LiteralPath $modsPath -File | Select-Object -ExpandProperty Name | Sort-Object)
    $unexpectedMods = @($mods | Where-Object { $_ -notin $expectedMods })
    $missingMods = @($expectedMods | Where-Object { $_ -notin $mods })
    if ($unexpectedMods.Count -gt 0 -or $missingMods.Count -gt 0) {
        throw "$Side dedicated smoke Mods mismatch. Expected: $($expectedMods -join ', '). Actual: $($mods -join ', ')."
    }

    $userLibs = @(Get-ChildItem -LiteralPath $userLibsPath -File | Select-Object -ExpandProperty Name | Sort-Object)
    $expectedUserLibs = @("NAudio.Core.dll", "NAudio.Wasapi.dll", "opus.dll", "SteamNetworkLib.dll") | Sort-Object
    $unexpectedUserLibs = @($userLibs | Where-Object { $_ -notin $expectedUserLibs })
    $missingUserLibs = @($expectedUserLibs | Where-Object { $_ -notin $userLibs })
    if ($unexpectedUserLibs.Count -gt 0 -or $missingUserLibs.Count -gt 0) {
        throw "$Side dedicated smoke UserLibs mismatch. Expected: $($expectedUserLibs -join ', '). Actual: $($userLibs -join ', ')."
    }

    Write-Host "$Side dedicated smoke S1VoiceChat HUD assets are embedded in the mod assembly." -ForegroundColor Gray
}

function New-IsolatedDedicatedInstalls {
    param([Parameter(Mandatory=$true)][string]$InstanceRoot)

    & (Join-Path $PSScriptRoot "Run-S1VoiceChatValidation.ps1") `
        -Scenario Dedicated `
        -MonoClientPath $MonoClientPath `
        -MonoServerPath $MonoServerPath `
        -Il2CppClientPath $Il2CppClientPath `
        -Il2CppServerPath $Il2CppServerPath `
        -InstanceRoot $InstanceRoot `
        -UseIsolatedInstalls `
        -KeepIsolatedInstalls | Out-Host

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to prepare dedicated isolated installs."
    }

    $roots = @(Get-ChildItem -LiteralPath $InstanceRoot -Directory)
    if ($roots.Count -ne 1) {
        throw "Expected exactly one isolated root under $InstanceRoot, found $($roots.Count)."
    }

    $root = $roots[0].FullName
    if ($Runtime -eq "Mono") {
        return @{
            Server = Join-Path $root "dedicated-mono-server"
            Client = Join-Path $root "dedicated-mono-client"
        }
    }

    return @{
        Server = Join-Path $root "dedicated-il2cpp-server"
        Client = Join-Path $root "dedicated-il2cpp-client"
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\dedicated-relay-smoke"
}

$runId = Get-Date -Format "yyyyMMdd-HHmmss"
$outputDir = Join-Path $OutputRoot "$runId-$($Runtime.ToLowerInvariant())"
$isolatedBase = Join-Path $outputDir "isolated"
$sharedRoot = Join-Path $isolatedBase "shared"
$logsDir = Join-Path $outputDir "logs"
$token = [Guid]::NewGuid().ToString("N")
$senderResultPath = Join-Path $outputDir "result-sender.txt"
$receiverResultPath = Join-Path $outputDir "result-receiver.txt"
$serverProcess = $null
$receiverProcess = $null
$senderProcess = $null
$serverGamePath = $null
$clientGamePath = $null

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
New-Item -ItemType Directory -Path $sharedRoot -Force | Out-Null

Write-Host "S1VoiceChat dedicated relay smoke test" -ForegroundColor Cyan
Write-Status "Runtime: $Runtime"
Write-Status "Output: $outputDir"
Write-Status "Token: $token"
Write-Status "Server: ${ServerIp}:$Port"
Write-Status "Smoke timeout: $TimeoutSeconds seconds"

try {
    Write-Step "Step 1: Prepare isolated dedicated installs"
    $paths = New-IsolatedDedicatedInstalls -InstanceRoot $sharedRoot
    $serverGamePath = $paths.Server
    $clientGamePath = $paths.Client

    Assert-Path (Join-Path $serverGamePath "Schedule I.exe") "Server executable"
    Assert-Path (Join-Path $clientGamePath "Schedule I.exe") "Client executable"
    Assert-DedicatedSmokeInstall -GamePath $serverGamePath -RuntimeName $Runtime -Side Server
    Assert-DedicatedSmokeInstall -GamePath $clientGamePath -RuntimeName $Runtime -Side Client

    Stop-ScheduleOneProcesses -Roots @($serverGamePath, $clientGamePath)

    Write-Step "Step 2: Start dedicated server"
    $serverExe = Join-Path $serverGamePath "Schedule I.exe"
    $serverArgs = @(
        "--batchmode",
        "--nographics",
        "--dedicated-server",
        "--stdio-console",
        "--server-port",
        $Port.ToString()
    )
    $serverProcess = Start-Process -FilePath $serverExe -ArgumentList $serverArgs -WorkingDirectory $serverGamePath -PassThru -WindowStyle Hidden
    Write-Host "Server PID: $($serverProcess.Id)" -ForegroundColor Green

    $serverLog = Join-Path $serverGamePath "MelonLoader\Latest.log"
    $null = Wait-ForLogPattern -Path $serverLog -Pattern "DEDICATED SERVER READY" -TimeoutSeconds $ServerReadyTimeoutSeconds -Description "dedicated server ready"

    Write-Step "Step 3: Start receiver client"
    Write-GoldbergConfig -TargetGameDir $clientGamePath -AccountName $ReceiverName -AccountSteamId $ReceiverSteamId
    $clientExe = Join-Path $clientGamePath "Schedule I.exe"
    $receiverArgs = @(
        "--server-ip",
        $ServerIp,
        "--server-port",
        $Port.ToString(),
        "--disable-friends-check",
        "--s1vc-smoke",
        "--s1vc-smoke-require-transport",
        "--s1vc-smoke-no-load",
        "--s1vc-smoke-role",
        "receiver",
        "--s1vc-smoke-token",
        $token,
        "--s1vc-smoke-peer-id",
        $SenderSteamId,
        "--s1vc-smoke-dir",
        $outputDir,
        "--s1vc-smoke-timeout",
        $TimeoutSeconds.ToString()
    )
    $receiverProcess = Start-Process -FilePath $clientExe -ArgumentList $receiverArgs -WorkingDirectory $clientGamePath -PassThru -WindowStyle Hidden
    Write-Host "Receiver PID: $($receiverProcess.Id)" -ForegroundColor Green

    if ($ClientStartDelaySeconds -gt 0) {
        Write-Status "Waiting $ClientStartDelaySeconds seconds before starting sender client"
        Start-Sleep -Seconds $ClientStartDelaySeconds
    }

    Write-Step "Step 4: Start sender client"
    Write-GoldbergConfig -TargetGameDir $clientGamePath -AccountName $SenderName -AccountSteamId $SenderSteamId
    $senderArgs = @(
        "--server-ip",
        $ServerIp,
        "--server-port",
        $Port.ToString(),
        "--disable-friends-check",
        "--s1vc-smoke",
        "--s1vc-smoke-require-transport",
        "--s1vc-smoke-no-load",
        "--s1vc-smoke-role",
        "sender",
        "--s1vc-smoke-token",
        $token,
        "--s1vc-smoke-peer-id",
        $ReceiverSteamId,
        "--s1vc-smoke-dir",
        $outputDir,
        "--s1vc-smoke-timeout",
        $TimeoutSeconds.ToString()
    )
    $senderProcess = Start-Process -FilePath $clientExe -ArgumentList $senderArgs -WorkingDirectory $clientGamePath -PassThru -WindowStyle Hidden
    Write-Host "Sender PID: $($senderProcess.Id)" -ForegroundColor Green

    Write-Step "Step 5: Wait for sender and receiver results"
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds + $ClientStartDelaySeconds + 30)
    $nextStatus = Get-Date
    while ((Get-Date) -lt $deadline) {
        if ((Test-Path -LiteralPath $senderResultPath) -and (Test-Path -LiteralPath $receiverResultPath)) {
            break
        }

        if ($serverProcess.HasExited) {
            throw "Dedicated server exited during relay smoke test. Exit code: $($serverProcess.ExitCode)"
        }

        if ($senderProcess.HasExited -and $receiverProcess.HasExited) {
            break
        }

        if ((Get-Date) -ge $nextStatus) {
            $senderState = if ($senderProcess.HasExited) { "exited:$($senderProcess.ExitCode)" } else { "running" }
            $receiverState = if ($receiverProcess.HasExited) { "exited:$($receiverProcess.ExitCode)" } else { "running" }
            $senderResult = Test-Path -LiteralPath $senderResultPath
            $receiverResult = Test-Path -LiteralPath $receiverResultPath
            Write-Status "Waiting for smoke results. Sender=$senderState result=$senderResult; Receiver=$receiverState result=$receiverResult"
            $nextStatus = (Get-Date).AddSeconds(5)
        }

        Start-Sleep -Seconds 1
    }
}
finally {
    Write-Step "Step 6: Collect evidence"
    if ($serverGamePath) {
        Copy-Logs -GamePath $serverGamePath -DestinationPath $logsDir -Prefix "server"
    }

    if ($clientGamePath) {
        Copy-Logs -GamePath $clientGamePath -DestinationPath $logsDir -Prefix "client"
    }

    $timeline = @()
    if ($serverGamePath) {
        $timeline += "=== SERVER TIMELINE ==="
        $timeline += Get-RelevantTimeline -Path (Join-Path $serverGamePath "MelonLoader\Latest.log")
        $timeline += ""
    }

    if ($clientGamePath) {
        foreach ($log in @(Get-ChildItem -LiteralPath $logsDir -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime)) {
            $timeline += "=== $($log.Name) ==="
            $timeline += Get-RelevantTimeline -Path $log.FullName
            $timeline += ""
        }
    }

    $timeline | Set-Content -LiteralPath (Join-Path $outputDir "timeline.txt") -Encoding UTF8

    if (-not $KeepGameRunning) {
        foreach ($process in @($senderProcess, $receiverProcess, $serverProcess)) {
            if ($process -ne $null -and -not $process.HasExited) {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            }
        }
    }

    if (-not $KeepIsolatedInstalls -and (Test-Path -LiteralPath $isolatedBase)) {
        Remove-TestRoot -RootPath $isolatedBase -AllowedBasePath $outputDir
    }
}

$senderText = if (Test-Path -LiteralPath $senderResultPath) {
    Get-Content -LiteralPath $senderResultPath -Raw
} else {
    "FAIL|No sender result file was written"
}

$receiverText = if (Test-Path -LiteralPath $receiverResultPath) {
    Get-Content -LiteralPath $receiverResultPath -Raw
} else {
    "FAIL|No receiver result file was written"
}

Write-Status "Sender: $senderText"
Write-Status "Receiver: $receiverText"
Write-Status ("Total duration: {0:n1}s" -f $script:RunStopwatch.Elapsed.TotalSeconds)

if ($senderText -notmatch "^PASS\|" -or $receiverText -notmatch "^PASS\|") {
    Write-Host "Logs: $logsDir" -ForegroundColor Yellow
    Write-Host "Output: $outputDir" -ForegroundColor Yellow
    exit 1
}

Write-Host "S1VoiceChat dedicated relay smoke test passed." -ForegroundColor Green
Write-Host "Output: $outputDir" -ForegroundColor Green
exit 0
