#Requires -Version 7.0
#Requires -PSEdition Core

[CmdletBinding()]
param(
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string] $Version,

    [string] $ReleaseNoteFile = 'Workshop\change-note.md',
    [string] $WorkshopUploadRoot,
    [string] $StableDataDir,
    [string] $PreviewDataDir,
    [string] $GodotExe,
    [switch] $Confirm
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Command,
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]] $Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed with exit code $LASTEXITCODE."
    }
}

function Get-NextWorkshopVersion([string] $releaseDirectory) {
    $versions = [Collections.Generic.List[version]]::new()
    $tags = & git tag --list 'v*'
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect local release tags.'
    }

    foreach ($tag in $tags) {
        if ($tag -match '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
            $versions.Add([version]::new(
                [int] $Matches[1],
                [int] $Matches[2],
                [int] $Matches[3]))
        }
    }

    if (Test-Path -LiteralPath $releaseDirectory -PathType Container) {
        foreach ($marker in Get-ChildItem -LiteralPath $releaseDirectory -File) {
            if ($marker.Name -match '^workshop-v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.json$') {
                $versions.Add([version]::new(
                    [int] $Matches[1],
                    [int] $Matches[2],
                    [int] $Matches[3]))
            }
        }
    }

    if ($versions.Count -eq 0) {
        return '0.1.0'
    }

    [version] $latest = $versions | Sort-Object -Descending | Select-Object -First 1
    return "$($latest.Major).$($latest.Minor).$($latest.Build + 1)"
}

