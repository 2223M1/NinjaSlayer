#Requires -Version 7.0
#Requires -PSEdition Core

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][string]$CandidateSha,
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][string]$WorkflowRunId,
    [Parameter(Mandatory)][string]$StableArchivePath,
    [Parameter(Mandatory)][string]$PreviewArchivePath,
    [Parameter(Mandatory)][string]$WorkshopArchivePath,
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$CompatibilityManifestPath = (Join-Path $PSScriptRoot '..\..\eng\compatibility.json')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'compatibility.ps1')
. (Join-Path $PSScriptRoot 'release-artifact.ps1')

$compatibility = Read-NinjaSlayerCompatibility -Path $CompatibilityManifestPath
$attestation = New-NinjaSlayerReleaseAttestation `
    -Compatibility $compatibility `
    -CompatibilityManifestSha256 (Get-NinjaSlayerCompatibilitySha256 -Path $CompatibilityManifestPath) `
    -Repository $Repository `
    -CandidateSha $CandidateSha `
    -Tag $Tag `
    -WorkflowRunId $WorkflowRunId `
    -ArchivesByChannel @{
        stable = $StableArchivePath
        preview = $PreviewArchivePath
    } `
    -WorkshopArchivePath $WorkshopArchivePath

$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedOutput)) | Out-Null
[IO.File]::WriteAllText(
    $resolvedOutput,
    ($attestation | ConvertTo-Json -Depth 10),
    [Text.UTF8Encoding]::new($false))
