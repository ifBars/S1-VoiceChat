#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Installs S1VoiceChat manual LocalLobby test files into a real Schedule I install.

.DESCRIPTION
    Builds S1VoiceChat, copies the runtime-specific DLL and SteamNetworkLib into the
    target install, downloads/copies LocalLobby, and writes host/client launcher
    scripts that set Goldberg account config before starting the game.
#>

param(
    [ValidateSet("Il2Cpp", "Mono")]
    [string]$Runtime = "Il2Cpp",
    [string]$GamePath = "D:\SteamLibrary\steamapps\common\Schedule I_public",
    [string]$MonoGamePath = "D:\SteamLibrary\steamapps\common\Schedule I_alternate",
    [string]$LocalLobbyVersion = "1.0.0",
    [string]$LocalLobbyAssetRoot = "",
    [string]$HostSteamId = "76561198000000019",
    [string]$ClientSteamId = "76561198000000009",
    [string]$HostName = "S1VCHost",
    [string]$ClientName = "S1VCClient",
    [string]$ManualKey = "F8",
    [switch]$EnableLiveVoice,
    [string]$PushToTalkKey = "V",
    [switch]$OpenMic,
    [switch]$HostOpenMic,
    [switch]$ClientOpenMic,
    [ValidateSet("Opus", "Pcm16")]
    [string]$Codec = "Opus",
    [int]$OpusBitrate = 24000,
    [ValidateSet("Global", "Proximity", "Whisper", "Shout", "Radio")]
    [string]$VoiceChannel = "Global",
    [ValidateSet("Microphone", "Wasapi", "Tone")]
    [string]$CaptureSource = "Wasapi",
    [string]$MicDevice = "auto",
    [string]$Token = "",
    [switch]$SkipBuild
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

function New-LiveVoiceArgs([bool]$IncludeOpenMic) {
    if (-not $EnableLiveVoice) {
        return ""
    }

    $liveVoiceParts = @(
        "--s1vc-live-voice",
        "--s1vc-ptt-key", (Quote-Argument $PushToTalkKey),
        "--s1vc-voice-channel", (Quote-Argument $VoiceChannel),
        "--s1vc-codec", (Quote-Argument $Codec),
        "--s1vc-capture-source", $CaptureSource.ToLowerInvariant()
    )

    if (-not [string]::IsNullOrWhiteSpace($MicDevice)) {
        $liveVoiceParts += "--s1vc-mic-device"
        $liveVoiceParts += Quote-Argument $MicDevice
    }

    if ($OpenMic -or $IncludeOpenMic) {
        $liveVoiceParts += "--s1vc-open-mic"
    }

    if ($Codec -eq "Opus" -and $OpusBitrate -gt 0) {
        $liveVoiceParts += "--s1vc-opus-bitrate"
        $liveVoiceParts += $OpusBitrate.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    }

    return $liveVoiceParts -join " "
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
    Write-Host "Downloading $assetName from LocalLobby $LocalLobbyVersion" -ForegroundColor Gray
    Invoke-WebRequest -Uri $downloadUrl -OutFile $assetPath -UseBasicParsing
    return $assetPath
}

function Write-Launcher {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [Parameter(Mandatory=$true)][string]$AccountName,
        [Parameter(Mandatory=$true)][string]$SteamId,
        [Parameter(Mandatory=$true)][string]$ModeArgs,
        [bool]$ClearLobbyFile
    )

    $escapedGamePath = $GamePath.Replace("'", "''")
    $escapedAccountName = $AccountName.Replace("'", "''")
    $escapedSteamId = $SteamId.Replace("'", "''")
    $escapedModeArgs = $ModeArgs.Replace("'", "''")

    $clearLobbyLine = if ($ClearLobbyFile) {
        "Remove-Item -LiteralPath (Join-Path `$gamePath 'UserData\LocalLobby\lobby.txt') -Force -ErrorAction SilentlyContinue"
    } else {
        "# Keep UserData\LocalLobby\lobby.txt so --join can read the host lobby."
    }

    $content = @"
`$ErrorActionPreference = "Stop"
`$gamePath = '$escapedGamePath'
`$settingsDir = Join-Path `$gamePath 'Schedule I_Data\Plugins\x86_64\steam_settings'
New-Item -ItemType Directory -Path `$settingsDir -Force | Out-Null
@'
[user::general]
account_name=$escapedAccountName
account_steamid=$escapedSteamId
language=english
'@ | Set-Content -LiteralPath (Join-Path `$settingsDir 'configs.user.ini') -Encoding UTF8
$clearLobbyLine
Start-Process -FilePath (Join-Path `$gamePath 'Schedule I.exe') -WorkingDirectory `$gamePath -ArgumentList '$escapedModeArgs --s1vc-manual-test --s1vc-manual-key $ManualKey --s1vc-manual-token $Token'
"@

    Set-Content -LiteralPath $Path -Value $content -Encoding UTF8
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\S1VoiceChat\S1VoiceChat.csproj"
$dependencyCacheRoot = Join-Path $repoRoot "artifacts\two-client-smoke\_dependencies\LocalLobby"

if ([string]::IsNullOrWhiteSpace($Token)) {
    $Token = [Guid]::NewGuid().ToString("N")
}

Assert-Path $GamePath "Game path"
Assert-Path (Join-Path $GamePath "Schedule I.exe") "Schedule I executable"

$configuration = if ($Runtime -eq "Mono") { "MonoMelon" } else { "Il2CppMelon" }
$assemblyName = if ($Runtime -eq "Mono") { "S1VoiceChat.MonoMelon.dll" } else { "S1VoiceChat.Il2CppMelon.dll" }
$snlRuntime = if ($Runtime -eq "Mono") { "Mono" } else { "Il2cpp" }

if (-not $SkipBuild) {
    Write-Step "Build S1VoiceChat $configuration"
    dotnet build $projectPath -c $configuration -v:q -clp:ErrorsOnly
}

$modSource = Join-Path $repoRoot "src\S1VoiceChat\bin\$configuration\$(if ($Runtime -eq 'Mono') { 'netstandard2.1' } else { 'net6.0' })\$assemblyName"
$snlSource = Join-Path $repoRoot "..\SteamNetworkLib\bin\$snlRuntime\netstandard2.1\SteamNetworkLib.dll"
$localLobbySource = Get-LocalLobbyDll -RuntimeName $Runtime -CacheRoot $dependencyCacheRoot
$assetsSource = Join-Path $repoRoot "assets"

Assert-Path $modSource "S1VoiceChat build output"
Assert-Path $snlSource "SteamNetworkLib build output"
Assert-Path $localLobbySource "LocalLobby DLL"
Assert-Path $assetsSource "S1VoiceChat assets"

$modsPath = Join-Path $GamePath "Mods"
$userLibsPath = Join-Path $GamePath "UserLibs"
$launchersPath = Join-Path $GamePath "UserData\S1VoiceChat"
$modAssetsPath = Join-Path $modsPath "S1VoiceChat\assets"
New-Item -ItemType Directory -Path $modsPath, $userLibsPath, $launchersPath, $modAssetsPath -Force | Out-Null

Write-Step "Deploy manual test files"
Copy-Item -LiteralPath $modSource -Destination (Join-Path $modsPath $assemblyName) -Force
Copy-Item -LiteralPath $localLobbySource -Destination (Join-Path $modsPath (Split-Path -Leaf $localLobbySource)) -Force
Copy-Item -LiteralPath $snlSource -Destination (Join-Path $userLibsPath "SteamNetworkLib.dll") -Force
Copy-VoiceChatUserLibDependencies -BuildOutputDir (Split-Path -Parent $modSource) -TargetUserLibsPath $userLibsPath
Get-ChildItem -LiteralPath $assetsSource -File -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $modAssetsPath $_.Name) -Force
}

