[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string] $CandidateSha,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[^/]+/[^/]+$')]
    [string] $Repository,

    [Parameter(Mandatory = $true)]
    [string] $Token,

    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory,

    [string] $ExpectedRitsuLibVersion = '0.4.62',
    [string] $ApiBaseUri = 'https://api.github.com'
)

$ErrorActionPreference = 'Stop'
$candidate = $CandidateSha.ToLowerInvariant()
$headers = @{
    Authorization = "Bearer $Token"
    Accept = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
}

function Invoke-GitHubApi {
    param([Parameter(Mandatory = $true)][string] $Uri)

    Invoke-RestMethod -Uri $Uri -Headers $headers
}

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)] $Actual,
        [Parameter(Mandatory = $true)] $Expected,
        [Parameter(Mandatory = $true)][string] $Field
    )

    if ($Actual -ne $Expected) {
        throw "$Field mismatch: expected '$Expected', received '$Actual'."
    }
}

$repositoryInfo = Invoke-GitHubApi "$ApiBaseUri/repos/$Repository"
$repositoryId = [long]$repositoryInfo.id
$artifactName = "private-contract-$candidate"
$encodedName = [Uri]::EscapeDataString($artifactName)
$listing = Invoke-GitHubApi "$ApiBaseUri/repos/$Repository/actions/artifacts?name=$encodedName&per_page=100"
$artifacts = @($listing.artifacts |
    Where-Object { -not $_.expired -and $_.name -eq $artifactName } |
    Sort-Object created_at -Descending)

if ($artifacts.Count -eq 0) {
    throw "No non-expired protected Contract attestation exists for $candidate."
}

$failures = [Collections.Generic.List[string]]::new()
foreach ($artifact in $artifacts) {
    $attemptDirectory = Join-Path $OutputDirectory ([string]$artifact.id)
    try {
        $artifactRun = $artifact.workflow_run
        if ($null -eq $artifactRun) {
            throw 'Artifact response did not include workflow_run provenance.'
        }

        Assert-Equal ([long]$artifactRun.repository_id) $repositoryId 'artifact.workflow_run.repository_id'
        Assert-Equal ([long]$artifactRun.head_repository_id) $repositoryId 'artifact.workflow_run.head_repository_id'
        Assert-Equal ([string]$artifactRun.head_sha).ToLowerInvariant() $candidate 'artifact.workflow_run.head_sha'

        $run = Invoke-GitHubApi "$ApiBaseUri/repos/$Repository/actions/runs/$($artifactRun.id)"
        Assert-Equal ([long]$run.repository.id) $repositoryId 'run.repository.id'
        Assert-Equal ([long]$run.head_repository.id) $repositoryId 'run.head_repository.id'
        Assert-Equal ([string]$run.head_sha).ToLowerInvariant() $candidate 'run.head_sha'
        Assert-Equal ([string]$run.head_branch) 'main' 'run.head_branch'
        Assert-Equal ([string]$run.path) '.github/workflows/contract.yml' 'run.path'
        Assert-Equal ([string]$run.event) 'workflow_dispatch' 'run.event'
        Assert-Equal ([string]$run.status) 'completed' 'run.status'
        Assert-Equal ([string]$run.conclusion) 'success' 'run.conclusion'

        New-Item -ItemType Directory -Path $attemptDirectory -Force | Out-Null
        $archive = Join-Path $attemptDirectory 'attestation.zip'
        Invoke-WebRequest -Uri $artifact.archive_download_url -Headers $headers -OutFile $archive
        Expand-Archive -LiteralPath $archive -DestinationPath $attemptDirectory -Force

        $attestationPath = Join-Path $attemptDirectory 'attestation.json'
        if (-not (Test-Path -LiteralPath $attestationPath -PathType Leaf)) {
            throw 'Artifact does not contain attestation.json at its root.'
        }

        $attestation = Get-Content -LiteralPath $attestationPath -Raw | ConvertFrom-Json
        $expectedProperties = @(
            'schemaVersion',
            'candidateSha',
            'repository',
            'gameAssemblyVersion',
            'ritsuLibVersion',
            'result',
            'runId'
        )
        $actualProperties = @($attestation.PSObject.Properties.Name | Sort-Object)
        $expectedPropertiesSorted = @($expectedProperties | Sort-Object)
        if (Compare-Object $actualProperties $expectedPropertiesSorted) {
            throw 'Attestation contains missing or unexpected fields.'
        }

        Assert-Equal ([int]$attestation.schemaVersion) 1 'attestation.schemaVersion'
        Assert-Equal ([string]$attestation.candidateSha).ToLowerInvariant() $candidate 'attestation.candidateSha'
        Assert-Equal ([string]$attestation.repository) $Repository 'attestation.repository'
        Assert-Equal ([string]$attestation.result) 'passed' 'attestation.result'
        Assert-Equal ([string]$attestation.runId) ([string]$run.id) 'attestation.runId'
        Assert-Equal ([string]$attestation.ritsuLibVersion) $ExpectedRitsuLibVersion 'attestation.ritsuLibVersion'
        if ([string]::IsNullOrWhiteSpace([string]$attestation.gameAssemblyVersion)) {
            throw 'attestation.gameAssemblyVersion must not be empty.'
        }

        $verifiedDirectory = Join-Path $OutputDirectory 'verified'
        New-Item -ItemType Directory -Path $verifiedDirectory -Force | Out-Null
        Copy-Item -LiteralPath $attestationPath -Destination (Join-Path $verifiedDirectory 'attestation.json') -Force
        Write-Output "Verified protected Contract run $($run.id) for $candidate."
        return
    }
    catch {
        $failures.Add("artifact $($artifact.id): $($_.Exception.Message)")
    }
}

throw "No matching artifact passed provenance validation. $($failures -join ' | ')"
