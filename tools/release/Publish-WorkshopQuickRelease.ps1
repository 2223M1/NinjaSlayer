#Requires -Version 7.0
#Requires -PSEdition Core

[CmdletBinding()]
param(
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string] $Version,

    [string] $ReleaseNoteFile = 'Workshop\change-note.md',
    [string] $WorkshopUploadRoot,
    [string] $Sts2DataDir,
    [string] $SteamModDir,
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

$stableDataDirectory = if (-not [string]::IsNullOrWhiteSpace($Sts2DataDir)) {
    [IO.Path]::GetFullPath($Sts2DataDir)
}
elseif (-not [string]::IsNullOrWhiteSpace($env:NINJASLAYER_STS2_STABLE_DATA_DIR)) {
    [IO.Path]::GetFullPath($env:NINJASLAYER_STS2_STABLE_DATA_DIR)
}
else {
    throw 'Stable Workshop publication requires -Sts2DataDir or NINJASLAYER_STS2_STABLE_DATA_DIR.'
}
$localInstallDirectory = if (-not [string]::IsNullOrWhiteSpace($SteamModDir)) {
    [IO.Path]::GetFullPath($SteamModDir)
}
else {
    Join-Path (Split-Path -Parent $stableDataDirectory) 'mods\NinjaSlayer'
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

$channelBuildParameters = @{
    Channel = 'stable'
    Version = $Version
    Sts2DataDir = $stableDataDirectory
    Target = 'InstallLocalAndStageWorkshop'
    SteamModDir = $localInstallDirectory
    WorkshopContentDir = $workshopContentDirectory
    BuildRoot = (Join-Path $repositoryRoot 'build\channel-build')
}
if (-not [string]::IsNullOrWhiteSpace($GodotExe)) {
    $channelBuildParameters.GodotExe = $GodotExe
}
& (Join-Path $PSScriptRoot 'Invoke-NinjaSlayerChannelBuild.ps1') @channelBuildParameters

$packageDirectory = Join-Path $repositoryRoot 'build\channel-build\stable\package\NinjaSlayer'
$requiredArtifacts = @('NinjaSlayer.dll', 'NinjaSlayer.json', 'NinjaSlayer.pck', 'SHA256SUMS')
foreach ($artifact in $requiredArtifacts) {
    if (-not (Test-Path -LiteralPath (Join-Path $packageDirectory $artifact) -PathType Leaf)) {
        throw "Package artifact is missing: $artifact"
    }
}

$manifest = Get-Content -LiteralPath (Join-Path $packageDirectory 'NinjaSlayer.json') -Raw -Encoding UTF8 |
    ConvertFrom-Json
if ($manifest.version -ne $Version) {
    throw "Package version $($manifest.version) does not match requested version $Version."
}

foreach ($line in Get-Content -LiteralPath (Join-Path $packageDirectory 'SHA256SUMS')) {
    if ($line -notmatch '^([0-9A-Fa-f]{64}) \*([^\\/]+)$') {
        throw "Invalid SHA256SUMS entry: $line"
    }
    $expected = $Matches[1].ToUpperInvariant()
    $artifactPath = Join-Path $packageDirectory $Matches[2]
    $actual = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash
    if ($actual -ne $expected) {
        throw "Package checksum mismatch for $($Matches[2])."
    }
}

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
