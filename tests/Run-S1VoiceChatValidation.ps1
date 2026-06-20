#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds and validates S1VoiceChat, then inspects local Schedule I mod folders.

.DESCRIPTION
    This script is intentionally non-destructive by default. It verifies the core
    voice pipeline tests, builds Mono and Il2Cpp outputs, and reports unexpected
    Mods/UserLibs entries before any runtime test run can be trusted.
#>

param(
    [ValidateSet("All", "P2P", "Dedicated")]
    [string]$Scenario = "All",
    [string]$MonoClientPath = "D:\SteamLibrary\steamapps\common\Schedule I_alternate",
    [string]$MonoServerPath = "D:\SteamLibrary\steamapps\common\Schedule I_server",
    [string]$Il2CppClientPath = "D:\SteamLibrary\steamapps\common\Schedule I_public",
    [string]$Il2CppServerPath = "D:\SteamLibrary\steamapps\common\Schedule I_public_server",
    [string]$InstanceRoot = "",
    [switch]$DeployVoiceChat,
    [switch]$UseIsolatedInstalls,
    [switch]$KeepIsolatedInstalls,
    [switch]$AllowExtraMods
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

function Invoke-Checked {
    param([scriptblock]$Command, [string]$Failure)

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw $Failure
    }
}

$validationIssues = [System.Collections.Generic.List[string]]::new()
$isolatedRoot = $null

function New-FileLinkOrCopy {
    param(
        [string]$SourcePath,
        [string]$DestinationPath
    )

    try {
        New-Item -ItemType HardLink -Path $DestinationPath -Target $SourcePath -Force | Out-Null
    }
    catch {
        Copy-Item -LiteralPath $SourcePath -Destination $DestinationPath -Force
    }
}

function Copy-GameFiles {
    param(
        [string]$SourcePath,
        [string]$DestinationPath
    )

    Assert-Path $SourcePath "Source game path"
    Assert-Path (Join-Path $SourcePath "Schedule I.exe") "Source Schedule I executable"

    New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null

    foreach ($file in @("Schedule I.exe", "UnityCrashHandler64.exe", "UnityPlayer.dll", "steam_appid.txt", "version.dll", "GameAssembly.dll", "baselib.dll", "start_server.bat", "server_config.toml")) {
        $sourceFile = Join-Path $SourcePath $file
        if (Test-Path -LiteralPath $sourceFile) {
            New-FileLinkOrCopy -SourcePath $sourceFile -DestinationPath (Join-Path $DestinationPath $file)
        }
    }

    $monoRuntime = Join-Path $SourcePath "MonoBleedingEdge"
    if (Test-Path -LiteralPath $monoRuntime) {
        New-Item -ItemType Junction -Path (Join-Path $DestinationPath "MonoBleedingEdge") -Target $monoRuntime -Force | Out-Null
    }

    $sourceData = Join-Path $SourcePath "Schedule I_Data"
    $destData = Join-Path $DestinationPath "Schedule I_Data"
    New-Item -ItemType Directory -Path $destData -Force | Out-Null

    foreach ($item in Get-ChildItem -LiteralPath $sourceData -Force) {
        $destItem = Join-Path $destData $item.Name
        if ($item.PSIsContainer) {
            New-Item -ItemType Junction -Path $destItem -Target $item.FullName -Force | Out-Null
        }
        else {
            New-FileLinkOrCopy -SourcePath $item.FullName -DestinationPath $destItem
        }
    }

    $melonLoaderDir = Join-Path $SourcePath "MelonLoader"
    if (Test-Path -LiteralPath $melonLoaderDir) {
        New-Item -ItemType Junction -Path (Join-Path $DestinationPath "MelonLoader") -Target $melonLoaderDir -Force | Out-Null
    }

    New-Item -ItemType Directory -Path (Join-Path $DestinationPath "Mods") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $DestinationPath "UserLibs") -Force | Out-Null
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

function Get-FileNames([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return @()
    }

    return @(Get-ChildItem -LiteralPath $Path -File -Force | Select-Object -ExpandProperty Name)
}

function Copy-VoiceChatAssets {
    param(
        [string]$Label,
        [string]$GamePath
    )

    Write-Host "$Label S1VoiceChat HUD assets are embedded in the mod assembly." -ForegroundColor Gray
}

