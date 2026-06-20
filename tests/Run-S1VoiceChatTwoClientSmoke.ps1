#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs a two-client S1VoiceChat synthetic packet smoke test from clean isolated P2P installs.

.DESCRIPTION
    Prepares a clean isolated P2P install, installs LocalLobby as the lobby driver,
    assigns each process a Goldberg Steam ID, launches the receiver as LocalLobby
    host and the sender as LocalLobby client, and requires the receiver to observe
    a real SteamNetworkLib-delivered S1VoiceChat VoicePacket containing the run token.
#>

param(
    [ValidateSet("Mono", "Il2Cpp")]
    [string]$Runtime = "Mono",
    [string]$MonoClientPath = "D:\SteamLibrary\steamapps\common\Schedule I_alternate",
    [string]$Il2CppClientPath = "D:\SteamLibrary\steamapps\common\Schedule I_public",
    [string]$OutputRoot = "",
    [string]$SenderSteamId = "76561198000000009",
    [string]$ReceiverSteamId = "76561198000000019",
    [string]$SenderName = "S1VCSender",
    [string]$ReceiverName = "S1VCReceiver",
    [ValidateRange(0,4)]
    [int]$SaveSlot = 0,
    [ValidateRange(0,120)]
    [int]$ClientStartDelaySeconds = 20,
    [int]$TimeoutSeconds = 90,
    [string]$LocalLobbyVersion = "1.0.0",
    [string]$LocalLobbyAssetRoot = "",
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
    Write-Host "Goldberg config written for $AccountName ($AccountSteamId)" -ForegroundColor Gray
}

function Get-LocalLobbyDll {
    param(
        [Parameter(Mandatory=$true)][string]$RuntimeName,
        [Parameter(Mandatory=$true)][string]$CacheRoot
    )

    $assetName = if ($RuntimeName -eq "Mono") { "LocalLobby-Mono.dll" } else { "LocalLobby-IL2CPP.dll" }

    if (-not [string]::IsNullOrWhiteSpace($LocalLobbyAssetRoot)) {
        $localAsset = Join-Path $LocalLobbyAssetRoot $assetName
        Assert-Path $localAsset "LocalLobby asset"
        return $localAsset
    }

    $versionRoot = Join-Path $CacheRoot $LocalLobbyVersion
    $assetPath = Join-Path $versionRoot $assetName
    if (Test-Path -LiteralPath $assetPath) {
        return $assetPath
    }

    New-Item -ItemType Directory -Path $versionRoot -Force | Out-Null
    $downloadUrl = "https://github.com/k073l/LocalLobby/releases/download/$LocalLobbyVersion/$assetName"
    Write-Status "Downloading $assetName from LocalLobby $LocalLobbyVersion"
    Invoke-WebRequest -Uri $downloadUrl -OutFile $assetPath -UseBasicParsing
    return $assetPath
}

function Install-LocalLobby {
    param(
        [Parameter(Mandatory=$true)][string]$GamePath,
        [Parameter(Mandatory=$true)][string]$SourceDll
    )

    $modsPath = Join-Path $GamePath "Mods"
    New-Item -ItemType Directory -Path $modsPath -Force | Out-Null
    Copy-Item -LiteralPath $SourceDll -Destination (Join-Path $modsPath (Split-Path -Leaf $SourceDll)) -Force
}

function Clear-LocalLobbyState {
    param(
        [Parameter(Mandatory=$true)][string]$GamePath
    )

    $lobbyFile = Join-Path $GamePath "UserData\LocalLobby\lobby.txt"
    if (Test-Path -LiteralPath $lobbyFile) {
        Remove-Item -LiteralPath $lobbyFile -Force
    }
}

function Wait-LocalLobbyFile {
    param(
        [Parameter(Mandatory=$true)][string]$GamePath,
        [Parameter(Mandatory=$true)][int]$TimeoutSeconds
    )

    $lobbyFile = Join-Path $GamePath "UserData\LocalLobby\lobby.txt"
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $nextStatus = Get-Date
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $lobbyFile) {
            $raw = (Get-Content -LiteralPath $lobbyFile -Raw).Trim()
            if ($raw -match '^\d+$') {
                Write-Status "LocalLobby host file ready: $raw"
                return $lobbyFile
            }
        }

        if ((Get-Date) -ge $nextStatus) {
            Write-Status "Waiting for LocalLobby host file..."
            $nextStatus = (Get-Date).AddSeconds(5)
        }

        Start-Sleep -Milliseconds 500
    }

    throw "Timed out waiting for LocalLobby host lobby file: $lobbyFile"
}

