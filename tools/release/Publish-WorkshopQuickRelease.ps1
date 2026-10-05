#Requires -Version 7.0
#Requires -PSEdition Core

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string] $SourceRevision,

    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string] $Version,

    [string] $WorkshopUploadRoot,
    [string] $StableDataDir,
    [string] $PreviewDataDir,
    [string] $GodotExe,
    [Parameter(Mandatory)][string] $GameRootDirectory,
    [Parameter(Mandatory)][string] $RitsuLibModDirectory,
    [ValidateSet('stable', 'preview')][string] $ValidationChannel = 'stable',
    [ValidateSet('Release113', 'Release114', 'Release100')][string[]] $PreUploadValidationModes = @(),
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
    # This catalog only advances after verifying the downloaded Workshop package.
    $published = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../../Website/content/current.json') -Raw | ConvertFrom-Json
    $versions.Add([version]$published.version)
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
        return '1.0.0'
    }

    [version] $latest = $versions | Sort-Object -Descending | Select-Object -First 1
    if ($latest -lt [version]'1.0.0') { return '1.0.0' }
    return "$($latest.Major).$($latest.Minor).$($latest.Build + 1)"
}

if (-not $Confirm) {
    throw 'Workshop quick release is disabled until -Confirm is supplied.'
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Set-Location $repositoryRoot
$worktreeStatus = & git status --porcelain
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to inspect the candidate worktree.'
}
if ($worktreeStatus) {
    throw 'Workshop quick release requires a clean candidate worktree.'
}
$head = (& git rev-parse HEAD | Out-String).Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to resolve the candidate HEAD.'
}
$SourceRevision = $SourceRevision.ToLowerInvariant()
if ($SourceRevision -ne $head) {
    throw "SourceRevision $SourceRevision does not match candidate HEAD $head."
}
$compatibility = Get-Content -LiteralPath (Join-Path $repositoryRoot 'eng\compatibility.json') `
    -Raw -Encoding utf8 | ConvertFrom-Json
$releaseDirectory = Join-Path $repositoryRoot 'build\releases'
[IO.Directory]::CreateDirectory($releaseDirectory) | Out-Null

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-NextWorkshopVersion $releaseDirectory
}
$tag = "v$Version"

$releaseNote = '我们修复了一些问题，增添了一些内容，调整了一些东西。'

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
        SourceRevision = $SourceRevision
    }
    if (-not [string]::IsNullOrWhiteSpace($GodotExe)) {
        $channelBuildParameters.GodotExe = $GodotExe
    }
    if ($channel -eq 'preview') {
        $channelBuildParameters.SharedResourcePack = Join-Path $buildRoot 'stable\package\NinjaSlayer\NinjaSlayer.pck'
    }
    & (Join-Path $PSScriptRoot 'Invoke-NinjaSlayerChannelBuild.ps1') @channelBuildParameters
}

$bundleDirectory = Join-Path $repositoryRoot 'build\workshop-bundle\NinjaSlayer'
& (Join-Path $PSScriptRoot 'New-NinjaSlayerWorkshopBundle.ps1') `
    -StablePackageDirectory (Join-Path $buildRoot 'stable\package\NinjaSlayer') `
    -PreviewPackageDirectory (Join-Path $buildRoot 'preview\package\NinjaSlayer') `
    -StableSts2DataDir $stableDataDirectory `
    -PreviewSts2DataDir $previewDataDirectory `
    -OutputDirectory $bundleDirectory `
    -BuildRoot (Join-Path $repositoryRoot 'build\workshop-bundle\build') `
    -Version $Version `
    -SourceRevision $SourceRevision

foreach ($validationMode in $PreUploadValidationModes) {
    & (Join-Path $repositoryRoot 'tools\smoke-harness\Invoke-NinjaSlayerSmoke.ps1') `
        -CandidateSha $SourceRevision -BundleVersion $Version -CandidateRoot $repositoryRoot `
        -BundleDirectory $bundleDirectory -TrustedRoot $repositoryRoot `
        -GameRootDirectory $GameRootDirectory -RitsuLibModDirectory $RitsuLibModDirectory `
        -OutputDirectory (Join-Path $releaseDirectory "verify-$tag-$($SourceRevision.Substring(0, 12))-$validationMode") `
        -Channel $ValidationChannel -Mode $validationMode -Seed '9NWJ1TS9WC2V' `
        -PhaseTimeoutSeconds 600 -NoScreenshots -BackgroundDesktop
}
$catalogDirectory = Join-Path $releaseDirectory "website-$tag-$($SourceRevision.Substring(0, 12))"
& (Join-Path $repositoryRoot 'tools\smoke-harness\Invoke-NinjaSlayerSmoke.ps1') `
    -CandidateSha $SourceRevision -BundleVersion $Version -CandidateRoot $repositoryRoot `
    -BundleDirectory $bundleDirectory -TrustedRoot $repositoryRoot `
    -GameRootDirectory $GameRootDirectory -RitsuLibModDirectory $RitsuLibModDirectory `
    -OutputDirectory $catalogDirectory -Channel $ValidationChannel -Mode Catalog -PhaseTimeoutSeconds 600
$catalog = Get-Content -LiteralPath (Join-Path $catalogDirectory 'content\catalog.json') -Raw | ConvertFrom-Json
if ($catalog.version -cne $Version -or $catalog.sourceRevision -cne $SourceRevision) {
    throw 'Runtime website catalog does not match the candidate.'
}
# Import and advance Website/content/current.json only after remote package verification,
# using import-website-catalog.mjs with the release evidence. Upload success alone is insufficient.

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
    '--source-revision', $SourceRevision,
    '--forbidden-path-root', $repositoryRoot
)

Push-Location $WorkshopUploadRoot
try {
    Invoke-Native -Command $uploader -Arguments @('upload', '-w', 'NinjaSlayer', '-i', [string]$compatibility.workshop.itemId)
}
finally {
    Pop-Location
}

Invoke-Native -Command dotnet -Arguments @(
    'run', '--project', (Join-Path $repositoryRoot 'tools/workshop-metadata/WorkshopMetadata.csproj'),
    '--configuration', 'Release', "-p:Sts2DataDir=$(Join-Path $GameRootDirectory 'data_sts2_windows_x86_64')", '--',
    'apply', $pendingMetadataPath, (Join-Path $repositoryRoot 'eng/compatibility.json')
)

Copy-Item -LiteralPath $pendingMetadataPath -Destination $completedMetadataPath -Force
[IO.File]::Delete($pendingMetadataPath)
Write-Host ''
Write-Host "NinjaSlayer $tag was uploaded to Steam Workshop. No GitHub operation was performed." `
    -ForegroundColor Green
