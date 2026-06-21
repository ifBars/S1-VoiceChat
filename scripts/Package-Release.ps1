param(
    [string]$Version,
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$ReleaseRoot = (Join-Path $ProjectRoot "artifacts\release")
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$project = Get-Content (Join-Path $ProjectRoot "src\S1VoiceChat\S1VoiceChat.csproj")
    $Version = $project.Project.PropertyGroup.Version | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Unable to determine release version."
}

function Copy-RequiredFile {
    param(
        [string]$Source,
        [string]$Destination
    )

    if (!(Test-Path -LiteralPath $Source)) {
        throw "Missing release input: $Source"
    }

    $destinationDirectory = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function New-VoiceChatPackage {
    param(
        [string]$RuntimeName,
        [string]$Configuration,
        [string]$TargetFramework,
        [string]$ModFileName,
        [string]$SteamNetworkLibPath
    )

    $packageName = "S1VoiceChat-$RuntimeName-v$Version"
    $packageRoot = Join-Path $ReleaseRoot $packageName
    $zipPath = Join-Path $ReleaseRoot "$packageName.zip"
    $outputRoot = Join-Path $ProjectRoot "src\S1VoiceChat\bin\$Configuration\$TargetFramework"

    Remove-Item -LiteralPath $packageRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue

    New-Item -ItemType Directory -Path (Join-Path $packageRoot "Mods") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $packageRoot "UserLibs") -Force | Out-Null

    Copy-RequiredFile (Join-Path $outputRoot $ModFileName) (Join-Path $packageRoot "Mods\$ModFileName")
    Copy-RequiredFile $SteamNetworkLibPath (Join-Path $packageRoot "UserLibs\SteamNetworkLib.dll")
    Copy-RequiredFile (Join-Path $outputRoot "NAudio.Core.dll") (Join-Path $packageRoot "UserLibs\NAudio.Core.dll")
    Copy-RequiredFile (Join-Path $outputRoot "NAudio.Wasapi.dll") (Join-Path $packageRoot "UserLibs\NAudio.Wasapi.dll")
    Copy-RequiredFile (Join-Path $outputRoot "opus.dll") (Join-Path $packageRoot "UserLibs\opus.dll")
    Copy-RequiredFile (Join-Path $ProjectRoot "README.md") (Join-Path $packageRoot "README.md")
    Copy-RequiredFile (Join-Path $ProjectRoot "assets\credits.txt") (Join-Path $packageRoot "ICON-CREDITS.txt")

    Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath -Force
    Write-Host "Packaged $zipPath"
}

New-Item -ItemType Directory -Path $ReleaseRoot -Force | Out-Null

New-VoiceChatPackage `
    -RuntimeName "Mono" `
    -Configuration "MonoMelon" `
    -TargetFramework "netstandard2.1" `
    -ModFileName "S1VoiceChat.MonoMelon.dll" `
    -SteamNetworkLibPath (Join-Path $ProjectRoot "..\SteamNetworkLib\bin\Mono\netstandard2.1\SteamNetworkLib.dll")

New-VoiceChatPackage `
    -RuntimeName "Il2Cpp" `
    -Configuration "Il2CppMelon" `
    -TargetFramework "net6.0" `
    -ModFileName "S1VoiceChat.Il2CppMelon.dll" `
    -SteamNetworkLibPath (Join-Path $ProjectRoot "..\SteamNetworkLib\bin\Il2cpp\netstandard2.1\SteamNetworkLib.dll")
