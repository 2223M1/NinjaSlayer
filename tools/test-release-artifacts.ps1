[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $repositoryRoot '.github\scripts\compatibility.ps1')
. (Join-Path $repositoryRoot '.github\scripts\release-artifact.ps1')
. (Join-Path $repositoryRoot '.github\scripts\actions-provenance.ps1')

function Assert-Throws([scriptblock]$Action, [string]$Pattern) {
    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notmatch $Pattern) {
            throw "Expected failure matching '$Pattern', received: $($_.Exception.Message)"
        }
        return
    }
    throw "Expected failure matching '$Pattern', but the operation succeeded."
}

function Copy-JsonValue($Value) {
    return $Value | ConvertTo-Json -Depth 20 | ConvertFrom-Json
}

function Invoke-ArtifactValidator([string]$AssemblyPath, [string]$ForbiddenPathRoot) {
    $arguments = @(
        'run',
        '--project', (Join-Path $repositoryRoot 'tools\artifact-contract\NinjaSlayer.ArtifactContract.csproj'),
        '--configuration', 'Release',
        '--no-launch-profile',
        '--',
        'validate-assembly',
        '--assembly', $AssemblyPath,
        '--channel', 'stable',
        '--game-api-version', '0.107.1',
        '--ritsulib-package-id', 'STS2.RitsuLib.Compat.0.107.1',
        '--ritsulib-version', '0.5.1',
        '--forbidden-path-root', $ForbiddenPathRoot
    )
    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = (Get-Command dotnet -ErrorAction Stop).Source
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    if ($null -ne $startInfo.PSObject.Properties['ArgumentList']) {
        foreach ($argument in $arguments) {
            $startInfo.ArgumentList.Add($argument)
        }
    }
    else {
        $startInfo.Arguments = (@($arguments | ForEach-Object {
            '"' + $_.Replace('"', '\"') + '"'
        }) -join ' ')
    }

    $process = New-Object Diagnostics.Process
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw 'Unable to start the artifact validator.'
        }
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($standardOutput, $standardError))
        $output = ($standardOutput.Result + [Environment]::NewLine + $standardError.Result).Trim()
        $exitCode = $process.ExitCode
    }
    finally {
        $process.Dispose()
    }
    if ($exitCode -ne 0) {
        throw $output
    }
}

