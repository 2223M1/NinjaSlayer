[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$CandidateSha,
    [Parameter(Mandatory)][ValidatePattern('^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')][string]$Tag,
    [Parameter(Mandatory)][ValidatePattern('^[^/]+/[^/]+$')][string]$Repository,
    [Parameter(Mandatory)][string]$Token,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$CompatibilityManifestPath = (Join-Path $PSScriptRoot '..\..\eng\compatibility.json'),
    [string]$ApiBaseUri = 'https://api.github.com'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'compatibility.ps1')
. (Join-Path $PSScriptRoot 'actions-provenance.ps1')
. (Join-Path $PSScriptRoot 'release-artifact.ps1')

$candidate = $CandidateSha.ToLowerInvariant()
$compatibility = Read-NinjaSlayerCompatibility -Path $CompatibilityManifestPath
$compatibilitySha = Get-NinjaSlayerCompatibilitySha256 -Path $CompatibilityManifestPath
$archiveNames = @($compatibility.channels.PSObject.Properties.Name | ForEach-Object {
    $profile = $compatibility.channels.$_
    "NinjaSlayer-$Tag-$_-sts2-$($profile.gameApiVersion).zip"
})
$artifactName = "protected-release-$Tag-$candidate"
$context = New-NinjaSlayerGitHubApiContext -Repository $Repository -Token $Token -ApiBaseUri $ApiBaseUri
$artifacts = Get-NinjaSlayerArtifactCandidates -Context $context -ArtifactName $artifactName
if ($artifacts.Count -eq 0) {
    throw "No non-expired protected Release artifact exists for $Tag/$candidate."
}

$failures = [Collections.Generic.List[string]]::new()
foreach ($artifact in $artifacts) {
    $attemptDirectory = Join-Path $OutputDirectory ([string]$artifact.id)
    try {
        $run = Assert-NinjaSlayerArtifactProvenance `
            -Context $context `
            -Artifact $artifact `
            -CandidateSha $candidate `
            -WorkflowPath '.github/workflows/release.yml'
        [IO.Directory]::CreateDirectory($attemptDirectory) | Out-Null
        $artifactArchive = Join-Path $attemptDirectory 'actions-artifact.zip'
        Save-NinjaSlayerActionsArtifact -Context $context -Artifact $artifact -DestinationPath $artifactArchive
        $expanded = Join-Path $attemptDirectory 'expanded'
        Expand-NinjaSlayerExactZip `
            -ArchivePath $artifactArchive `
            -DestinationPath $expanded `
            -ExpectedFileNames @($archiveNames + 'release-attestation.json')
        $attestationPath = Join-Path $expanded 'release-attestation.json'
        $attestation = Get-Content -LiteralPath $attestationPath -Raw -Encoding utf8 | ConvertFrom-Json
        Assert-NinjaSlayerReleaseAttestation `
            -Attestation $attestation `
            -Compatibility $compatibility `
            -CompatibilityManifestSha256 $compatibilitySha `
            -Repository $Repository `
            -CandidateSha $candidate `
            -Tag $Tag `
            -WorkflowRunId ([string]$run.id) `
            -ArchiveDirectory $expanded

        $verified = Join-Path $OutputDirectory 'verified'
        if (Test-Path -LiteralPath $verified) {
            Remove-Item -LiteralPath $verified -Recurse -Force
        }
        [IO.Directory]::CreateDirectory($verified) | Out-Null
        foreach ($name in @($archiveNames + 'release-attestation.json')) {
            Copy-Item -LiteralPath (Join-Path $expanded $name) -Destination (Join-Path $verified $name)
        }
        Write-Output "Verified protected Release run $($run.id) for $Tag/$candidate."
        return
    }
    catch {
        $failures.Add("artifact $($artifact.id): $($_.Exception.Message)")
    }
}

throw "No matching Release artifact passed provenance validation. $($failures -join ' | ')"
