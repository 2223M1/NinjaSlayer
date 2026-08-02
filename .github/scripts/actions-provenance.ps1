Set-StrictMode -Version Latest

function Assert-NinjaSlayerEqual($Actual, $Expected, [string]$Field) {
    if ($Actual -cne $Expected) {
        throw "$Field mismatch: expected '$Expected', received '$Actual'."
    }
}

function New-NinjaSlayerGitHubApiContext {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^[^/]+/[^/]+$')][string]$Repository,
        [Parameter(Mandatory)][string]$Token,
        [string]$ApiBaseUri = 'https://api.github.com'
    )

    $headers = @{
        Authorization = "Bearer $Token"
        Accept = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
    }
    $repositoryInfo = Invoke-RestMethod -Uri "$ApiBaseUri/repos/$Repository" -Headers $headers
    return [pscustomobject]@{
        Repository = $Repository
        RepositoryId = [long]$repositoryInfo.id
        ApiBaseUri = $ApiBaseUri.TrimEnd('/')
        Headers = $headers
    }
}

function Invoke-NinjaSlayerGitHubApi($Context, [string]$Uri) {
    Invoke-RestMethod -Uri $Uri -Headers $Context.Headers
}

function Assert-NinjaSlayerImmutableReleaseSettings {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Settings)

    $enabledProperty = $Settings.PSObject.Properties['enabled']
    if ($null -eq $enabledProperty -or $enabledProperty.Value -isnot [bool]) {
        throw 'GitHub Immutable Releases returned an invalid enabled value.'
    }
    if (-not $enabledProperty.Value) {
        throw 'GitHub Immutable Releases must be enabled before publishing.'
    }
    return $Settings
}

function Assert-NinjaSlayerImmutableReleasesEnabled {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Context)

    $headers = @{} + $Context.Headers
    $headers['X-GitHub-Api-Version'] = '2026-03-10'
    try {
        $settings = Invoke-RestMethod `
            -Uri "$($Context.ApiBaseUri)/repos/$($Context.Repository)/immutable-releases" `
            -Headers $headers
    }
    catch {
        throw "GitHub Immutable Releases status could not be verified: $($_.Exception.Message)"
    }
    return Assert-NinjaSlayerImmutableReleaseSettings -Settings $settings
}

function Get-NinjaSlayerHttpStatusCode {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$ErrorRecord)

    $exception = $ErrorRecord.Exception
    if ($null -eq $exception) {
        return $null
    }
    $responseProperty = $exception.PSObject.Properties['Response']
    if ($null -eq $responseProperty -or $null -eq $responseProperty.Value) {
        return $null
    }
    $statusProperty = $responseProperty.Value.PSObject.Properties['StatusCode']
    if ($null -eq $statusProperty) {
        return $null
    }
    try {
        return [int]$statusProperty.Value
    }
    catch {
        return $null
    }
}

function Test-NinjaSlayerGitHubReleaseExists {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)][ValidatePattern('^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
        [string]$Tag
    )

    $encodedTag = [Uri]::EscapeDataString($Tag)
    $uri = "$($Context.ApiBaseUri)/repos/$($Context.Repository)/releases/tags/$encodedTag"
    try {
        $null = Invoke-NinjaSlayerGitHubApi $Context $uri
        return $true
    }
    catch {
        if ((Get-NinjaSlayerHttpStatusCode -ErrorRecord $_) -eq 404) {
            return $false
        }
        throw "GitHub Release $Tag status could not be verified: $($_.Exception.Message)"
    }
}

function Get-NinjaSlayerArtifactCandidates {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)][string]$ArtifactName
    )

    $encodedName = [Uri]::EscapeDataString($ArtifactName)
    $listing = Invoke-NinjaSlayerGitHubApi $Context `
        "$($Context.ApiBaseUri)/repos/$($Context.Repository)/actions/artifacts?name=$encodedName&per_page=100"
    return @($listing.artifacts |
        Where-Object { -not $_.expired -and $_.name -ceq $ArtifactName } |
        Sort-Object created_at -Descending)
}

function Assert-NinjaSlayerArtifactProvenance {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)]$Artifact,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$CandidateSha,
        [Parameter(Mandatory)][string]$WorkflowPath,
        [string]$HeadBranch = 'main',
        [string]$Event = 'workflow_dispatch'
    )

    $candidate = $CandidateSha.ToLowerInvariant()
    $artifactRun = $Artifact.workflow_run
    if ($null -eq $artifactRun) {
        throw 'Artifact response did not include workflow_run provenance.'
    }
    Assert-NinjaSlayerEqual ([long]$artifactRun.repository_id) $Context.RepositoryId `
        'artifact.workflow_run.repository_id'
    Assert-NinjaSlayerEqual ([long]$artifactRun.head_repository_id) $Context.RepositoryId `
        'artifact.workflow_run.head_repository_id'
    Assert-NinjaSlayerEqual ([string]$artifactRun.head_sha).ToLowerInvariant() $candidate `
        'artifact.workflow_run.head_sha'

    $run = Invoke-NinjaSlayerGitHubApi $Context `
        "$($Context.ApiBaseUri)/repos/$($Context.Repository)/actions/runs/$($artifactRun.id)"
    Assert-NinjaSlayerEqual ([long]$run.repository.id) $Context.RepositoryId 'run.repository.id'
    Assert-NinjaSlayerEqual ([long]$run.head_repository.id) $Context.RepositoryId 'run.head_repository.id'
    Assert-NinjaSlayerEqual ([string]$run.head_sha).ToLowerInvariant() $candidate 'run.head_sha'
    Assert-NinjaSlayerEqual ([string]$run.head_branch) $HeadBranch 'run.head_branch'
    Assert-NinjaSlayerEqual ([string]$run.path) $WorkflowPath 'run.path'
    Assert-NinjaSlayerEqual ([string]$run.event) $Event 'run.event'
    Assert-NinjaSlayerEqual ([string]$run.status) 'completed' 'run.status'
    Assert-NinjaSlayerEqual ([string]$run.conclusion) 'success' 'run.conclusion'
    return $run
}

function Save-NinjaSlayerActionsArtifact {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)]$Artifact,
        [Parameter(Mandatory)][string]$DestinationPath
    )

    $parent = Split-Path -Parent ([IO.Path]::GetFullPath($DestinationPath))
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    Invoke-WebRequest `
        -Uri $Artifact.archive_download_url `
        -Headers $Context.Headers `
        -OutFile $DestinationPath
}
