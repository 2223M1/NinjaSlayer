[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$CandidateSha,
    [Parameter(Mandatory)][ValidatePattern('^[^/]+/[^/]+$')][string]$Repository,
    [Parameter(Mandatory)][string]$Token,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('FirstCombatRestart', 'FullAutoSlay')][string]$ExpectedMode = 'FirstCombatRestart',
    [string]$ApiBaseUri = 'https://api.github.com'
)

$ErrorActionPreference = 'Stop'
$candidate = $CandidateSha.ToLowerInvariant()
$headers = @{
    Authorization = "Bearer $Token"
    Accept = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
}

function Invoke-GitHubApi([string]$Uri) { Invoke-RestMethod -Uri $Uri -Headers $headers }
function Assert-Equal($Actual, $Expected, [string]$Field) {
    if ($Actual -ne $Expected) { throw "$Field mismatch: expected '$Expected', received '$Actual'." }
}

$repositoryInfo = Invoke-GitHubApi "$ApiBaseUri/repos/$Repository"
$repositoryId = [long]$repositoryInfo.id
$artifactName = "game-smoke-$ExpectedMode-$candidate"
$expectedAttestationMode = if ($ExpectedMode -eq 'FullAutoSlay') {
    'singleplayer-full-autoslay'
} else {
    'singleplayer-first-combat-restart'
}
$listing = Invoke-GitHubApi "$ApiBaseUri/repos/$Repository/actions/artifacts?name=$([Uri]::EscapeDataString($artifactName))&per_page=100"
$artifacts = @($listing.artifacts | Where-Object { -not $_.expired -and $_.name -eq $artifactName } | Sort-Object created_at -Descending)
if ($artifacts.Count -eq 0) { throw "No non-expired protected smoke attestation exists for $candidate." }

$failures = [Collections.Generic.List[string]]::new()
foreach ($artifact in $artifacts) {
    $attemptDirectory = Join-Path $OutputDirectory ([string]$artifact.id)
    try {
        $run = Invoke-GitHubApi "$ApiBaseUri/repos/$Repository/actions/runs/$($artifact.workflow_run.id)"
        Assert-Equal ([long]$run.repository.id) $repositoryId 'run.repository.id'
        Assert-Equal ([long]$run.head_repository.id) $repositoryId 'run.head_repository.id'
        Assert-Equal ([string]$run.path) '.github/workflows/smoke.yml' 'run.path'
        Assert-Equal ([string]$run.event) 'workflow_dispatch' 'run.event'
        Assert-Equal ([string]$run.status) 'completed' 'run.status'
        Assert-Equal ([string]$run.conclusion) 'success' 'run.conclusion'

        New-Item -ItemType Directory -Path $attemptDirectory -Force | Out-Null
        $archive = Join-Path $attemptDirectory 'attestation.zip'
        Invoke-WebRequest -Uri $artifact.archive_download_url -Headers $headers -OutFile $archive
        Expand-Archive -LiteralPath $archive -DestinationPath $attemptDirectory -Force
        $path = Join-Path $attemptDirectory 'attestation.json'
        $attestation = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $expected = @('candidateSha', 'completedAtUtc', 'gameAssemblyVersion', 'mode', 'repository', 'result', 'ritsuLibVersion', 'runId', 'schemaVersion') | Sort-Object
        if (Compare-Object @($attestation.PSObject.Properties.Name | Sort-Object) $expected) {
            throw 'Smoke attestation contains missing or unexpected fields.'
        }
        Assert-Equal ([int]$attestation.schemaVersion) 1 'attestation.schemaVersion'
        Assert-Equal ([string]$attestation.candidateSha).ToLowerInvariant() $candidate 'attestation.candidateSha'
        Assert-Equal ([string]$attestation.repository) $Repository 'attestation.repository'
        Assert-Equal ([string]$attestation.runId) ([string]$run.id) 'attestation.runId'
        Assert-Equal ([string]$attestation.result) 'passed' 'attestation.result'
        Assert-Equal ([string]$attestation.mode) $expectedAttestationMode 'attestation.mode'
        Assert-Equal ([string]$attestation.ritsuLibVersion) '0.4.62' 'attestation.ritsuLibVersion'

        $verified = Join-Path $OutputDirectory 'verified'
        New-Item -ItemType Directory -Path $verified -Force | Out-Null
        Copy-Item -LiteralPath $path -Destination (Join-Path $verified 'attestation.json') -Force
        Write-Output "Verified protected smoke run $($run.id) for $candidate."
        return
    }
    catch { $failures.Add("artifact $($artifact.id): $($_.Exception.Message)") }
}

throw "No smoke artifact passed provenance validation. $($failures -join ' | ')"
