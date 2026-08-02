[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $repositoryRoot '.github\scripts\compatibility.ps1')
. (Join-Path $repositoryRoot '.github\scripts\release-artifact.ps1')
. (Join-Path $repositoryRoot '.github\scripts\actions-provenance.ps1')

function Get-FileHash {
    throw 'Get-NinjaSlayerCompatibilitySha256 must not depend on Get-FileHash.'
}

$temporaryFile = Join-Path ([IO.Path]::GetTempPath()) `
    "ninjaslayer-compatibility-hash-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.File]::WriteAllBytes($temporaryFile, [byte[]]::new(0))
    $expected = 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'
    foreach ($actual in @(
        (Get-NinjaSlayerFileSha256 -Path $temporaryFile),
        (Get-NinjaSlayerCompatibilitySha256 -Path $temporaryFile),
        (Get-NinjaSlayerSha256 -Path $temporaryFile)
    )) {
        if ($actual -cne $expected) {
            throw "PowerShell SHA-256 mismatch: expected $expected, got $actual."
        }
    }

    $script:artifactFixture = @()
    function Invoke-NinjaSlayerGitHubApi {
        return [pscustomobject]@{ artifacts = $script:artifactFixture }
    }

    $artifactContext = [pscustomobject]@{
        ApiBaseUri = 'https://api.invalid'
        Repository = 'owner/repository'
        Headers = @{}
    }
    $artifactName = 'private-contract-candidate'
    foreach ($expectedCount in @(0, 1, 2)) {
        $script:artifactFixture = if ($expectedCount -eq 0) {
            @()
        }
        else {
            @(1..$expectedCount | ForEach-Object {
                [pscustomobject]@{
                    name = $artifactName
                    expired = $false
                    created_at = "2026-08-0$($_)T00:00:00Z"
                }
            })
        }
        $candidates = Get-NinjaSlayerArtifactCandidates `
            -Context $artifactContext `
            -ArtifactName $artifactName
        if ($candidates -isnot [array]) {
            throw "Artifact candidates must remain an array at cardinality $expectedCount."
        }
        if ($candidates.Count -ne $expectedCount) {
            throw "Expected $expectedCount artifact candidates, got $($candidates.Count)."
        }
    }

    Write-Output 'PowerShell compatibility tests passed.'
}
finally {
    if (Test-Path -LiteralPath $temporaryFile) {
        [IO.File]::Delete($temporaryFile)
    }
}