function Assert-P2PSmokeInstall {
    param(
        [Parameter(Mandatory=$true)][string]$GamePath,
        [Parameter(Mandatory=$true)][string]$RuntimeName
    )

    $modsPath = Join-Path $GamePath "Mods"
    $userLibsPath = Join-Path $GamePath "UserLibs"
    $expectedVoiceChat = if ($RuntimeName -eq "Mono") { "S1VoiceChat.MonoMelon.dll" } else { "S1VoiceChat.Il2CppMelon.dll" }
    $expectedLocalLobby = if ($RuntimeName -eq "Mono") { "LocalLobby-Mono.dll" } else { "LocalLobby-IL2CPP.dll" }

    $mods = @(Get-ChildItem -LiteralPath $modsPath -File | Select-Object -ExpandProperty Name | Sort-Object)
    $expectedMods = @($expectedVoiceChat, $expectedLocalLobby) | Sort-Object
    $unexpectedMods = @($mods | Where-Object { $_ -notin $expectedMods })
    $missingMods = @($expectedMods | Where-Object { $_ -notin $mods })
    if ($unexpectedMods.Count -gt 0 -or $missingMods.Count -gt 0) {
        throw "P2P smoke Mods mismatch. Expected: $($expectedMods -join ', '). Actual: $($mods -join ', ')."
    }

    $userLibs = @(Get-ChildItem -LiteralPath $userLibsPath -File | Select-Object -ExpandProperty Name | Sort-Object)
    $expectedUserLibs = @("NAudio.Core.dll", "NAudio.Wasapi.dll", "opus.dll", "SteamNetworkLib.dll") | Sort-Object
    $unexpectedUserLibs = @($userLibs | Where-Object { $_ -notin $expectedUserLibs })
    $missingUserLibs = @($expectedUserLibs | Where-Object { $_ -notin $userLibs })
    if ($unexpectedUserLibs.Count -gt 0 -or $missingUserLibs.Count -gt 0) {
        throw "P2P smoke UserLibs mismatch. Expected: $($expectedUserLibs -join ', '). Actual: $($userLibs -join ', ')."
    }

    $assetsPath = Join-Path $modsPath "S1VoiceChat\assets"
    $assets = @(Get-ChildItem -LiteralPath $assetsPath -File -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name | Sort-Object)
    $expectedAssets = @("microphone.png", "mute.png") | Sort-Object
    $missingAssets = @($expectedAssets | Where-Object { $_ -notin $assets })
    if ($missingAssets.Count -gt 0) {
        throw "P2P smoke S1VoiceChat assets mismatch. Missing: $($missingAssets -join ', '). Actual: $($assets -join ', ')."
    }
}

function New-IsolatedP2PInstall {
    param(
        [Parameter(Mandatory=$true)][string]$Label,
        [Parameter(Mandatory=$true)][string]$InstanceRoot
    )

    & (Join-Path $PSScriptRoot "Run-S1VoiceChatValidation.ps1") `
        -Scenario P2P `
        -MonoClientPath $MonoClientPath `
        -Il2CppClientPath $Il2CppClientPath `
        -InstanceRoot $InstanceRoot `
        -UseIsolatedInstalls `
        -KeepIsolatedInstalls | Out-Host

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to prepare $Label isolated install."
    }

    $roots = @(Get-ChildItem -LiteralPath $InstanceRoot -Directory)
    if ($roots.Count -ne 1) {
        throw "Expected exactly one isolated root for $Label under $InstanceRoot, found $($roots.Count)."
    }

    if ($Runtime -eq "Mono") {
        Write-Output (Join-Path $roots[0].FullName "p2p-mono-client")
        return
    }

    Write-Output (Join-Path $roots[0].FullName "p2p-il2cpp-client")
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
            Where-Object { $_.LastWriteTime -ge $script:RunStartedAt } |
            Sort-Object LastWriteTime |
            ForEach-Object {
                $safeName = $_.Name -replace '[^\w\.\-]', '_'
                Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $DestinationPath "$Prefix-rotated-$safeName") -Force
            }
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\two-client-smoke"
}

$runId = Get-Date -Format "yyyyMMdd-HHmmss"
$outputDir = Join-Path $OutputRoot "$runId-$($Runtime.ToLowerInvariant())"
$isolatedBase = Join-Path $outputDir "isolated"
$sharedRoot = Join-Path $isolatedBase "shared"
$logsDir = Join-Path $outputDir "logs"
$dependencyCacheRoot = Join-Path $OutputRoot "_dependencies\LocalLobby"
$token = [Guid]::NewGuid().ToString("N")
$senderResultPath = Join-Path $outputDir "result-sender.txt"
$receiverResultPath = Join-Path $outputDir "result-receiver.txt"
$receiverGamePath = $null
$senderGamePath = $null

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
New-Item -ItemType Directory -Path $sharedRoot -Force | Out-Null