if (-not $Confirm) {
    throw 'Workshop quick release is disabled until -Confirm is supplied.'
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Set-Location $repositoryRoot
$compatibility = Get-Content -LiteralPath (Join-Path $repositoryRoot 'eng\compatibility.json') `
    -Raw -Encoding utf8 | ConvertFrom-Json
$releaseDirectory = Join-Path $repositoryRoot 'build\releases'
[IO.Directory]::CreateDirectory($releaseDirectory) | Out-Null

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-NextWorkshopVersion $releaseDirectory
}
$tag = "v$Version"

$releaseNotePath = if ([IO.Path]::IsPathRooted($ReleaseNoteFile)) {
    [IO.Path]::GetFullPath($ReleaseNoteFile)
}
else {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $ReleaseNoteFile))
}
if (-not (Test-Path -LiteralPath $releaseNotePath -PathType Leaf)) {
    throw "Release note file is missing: $releaseNotePath"
}
$releaseNote = (Get-Content -LiteralPath $releaseNotePath -Raw -Encoding UTF8).Trim()
if ([string]::IsNullOrWhiteSpace($releaseNote)) {
    throw 'Release note must contain at least one sentence.'
}

if ([string]::IsNullOrWhiteSpace($WorkshopUploadRoot)) {
    $workspaceRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '..'))
    $uploadRoots = @(Get-ChildItem -LiteralPath $workspaceRoot -Directory | Where-Object {
        Test-Path -LiteralPath (Join-Path $_.FullName 'ModUploader.exe') -PathType Leaf
    })
    if ($uploadRoots.Count -ne 1) {
        throw 'Unable to identify one Workshop upload directory. Pass -WorkshopUploadRoot explicitly.'
    }
    $WorkshopUploadRoot = $uploadRoots[0].FullName
}
else {
    $WorkshopUploadRoot = [IO.Path]::GetFullPath($WorkshopUploadRoot)
}

$workshopDirectory = Join-Path $WorkshopUploadRoot 'NinjaSlayer'
$workshopContentDirectory = Join-Path $workshopDirectory 'content'
$uploader = Join-Path $WorkshopUploadRoot 'ModUploader.exe'
if (-not (Test-Path -LiteralPath $uploader -PathType Leaf)) {
    throw "Workshop uploader is missing: $uploader"
}

$stableDataDirectory = if (-not [string]::IsNullOrWhiteSpace($StableDataDir)) {
    [IO.Path]::GetFullPath($StableDataDir)
}
elseif (-not [string]::IsNullOrWhiteSpace($env:NINJASLAYER_STS2_STABLE_DATA_DIR)) {
    [IO.Path]::GetFullPath($env:NINJASLAYER_STS2_STABLE_DATA_DIR)
}
else {
    throw 'Workshop publication requires -StableDataDir or NINJASLAYER_STS2_STABLE_DATA_DIR.'
}
$previewDataDirectory = if (-not [string]::IsNullOrWhiteSpace($PreviewDataDir)) {
    [IO.Path]::GetFullPath($PreviewDataDir)
}
elseif (-not [string]::IsNullOrWhiteSpace($env:NINJASLAYER_STS2_PREVIEW_DATA_DIR)) {
    [IO.Path]::GetFullPath($env:NINJASLAYER_STS2_PREVIEW_DATA_DIR)
}
else {
    throw 'Workshop publication requires -PreviewDataDir or NINJASLAYER_STS2_PREVIEW_DATA_DIR.'
}
Write-Host ''
Write-Host "Publishing NinjaSlayer $tag to Steam Workshop only" -ForegroundColor Cyan
Write-Host "Release note: $releaseNote"
Write-Host 'GitHub commits, tags, pushes, pull requests, and Releases are disabled for this path.'
Write-Host ''

[IO.Directory]::CreateDirectory($workshopDirectory) | Out-Null
$workshopMetadata = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Workshop\workshop.json') `
    -Raw -Encoding UTF8 | ConvertFrom-Json
$workshopMetadata.changeNote = $releaseNote
$pendingMetadataPath = Join-Path $releaseDirectory "workshop-pending-$tag.json"
$completedMetadataPath = Join-Path $releaseDirectory "workshop-$tag.json"
$workshopMetadataJson = $workshopMetadata | ConvertTo-Json -Depth 10
[IO.File]::WriteAllText(
    $pendingMetadataPath,
    $workshopMetadataJson,
    [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath $pendingMetadataPath -Destination (Join-Path $workshopDirectory 'workshop.json') -Force

$buildRoot = Join-Path $repositoryRoot 'build\channel-build'
foreach ($channel in @('stable', 'preview')) {
    $channelBuildParameters = @{
        Channel = $channel
        Version = $Version
        Sts2DataDir = if ($channel -eq 'stable') { $stableDataDirectory } else { $previewDataDirectory }
        Target = 'PackageMod'
        BuildRoot = $buildRoot
    }
    if (-not [string]::IsNullOrWhiteSpace($GodotExe)) {
        $channelBuildParameters.GodotExe = $GodotExe
    }
    & (Join-Path $PSScriptRoot 'Invoke-NinjaSlayerChannelBuild.ps1') @channelBuildParameters
}

$bundleDirectory = Join-Path $repositoryRoot 'build\workshop-bundle\NinjaSlayer'
& (Join-Path $PSScriptRoot 'New-NinjaSlayerWorkshopBundle.ps1') `
    -StablePackageDirectory (Join-Path $buildRoot 'stable\package\NinjaSlayer') `
    -PreviewPackageDirectory (Join-Path $buildRoot 'preview\package\NinjaSlayer') `
    -StableSts2DataDir $stableDataDirectory `
    -OutputDirectory $bundleDirectory `
    -BuildRoot (Join-Path $repositoryRoot 'build\workshop-bundle\build') `
    -Version $Version

if (Test-Path -LiteralPath $workshopContentDirectory) {
    Remove-Item -LiteralPath $workshopContentDirectory -Recurse -Force
}
Copy-Item -LiteralPath $bundleDirectory -Destination $workshopContentDirectory -Recurse
Invoke-Native -Command dotnet -Arguments @(
    'run',
    '--project', (Join-Path $repositoryRoot 'tools\artifact-contract\NinjaSlayer.ArtifactContract.csproj'),
    '--configuration', 'Release',
    '--no-launch-profile',
    '--',
    'validate-workshop-bundle',
    '--directory', $workshopContentDirectory,
    '--compatibility', (Join-Path $repositoryRoot 'eng\compatibility.json'),
    '--version', $Version,
    '--ritsulib-version', [string]$compatibility.ritsuLibVersion,
    '--forbidden-path-root', $repositoryRoot
)

Push-Location $WorkshopUploadRoot
try {
    Invoke-Native -Command $uploader -Arguments @('upload', '-w', 'NinjaSlayer')
}
finally {
    Pop-Location
}

Copy-Item -LiteralPath $pendingMetadataPath -Destination $completedMetadataPath -Force
[IO.File]::Delete($pendingMetadataPath)
Write-Host ''
Write-Host "NinjaSlayer $tag was uploaded to Steam Workshop. No GitHub operation was performed." `
    -ForegroundColor Green
