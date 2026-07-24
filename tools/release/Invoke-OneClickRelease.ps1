[CmdletBinding()]
param()

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

try {
    $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
    Set-Location $repositoryRoot

    Invoke-Native -Command gh -Arguments @('auth', 'status')
    Invoke-Native -Command git -Arguments @('fetch', 'origin', 'main', '--tags')

    $patchVersions = @(
        & git tag --list 'v0.1.*' |
            ForEach-Object {
                if ($_ -match '^v0\.1\.(0|[1-9][0-9]?)$') {
                    [int] $Matches[1]
                }
            }
    )
    $nextPatch = if ($patchVersions.Count -eq 0) {
        0
    }
    else {
        ($patchVersions | Measure-Object -Maximum).Maximum + 1
    }
    if ($nextPatch -gt 99) {
        throw 'The v0.1.x release series is exhausted.'
    }

    $version = "0.1.$nextPatch"
    $currentBranch = Get-NativeText -Command git -Arguments @('branch', '--show-current')
    if ($currentBranch -ne 'main') {
        throw "One-click release must start from main, not $currentBranch."
    }

    $head = Get-NativeText -Command git -Arguments @('rev-parse', 'HEAD')
    $originMain = Get-NativeText -Command git -Arguments @('rev-parse', 'origin/main')
    if ($head -ne $originMain) {
        throw 'Local main must match origin/main before one-click release can commit changes.'
    }

    $status = Get-NativeText -Command git -Arguments @('status', '--porcelain')
    if (-not [string]::IsNullOrWhiteSpace($status)) {
        $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $releaseBranch = "release/v$version-one-click-$timestamp"
        Write-Host "Committing current work on $releaseBranch..." -ForegroundColor Cyan

        Invoke-Native -Command git -Arguments @('switch', '-c', $releaseBranch)
        Invoke-Native -Command git -Arguments @('add', '--all')
        Invoke-Native -Command git -Arguments @('commit', '-m', "Prepare NinjaSlayer v$version")
        Invoke-Native -Command git -Arguments @('push', '-u', 'origin', $releaseBranch)

        $pullRequestUrl = Get-NativeText -Command gh -Arguments @(
            'pr', 'create',
            '--base', 'main',
            '--head', $releaseBranch,
            '--title', "Prepare NinjaSlayer v$version",
            '--body', "One-click test release for NinjaSlayer v$version."
        )
        Invoke-Native -Command gh -Arguments @('pr', 'merge', $pullRequestUrl, '--admin', '--merge', '--delete-branch')
        Invoke-Native -Command git -Arguments @('switch', 'main')
        Invoke-Native -Command git -Arguments @('pull', '--ff-only', 'origin', 'main')
    }

    $releaseNotePath = Join-Path $repositoryRoot 'Workshop\change-note.md'
    $releaseNote = (Get-Content -LiteralPath $releaseNotePath -Raw -Encoding UTF8).Trim()
    if ([string]::IsNullOrWhiteSpace($releaseNote)) {
        throw 'Workshop\change-note.md is empty. Add the release sentence before publishing.'
    }

    Write-Host ''
    Write-Host "Publishing NinjaSlayer v$version" -ForegroundColor Cyan
    Write-Host "Release note: $releaseNote"
    Write-Host ''

    & (Join-Path $PSScriptRoot 'Publish-QuickRelease.ps1') `
        -Version $version `
        -ReleaseNoteFile $releaseNotePath `
        -Confirm

    Write-Host ''
    Write-Host "NinjaSlayer v$version was published successfully." -ForegroundColor Green
}
catch {
    Write-Host ''
    Write-Host 'ONE-CLICK RELEASE FAILED' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ''
    Write-Host 'The window remains open so the error can be corrected.' -ForegroundColor Yellow
}
