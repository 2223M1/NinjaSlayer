#Requires -Version 7.0
#Requires -PSEdition Core

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$StablePackageDirectory,
    [Parameter(Mandatory)][string]$PreviewPackageDirectory,
    [Parameter(Mandatory)][string]$StableSts2DataDir,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string]$Version,
    [string]$BuildRoot,
    [string]$CompatibilityManifestPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Native {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed with exit code $LASTEXITCODE."
    }
}

function Resolve-RequiredDirectory([string]$Path, [string]$Description) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $resolved -PathType Container)) {
        throw "$Description does not exist: $resolved"
    }
    return $resolved
}

function Assert-ChannelPackage([string]$Directory, [string]$Channel, $Profile, [string]$RitsuVersion) {
    $expected = @('NinjaSlayer.dll', 'NinjaSlayer.json', 'NinjaSlayer.pck', 'SHA256SUMS')
    $actualFiles = @(Get-ChildItem -LiteralPath $Directory -File -Force)
    $actualDirectories = @(Get-ChildItem -LiteralPath $Directory -Directory -Force)
    $difference = Compare-Object `
        -ReferenceObject @($expected | Sort-Object) `
        -DifferenceObject @($actualFiles.Name | Sort-Object) `
        -CaseSensitive
    if ($actualDirectories.Count -ne 0 -or $actualFiles.Count -ne $expected.Count -or $null -ne $difference) {
        throw "$Channel package must contain exactly the four channel artifacts."
    }

    $manifest = Get-Content -LiteralPath (Join-Path $Directory 'NinjaSlayer.json') -Raw -Encoding utf8 |
        ConvertFrom-Json
    if ([string]$manifest.version -cne $Version -or
        [string]$manifest.min_game_version -cne [string]$Profile.gameApiVersion) {
        throw "$Channel package manifest does not match version $Version / STS2 $($Profile.gameApiVersion)."
    }
    $ritsuDependencies = @($manifest.dependencies | Where-Object id -CEQ 'STS2-RitsuLib')
    if ($ritsuDependencies.Count -ne 1 -or
        [string]$ritsuDependencies[0].min_version -cne $RitsuVersion) {
        throw "$Channel package manifest has an invalid RitsuLib dependency."
    }

    $checksums = @{}
    foreach ($line in Get-Content -LiteralPath (Join-Path $Directory 'SHA256SUMS')) {
        if ($line -notmatch '^([0-9A-Fa-f]{64}) \*([^/\\]+)$' -or $checksums.ContainsKey($Matches[2])) {
            throw "Invalid $Channel SHA256SUMS entry: $line"
        }
        $checksums[$Matches[2]] = $Matches[1].ToLowerInvariant()
    }
    if ($checksums.Count -ne 3) {
        throw "$Channel SHA256SUMS must contain exactly three entries."
    }
    foreach ($name in $expected | Where-Object { $_ -ne 'SHA256SUMS' }) {
        $actual = (Get-FileHash -LiteralPath (Join-Path $Directory $name) -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($checksums[$name] -cne $actual) {
            throw "$Channel SHA256SUMS does not match $name."
        }
    }
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($CompatibilityManifestPath)) {
    $CompatibilityManifestPath = Join-Path $repositoryRoot 'eng\compatibility.json'
}
. (Join-Path $repositoryRoot '.github\scripts\compatibility.ps1')

$compatibility = Read-NinjaSlayerCompatibility -Path $CompatibilityManifestPath
$stableProfile = Get-NinjaSlayerCompatibilityChannel -Manifest $compatibility -Channel stable
$previewProfile = Get-NinjaSlayerCompatibilityChannel -Manifest $compatibility -Channel preview
$stablePackage = Resolve-RequiredDirectory $StablePackageDirectory 'Stable package directory'
$previewPackage = Resolve-RequiredDirectory $PreviewPackageDirectory 'Preview package directory'
$stableData = Resolve-RequiredDirectory $StableSts2DataDir 'Stable STS2 data directory'
$output = [IO.Path]::GetFullPath($OutputDirectory)
$outputRoot = [IO.Path]::GetPathRoot($output)
$outputParent = [IO.Directory]::GetParent($output)
if ([IO.Path]::GetFileName($output) -cne 'NinjaSlayer' -or
    [string]::IsNullOrWhiteSpace($outputRoot) -or
    $null -eq $outputParent -or
    $outputParent.FullName.TrimEnd([IO.Path]::DirectorySeparatorChar) -ceq
        $outputRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) -or
    $output -in @($repositoryRoot, $stablePackage, $previewPackage, $stableData)) {
    throw "Workshop bundle output must be a dedicated NinjaSlayer directory: $output"
}
if (Test-Path -LiteralPath $output) {
    $outputAttributes = [IO.File]::GetAttributes($output)
    if (($outputAttributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Workshop bundle output must not be a reparse point: $output"
    }
}

Assert-ChannelPackage $stablePackage stable $stableProfile ([string]$compatibility.ritsuLibVersion)
Assert-ChannelPackage $previewPackage preview $previewProfile ([string]$compatibility.ritsuLibVersion)
$stablePckHash = (Get-FileHash -LiteralPath (Join-Path $stablePackage 'NinjaSlayer.pck') -Algorithm SHA256).Hash
$previewPckHash = (Get-FileHash -LiteralPath (Join-Path $previewPackage 'NinjaSlayer.pck') -Algorithm SHA256).Hash
if ($stablePckHash -cne $previewPckHash) {
    throw 'Stable and preview PCK files differ; one universal resource payload cannot be assembled.'
}

$stableMvid = Get-NinjaSlayerGameModuleMvid -AssemblyPath (Join-Path $stableData 'sts2.dll')
if ($stableMvid -cne [string]$stableProfile.hostContract.moduleMvid) {
    throw "Stable loader references use host MVID $stableMvid, expected $($stableProfile.hostContract.moduleMvid)."
}
if ([string]::IsNullOrWhiteSpace($BuildRoot)) {
    $BuildRoot = Join-Path $repositoryRoot 'build\workshop-bundle'
}
$loaderOutput = Join-Path ([IO.Path]::GetFullPath($BuildRoot)) 'loader'
if (Test-Path -LiteralPath $loaderOutput) {
    $loaderAttributes = [IO.File]::GetAttributes($loaderOutput)
    if (($loaderAttributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Loader output must not be a reparse point: $loaderOutput"
    }
    Remove-Item -LiteralPath $loaderOutput -Recurse -Force
}
[IO.Directory]::CreateDirectory($loaderOutput) | Out-Null
Invoke-Native dotnet @(
    'build', (Join-Path $repositoryRoot 'tools\loader\NinjaSlayer.Loader.csproj'),
    '--configuration', 'Release',
    '--output', $loaderOutput,
    '-v:minimal',
    "-p:Sts2DataDir=$stableData",
    "-p:Version=$Version",
    "-p:AssemblyVersion=$Version.0",
    "-p:FileVersion=$Version.0",
    "-p:InformationalVersion=$Version"
)
$loaderAssembly = Join-Path $loaderOutput 'NinjaSlayer.Loader.dll'
if (-not (Test-Path -LiteralPath $loaderAssembly -PathType Leaf)) {
    throw "Loader build did not produce $loaderAssembly"
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}
[IO.Directory]::CreateDirectory($output) | Out-Null
Copy-Item -LiteralPath $loaderAssembly -Destination (Join-Path $output 'NinjaSlayer.dll')
Copy-Item -LiteralPath (Join-Path $stablePackage 'NinjaSlayer.json') -Destination $output
Copy-Item -LiteralPath (Join-Path $stablePackage 'NinjaSlayer.pck') -Destination $output

$variants = [Collections.Generic.List[object]]::new()
foreach ($channelName in @('stable', 'preview')) {
    $profile = $compatibility.channels.$channelName
    $source = if ($channelName -eq 'stable') { $stablePackage } else { $previewPackage }
    $variantDirectory = Join-Path $output "lib\$($profile.gameApiVersion)"
    [IO.Directory]::CreateDirectory($variantDirectory) | Out-Null
    $variantAssembly = Join-Path $variantDirectory 'NinjaSlayer.dll'
    Copy-Item -LiteralPath (Join-Path $source 'NinjaSlayer.dll') -Destination $variantAssembly
    [IO.File]::WriteAllText(
        (Join-Path $variantDirectory 'compat-target.txt'),
        "$($profile.gameApiVersion)`n",
        [Text.UTF8Encoding]::new($false))
    $variants.Add([ordered]@{
        channel = $channelName
        gameApiVersion = [string]$profile.gameApiVersion
        moduleMvid = ([Guid][string]$profile.hostContract.moduleMvid).ToString('D')
        directory = "lib/$($profile.gameApiVersion)"
        assembly = 'NinjaSlayer.dll'
        sha256 = (Get-FileHash -LiteralPath $variantAssembly -Algorithm SHA256).Hash.ToLowerInvariant()
    })
}
$variantManifest = [ordered]@{ schemaVersion = 1; variants = $variants.ToArray() }
[IO.File]::WriteAllText(
    (Join-Path $output 'ninjaslayer-variants.manifest'),
    (($variantManifest | ConvertTo-Json -Depth 6) + "`n"),
    [Text.UTF8Encoding]::new($false))

$checksumLines = Get-ChildItem -LiteralPath $output -File -Recurse -Force |
    Where-Object Name -CNE 'SHA256SUMS' |
    ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($output, $_.FullName).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash *$relative"
    } |
    Sort-Object
[IO.File]::WriteAllLines(
    (Join-Path $output 'SHA256SUMS'),
    $checksumLines,
    [Text.UTF8Encoding]::new($false))

Invoke-Native dotnet @(
    'run',
    '--project', (Join-Path $repositoryRoot 'tools\artifact-contract\NinjaSlayer.ArtifactContract.csproj'),
    '--configuration', 'Release',
    '--no-launch-profile',
    '--',
    'validate-workshop-bundle',
    '--directory', $output,
    '--compatibility', ([IO.Path]::GetFullPath($CompatibilityManifestPath)),
    '--version', $Version,
    '--ritsulib-version', [string]$compatibility.ritsuLibVersion,
    '--forbidden-path-root', $repositoryRoot
)

[pscustomobject]@{
    Version = $Version
    Directory = $output
    StableGameApiVersion = [string]$stableProfile.gameApiVersion
    PreviewGameApiVersion = [string]$previewProfile.gameApiVersion
}
