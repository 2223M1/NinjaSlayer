[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$CandidateSha,
    [Parameter(Mandatory)][ValidatePattern('^[^/]+/[^/]+$')][string]$Repository,
    [Parameter(Mandatory)][string]$Token,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('FirstCombatRestart', 'FullAutoSlay')][string]$ExpectedMode = 'FirstCombatRestart',
    [string]$CompatibilityManifestPath = (Join-Path $PSScriptRoot '..\..\eng\compatibility.json'),
    [string]$ApiBaseUri = 'https://api.github.com'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'compatibility.ps1')
. (Join-Path $PSScriptRoot 'actions-provenance.ps1')

$candidate = $CandidateSha.ToLowerInvariant()
$compatibility = Read-NinjaSlayerCompatibility -Path $CompatibilityManifestPath
$compatibilitySha = Get-NinjaSlayerCompatibilitySha256 -Path $CompatibilityManifestPath
$channelNames = @($compatibility.channels.PSObject.Properties.Name)
$expectedAttestationMode = if ($ExpectedMode -eq 'FullAutoSlay') {
    'singleplayer-full-autoslay'
} else {
    'singleplayer-first-combat-restart'
}
$context = New-NinjaSlayerGitHubApiContext -Repository $Repository -Token $Token -ApiBaseUri $ApiBaseUri
$artifactName = "game-smoke-$ExpectedMode-$candidate"
$artifacts = Get-NinjaSlayerArtifactCandidates -Context $context -ArtifactName $artifactName
if ($artifacts.Count -eq 0) {
    throw "No non-expired protected smoke attestation exists for $candidate."
}

$expectedProperties = @(
    'candidateSha', 'channel', 'compatibilityManifestSha256', 'completedAtUtc',
    'gameApiVersion', 'gameAssemblyVersion', 'gameModuleMvid', 'mode', 'repository',
    'result', 'ritsuLibPackageId', 'ritsuLibVersion', 'schemaVersion', 'workflowRunId'
) | Sort-Object
$failures = [Collections.Generic.List[string]]::new()
foreach ($artifact in $artifacts) {
    $attemptDirectory = Join-Path $OutputDirectory ([string]$artifact.id)
    try {
        $run = Assert-NinjaSlayerArtifactProvenance `
            -Context $context `
            -Artifact $artifact `
            -CandidateSha $candidate `
            -WorkflowPath '.github/workflows/smoke.yml'

        New-Item -ItemType Directory -Path $attemptDirectory -Force | Out-Null
        $archive = Join-Path $attemptDirectory 'attestation.zip'
        Save-NinjaSlayerActionsArtifact -Context $context -Artifact $artifact -DestinationPath $archive
        Expand-Archive -LiteralPath $archive -DestinationPath $attemptDirectory -Force
        $attestationFiles = @(Get-ChildItem -LiteralPath $attemptDirectory -Recurse -File -Filter 'attestation.json')
        if ($attestationFiles.Count -ne $channelNames.Count) {
            throw "Artifact must contain exactly $($channelNames.Count) smoke attestations."
        }

        $verifiedDirectory = Join-Path $OutputDirectory 'verified'
        New-Item -ItemType Directory -Path $verifiedDirectory -Force | Out-Null
        foreach ($channelName in $channelNames) {
            $channelProfile = Get-NinjaSlayerCompatibilityChannel -Manifest $compatibility -Channel $channelName
            $path = Join-Path $attemptDirectory "$channelName\attestation.json"
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "Artifact does not contain the $channelName smoke attestation."
            }
            $attestation = Get-Content -LiteralPath $path -Raw -Encoding utf8 | ConvertFrom-Json
            if (Compare-Object @($attestation.PSObject.Properties.Name | Sort-Object) $expectedProperties) {
                throw "$channelName smoke attestation contains missing or unexpected fields."
            }
            Assert-NinjaSlayerEqual ([int]$attestation.schemaVersion) 3 "$channelName.schemaVersion"
            Assert-NinjaSlayerEqual ([string]$attestation.candidateSha).ToLowerInvariant() $candidate "$channelName.candidateSha"
            Assert-NinjaSlayerEqual ([string]$attestation.repository) $Repository "$channelName.repository"
            Assert-NinjaSlayerEqual ([string]$attestation.workflowRunId) ([string]$run.id) "$channelName.workflowRunId"
            Assert-NinjaSlayerEqual ([string]$attestation.result) 'passed' "$channelName.result"
            Assert-NinjaSlayerEqual ([string]$attestation.mode) $expectedAttestationMode "$channelName.mode"
            Assert-NinjaSlayerEqual ([string]$attestation.channel) $channelName "$channelName.channel"
            Assert-NinjaSlayerEqual ([string]$attestation.gameApiVersion) ([string]$channelProfile.gameApiVersion) "$channelName.gameApiVersion"
            Assert-NinjaSlayerEqual ([string]$attestation.gameAssemblyVersion) ([string]$channelProfile.hostContract.assemblyVersion) "$channelName.gameAssemblyVersion"
            Assert-NinjaSlayerEqual ([string]$attestation.gameModuleMvid).ToLowerInvariant() ([string]$channelProfile.hostContract.moduleMvid).ToLowerInvariant() "$channelName.gameModuleMvid"
            Assert-NinjaSlayerEqual ([string]$attestation.ritsuLibPackageId) ([string]$channelProfile.ritsuLibPackageId) "$channelName.ritsuLibPackageId"
            Assert-NinjaSlayerEqual ([string]$attestation.ritsuLibVersion) ([string]$compatibility.ritsuLibVersion) "$channelName.ritsuLibVersion"
            Assert-NinjaSlayerEqual ([string]$attestation.compatibilityManifestSha256).ToLowerInvariant() $compatibilitySha "$channelName.compatibilityManifestSha256"
            if ([string]::IsNullOrWhiteSpace([string]$attestation.completedAtUtc)) {
                throw "$channelName.completedAtUtc must not be empty."
            }

            $verifiedHostDirectory = Join-Path $verifiedDirectory $channelName
            New-Item -ItemType Directory -Path $verifiedHostDirectory -Force | Out-Null
            Copy-Item -LiteralPath $path -Destination (Join-Path $verifiedHostDirectory 'attestation.json') -Force
        }
        Write-Output "Verified protected smoke run $($run.id) for $candidate on stable and preview."
        return
    }
    catch {
        $failures.Add("artifact $($artifact.id): $($_.Exception.Message)")
    }
}

throw "No smoke artifact passed provenance validation. $($failures -join ' | ')"
