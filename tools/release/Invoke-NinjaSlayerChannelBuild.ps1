#Requires -Version 7.0
#Requires -PSEdition Core

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('stable', 'preview')]
    [string]$Channel,

    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$Sts2DataDir,

    [Parameter(Mandatory)]
    [ValidateSet('PackageMod', 'InstallLocal')]
    [string]$Target,

    [string]$GodotExe,
    [string]$SteamModDir,
    [string]$BuildRoot,
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$SourceRevision,
    [switch]$ReuseCache
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Native {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter(ValueFromRemainingArguments)][string[]]$Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed with exit code $LASTEXITCODE."
    }
}

function Resolve-RequiredDirectory([string]$Path, [string]$Description) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "$Description is required."
    }

    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $resolved -PathType Container)) {
        throw "$Description does not exist: $resolved"
    }
    return $resolved
}

function Remove-IsolatedDirectory([string]$Path, [string]$AllowedRoot) {
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $resolvedRoot = [IO.Path]::GetFullPath($AllowedRoot)
    $rootPrefix = $resolvedRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear a channel build directory outside $resolvedRoot`: $resolvedPath"
    }
    if (Test-Path -LiteralPath $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
}

function Add-MsBuildProperty(
    [Collections.Generic.List[string]]$Arguments,
    [string]$Name,
    [string]$Value) {
    if (-not [string]::IsNullOrWhiteSpace($Value)) {
        $Arguments.Add("-p:$Name=$Value")
    }
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$projectPath = Join-Path $repositoryRoot 'NinjaSlayer.csproj'
$compatibilityPath = Join-Path $repositoryRoot 'eng\compatibility.json'
. (Join-Path $repositoryRoot '.github\scripts\compatibility.ps1')

$compatibility = Read-NinjaSlayerCompatibility -Path $compatibilityPath
$profile = Get-NinjaSlayerCompatibilityChannel -Manifest $compatibility -Channel $Channel
$resolvedDataDir = Resolve-RequiredDirectory $Sts2DataDir "$Channel STS2 data directory"
$sts2Assembly = Join-Path $resolvedDataDir 'sts2.dll'
if (-not (Test-Path -LiteralPath $sts2Assembly -PathType Leaf)) {
    throw "$Channel STS2 data directory is missing sts2.dll: $resolvedDataDir"
}

$actualMvid = Get-NinjaSlayerGameModuleMvid -AssemblyPath $sts2Assembly
$expectedMvid = [string]$profile.hostContract.moduleMvid
if ($actualMvid -ne $expectedMvid) {
    throw "$Channel sts2.dll MVID is $actualMvid; compatibility.json requires $expectedMvid."
}

if ([string]::IsNullOrWhiteSpace($BuildRoot)) {
    $BuildRoot = Join-Path $repositoryRoot 'build\channel-build'
}
$resolvedBuildRoot = [IO.Path]::GetFullPath($BuildRoot)
$channelBuildRoot = Join-Path $resolvedBuildRoot $Channel
$intermediateDirectory = Join-Path $channelBuildRoot 'obj'
$outputDirectory = Join-Path $channelBuildRoot 'bin'
$packageDirectory = Join-Path $channelBuildRoot 'package\NinjaSlayer'

if (-not $ReuseCache) {
    Remove-IsolatedDirectory -Path $channelBuildRoot -AllowedRoot $resolvedBuildRoot
}
[IO.Directory]::CreateDirectory($intermediateDirectory) | Out-Null
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

$requiresInstallDirectory = $Target -eq 'InstallLocal'
if ($requiresInstallDirectory -and [string]::IsNullOrWhiteSpace($SteamModDir)) {
    throw 'InstallLocal requires SteamModDir.'
}

$commonArguments = [Collections.Generic.List[string]]::new()
$commonArguments.Add($projectPath)
$commonArguments.Add('-nologo')
$commonArguments.Add('-v:minimal')
Add-MsBuildProperty $commonArguments 'Configuration' 'Release'
Add-MsBuildProperty $commonArguments 'NinjaSlayerHostChannel' $Channel
Add-MsBuildProperty $commonArguments 'NinjaSlayerVersion' $Version
Add-MsBuildProperty $commonArguments 'Sts2DataDir' $resolvedDataDir
Add-MsBuildProperty $commonArguments 'BaseIntermediateOutputPath' ($intermediateDirectory + [IO.Path]::DirectorySeparatorChar)
Add-MsBuildProperty $commonArguments 'MSBuildProjectExtensionsPath' ($intermediateDirectory + [IO.Path]::DirectorySeparatorChar)
Add-MsBuildProperty $commonArguments 'BaseOutputPath' ($outputDirectory + [IO.Path]::DirectorySeparatorChar)
Add-MsBuildProperty $commonArguments 'NinjaSlayerIsolatedIntermediateRoot' ($intermediateDirectory + [IO.Path]::DirectorySeparatorChar)
Add-MsBuildProperty $commonArguments 'NinjaSlayerIsolatedOutputRoot' ($outputDirectory + [IO.Path]::DirectorySeparatorChar)
Add-MsBuildProperty $commonArguments 'PostBuildModDir' ($packageDirectory + [IO.Path]::DirectorySeparatorChar)
Add-MsBuildProperty $commonArguments 'GodotExe' $GodotExe
Add-MsBuildProperty $commonArguments 'SteamModDir' $SteamModDir
$normalizedSourceRevision = $SourceRevision.ToLowerInvariant()
Add-MsBuildProperty $commonArguments 'GitDescribe' $normalizedSourceRevision
Add-MsBuildProperty $commonArguments 'GitReleaseTags' "v$Version"
Add-MsBuildProperty $commonArguments 'RepositoryCommit' $normalizedSourceRevision

$restoreArguments = [Collections.Generic.List[string]]::new()
$restoreArguments.Add('restore')
foreach ($argument in $commonArguments) {
    $restoreArguments.Add($argument)
}
if (-not $ReuseCache) {
    $restoreArguments.Add('--force')
}
Invoke-Native -Command dotnet -Arguments $restoreArguments.ToArray()

$assetsPath = Join-Path $intermediateDirectory 'project.assets.json'
if (-not (Test-Path -LiteralPath $assetsPath -PathType Leaf)) {
    throw "Restore did not produce the isolated assets file: $assetsPath"
}
$assets = Get-Content -LiteralPath $assetsPath -Raw -Encoding utf8 | ConvertFrom-Json
$libraryNames = @($assets.libraries.PSObject.Properties.Name)
$expectedPackagePrefix = "$([string]$profile.ritsuLibPackageId)/"
$selectedPackages = @($libraryNames | Where-Object {
    $_.StartsWith($expectedPackagePrefix, [StringComparison]::OrdinalIgnoreCase)
})
if ($selectedPackages.Count -ne 1) {
    throw "$Channel restore resolved $($selectedPackages.Count) copies of $($profile.ritsuLibPackageId); expected exactly one."
}
$expectedPackageIdentity = "$([string]$profile.ritsuLibPackageId)/$([string]$compatibility.ritsuLibVersion)"
if (-not $selectedPackages[0].Equals(
        $expectedPackageIdentity,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "$Channel restore resolved $($selectedPackages[0]); expected $expectedPackageIdentity."
}
foreach ($otherChannel in @($compatibility.channels.PSObject.Properties.Name | Where-Object { $_ -ne $Channel })) {
    $otherPackage = [string]$compatibility.channels.$otherChannel.ritsuLibPackageId
    if ($otherPackage -eq [string]$profile.ritsuLibPackageId) {
        continue
    }
    $otherPrefix = "$otherPackage/"
    if ($libraryNames.Where({ $_.StartsWith($otherPrefix, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
        throw "$Channel restore leaked the $otherChannel RitsuLib package $otherPackage into project.assets.json."
    }
}

$buildArguments = [Collections.Generic.List[string]]::new()
$buildArguments.Add('msbuild')
foreach ($argument in $commonArguments) {
    $buildArguments.Add($argument)
}
$buildArguments.Add("-t:$Target")
Invoke-Native -Command dotnet -Arguments $buildArguments.ToArray()

Write-Output ([pscustomobject]@{
    Channel = $Channel
    Target = $Target
    GameApiVersion = [string]$profile.gameApiVersion
    RitsuLibPackageId = [string]$profile.ritsuLibPackageId
    SourceRevision = $normalizedSourceRevision
    AssetsPath = $assetsPath
    PackageDirectory = $packageDirectory
})