function New-PackageArchive(
    [string]$Directory,
    [string]$ArchivePath,
    [switch]$ExtraFile,
    [switch]$WrongCase,
    [switch]$WrongChecksumCase,
    [switch]$TamperedChecksum) {
    if (Test-Path -LiteralPath $Directory) {
        Remove-Item -LiteralPath $Directory -Recurse -Force
    }
    [IO.Directory]::CreateDirectory($Directory) | Out-Null
    $artifactNames = @(
        $(if ($WrongCase) { 'ninjaslayer.dll' } else { 'NinjaSlayer.dll' }),
        'NinjaSlayer.json',
        'NinjaSlayer.pck'
    )
    foreach ($name in $artifactNames) {
        [IO.File]::WriteAllText(
            (Join-Path $Directory $name),
            "fixture:$name",
            [Text.UTF8Encoding]::new($false))
    }
    $checksumLines = foreach ($name in $artifactNames) {
        $hash = (Get-FileHash -LiteralPath (Join-Path $Directory $name) -Algorithm SHA256).Hash
        if ($TamperedChecksum -and $name -eq 'NinjaSlayer.json') {
            $hash = '0' * 64
        }
        $checksumName = if ($WrongChecksumCase -and $name -eq 'NinjaSlayer.dll') {
            'ninjaslayer.dll'
        }
        else {
            $name
        }
        "$hash *$checksumName"
    }
    [IO.File]::WriteAllLines(
        (Join-Path $Directory 'SHA256SUMS'),
        $checksumLines,
        [Text.UTF8Encoding]::new($false))
    if ($ExtraFile) {
        [IO.File]::WriteAllText((Join-Path $Directory 'unexpected.txt'), 'unexpected')
    }
    if (Test-Path -LiteralPath $ArchivePath) {
        Remove-Item -LiteralPath $ArchivePath -Force
    }
    [IO.Compression.ZipFile]::CreateFromDirectory($Directory, $ArchivePath)
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) `
    "ninjaslayer-release-artifacts-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    $immutableSettings = Assert-NinjaSlayerImmutableReleaseSettings `
        -Settings ([pscustomobject]@{ enabled = $true; enforced_by_owner = $false })
    if ($immutableSettings.enabled -ne $true) {
        throw 'Immutable Release settings validator did not return the validated response.'
    }
    Assert-Throws {
        Assert-NinjaSlayerImmutableReleaseSettings -Settings ([pscustomobject]@{ enabled = $false })
    } 'must be enabled'
    Assert-Throws {
        Assert-NinjaSlayerImmutableReleaseSettings -Settings ([pscustomobject]@{})
    } 'invalid enabled value'
    Assert-Throws {
        Assert-NinjaSlayerImmutableReleaseSettings -Settings ([pscustomobject]@{ enabled = 'true' })
    } 'invalid enabled value'

    $compatibilityPath = Join-Path $repositoryRoot 'eng\compatibility.json'
    $compatibility = Read-NinjaSlayerCompatibility -Path $compatibilityPath
    $compatibilitySha = Get-NinjaSlayerCompatibilitySha256 -Path $compatibilityPath
    $tag = 'v9.8.7'
    $candidate = '0123456789abcdef0123456789abcdef01234567'
    $repository = 'owner/NinjaSlayer'
    $runId = '123456'
    $archives = @{}
    foreach ($channelName in @($compatibility.channels.PSObject.Properties.Name)) {
        $profile = $compatibility.channels.$channelName
        $archiveName = "NinjaSlayer-$tag-$channelName-sts2-$($profile.gameApiVersion).zip"
        $archivePath = Join-Path $temporaryRoot $archiveName
        New-PackageArchive `
            -Directory (Join-Path $temporaryRoot "package-$channelName") `
            -ArchivePath $archivePath
        $archives[$channelName] = $archivePath
    }

    $attestation = New-NinjaSlayerReleaseAttestation `
        -Compatibility $compatibility `
        -CompatibilityManifestSha256 $compatibilitySha `
        -Repository $repository `
        -CandidateSha $candidate `
        -Tag $tag `
        -WorkflowRunId $runId `
        -ArchivesByChannel $archives
    $roundTripped = Copy-JsonValue $attestation
    Assert-NinjaSlayerReleaseAttestation `
        -Attestation $roundTripped `
        -Compatibility $compatibility `
        -CompatibilityManifestSha256 $compatibilitySha `
        -Repository $repository `
        -CandidateSha $candidate `
        -Tag $tag `
        -WorkflowRunId $runId `
        -ArchiveDirectory $temporaryRoot

    foreach ($mutation in @(
        @{ Field = 'candidateSha'; Value = (('f' * 40) -join ''); Pattern = 'candidateSha mismatch' },
        @{ Field = 'tag'; Value = 'v9.8.6'; Pattern = 'tag mismatch' },
        @{ Field = 'workflowPath'; Value = '.github/workflows/ci.yml'; Pattern = 'workflowPath mismatch' }
    )) {
        $invalid = Copy-JsonValue $attestation
        $invalid.($mutation.Field) = $mutation.Value
        Assert-Throws {
            Assert-NinjaSlayerReleaseAttestation $invalid $compatibility $compatibilitySha `
                $repository $candidate $tag $runId $temporaryRoot
        } $mutation.Pattern
    }

    $wrongChannel = Copy-JsonValue $attestation
    $wrongChannel.channels.stable.channel = 'preview'
    Assert-Throws {
        Assert-NinjaSlayerReleaseAttestation $wrongChannel $compatibility $compatibilitySha `
            $repository $candidate $tag $runId $temporaryRoot
    } 'stable.channel mismatch'

    $stableArchive = [string]$archives.stable
    [IO.File]::AppendAllText($stableArchive, 'tampered')
    Assert-Throws {
        Assert-NinjaSlayerReleaseAttestation $roundTripped $compatibility $compatibilitySha `
            $repository $candidate $tag $runId $temporaryRoot
    } 'stable.archive.(length|sha256) mismatch'
    New-PackageArchive -Directory (Join-Path $temporaryRoot 'package-stable') -ArchivePath $stableArchive

    $extraArchive = Join-Path $temporaryRoot 'extra.zip'
    New-PackageArchive `
        -Directory (Join-Path $temporaryRoot 'package-extra') `
        -ArchivePath $extraArchive `
        -ExtraFile
    Assert-Throws { Read-NinjaSlayerPackageArchive $extraArchive } 'exactly the four'

    $wrongCaseArchive = Join-Path $temporaryRoot 'wrong-case.zip'
    New-PackageArchive `
        -Directory (Join-Path $temporaryRoot 'package-wrong-case') `
        -ArchivePath $wrongCaseArchive `
        -WrongCase
    Assert-Throws { Read-NinjaSlayerPackageArchive $wrongCaseArchive } 'exactly the four'

    $tamperedChecksumArchive = Join-Path $temporaryRoot 'tampered-checksum.zip'
    New-PackageArchive `
        -Directory (Join-Path $temporaryRoot 'package-tampered-checksum') `
        -ArchivePath $tamperedChecksumArchive `
        -TamperedChecksum
    Assert-Throws { Read-NinjaSlayerPackageArchive $tamperedChecksumArchive } `
        'SHA256SUMS does not match NinjaSlayer.json'

    $wrongChecksumCaseArchive = Join-Path $temporaryRoot 'wrong-checksum-case.zip'
    New-PackageArchive `
        -Directory (Join-Path $temporaryRoot 'package-wrong-checksum-case') `
        -ArchivePath $wrongChecksumCaseArchive `
        -WrongChecksumCase
    Assert-Throws { Read-NinjaSlayerPackageArchive $wrongChecksumCaseArchive } `
        'SHA256SUMS references an invalid or duplicate package file'

    $duplicateArchive = Join-Path $temporaryRoot 'duplicate.zip'
    $duplicate = [IO.Compression.ZipFile]::Open(
        $duplicateArchive,
        [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in @(
            'NinjaSlayer.dll',
            'NinjaSlayer.dll',
            'NinjaSlayer.json',
            'NinjaSlayer.pck',
            'SHA256SUMS')) {
            $null = $duplicate.CreateEntry($name)
        }
    }
    finally {
        $duplicate.Dispose()
    }
    Assert-Throws { Read-NinjaSlayerPackageArchive $duplicateArchive } `
        '(exactly the four|duplicate ZIP entries)'

    $unsafeArchive = Join-Path $temporaryRoot 'unsafe.zip'
    $unsafe = [IO.Compression.ZipFile]::Open($unsafeArchive, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in @('../NinjaSlayer.dll', 'NinjaSlayer.json', 'NinjaSlayer.pck', 'SHA256SUMS')) {
            $null = $unsafe.CreateEntry($name)
        }
    }
    finally {
        $unsafe.Dispose()
    }
    Assert-Throws { Read-NinjaSlayerPackageArchive $unsafeArchive } '(exactly the four|unsafe ZIP entry)'

    $assemblyFixture = Join-Path $temporaryRoot 'assembly-fixture'
    [IO.Directory]::CreateDirectory($assemblyFixture) | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $assemblyFixture 'AssemblyFixture.csproj'),
        @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <AssemblyName>ArtifactFixture</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <AssemblyMetadata Include="NinjaSlayerHostChannel" Value="stable" />
    <AssemblyMetadata Include="NinjaSlayerGameApiVersion" Value="0.107.1" />
    <AssemblyMetadata Include="NinjaSlayerRitsuLibPackageId" Value="STS2.RitsuLib.Compat.0.107.1" />
    <AssemblyMetadata Include="NinjaSlayerRitsuLibVersion" Value="0.5.1" />
  </ItemGroup>
</Project>
'@,
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        (Join-Path $assemblyFixture 'Fixture.cs'),
        'public static class Fixture { }',
        [Text.UTF8Encoding]::new($false))
    & dotnet build (Join-Path $assemblyFixture 'AssemblyFixture.csproj') `
        -c Release -v:q -p:DebugType=portable -p:DebugSymbols=true
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to build the CodeView artifact fixture.'
    }
    $fixtureAssembly = Join-Path $assemblyFixture 'bin\Release\net9.0\ArtifactFixture.dll'
    Assert-Throws {
        Invoke-ArtifactValidator $fixtureAssembly $assemblyFixture
    } 'CodeView/PDB path'

    & dotnet build (Join-Path $assemblyFixture 'AssemblyFixture.csproj') `
        -c Release -v:q -t:Rebuild -p:DebugType=none -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to build the symbol-free artifact fixture.'
    }
    Invoke-ArtifactValidator $fixtureAssembly $assemblyFixture

    $escapedFixtureRoot = $assemblyFixture.Replace('"', '""')
    [IO.File]::WriteAllText(
        (Join-Path $assemblyFixture 'Fixture.cs'),
        "public static class Fixture { public const string BuildRoot = @`"$escapedFixtureRoot`"; }",
        [Text.UTF8Encoding]::new($false))
    & dotnet build (Join-Path $assemblyFixture 'AssemblyFixture.csproj') `
        -c Release -v:q -t:Rebuild -p:DebugType=none -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to build the absolute-path artifact fixture.'
    }
    Assert-Throws {
        Invoke-ArtifactValidator $fixtureAssembly $assemblyFixture
    } 'absolute build root'
    if ($LASTEXITCODE -ne 0) {
        throw 'Expected artifact validation failures leaked a native process exit code.'
    }

    Write-Output 'Release artifact tests passed.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
