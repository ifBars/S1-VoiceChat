#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs an in-game S1VoiceChat smoke test from a clean isolated install.

.DESCRIPTION
    Uses Run-S1VoiceChatValidation.ps1 to build and prepare isolated installs,
    launches one selected install with S1VoiceChat smoke flags, waits for the
    mod's result file, captures MelonLoader logs, then removes the isolated game
    copy unless -KeepIsolatedInstalls is set.
#>

param(
    [ValidateSet("P2P", "Dedicated")]
    [string]$Scenario = "P2P",
    [ValidateSet("Mono", "Il2Cpp")]
    [string]$Runtime = "Mono",
    [ValidateSet("Client", "Server")]
    [string]$Side = "Client",
    [string]$MonoClientPath = "D:\SteamLibrary\steamapps\common\Schedule I_alternate",
    [string]$MonoServerPath = "D:\SteamLibrary\steamapps\common\Schedule I_server",
    [string]$Il2CppClientPath = "D:\SteamLibrary\steamapps\common\Schedule I_public",
    [string]$Il2CppServerPath = "D:\SteamLibrary\steamapps\common\Schedule I_public_server",
    [string]$OutputRoot = "",
    [int]$TimeoutSeconds = 90,
    [switch]$RequireTransport,
    [switch]$KeepGameRunning,
    [switch]$KeepIsolatedInstalls
)

$ErrorActionPreference = "Stop"

function Write-Step([string]$Message) {
    Write-Host $Message -ForegroundColor Yellow
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

function Get-ProfilePath {
    param(
        [string]$RootPath,
        [string]$SelectedScenario,
        [string]$SelectedRuntime,
        [string]$SelectedSide
    )

    if ($SelectedScenario -eq "P2P") {
        if ($SelectedSide -ne "Client") {
            throw "P2P smoke tests only support -Side Client."
        }

        if ($SelectedRuntime -eq "Mono") {
            return Join-Path $RootPath "p2p-mono-client"
        }

        return Join-Path $RootPath "p2p-il2cpp-client"
    }

    if ($SelectedRuntime -eq "Mono") {
        if ($SelectedSide -eq "Client") {
            return Join-Path $RootPath "dedicated-mono-client"
        }

        return Join-Path $RootPath "dedicated-mono-server"
    }

    if ($SelectedSide -eq "Client") {
        return Join-Path $RootPath "dedicated-il2cpp-client"
    }

    return Join-Path $RootPath "dedicated-il2cpp-server"
}

function Copy-Logs {
    param(
        [string]$GamePath,
        [string]$DestinationPath
    )

    New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null

    foreach ($candidate in @(
        (Join-Path $GamePath "MelonLoader\Latest.log"),
        (Join-Path $GamePath "UserData\MelonLoader\Latest.log")
    )) {
        if (Test-Path -LiteralPath $candidate) {
            $name = Split-Path -Leaf (Split-Path -Parent $candidate)
            Copy-Item -LiteralPath $candidate -Destination (Join-Path $DestinationPath "$name-Latest.log") -Force
        }
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\runtime-smoke"
}

$runId = Get-Date -Format "yyyyMMdd-HHmmss"
$outputDir = Join-Path $OutputRoot "$runId-$($Scenario.ToLowerInvariant())-$($Runtime.ToLowerInvariant())-$($Side.ToLowerInvariant())"
$isolatedBase = Join-Path $outputDir "isolated"
$role = "$($Scenario.ToLowerInvariant())-$($Runtime.ToLowerInvariant())-$($Side.ToLowerInvariant())"
$resultPath = Join-Path $outputDir "result-$role.txt"
$logsDir = Join-Path $outputDir "logs"

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
New-Item -ItemType Directory -Path $isolatedBase -Force | Out-Null

Write-Host "S1VoiceChat runtime smoke test" -ForegroundColor Cyan
Write-Host "Scenario: $Scenario" -ForegroundColor Gray
Write-Host "Runtime: $Runtime" -ForegroundColor Gray
Write-Host "Side: $Side" -ForegroundColor Gray
Write-Host "Output: $outputDir" -ForegroundColor Gray

Write-Step "Step 1: Build and prepare clean isolated install"
try {
    & (Join-Path $PSScriptRoot "Run-S1VoiceChatValidation.ps1") `
        -Scenario $Scenario `
        -MonoClientPath $MonoClientPath `
        -MonoServerPath $MonoServerPath `
        -Il2CppClientPath $Il2CppClientPath `
        -Il2CppServerPath $Il2CppServerPath `
        -InstanceRoot $isolatedBase `
        -UseIsolatedInstalls `
        -KeepIsolatedInstalls

    if ($LASTEXITCODE -ne 0) {
        throw "S1VoiceChat isolated validation failed"
    }
}
catch {
    if (-not $KeepIsolatedInstalls -and (Test-Path -LiteralPath $isolatedBase)) {
        Remove-TestRoot -RootPath $isolatedBase -AllowedBasePath $outputDir
    }

    throw
}

$isolatedRoots = @(Get-ChildItem -LiteralPath $isolatedBase -Directory)
if ($isolatedRoots.Count -ne 1) {
    throw "Expected exactly one isolated validation root under $isolatedBase, found $($isolatedRoots.Count)."
}

$isolatedRoot = $isolatedRoots[0].FullName
$gamePath = Get-ProfilePath -RootPath $isolatedRoot -SelectedScenario $Scenario -SelectedRuntime $Runtime -SelectedSide $Side
$exePath = Join-Path $gamePath "Schedule I.exe"
Assert-Path $gamePath "Selected isolated game path"
Assert-Path $exePath "Schedule I executable"

Write-Step "Step 2: Launch Schedule I smoke process"
$args = "--s1vc-smoke --s1vc-smoke-dir `"$outputDir`" --s1vc-smoke-role $role --s1vc-smoke-timeout $TimeoutSeconds"
if ($RequireTransport) {
    $args += " --s1vc-smoke-require-transport"
}

if ($Scenario -eq "Dedicated" -and $Side -eq "Server") {
    $args = "--batchmode --nographics --dedicated-server --stdio-console $args"
}

$process = $null
try {
    $process = Start-Process -FilePath $exePath -ArgumentList $args -WorkingDirectory $gamePath -PassThru -WindowStyle Hidden
    Write-Host "PID: $($process.Id)" -ForegroundColor Green

    Write-Step "Step 3: Wait for smoke result"
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds + 20)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2

        if (Test-Path -LiteralPath $resultPath) {
            break
        }

        if ($process.HasExited) {
            break
        }
    }
}
finally {
    Write-Step "Step 4: Collect evidence"
    Copy-Logs -GamePath $gamePath -DestinationPath $logsDir

    if (-not $KeepGameRunning -and $process -ne $null -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }

    if (-not $KeepIsolatedInstalls -and (Test-Path -LiteralPath $isolatedRoot)) {
        Remove-TestRoot -RootPath $isolatedRoot -AllowedBasePath $isolatedBase
    }
}

$resultText = if (Test-Path -LiteralPath $resultPath) {
    Get-Content -LiteralPath $resultPath -Raw
} else {
    "FAIL|No smoke result file was written"
}

Write-Host "Result: $resultText" -ForegroundColor Gray

if ($resultText -notmatch "^PASS\|") {
    Write-Host "Logs: $logsDir" -ForegroundColor Yellow
    Write-Host "Output: $outputDir" -ForegroundColor Yellow
    exit 1
}

Write-Host "S1VoiceChat runtime smoke test passed." -ForegroundColor Green
Write-Host "Output: $outputDir" -ForegroundColor Green
exit 0