function Copy-VoiceChatUserLibDependencies {
    param(
        [string]$Label,
        [string]$GamePath,
        [string]$SourceDll
    )

    $sourceDir = Split-Path -Parent $SourceDll
    $userLibsDir = Join-Path $GamePath "UserLibs"
    New-Item -ItemType Directory -Path $userLibsDir -Force | Out-Null

    foreach ($dependency in @("NAudio.Core.dll", "NAudio.Wasapi.dll", "opus.dll")) {
        $source = Join-Path $sourceDir $dependency
        Assert-Path $source "$Label voice dependency"
        Copy-Item -LiteralPath $source -Destination (Join-Path $userLibsDir $dependency) -Force
        Write-Host "Deployed $dependency to $userLibsDir" -ForegroundColor Gray
    }
}

function Test-ModFolder {
    param(
        [string]$Label,
        [string]$GamePath,
        [string[]]$ExpectedMods,
        [string[]]$ExpectedUserLibs,
        [string[]]$ExpectedVoiceAssets = @()
    )

    Write-Step "Inspect $Label install"
    Assert-Path $GamePath "$Label game path"
    Assert-Path (Join-Path $GamePath "Schedule I.exe") "$Label executable"

    $modsDir = Join-Path $GamePath "Mods"
    $userLibsDir = Join-Path $GamePath "UserLibs"
    Assert-Path $modsDir "$Label Mods directory"

    $mods = Get-FileNames $modsDir
    $userLibs = Get-FileNames $userLibsDir
    $voiceAssetsDir = Join-Path $modsDir "S1VoiceChat\assets"
    $voiceAssets = Get-FileNames $voiceAssetsDir

    Write-Host "$Label Mods: $($mods -join ', ')" -ForegroundColor Gray
    Write-Host "$Label UserLibs: $($userLibs -join ', ')" -ForegroundColor Gray
    if ($ExpectedVoiceAssets.Count -gt 0) {
        Write-Host "$Label S1VoiceChat assets: $($voiceAssets -join ', ')" -ForegroundColor Gray
    }

    $missingMods = @($ExpectedMods | Where-Object { $mods -notcontains $_ })
    $missingUserLibs = @()
    foreach ($expectedUserLib in $ExpectedUserLibs) {
        $allowedNames = @($expectedUserLib -split '\|' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $matched = @($allowedNames | Where-Object { $userLibs -contains $_ })
        if ($matched.Count -eq 0) {
            $missingUserLibs += $expectedUserLib
        }
    }
    $extraMods = @($mods | Where-Object { $ExpectedMods -notcontains $_ })

    if ($missingMods.Count -gt 0) {
        $script:validationIssues.Add("$Label missing expected Mods entries: $($missingMods -join ', ')")
    }

    if ($missingUserLibs.Count -gt 0) {
        $script:validationIssues.Add("$Label missing expected UserLibs entries: $($missingUserLibs -join ', ')")
    }

    if ($ExpectedVoiceAssets.Count -gt 0) {
        $missingVoiceAssets = @($ExpectedVoiceAssets | Where-Object { $voiceAssets -notcontains $_ })
        if ($missingVoiceAssets.Count -gt 0) {
            $script:validationIssues.Add("$Label missing expected S1VoiceChat asset files: $($missingVoiceAssets -join ', ')")
        }
    }

    if (-not $AllowExtraMods -and $extraMods.Count -gt 0) {
        $script:validationIssues.Add("$Label has extra Mods entries that can interfere with voice validation: $($extraMods -join ', ')")
    }
}

function Copy-VoiceChatMod {
    param(
        [string]$Label,
        [string]$GamePath,
        [string]$SourceDll,
        [string]$TargetName
    )

    $modsDir = Join-Path $GamePath "Mods"
    Assert-Path $GamePath "$Label game path"
    Assert-Path $SourceDll "$Label S1VoiceChat build output"
    New-Item -ItemType Directory -Path $modsDir -Force | Out-Null
    Copy-Item -LiteralPath $SourceDll -Destination (Join-Path $modsDir $TargetName) -Force
    Copy-VoiceChatAssets -Label $Label -GamePath $GamePath
    Copy-VoiceChatUserLibDependencies -Label $Label -GamePath $GamePath -SourceDll $SourceDll
    Write-Host "Deployed $TargetName to $modsDir" -ForegroundColor Gray
}

function Copy-ModArtifact {
    param(
        [string]$Label,
        [string]$GamePath,
        [string]$SourceDll,
        [string]$TargetName
    )

    $modsDir = Join-Path $GamePath "Mods"
    Assert-Path $SourceDll "$Label source DLL"
    New-Item -ItemType Directory -Path $modsDir -Force | Out-Null
    Copy-Item -LiteralPath $SourceDll -Destination (Join-Path $modsDir $TargetName) -Force
    if ($TargetName.StartsWith("S1VoiceChat.", [System.StringComparison]::OrdinalIgnoreCase)) {
        Copy-VoiceChatAssets -Label $Label -GamePath $GamePath
        Copy-VoiceChatUserLibDependencies -Label $Label -GamePath $GamePath -SourceDll $SourceDll
    }
    Write-Host "Deployed $TargetName to $modsDir" -ForegroundColor Gray
}

function Copy-UserLibArtifact {
    param(
        [string]$Label,
        [string]$GamePath,
        [string]$SourceDll,
        [string]$TargetName = "SteamNetworkLib.dll"
    )

    $userLibsDir = Join-Path $GamePath "UserLibs"
    Assert-Path $SourceDll "$Label source DLL"
    New-Item -ItemType Directory -Path $userLibsDir -Force | Out-Null
    Copy-Item -LiteralPath $SourceDll -Destination (Join-Path $userLibsDir $TargetName) -Force
    Write-Host "Deployed $TargetName to $userLibsDir" -ForegroundColor Gray
}

function Deploy-VoiceChatForScenario {
    $monoDll = Join-Path $repoRoot "src\S1VoiceChat\bin\MonoMelon\netstandard2.1\S1VoiceChat.MonoMelon.dll"
    $il2cppDll = Join-Path $repoRoot "src\S1VoiceChat\bin\Il2CppMelon\net6.0\S1VoiceChat.Il2CppMelon.dll"

    if ($Scenario -eq "All" -or $Scenario -eq "P2P") {
        Copy-VoiceChatMod -Label "P2P Mono client" -GamePath $MonoClientPath -SourceDll $monoDll -TargetName "S1VoiceChat.MonoMelon.dll"
        Copy-VoiceChatMod -Label "P2P Il2Cpp client" -GamePath $Il2CppClientPath -SourceDll $il2cppDll -TargetName "S1VoiceChat.Il2CppMelon.dll"
    }

    if ($Scenario -eq "All" -or $Scenario -eq "Dedicated") {
        Copy-VoiceChatMod -Label "Dedicated Mono client" -GamePath $MonoClientPath -SourceDll $monoDll -TargetName "S1VoiceChat.MonoMelon.dll"
        Copy-VoiceChatMod -Label "Dedicated Mono server" -GamePath $MonoServerPath -SourceDll $monoDll -TargetName "S1VoiceChat.MonoMelon.dll"
        Copy-VoiceChatMod -Label "Dedicated Il2Cpp client" -GamePath $Il2CppClientPath -SourceDll $il2cppDll -TargetName "S1VoiceChat.Il2CppMelon.dll"
        Copy-VoiceChatMod -Label "Dedicated Il2Cpp server" -GamePath $Il2CppServerPath -SourceDll $il2cppDll -TargetName "S1VoiceChat.Il2CppMelon.dll"
    }
}

function Deploy-P2PArtifacts {
    param(
        [string]$Runtime,
        [string]$GamePath
    )

    if ($Runtime -eq "Mono") {
        Copy-ModArtifact -Label "P2P Mono client" -GamePath $GamePath -SourceDll $monoVoiceDll -TargetName "S1VoiceChat.MonoMelon.dll"
        Copy-UserLibArtifact -Label "P2P Mono client" -GamePath $GamePath -SourceDll $monoSteamNetworkLibDll
        return
    }

    Copy-ModArtifact -Label "P2P Il2Cpp client" -GamePath $GamePath -SourceDll $il2cppVoiceDll -TargetName "S1VoiceChat.Il2CppMelon.dll"
    Copy-UserLibArtifact -Label "P2P Il2Cpp client" -GamePath $GamePath -SourceDll $il2cppSteamNetworkLibDll
}

function Deploy-DedicatedArtifacts {
    param(
        [string]$Runtime,
        [string]$Side,
        [string]$GamePath
    )

    if ($Runtime -eq "Mono") {
        $dedicatedDll = if ($Side -eq "Client") { $monoDedicatedClientDll } else { $monoDedicatedServerDll }
        $dedicatedName = if ($Side -eq "Client") { "DedicatedServerMod_Mono_Client.dll" } else { "DedicatedServerMod_Mono_Server.dll" }

        Copy-ModArtifact -Label "Dedicated Mono $Side" -GamePath $GamePath -SourceDll $dedicatedDll -TargetName $dedicatedName
        Copy-ModArtifact -Label "Dedicated Mono $Side" -GamePath $GamePath -SourceDll $monoS1ApiDll -TargetName "S1API.Mono.MelonLoader.dll"
        Copy-ModArtifact -Label "Dedicated Mono $Side" -GamePath $GamePath -SourceDll $monoVoiceDll -TargetName "S1VoiceChat.MonoMelon.dll"
        Copy-UserLibArtifact -Label "Dedicated Mono $Side" -GamePath $GamePath -SourceDll $monoSteamNetworkLibDll
        return
    }

    $il2cppDedicatedDll = if ($Side -eq "Client") { $il2cppDedicatedClientDll } else { $il2cppDedicatedServerDll }
    $il2cppDedicatedName = if ($Side -eq "Client") { "DedicatedServerMod_Il2cpp_Client.dll" } else { "DedicatedServerMod_Il2cpp_Server.dll" }

    Copy-ModArtifact -Label "Dedicated Il2Cpp $Side" -GamePath $GamePath -SourceDll $il2cppDedicatedDll -TargetName $il2cppDedicatedName
    Copy-ModArtifact -Label "Dedicated Il2Cpp $Side" -GamePath $GamePath -SourceDll $il2cppS1ApiDll -TargetName "S1API.Il2Cpp.MelonLoader.dll"
    Copy-ModArtifact -Label "Dedicated Il2Cpp $Side" -GamePath $GamePath -SourceDll $il2cppVoiceDll -TargetName "S1VoiceChat.Il2CppMelon.dll"
    Copy-UserLibArtifact -Label "Dedicated Il2Cpp $Side" -GamePath $GamePath -SourceDll $il2cppSteamNetworkLibDll
}

function Prepare-IsolatedInstalls {
    if ($Scenario -eq "All") {
        throw "Use -Scenario P2P or -Scenario Dedicated with -UseIsolatedInstalls so each clean profile has unambiguous install paths."
    }

    if ([string]::IsNullOrWhiteSpace($InstanceRoot)) {
        $script:InstanceRoot = Join-Path $repoRoot "artifacts\isolated-installs"
    }

    $script:isolatedRoot = Join-Path $InstanceRoot ([Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $isolatedRoot -Force | Out-Null
    Write-Host "Isolated root: $isolatedRoot" -ForegroundColor Gray

    if ($Scenario -eq "All" -or $Scenario -eq "P2P") {
        $monoP2P = Join-Path $isolatedRoot "p2p-mono-client"
        $il2cppP2P = Join-Path $isolatedRoot "p2p-il2cpp-client"
        Copy-GameFiles -SourcePath $MonoClientPath -DestinationPath $monoP2P
        Copy-GameFiles -SourcePath $Il2CppClientPath -DestinationPath $il2cppP2P
        Deploy-P2PArtifacts -Runtime "Mono" -GamePath $monoP2P
        Deploy-P2PArtifacts -Runtime "Il2Cpp" -GamePath $il2cppP2P
        $script:MonoClientPath = $monoP2P
        $script:Il2CppClientPath = $il2cppP2P
    }

    if ($Scenario -eq "All" -or $Scenario -eq "Dedicated") {
        $monoDedicatedClient = Join-Path $isolatedRoot "dedicated-mono-client"
        $monoDedicatedServer = Join-Path $isolatedRoot "dedicated-mono-server"
        $il2cppDedicatedClient = Join-Path $isolatedRoot "dedicated-il2cpp-client"
        $il2cppDedicatedServer = Join-Path $isolatedRoot "dedicated-il2cpp-server"

        Copy-GameFiles -SourcePath $MonoClientPath -DestinationPath $monoDedicatedClient
        Copy-GameFiles -SourcePath $MonoServerPath -DestinationPath $monoDedicatedServer
        Copy-GameFiles -SourcePath $Il2CppClientPath -DestinationPath $il2cppDedicatedClient
        Copy-GameFiles -SourcePath $Il2CppServerPath -DestinationPath $il2cppDedicatedServer

        Deploy-DedicatedArtifacts -Runtime "Mono" -Side "Client" -GamePath $monoDedicatedClient
        Deploy-DedicatedArtifacts -Runtime "Mono" -Side "Server" -GamePath $monoDedicatedServer
        Deploy-DedicatedArtifacts -Runtime "Il2Cpp" -Side "Client" -GamePath $il2cppDedicatedClient
        Deploy-DedicatedArtifacts -Runtime "Il2Cpp" -Side "Server" -GamePath $il2cppDedicatedServer

        $script:MonoClientPath = $monoDedicatedClient
        $script:MonoServerPath = $monoDedicatedServer
        $script:Il2CppClientPath = $il2cppDedicatedClient
        $script:Il2CppServerPath = $il2cppDedicatedServer
    }
}

function Test-P2PInstalls {
    Write-Step "Validate regular P2P install profiles"
    Test-ModFolder `
        -Label "P2P Mono client" `
        -GamePath $MonoClientPath `
        -ExpectedMods @("S1VoiceChat.MonoMelon.dll") `
        -ExpectedUserLibs $requiredVoiceUserLibs `
        -ExpectedVoiceAssets $requiredVoiceAssetFiles

    Test-ModFolder `
        -Label "P2P Il2Cpp client" `
        -GamePath $Il2CppClientPath `
        -ExpectedMods @("S1VoiceChat.Il2CppMelon.dll") `
        -ExpectedUserLibs $requiredVoiceUserLibs `
        -ExpectedVoiceAssets $requiredVoiceAssetFiles
}

function Test-DedicatedInstalls {
    Write-Step "Validate dedicated-server install profiles"
    Test-ModFolder `
        -Label "Dedicated Mono client" `
        -GamePath $MonoClientPath `
        -ExpectedMods @("DedicatedServerMod_Mono_Client.dll", "S1API.Mono.MelonLoader.dll", "S1VoiceChat.MonoMelon.dll") `
        -ExpectedUserLibs $requiredVoiceUserLibs `
        -ExpectedVoiceAssets $requiredVoiceAssetFiles

    Test-ModFolder `
        -Label "Dedicated Mono server" `
        -GamePath $MonoServerPath `
        -ExpectedMods @("DedicatedServerMod_Mono_Server.dll", "S1API.Mono.MelonLoader.dll", "S1VoiceChat.MonoMelon.dll") `
        -ExpectedUserLibs $requiredVoiceUserLibs `
        -ExpectedVoiceAssets $requiredVoiceAssetFiles

    Test-ModFolder `
        -Label "Dedicated Il2Cpp client" `
        -GamePath $Il2CppClientPath `
        -ExpectedMods @("DedicatedServerMod_Il2cpp_Client.dll", "S1API.Il2Cpp.MelonLoader.dll", "S1VoiceChat.Il2CppMelon.dll") `
        -ExpectedUserLibs $requiredVoiceUserLibs `
        -ExpectedVoiceAssets $requiredVoiceAssetFiles

    Test-ModFolder `
        -Label "Dedicated Il2Cpp server" `
        -GamePath $Il2CppServerPath `
        -ExpectedMods @("DedicatedServerMod_Il2cpp_Server.dll", "S1API.Il2Cpp.MelonLoader.dll", "S1VoiceChat.Il2CppMelon.dll") `
        -ExpectedUserLibs $requiredVoiceUserLibs `
        -ExpectedVoiceAssets $requiredVoiceAssetFiles
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\S1VoiceChat\S1VoiceChat.csproj"
$tests = Join-Path $repoRoot "tests\S1VoiceChat.Tests\S1VoiceChat.Tests.csproj"
$workspaceRoot = Split-Path -Parent $repoRoot
$monoVoiceDll = Join-Path $repoRoot "src\S1VoiceChat\bin\MonoMelon\netstandard2.1\S1VoiceChat.MonoMelon.dll"
$il2cppVoiceDll = Join-Path $repoRoot "src\S1VoiceChat\bin\Il2CppMelon\net6.0\S1VoiceChat.Il2CppMelon.dll"
$monoSteamNetworkLibDll = Join-Path $workspaceRoot "SteamNetworkLib\bin\Mono\netstandard2.1\SteamNetworkLib.dll"
$il2cppSteamNetworkLibDll = Join-Path $workspaceRoot "SteamNetworkLib\bin\Il2cpp\netstandard2.1\SteamNetworkLib.dll"
$monoDedicatedClientDll = Join-Path $workspaceRoot "DedicatedServerMod\bin\Mono_Client\netstandard2.1\DedicatedServerMod_Mono_Client.dll"
$monoDedicatedServerDll = Join-Path $workspaceRoot "DedicatedServerMod\bin\Mono_Server\netstandard2.1\DedicatedServerMod_Mono_Server.dll"
$il2cppDedicatedClientDll = Join-Path $workspaceRoot "DedicatedServerMod\bin\Il2cpp_Client\net6.0\DedicatedServerMod_Il2cpp_Client.dll"
$il2cppDedicatedServerDll = Join-Path $workspaceRoot "DedicatedServerMod\bin\Il2cpp_Server\net6.0\DedicatedServerMod_Il2cpp_Server.dll"
$monoS1ApiDll = Join-Path $workspaceRoot "S1API\S1API\bin\MonoMelon\netstandard2.1\S1API.dll"
$il2cppS1ApiDll = Join-Path $workspaceRoot "S1API\S1API\bin\Il2CppMelon\net6.0\S1API.dll"
$requiredVoiceAssetFiles = @()
$requiredVoiceUserLibs = @("SteamNetworkLib.dll|SteamNetworkLib-Mono.dll|SteamNetworkLib-IL2Cpp.dll", "NAudio.Core.dll", "NAudio.Wasapi.dll", "opus.dll")

Write-Host "S1VoiceChat validation" -ForegroundColor Cyan
Write-Host "Repo: $repoRoot" -ForegroundColor Gray
Write-Host "Scenario: $Scenario" -ForegroundColor Gray

Write-Step "Run core test harness"
Invoke-Checked { dotnet run --project $tests -c Release } "S1VoiceChat core tests failed"

Write-Step "Build MonoMelon"
Invoke-Checked { dotnet build $project -c MonoMelon -v:q -clp:ErrorsOnly } "MonoMelon build failed"

Write-Step "Build Il2CppMelon"
Invoke-Checked { dotnet build $project -c Il2CppMelon -v:q -clp:ErrorsOnly } "Il2CppMelon build failed"

if ($UseIsolatedInstalls) {
    Write-Step "Prepare isolated clean installs"
    Prepare-IsolatedInstalls
    $script:AllowExtraMods = $false
}

if ($DeployVoiceChat) {
    Write-Step "Deploy S1VoiceChat DLLs"
    Deploy-VoiceChatForScenario
}

if ($Scenario -eq "All" -or $Scenario -eq "P2P") {
    Test-P2PInstalls
}

if ($Scenario -eq "All" -or $Scenario -eq "Dedicated") {
    Test-DedicatedInstalls
}

if ($validationIssues.Count -gt 0) {
    Write-Host "S1VoiceChat install validation failed:" -ForegroundColor Red
    foreach ($issue in $validationIssues) {
        Write-Host "- $issue" -ForegroundColor Red
    }

    exit 1
}

Write-Host "S1VoiceChat validation passed." -ForegroundColor Green

if ($UseIsolatedInstalls -and -not $KeepIsolatedInstalls -and $isolatedRoot -and (Test-Path -LiteralPath $isolatedRoot)) {
    Remove-TestRoot -RootPath $isolatedRoot -AllowedBasePath $InstanceRoot
}
