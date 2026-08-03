[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $repositoryRoot '.github\scripts\compatibility.ps1')
. (Join-Path $repositoryRoot '.github\scripts\release-artifact.ps1')
. (Join-Path $repositoryRoot '.github\scripts\actions-provenance.ps1')
. (Join-Path $repositoryRoot '.github\scripts\spine-extension.ps1')
. (Join-Path $repositoryRoot '.github\scripts\release-candidate.ps1')

function Get-FileHash {
    throw 'Get-NinjaSlayerCompatibilitySha256 must not depend on Get-FileHash.'
}

function Invoke-FixtureGit([string]$Repository, [string[]]$Arguments) {
    & git -C $Repository @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Fixture git command failed: $($Arguments -join ' ')"
    }
}

$temporaryFile = Join-Path ([IO.Path]::GetTempPath()) `
    "ninjaslayer-compatibility-hash-$([Guid]::NewGuid().ToString('N'))"
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) `
    "ninjaslayer-release-candidate-$([Guid]::NewGuid().ToString('N'))"
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

    $spineSource = Join-Path $temporaryRoot 'spine-source'
    $spineDestination = Join-Path $temporaryRoot 'spine-destination'
    [IO.Directory]::CreateDirectory($spineSource) | Out-Null
    $spineContracts = @(
        [pscustomobject]@{ name = 'libspine_godot.windows.editor.x86_64.dll'; sha256 = $expected },
        [pscustomobject]@{ name = 'libspine_godot.windows.template_debug.x86_64.dll'; sha256 = $expected },
        [pscustomobject]@{ name = 'libspine_godot.windows.template_release.x86_64.dll'; sha256 = $expected }
    )
    $fixtureCompatibility = [pscustomobject]@{
        spineExtension = [pscustomobject]@{ windowsFiles = $spineContracts }
    }
    foreach ($contract in $spineContracts) {
        [IO.File]::WriteAllBytes((Join-Path $spineSource $contract.name), [byte[]]::new(0))
    }
    [IO.File]::WriteAllText((Join-Path $spineSource '~ignored.TMP'), 'not copied')
    $copied = @(Copy-NinjaSlayerVerifiedSpineExtension `
        -Compatibility $fixtureCompatibility `
        -SourceDirectory $spineSource `
        -DestinationDirectory $spineDestination)
    if ($copied.Count -ne 3 -or @(Get-ChildItem $spineDestination -File).Count -ne 3) {
        throw 'Verified Spine copying did not produce exactly three files.'
    }
    [IO.File]::WriteAllText((Join-Path $spineSource $spineContracts[0].name), 'tampered')
    try {
        $null = Get-NinjaSlayerVerifiedSpineExtension `
            -Compatibility $fixtureCompatibility `
            -SourceDirectory $spineSource
        throw 'Tampered Spine input was accepted.'
    }
    catch {
        if ($_.Exception.Message -notmatch 'hash mismatch') {
            throw
        }
    }
    [IO.File]::WriteAllBytes(
        (Join-Path $spineSource $spineContracts[0].name),
        [byte[]]::new(0))

    $fixtureRepository = Join-Path $temporaryRoot 'repository'
    $fixtureCandidateRoot = Join-Path $temporaryRoot 'candidates'
    [IO.Directory]::CreateDirectory((Join-Path $fixtureRepository 'Workshop')) | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $fixtureRepository 'source.txt'),
        'committed source',
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        (Join-Path $fixtureRepository 'Workshop\change-note.md'),
        'committed note',
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        (Join-Path $fixtureRepository 'Workshop\workshop.json'),
        '{"title":"committed"}',
        [Text.UTF8Encoding]::new($false))
    Invoke-FixtureGit $fixtureRepository @('init', '--quiet')
    Invoke-FixtureGit $fixtureRepository @('add', '--all')
    Invoke-FixtureGit $fixtureRepository @(
        '-c', 'user.name=NinjaSlayer Tests',
        '-c', 'user.email=tests@example.invalid',
        'commit', '--quiet', '-m', 'fixture'
    )
    $fixtureSha = (& git -C $fixtureRepository rev-parse HEAD | Out-String).Trim()
    $fixtureTree = (& git -C $fixtureRepository rev-parse 'HEAD^{tree}' | Out-String).Trim()
    [IO.File]::WriteAllText((Join-Path $fixtureRepository 'source.txt'), 'live mutation')
    [IO.File]::WriteAllText((Join-Path $fixtureRepository 'Workshop\change-note.md'), 'live note')
    [IO.File]::WriteAllText((Join-Path $fixtureRepository 'untracked.txt'), 'not archived')

    $candidate = New-NinjaSlayerReleaseCandidate `
        -RepositoryRoot $fixtureRepository `
        -CandidateSha $fixtureSha `
        -CandidateRoot $fixtureCandidateRoot `
        -Compatibility $fixtureCompatibility `
        -SpineExtensionDirectory $spineSource `
        -ReleaseNoteRelativePath 'Workshop/change-note.md'
    if ((Get-Content (Join-Path $candidate.Root 'source.txt') -Raw) -cne 'committed source' -or
        (Get-Content $candidate.ReleaseNotePath -Raw) -cne 'committed note' -or
        (Test-Path (Join-Path $candidate.Root 'untracked.txt')) -or
        [string]$candidate.TreeSha -ne $fixtureTree) {
        throw 'Release candidate did not remain bound to the committed Git tree.'
    }

    $releaseNoteBlob = (& git -C $fixtureRepository rev-parse 'HEAD:Workshop/change-note.md' |
        Out-String).Trim()
    $workshopBlob = (& git -C $fixtureRepository rev-parse 'HEAD:Workshop/workshop.json' |
        Out-String).Trim()
    $compatibilitySha = 'c' * 64
    $state = [pscustomobject]@{
        schemaVersion = 2
        reusable = $true
        version = '9.8.7'
        candidateSha = $fixtureSha
        candidateTree = $fixtureTree
        compatibilityManifestSha256 = $compatibilitySha
        frozenInputs = [pscustomobject]@{
            releaseNote = [pscustomobject]@{
                relativePath = 'Workshop/change-note.md'
                gitBlob = $releaseNoteBlob
                sha256 = Get-NinjaSlayerFileSha256 -Path $candidate.ReleaseNotePath
            }
            workshopMetadata = [pscustomobject]@{
                relativePath = 'Workshop/workshop.json'
                gitBlob = $workshopBlob
                sha256 = Get-NinjaSlayerFileSha256 -Path $candidate.WorkshopMetadataPath
            }
            spineFiles = @($spineContracts | ForEach-Object {
                [pscustomobject]@{ name = $_.name; sha256 = $_.sha256 }
            })
        }
    }
    $stateParameters = @{
        State = $state
        Version = '9.8.7'
        CandidateSha = $fixtureSha
        CandidateTree = $fixtureTree
        CompatibilitySha = $compatibilitySha
        Compatibility = $fixtureCompatibility
        ReleaseNoteRelativePath = 'Workshop/change-note.md'
        ReleaseNoteBlob = $releaseNoteBlob
        ReleaseNotePath = $candidate.ReleaseNotePath
        WorkshopMetadataRelativePath = 'Workshop/workshop.json'
        WorkshopMetadataBlob = $workshopBlob
        WorkshopMetadataPath = $candidate.WorkshopMetadataPath
    }
    if (-not (Test-NinjaSlayerFrozenReleaseInputs @stateParameters)) {
        throw 'A valid frozen release state was rejected.'
    }
    $state.candidateTree = 'f' * 40
    if (Test-NinjaSlayerFrozenReleaseInputs @stateParameters) {
        throw 'A release state with the wrong candidate tree was accepted.'
    }
    $state.candidateTree = $fixtureTree
    $state.frozenInputs.spineFiles[0].sha256 = 'f' * 64
    if (Test-NinjaSlayerFrozenReleaseInputs @stateParameters) {
        throw 'A release state with the wrong Spine hash was accepted.'
    }
    $state.frozenInputs.spineFiles[0].sha256 = $expected
    [IO.File]::WriteAllText($candidate.ReleaseNotePath, 'tampered frozen note')
    if (Test-NinjaSlayerFrozenReleaseInputs @stateParameters) {
        throw 'A release state with a tampered frozen note was accepted.'
    }
    Remove-NinjaSlayerReleaseCandidate -Path $candidate.Root -CandidateRoot $fixtureCandidateRoot

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
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
