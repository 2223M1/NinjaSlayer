#Requires -Version 7.0
#Requires -PSEdition Core

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string]$Version,

    [string]$ReleaseNoteFile = 'Workshop\change-note.md',
    [string]$WorkshopUploadRoot,
    [string]$StableDataDir,
    [string]$PreviewDataDir,
    [string]$GodotExe,
    [ValidatePattern('^$|^[0-9a-fA-F]{40}$')]
    [string]$SourceRevision,
    [switch]$SkipGitHub,
    [switch]$SkipWorkshop,
    [switch]$Confirm
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

function Get-NativeText {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter(ValueFromRemainingArguments)][string[]]$Arguments
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

if ($SkipGitHub) {
    if ([string]::IsNullOrWhiteSpace($SourceRevision)) {
        throw 'Workshop quick release requires -SourceRevision with the candidate full SHA.'
    }
    $workshopParameters = @{
        Version = $Version
        ReleaseNoteFile = $ReleaseNoteFile
        SourceRevision = $SourceRevision
        Confirm = $true
    }
    if (-not [string]::IsNullOrWhiteSpace($WorkshopUploadRoot)) {
        $workshopParameters.WorkshopUploadRoot = $WorkshopUploadRoot
    }
    if (-not [string]::IsNullOrWhiteSpace($StableDataDir)) {
        $workshopParameters.StableDataDir = $StableDataDir
    }
    if (-not [string]::IsNullOrWhiteSpace($PreviewDataDir)) {
        $workshopParameters.PreviewDataDir = $PreviewDataDir
    }
    if (-not [string]::IsNullOrWhiteSpace($GodotExe)) {
        $workshopParameters.GodotExe = $GodotExe
    }
    & (Join-Path $PSScriptRoot 'Publish-WorkshopQuickRelease.ps1') @workshopParameters
    return
}

if (-not $SkipWorkshop) {
    throw 'Protected dual-host GitHub Release and Workshop publication are separate operations. Pass -SkipWorkshop to dispatch Release, then run the protected Workshop workflow after Release succeeds.'
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Set-Location $repositoryRoot
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
else {
    Invoke-Native -Command git -Arguments @('tag', '-a', $tag, '-m', "NinjaSlayer $tag")
}

$remoteTag = Get-NativeText -Command git -Arguments @('ls-remote', '--tags', 'origin', "refs/tags/$tag^{}")
if ([string]::IsNullOrWhiteSpace($remoteTag)) {
    Invoke-Native -Command git -Arguments @('push', 'origin', $tag)
}

Invoke-Native -Command gh -Arguments @('auth', 'status')
Invoke-Native -Command gh -Arguments @(
    'workflow',
    'run',
    'release.yml',
    '--ref',
    'main',
    '-f',
    "release_tag=$tag"
)

Write-Host "Protected dual-host Release dispatched for $tag. Approve release-production and start one ephemeral Release runner."
