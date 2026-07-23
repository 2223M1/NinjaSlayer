[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^0\.1\.(0|[1-9][0-9]?)$')]
    [string] $Version,

    [string] $ReleaseNoteFile = 'Workshop\change-note.md',
    [string] $WorkshopUploadRoot,
    [switch] $SkipGitHub,
    [switch] $SkipWorkshop,
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

function Get-NativeText {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Command,
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]] $Arguments
    )

    $output = & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed with exit code $LASTEXITCODE."
    }

    return ($output | Out-String).Trim()
}

if (-not $Confirm) {
    throw 'Quick release is disabled until -Confirm is supplied.'
}
if ($SkipGitHub -and $SkipWorkshop) {
    throw 'SkipGitHub and SkipWorkshop cannot both be selected.'
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Set-Location $repositoryRoot

$releaseNotePath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $ReleaseNoteFile))
if (-not (Test-Path -LiteralPath $releaseNotePath -PathType Leaf)) {
    throw "Release note file is missing: $releaseNotePath"
}
$releaseNote = (Get-Content -LiteralPath $releaseNotePath -Raw -Encoding UTF8).Trim()
if ([string]::IsNullOrWhiteSpace($releaseNote)) {
    throw 'Release note must contain at least one sentence.'
}

Invoke-Native -Command git -Arguments @('fetch', 'origin', 'main', '--tags')
$branch = Get-NativeText -Command git -Arguments @('branch', '--show-current')
if ($branch -ne 'main') {
    throw "Quick release must run from main, not $branch."
}
if (-not [string]::IsNullOrWhiteSpace((Get-NativeText -Command git -Arguments @('status', '--porcelain')))) {
    throw 'Quick release requires a clean worktree.'
}

$head = Get-NativeText -Command git -Arguments @('rev-parse', 'HEAD')
$originMain = Get-NativeText -Command git -Arguments @('rev-parse', 'origin/main')
if ($head -ne $originMain) {
    throw 'Quick release requires HEAD to match origin/main exactly.'
}

$tag = "v$Version"
$existingTag = & git tag --list $tag
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to inspect local release tags.'
}
if ($existingTag) {
    $tagCommit = Get-NativeText -Command git -Arguments @('rev-list', '-n', '1', $tag)
    if ($tagCommit -ne $head) {
        throw "$tag already points to $tagCommit instead of HEAD $head."
    }
}

Write-Host "Building and installing NinjaSlayer $Version..."
Invoke-Native -Command dotnet -Arguments @('msbuild', '.\NinjaSlayer.csproj', '-t:InstallLocal', '-p:Configuration=Release', "-p:NinjaSlayerVersion=$Version", '-v:minimal')

$packageDirectory = Join-Path $repositoryRoot 'build\mods\NinjaSlayer'
$requiredArtifacts = @('NinjaSlayer.dll', 'NinjaSlayer.json', 'NinjaSlayer.pck', 'SHA256SUMS')
foreach ($artifact in $requiredArtifacts) {
    if (-not (Test-Path -LiteralPath (Join-Path $packageDirectory $artifact) -PathType Leaf)) {
        throw "Package artifact is missing: $artifact"
    }
}

$manifest = Get-Content -LiteralPath (Join-Path $packageDirectory 'NinjaSlayer.json') -Raw -Encoding UTF8 | ConvertFrom-Json
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

if (-not $existingTag) {
    Invoke-Native -Command git -Arguments @('tag', '-a', $tag, '-m', "NinjaSlayer $tag")
    Invoke-Native -Command git -Arguments @('push', 'origin', $tag)
}
else {
    $remoteTag = Get-NativeText -Command git -Arguments @('ls-remote', '--tags', 'origin', "refs/tags/$tag^{}")
    if ([string]::IsNullOrWhiteSpace($remoteTag)) {
        Invoke-Native -Command git -Arguments @('push', 'origin', $tag)
    }
}

$releaseDirectory = Join-Path $repositoryRoot 'build\releases'
[IO.Directory]::CreateDirectory($releaseDirectory) | Out-Null
$archivePath = Join-Path $releaseDirectory "NinjaSlayer-$tag.zip"
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory(
    $packageDirectory,
    $archivePath,
    [IO.Compression.CompressionLevel]::Optimal,
    $false)

if (-not $SkipGitHub) {
    Invoke-Native -Command gh -Arguments @('auth', 'status')
    & gh release view $tag *> $null
    if ($LASTEXITCODE -eq 0) {
        Invoke-Native -Command gh -Arguments @('release', 'edit', $tag, '--title', "NinjaSlayer $tag", '--notes-file', $releaseNotePath)
    }
    else {
        Invoke-Native -Command gh -Arguments @('release', 'create', $tag, '--verify-tag', '--draft', '--title', "NinjaSlayer $tag", '--notes-file', $releaseNotePath)
    }
    Invoke-Native -Command gh -Arguments @('release', 'upload', $tag, $archivePath, '--clobber')
    Invoke-Native -Command gh -Arguments @('release', 'edit', $tag, '--draft=false')
}

if (-not $SkipWorkshop) {
    if ([string]::IsNullOrWhiteSpace($WorkshopUploadRoot)) {
        $WorkshopUploadRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '..\上传mod'))
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

    [IO.Directory]::CreateDirectory($workshopDirectory) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'Workshop\workshop.json') -Destination (Join-Path $workshopDirectory 'workshop.json') -Force
    Invoke-Native -Command dotnet -Arguments @('msbuild', '.\NinjaSlayer.csproj', '-t:StageWorkshop', '-p:Configuration=Release', "-p:NinjaSlayerVersion=$Version", "-p:WorkshopUploadRoot=$WorkshopUploadRoot", "-p:WorkshopContentDir=$workshopContentDirectory", '-v:minimal')
    Push-Location $WorkshopUploadRoot
    try {
        Invoke-Native -Command $uploader -Arguments @('upload', '-w', 'NinjaSlayer')
    }
    finally {
        Pop-Location
    }
}

Write-Host "NinjaSlayer $tag quick release completed."