$unexpectedMods = @(Get-ChildItem -LiteralPath $modsPath -File |
    Where-Object { $_.Name -notin @($assemblyName, (Split-Path -Leaf $localLobbySource)) } |
    Select-Object -ExpandProperty Name)
if ($unexpectedMods.Count -gt 0) {
    Write-Host "Warning: other mods are present and may affect manual testing: $($unexpectedMods -join ', ')" -ForegroundColor Yellow
}

$hostLauncher = Join-Path $launchersPath "Start-S1VC-ManualHost.ps1"
$clientLauncher = Join-Path $launchersPath "Start-S1VC-ManualClient.ps1"

$hostLiveVoiceArgs = New-LiveVoiceArgs -IncludeOpenMic $HostOpenMic
$clientLiveVoiceArgs = New-LiveVoiceArgs -IncludeOpenMic $ClientOpenMic
Write-Launcher -Path $hostLauncher -AccountName $HostName -SteamId $HostSteamId -ModeArgs "--host $hostLiveVoiceArgs" -ClearLobbyFile $true
Write-Launcher -Path $clientLauncher -AccountName $ClientName -SteamId $ClientSteamId -ModeArgs "--join $clientLiveVoiceArgs" -ClearLobbyFile $false

Write-Host "Manual S1VoiceChat LocalLobby test installed." -ForegroundColor Green
Write-Host "Host launcher: $hostLauncher" -ForegroundColor Green
Write-Host "Client launcher: $clientLauncher" -ForegroundColor Green
Write-Host "Press $ManualKey in either client after both are in the LocalLobby session." -ForegroundColor Green
if ($EnableLiveVoice) {
    Write-Host "Live voice enabled. Hold $PushToTalkKey to transmit $CaptureSource audio on $VoiceChannel channel using $Codec codec." -ForegroundColor Green
    if ($OpenMic -or $HostOpenMic -or $ClientOpenMic) {
        Write-Host "Open mic launch argument is enabled for: $(if ($OpenMic) { 'both' } elseif ($HostOpenMic -and $ClientOpenMic) { 'host, client' } elseif ($HostOpenMic) { 'host' } else { 'client' })." -ForegroundColor Green
    }
}
Write-Host "Manual test log: $(Join-Path $launchersPath 'manual-test.log')" -ForegroundColor Green
