#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Prepares a real-Steam manual live voice test for S1VoiceChat.

.DESCRIPTION
    Builds and deploys S1VoiceChat, swaps the active Schedule I steam_api64.dll
    back to the preserved real Steam DLL, and writes Steam launch options for
    S1VoiceChat live voice capture.

    Use -RestoreGoldberg to copy the backed-up Goldberg steam_api64.dll back
    after testing.
#>

param(
    [ValidateSet("Il2Cpp", "Mono")]
    [string]$Runtime = "Il2Cpp",
    [string]$GamePath = "D:\SteamLibrary\steamapps\common\Schedule I_public",
    [string]$PushToTalkKey = "V",
    [ValidateSet("Global", "Proximity", "Whisper", "Shout", "Radio")]
    [string]$VoiceChannel = "Global",
    [ValidateSet("Opus", "Pcm16")]
    [string]$Codec = "Opus",
    [int]$OpusBitrate = 24000,
    [ValidateSet("Microphone", "Wasapi", "Tone")]
    [string]$CaptureSource = "Wasapi",
    [string]$MicDevice = "auto",
    [switch]$OpenMic,
    [switch]$ProbeOnly,
    [switch]$SkipBuild,
    [switch]$RestoreGoldberg
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

function Quote-Argument([string]$Value) {
    if ($Value -notmatch '\s|"') {
        return $Value
    }

    return '"' + $Value.Replace('\', '\\').Replace('"', '\"') + '"'
}

function Copy-VoiceChatUserLibDependencies {
    param(
        [string]$BuildOutputDir,
        [string]$TargetUserLibsPath
    )

    foreach ($dependency in @("NAudio.Core.dll", "NAudio.Wasapi.dll", "opus.dll")) {
        $source = Join-Path $BuildOutputDir $dependency
        Assert-Path $source "S1VoiceChat voice dependency"
        Copy-Item -LiteralPath $source -Destination (Join-Path $TargetUserLibsPath $dependency) -Force
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\S1VoiceChat\S1VoiceChat.csproj"
$configuration = if ($Runtime -eq "Mono") { "MonoMelon" } else { "Il2CppMelon" }
$assemblyName = if ($Runtime -eq "Mono") { "S1VoiceChat.MonoMelon.dll" } else { "S1VoiceChat.Il2CppMelon.dll" }
$snlRuntime = if ($Runtime -eq "Mono") { "Mono" } else { "Il2cpp" }

$exePath = Join-Path $GamePath "Schedule I.exe"
$pluginsPath = Join-Path $GamePath "Schedule I_Data\Plugins\x86_64"
$activeSteamApi = Join-Path $pluginsPath "steam_api64.dll"
$realSteamApi = Join-Path $pluginsPath "steam_api64.dll.real"
$goldbergBackup = Join-Path $pluginsPath "steam_api64.dll.goldberg"
$modsPath = Join-Path $GamePath "Mods"
$userLibsPath = Join-Path $GamePath "UserLibs"
$launchersPath = Join-Path $GamePath "UserData\S1VoiceChat"
$launchOptionsPath = Join-Path $launchersPath "steam-launch-options.txt"
$steamLauncherPath = Join-Path $launchersPath "Start-S1VoiceChat-Steam.ps1"

Assert-Path $GamePath "Game path"
Assert-Path $exePath "Schedule I executable"
Assert-Path $activeSteamApi "Active steam_api64.dll"

if ($RestoreGoldberg) {
    Assert-Path $goldbergBackup "Goldberg steam_api64.dll backup"
    Write-Step "Restore Goldberg steam_api64.dll"
    Copy-Item -LiteralPath $goldbergBackup -Destination $activeSteamApi -Force
    Write-Host "Restored Goldberg steam_api64.dll from $goldbergBackup" -ForegroundColor Green
    return
}

Assert-Path $realSteamApi "Real Steam steam_api64.dll"

if (-not $SkipBuild) {
    Write-Step "Build S1VoiceChat $configuration"
    dotnet build $projectPath -c $configuration -v:q -clp:ErrorsOnly
}

$modSource = Join-Path $repoRoot "src\S1VoiceChat\bin\$configuration\$(if ($Runtime -eq 'Mono') { 'netstandard2.1' } else { 'net6.0' })\$assemblyName"
$snlSource = Join-Path $repoRoot "..\SteamNetworkLib\bin\$snlRuntime\netstandard2.1\SteamNetworkLib.dll"
$assetsSource = Join-Path $repoRoot "assets"

Assert-Path $modSource "S1VoiceChat build output"
Assert-Path $snlSource "SteamNetworkLib build output"
Assert-Path $assetsSource "S1VoiceChat assets"

New-Item -ItemType Directory -Path $modsPath, $userLibsPath, $launchersPath, (Join-Path $modsPath "S1VoiceChat\assets") -Force | Out-Null

Write-Step "Deploy S1VoiceChat"
Copy-Item -LiteralPath $modSource -Destination (Join-Path $modsPath $assemblyName) -Force
Copy-Item -LiteralPath $snlSource -Destination (Join-Path $userLibsPath "SteamNetworkLib.dll") -Force
Copy-VoiceChatUserLibDependencies -BuildOutputDir (Split-Path -Parent $modSource) -TargetUserLibsPath $userLibsPath
Get-ChildItem -LiteralPath $assetsSource -File -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $modsPath "S1VoiceChat\assets\$($_.Name)") -Force
}

if (-not (Test-Path -LiteralPath $goldbergBackup)) {
    Write-Step "Back up current steam_api64.dll as Goldberg"
    Copy-Item -LiteralPath $activeSteamApi -Destination $goldbergBackup -Force
}

Write-Step "Activate real Steam steam_api64.dll"
Copy-Item -LiteralPath $realSteamApi -Destination $activeSteamApi -Force

$liveVoiceArgs = if ($ProbeOnly) {
    "--s1vc-steam-voice-probe --s1vc-ptt-key $PushToTalkKey"
} else {
    "--s1vc-live-voice --s1vc-ptt-key $(Quote-Argument $PushToTalkKey) --s1vc-voice-channel $(Quote-Argument $VoiceChannel) --s1vc-codec $(Quote-Argument $Codec) --s1vc-capture-source $($CaptureSource.ToLowerInvariant())"
}

if ($OpenMic -and -not $ProbeOnly) {
    $liveVoiceArgs += " --s1vc-open-mic"
}

if (-not $ProbeOnly -and -not [string]::IsNullOrWhiteSpace($MicDevice)) {
    $liveVoiceArgs += " --s1vc-mic-device $(Quote-Argument $MicDevice)"
}

if (-not $ProbeOnly -and $Codec -eq "Opus" -and $OpusBitrate -gt 0) {
    $liveVoiceArgs += " --s1vc-opus-bitrate $OpusBitrate"
}

Set-Content -LiteralPath $launchOptionsPath -Value $liveVoiceArgs -Encoding UTF8

$steamInfo = Get-ItemProperty -LiteralPath "HKCU:\Software\Valve\Steam" -ErrorAction SilentlyContinue
$steamExe = if ($steamInfo -and -not [string]::IsNullOrWhiteSpace($steamInfo.SteamExe)) {
    $steamInfo.SteamExe -replace '/', '\'
} else {
    "C:\Program Files (x86)\Steam\steam.exe"
}

$appIdPath = Join-Path $GamePath "steam_appid.txt"
$appId = if (Test-Path -LiteralPath $appIdPath) {
    (Get-Content -LiteralPath $appIdPath -Raw).Trim()
} else {
    "3164500"
}

$escapedSteamExe = $steamExe.Replace("'", "''")
$escapedLiveVoiceArgs = $liveVoiceArgs.Replace("'", "''")
$escapedAppId = $appId.Replace("'", "''")
@"
`$ErrorActionPreference = "Stop"
`$steamExe = '$escapedSteamExe'
`$appId = '$escapedAppId'
`$voiceArgs = '$escapedLiveVoiceArgs'
Start-Process -FilePath `$steamExe -ArgumentList "-applaunch `$appId `$voiceArgs"
"@ | Set-Content -LiteralPath $steamLauncherPath -Encoding UTF8

Write-Host "S1VoiceChat real-Steam manual test is ready." -ForegroundColor Green
Write-Host "Steam launch options:" -ForegroundColor Green
Write-Host $liveVoiceArgs -ForegroundColor Cyan
Write-Host "Saved launch options: $launchOptionsPath" -ForegroundColor Green
Write-Host "Steam launch helper: $steamLauncherPath" -ForegroundColor Green
Write-Host "In Steam: Schedule I > Properties > General > Launch Options, paste the line above. Then launch from Steam and hold $PushToTalkKey. Add --s1vc-debug-logs only when you need verbose capture diagnostics." -ForegroundColor Green
Write-Host "Restore Goldberg later with: $PSCommandPath -Runtime $Runtime -GamePath '$GamePath' -RestoreGoldberg" -ForegroundColor Yellow
