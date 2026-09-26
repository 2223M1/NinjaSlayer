#Requires -Version 7.0
#Requires -PSEdition Core

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishedEvidence,
    [Parameter(Mandatory)][string]$CandidateRoot,
    [Parameter(Mandatory)][string]$BundleDirectory,
    [Parameter(Mandatory)][string]$GameRootDirectory,
    [Parameter(Mandatory)][string]$RitsuLibModDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$evidencePath = (Resolve-Path -LiteralPath $PublishedEvidence).Path
$evidence = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json
if ($evidence.workshop.itemId -cne '3776911445' -or
    $evidence.workshop.remotePackageVerified -ne $true -or
    $evidence.workshop.remoteChangeNoteVerified -ne $true) {
    throw 'Verify the published Workshop package and change note before syncing its website catalog.'
}

& (Join-Path $repositoryRoot 'tools\smoke-harness\Invoke-NinjaSlayerSmoke.ps1') `
    -CandidateSha $evidence.sourceRevision -BundleVersion $evidence.version `
    -CandidateRoot $CandidateRoot -BundleDirectory $BundleDirectory -TrustedRoot $repositoryRoot `
    -GameRootDirectory $GameRootDirectory -RitsuLibModDirectory $RitsuLibModDirectory `
    -OutputDirectory $OutputDirectory -Channel stable -Mode Catalog -PhaseTimeoutSeconds 600

Push-Location -LiteralPath $repositoryRoot
try {
    & node tools/release/import-website-catalog.mjs (Join-Path $OutputDirectory 'content') $evidencePath
    if ($LASTEXITCODE -ne 0) { throw 'Importing the verified website catalog failed.' }
}
finally { Pop-Location }
Write-Host "Website/content now matches Workshop $($evidence.version). Commit these files to deploy Pages."