Write-Host "S1VoiceChat two-client smoke test" -ForegroundColor Cyan
Write-Status "Runtime: $Runtime"
Write-Status "Output: $outputDir"
Write-Status "Token: $token"
Write-Status "LocalLobby: $LocalLobbyVersion"
Write-Status "Smoke timeout: $TimeoutSeconds seconds"

$startedProcesses = New-Object System.Collections.Generic.List[System.Diagnostics.Process]

try {
    Write-Step "Step 1: Prepare shared isolated install"
    $receiverGamePath = New-IsolatedP2PInstall -Label "shared" -InstanceRoot $sharedRoot
    $senderGamePath = $receiverGamePath

    Assert-Path (Join-Path $receiverGamePath "Schedule I.exe") "Receiver executable"
    Assert-Path (Join-Path $senderGamePath "Schedule I.exe") "Sender executable"

    $localLobbyDll = Get-LocalLobbyDll -RuntimeName $Runtime -CacheRoot $dependencyCacheRoot
    Install-LocalLobby -GamePath $receiverGamePath -SourceDll $localLobbyDll
    Clear-LocalLobbyState -GamePath $receiverGamePath
    Assert-P2PSmokeInstall -GamePath $receiverGamePath -RuntimeName $Runtime

    Write-GoldbergConfig -TargetGameDir $receiverGamePath -AccountName $ReceiverName -AccountSteamId $ReceiverSteamId

    Write-Step "Step 2: Start receiver as LocalLobby host"
    $receiverExe = Join-Path $receiverGamePath "Schedule I.exe"
    $s1SaveSlot = $SaveSlot + 1
    $receiverArgs = "--host --s1vc-smoke --s1vc-smoke-require-transport --s1vc-smoke-role receiver --s1vc-smoke-token $token --s1vc-smoke-peer-id $SenderSteamId --s1vc-smoke-dir `"$outputDir`" --s1vc-smoke-save-slot $s1SaveSlot --s1vc-smoke-timeout $TimeoutSeconds"
    $receiverProcess = Start-Process -FilePath $receiverExe -ArgumentList $receiverArgs -WorkingDirectory $receiverGamePath -PassThru -WindowStyle Hidden
    $startedProcesses.Add($receiverProcess)
    Write-Host "Receiver PID: $($receiverProcess.Id)" -ForegroundColor Green

    $hostLobbyFile = Wait-LocalLobbyFile -GamePath $receiverGamePath -TimeoutSeconds $TimeoutSeconds
    Write-Status "LocalLobby host file: $hostLobbyFile"
    Copy-Logs -GamePath $receiverGamePath -DestinationPath $logsDir -Prefix "receiver-before-client"

    if ($ClientStartDelaySeconds -gt 0) {
        Write-Status "Waiting $ClientStartDelaySeconds seconds before starting LocalLobby client"
        Start-Sleep -Seconds $ClientStartDelaySeconds
    }

    Write-GoldbergConfig -TargetGameDir $senderGamePath -AccountName $SenderName -AccountSteamId $SenderSteamId

    Write-Step "Step 3: Start sender as LocalLobby client"
    $senderExe = Join-Path $senderGamePath "Schedule I.exe"
    $senderArgs = "--join --s1vc-smoke --s1vc-smoke-require-transport --s1vc-smoke-no-load --s1vc-smoke-role sender --s1vc-smoke-token $token --s1vc-smoke-peer-id $ReceiverSteamId --s1vc-smoke-dir `"$outputDir`" --s1vc-smoke-timeout $TimeoutSeconds"
    $senderProcess = Start-Process -FilePath $senderExe -ArgumentList $senderArgs -WorkingDirectory $senderGamePath -PassThru -WindowStyle Hidden
    $startedProcesses.Add($senderProcess)
    Write-Host "Sender PID: $($senderProcess.Id)" -ForegroundColor Green

    Write-Step "Step 4: Wait for sender and receiver results"
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds + 20)
    $nextStatus = Get-Date
    while ((Get-Date) -lt $deadline) {
        if ((Test-Path -LiteralPath $senderResultPath) -and (Test-Path -LiteralPath $receiverResultPath)) {
            break
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
    Write-Step "Step 5: Collect evidence"
    if ($receiverGamePath) {
        Copy-Logs -GamePath $receiverGamePath -DestinationPath $logsDir -Prefix "receiver"
    }

    if ($senderGamePath) {
        Copy-Logs -GamePath $senderGamePath -DestinationPath $logsDir -Prefix "sender"
    }

    if (-not $KeepGameRunning) {
        foreach ($process in $startedProcesses) {
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

Write-Host "S1VoiceChat two-client smoke test passed." -ForegroundColor Green
Write-Host "Output: $outputDir" -ForegroundColor Green
exit 0
