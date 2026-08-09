#Requires -Version 7.0
#Requires -PSEdition Core

Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'file-hash.ps1')

if ($null -eq ('System.IO.Compression.ZipFile' -as [type])) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
}

$script:NinjaSlayerPackageFiles = @(
    'NinjaSlayer.dll',
    'NinjaSlayer.json',
    'NinjaSlayer.pck',
    'SHA256SUMS'
)

function Get-NinjaSlayerSha256([string]$Path) {
    return Get-NinjaSlayerFileSha256 -Path $Path
}

function Assert-NinjaSlayerReleaseEqual($Actual, $Expected, [string]$Field) {
    if ($Actual -cne $Expected) {
        throw "$Field mismatch: expected '$Expected', received '$Actual'."
    }
}

function Get-NinjaSlayerStreamSha256([IO.Stream]$Stream) {
    $hasher = [Security.Cryptography.SHA256]::Create()
    try {
        $hex = [BitConverter]::ToString($hasher.ComputeHash($Stream))
        return $hex.Replace('-', '').ToLowerInvariant()
    }
    finally {
        $hasher.Dispose()
    }
}

function Assert-NinjaSlayerPropertySet($Value, [string[]]$Expected, [string]$Description) {
    $actual = @($Value.PSObject.Properties.Name | Sort-Object)
    $difference = Compare-Object `
        -ReferenceObject $actual `
        -DifferenceObject @($Expected | Sort-Object) `
        -CaseSensitive
    if ($null -ne $difference) {
        throw "$Description contains missing or unexpected fields."
    }
}

function Read-NinjaSlayerPackageArchive {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    return Read-NinjaSlayerChecksummedArchive `
        -Path $Path `
        -ExpectedFileNames $script:NinjaSlayerPackageFiles `
        -ExactSetDescription 'exactly the four NinjaSlayer package files'
}

function Get-NinjaSlayerWorkshopBundleFiles {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Compatibility)

    $files = [Collections.Generic.List[string]]::new()
    foreach ($name in @(
            'NinjaSlayer.dll',
            'NinjaSlayer.json',
            'NinjaSlayer.pck',
            'ninjaslayer-variants.manifest',
            'SHA256SUMS')) {
        $files.Add($name)
    }
    foreach ($channelName in @($Compatibility.channels.PSObject.Properties.Name)) {
        $version = [string]$Compatibility.channels.$channelName.gameApiVersion
        $files.Add("lib/$version/compat-target.txt")
        $files.Add("lib/$version/NinjaSlayer.dll")
    }
    return $files.ToArray()
}

function Read-NinjaSlayerWorkshopBundleArchive {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)]$Compatibility
    )

    return Read-NinjaSlayerChecksummedArchive `
        -Path $Path `
        -ExpectedFileNames (Get-NinjaSlayerWorkshopBundleFiles -Compatibility $Compatibility) `
        -ExactSetDescription 'the exact universal Workshop bundle files'
}

function Test-NinjaSlayerSafeArchivePath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or
        [IO.Path]::IsPathRooted($Path) -or
        $Path.Contains('\') -or
        $Path.StartsWith('/', [StringComparison]::Ordinal)) {
        return $false
    }
    $segments = $Path.Split('/')
    return $segments.Count -gt 0 -and
        @($segments | Where-Object { $_ -in @('', '.', '..') }).Count -eq 0
}

function Resolve-NinjaSlayerSafeReplaceDirectory([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetPathRoot($resolved)
    $parent = [IO.Directory]::GetParent($resolved)
    if ([string]::IsNullOrWhiteSpace($root) -or
        $null -eq $parent -or
        $parent.FullName.TrimEnd([IO.Path]::DirectorySeparatorChar) -ceq
            $root.TrimEnd([IO.Path]::DirectorySeparatorChar)) {
        throw "Replacement directory must be below a dedicated parent: $resolved"
    }
    if (Test-Path -LiteralPath $resolved) {
        $attributes = [IO.File]::GetAttributes($resolved)
        if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Replacement directory must not be a reparse point: $resolved"
        }
    }

    return $resolved
}

function Read-NinjaSlayerChecksummedArchive {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string[]]$ExpectedFileNames,
        [Parameter(Mandatory)][string]$ExactSetDescription
    )

    $resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    $zip = [IO.Compression.ZipFile]::OpenRead($resolved)
    try {
        $entries = @($zip.Entries)
        $entryNames = @($entries | ForEach-Object FullName)
        $entryDifference = Compare-Object `
            -ReferenceObject @($entryNames | Sort-Object) `
            -DifferenceObject @($ExpectedFileNames | Sort-Object) `
            -CaseSensitive
        if ($entryNames.Count -ne $ExpectedFileNames.Count -or $null -ne $entryDifference) {
            throw "$(Split-Path -Leaf $resolved) must contain $ExactSetDescription."
        }
        if (@($entryNames | Group-Object { $_.ToLowerInvariant() } | Where-Object Count -gt 1).Count -gt 0) {
            throw "$(Split-Path -Leaf $resolved) contains duplicate ZIP entries."
        }
        foreach ($name in $entryNames) {
            if (-not (Test-NinjaSlayerSafeArchivePath $name)) {
                throw "$(Split-Path -Leaf $resolved) contains an unsafe ZIP entry: $name"
            }
        }

        $files = [Collections.Generic.List[object]]::new()
        $hashes = [Collections.Generic.Dictionary[string, string]]::new(
            [StringComparer]::Ordinal)
        foreach ($entry in $entries | Sort-Object FullName) {
            $stream = $entry.Open()
            try {
                $sha = Get-NinjaSlayerStreamSha256 $stream
            }
            finally {
                $stream.Dispose()
            }
            $hashes[$entry.FullName] = $sha
            $files.Add([ordered]@{
                path = $entry.FullName
                length = [long]$entry.Length
                sha256 = $sha
            })
        }

        $checksumEntry = @($entries | Where-Object { $_.FullName -ceq 'SHA256SUMS' })[0]
        $checksumStream = $checksumEntry.Open()
        try {
            $reader = [IO.StreamReader]::new($checksumStream, [Text.UTF8Encoding]::new($false), $true, 1024, $true)
            try {
                $checksumText = $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
            }
        }
        finally {
            $checksumStream.Dispose()
        }
        $checksumLines = @($checksumText -split '\r?\n' | Where-Object { $_ -ne '' })
        if ($checksumLines.Count -ne ($ExpectedFileNames.Count - 1)) {
            throw "SHA256SUMS must contain exactly $($ExpectedFileNames.Count - 1) package checksums."
        }
        $seenChecksums = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($line in $checksumLines) {
            if ($line -notmatch '^([0-9A-Fa-f]{64}) \*(.+)$') {
                throw "Invalid SHA256SUMS entry: $line"
            }
            $name = $Matches[2]
            if (-not (Test-NinjaSlayerSafeArchivePath $name) -or
                $name -ceq 'SHA256SUMS' -or
                -not $hashes.ContainsKey($name) -or
                -not $seenChecksums.Add($name)) {
                throw "SHA256SUMS references an invalid or duplicate package file: $name"
            }
            if ($hashes[$name] -ne $Matches[1].ToLowerInvariant()) {
                throw "SHA256SUMS does not match $name."
            }
        }

        return [ordered]@{
            name = Split-Path -Leaf $resolved
            length = [long](Get-Item -LiteralPath $resolved).Length
            sha256 = Get-NinjaSlayerSha256 $resolved
            files = $files.ToArray()
        }
    }
    finally {
        $zip.Dispose()
    }
}

function Expand-NinjaSlayerExactZip {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ArchivePath,
        [Parameter(Mandatory)][string]$DestinationPath,
        [Parameter(Mandatory)][string[]]$ExpectedFileNames
    )

    $resolvedArchive = (Resolve-Path -LiteralPath $ArchivePath -ErrorAction Stop).Path
    $resolvedDestination = Resolve-NinjaSlayerSafeReplaceDirectory $DestinationPath
    $zip = [IO.Compression.ZipFile]::OpenRead($resolvedArchive)
    try {
        $names = @($zip.Entries | ForEach-Object FullName)
        $entryDifference = Compare-Object `
            -ReferenceObject @($names | Sort-Object) `
            -DifferenceObject @($ExpectedFileNames | Sort-Object) `
            -CaseSensitive
        if ($names.Count -ne $ExpectedFileNames.Count -or $null -ne $entryDifference) {
            throw "$(Split-Path -Leaf $resolvedArchive) contains missing or unexpected entries."
        }
        if (@($names | Group-Object { $_.ToLowerInvariant() } | Where-Object Count -gt 1).Count -gt 0) {
            throw "$(Split-Path -Leaf $resolvedArchive) contains duplicate entries."
        }
        foreach ($name in $names) {
            if (-not (Test-NinjaSlayerSafeArchivePath $name)) {
                throw "$(Split-Path -Leaf $resolvedArchive) contains an unsafe entry: $name"
            }
        }

        if (Test-Path -LiteralPath $resolvedDestination) {
            Remove-Item -LiteralPath $resolvedDestination -Recurse -Force
        }
        [IO.Directory]::CreateDirectory($resolvedDestination) | Out-Null
        foreach ($entry in $zip.Entries) {
            $destination = Join-Path $resolvedDestination $entry.FullName
            [IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
            $source = $entry.Open()
            try {
                $target = [IO.File]::Create($destination)
                try {
                    $source.CopyTo($target)
                }
                finally {
                    $target.Dispose()
                }
            }
            finally {
                $source.Dispose()
            }
        }
    }
    finally {
        $zip.Dispose()
    }
}

function New-NinjaSlayerExactZip {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SourceDirectory,
        [Parameter(Mandatory)][string]$ArchivePath,
        [Parameter(Mandatory)][string[]]$ExpectedFileNames
    )

    $resolvedSource = (Resolve-Path -LiteralPath $SourceDirectory -ErrorAction Stop).Path
    $resolvedArchive = [IO.Path]::GetFullPath($ArchivePath)
    $actual = @(Get-ChildItem -LiteralPath $resolvedSource -File -Recurse -Force | ForEach-Object {
        [IO.Path]::GetRelativePath($resolvedSource, $_.FullName).Replace('\', '/')
    })
    $difference = Compare-Object `
        -ReferenceObject @($actual | Sort-Object) `
        -DifferenceObject @($ExpectedFileNames | Sort-Object) `
        -CaseSensitive
    if ($actual.Count -ne $ExpectedFileNames.Count -or $null -ne $difference) {
        throw "Source directory contains missing or unexpected archive files: $resolvedSource"
    }
    foreach ($entry in Get-ChildItem -LiteralPath $resolvedSource -Force -Recurse) {
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Source directory contains a reparse point: $($entry.FullName)"
        }
    }

    [IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedArchive)) | Out-Null
    if (Test-Path -LiteralPath $resolvedArchive) {
        Remove-Item -LiteralPath $resolvedArchive -Force
    }
    $zip = [IO.Compression.ZipFile]::Open($resolvedArchive, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in $ExpectedFileNames) {
            if (-not (Test-NinjaSlayerSafeArchivePath $name)) {
                throw "Unsafe archive path: $name"
            }
            $entry = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::NoCompression)
            $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $source = [IO.File]::OpenRead((Join-Path $resolvedSource $name.Replace('/', [IO.Path]::DirectorySeparatorChar)))
            try {
                $destination = $entry.Open()
                try {
                    $source.CopyTo($destination)
                }
                finally {
                    $destination.Dispose()
                }
            }
            finally {
                $source.Dispose()
            }
        }
    }
    finally {
        $zip.Dispose()
    }
}

function New-NinjaSlayerReleaseAttestation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Compatibility,
        [Parameter(Mandatory)][string]$CompatibilityManifestSha256,
        [Parameter(Mandatory)][ValidatePattern('^[^/]+/[^/]+$')][string]$Repository,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$CandidateSha,
        [Parameter(Mandatory)][ValidatePattern('^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')][string]$Tag,
        [Parameter(Mandatory)][string]$WorkflowRunId,
        [Parameter(Mandatory)][hashtable]$ArchivesByChannel,
        [Parameter(Mandatory)][string]$WorkshopArchivePath
    )

    $channels = [ordered]@{}
    foreach ($channelName in @($Compatibility.channels.PSObject.Properties.Name)) {
        if (-not $ArchivesByChannel.ContainsKey($channelName)) {
            throw "Release attestation is missing the $channelName archive."
        }
        $profile = $Compatibility.channels.$channelName
        $archive = Read-NinjaSlayerPackageArchive -Path $ArchivesByChannel[$channelName]
        $expectedArchiveName = "NinjaSlayer-$Tag-$channelName-sts2-$($profile.gameApiVersion).zip"
        Assert-NinjaSlayerReleaseEqual $archive.name $expectedArchiveName "$channelName.archive.name"
        $channels[$channelName] = [ordered]@{
            channel = $channelName
            gameApiVersion = [string]$profile.gameApiVersion
            ritsuLibPackageId = [string]$profile.ritsuLibPackageId
            archive = [ordered]@{
                name = $archive.name
                length = $archive.length
                sha256 = $archive.sha256
            }
            files = $archive.files
        }
    }

    $workshopArchive = Read-NinjaSlayerWorkshopBundleArchive `
        -Path $WorkshopArchivePath `
        -Compatibility $Compatibility
    $expectedWorkshopName = "NinjaSlayer-$Tag-workshop-universal.zip"
    Assert-NinjaSlayerReleaseEqual $workshopArchive.name $expectedWorkshopName 'workshop.archive.name'

    return [ordered]@{
        schemaVersion = 2
        repository = $Repository
        candidateSha = $CandidateSha.ToLowerInvariant()
        tag = $Tag
        workflowRunId = [string]$WorkflowRunId
        workflowPath = '.github/workflows/release.yml'
        compatibilityManifestSha256 = $CompatibilityManifestSha256.ToLowerInvariant()
        ritsuLibVersion = [string]$Compatibility.ritsuLibVersion
        channels = $channels
        workshop = [ordered]@{
            archive = [ordered]@{
                name = $workshopArchive.name
                length = $workshopArchive.length
                sha256 = $workshopArchive.sha256
            }
            files = $workshopArchive.files
        }
    }
}

function Assert-NinjaSlayerReleaseAttestation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Attestation,
        [Parameter(Mandatory)]$Compatibility,
        [Parameter(Mandatory)][string]$CompatibilityManifestSha256,
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][string]$CandidateSha,
        [Parameter(Mandatory)][string]$Tag,
        [Parameter(Mandatory)][string]$WorkflowRunId,
        [Parameter(Mandatory)][string]$ArchiveDirectory
    )

    Assert-NinjaSlayerPropertySet $Attestation @(
        'schemaVersion', 'repository', 'candidateSha', 'tag', 'workflowRunId',
        'workflowPath', 'compatibilityManifestSha256', 'ritsuLibVersion', 'channels', 'workshop'
    ) 'Release attestation'
    Assert-NinjaSlayerReleaseEqual ([int]$Attestation.schemaVersion) 2 'release.schemaVersion'
    Assert-NinjaSlayerReleaseEqual ([string]$Attestation.repository) $Repository 'release.repository'
    Assert-NinjaSlayerReleaseEqual ([string]$Attestation.candidateSha).ToLowerInvariant() `
        $CandidateSha.ToLowerInvariant() 'release.candidateSha'
    Assert-NinjaSlayerReleaseEqual ([string]$Attestation.tag) $Tag 'release.tag'
    Assert-NinjaSlayerReleaseEqual ([string]$Attestation.workflowRunId) ([string]$WorkflowRunId) 'release.workflowRunId'
    Assert-NinjaSlayerReleaseEqual ([string]$Attestation.workflowPath) '.github/workflows/release.yml' 'release.workflowPath'
    Assert-NinjaSlayerReleaseEqual ([string]$Attestation.compatibilityManifestSha256).ToLowerInvariant() `
        $CompatibilityManifestSha256.ToLowerInvariant() 'release.compatibilityManifestSha256'
    Assert-NinjaSlayerReleaseEqual ([string]$Attestation.ritsuLibVersion) `
        ([string]$Compatibility.ritsuLibVersion) 'release.ritsuLibVersion'

    Assert-NinjaSlayerPropertySet $Attestation.channels @('stable', 'preview') 'Release channels'
    foreach ($channelName in @($Compatibility.channels.PSObject.Properties.Name)) {
        $profile = $Compatibility.channels.$channelName
        $channel = $Attestation.channels.$channelName
        Assert-NinjaSlayerPropertySet $channel @(
            'channel', 'gameApiVersion', 'ritsuLibPackageId', 'archive', 'files'
        ) "$channelName release channel"
        Assert-NinjaSlayerReleaseEqual ([string]$channel.channel) $channelName "$channelName.channel"
        Assert-NinjaSlayerReleaseEqual ([string]$channel.gameApiVersion) `
            ([string]$profile.gameApiVersion) "$channelName.gameApiVersion"
        Assert-NinjaSlayerReleaseEqual ([string]$channel.ritsuLibPackageId) `
            ([string]$profile.ritsuLibPackageId) "$channelName.ritsuLibPackageId"
        Assert-NinjaSlayerPropertySet $channel.archive @('name', 'length', 'sha256') `
            "$channelName archive"
        $expectedArchiveName = "NinjaSlayer-$Tag-$channelName-sts2-$($profile.gameApiVersion).zip"
        Assert-NinjaSlayerReleaseEqual ([string]$channel.archive.name) $expectedArchiveName `
            "$channelName.archive.name"

        $archivePath = Join-Path $ArchiveDirectory ([string]$channel.archive.name)
        $actual = Read-NinjaSlayerPackageArchive -Path $archivePath
        Assert-NinjaSlayerReleaseEqual ([long]$channel.archive.length) ([long]$actual.length) `
            "$channelName.archive.length"
        Assert-NinjaSlayerReleaseEqual ([string]$channel.archive.sha256).ToLowerInvariant() $actual.sha256 `
            "$channelName.archive.sha256"
        if (@($channel.files).Count -ne 4) {
            throw "$channelName.files must contain exactly four entries."
        }
        for ($index = 0; $index -lt 4; $index++) {
            $expectedFile = @($channel.files)[$index]
            $actualFile = @($actual.files)[$index]
            Assert-NinjaSlayerPropertySet $expectedFile @('path', 'length', 'sha256') `
                "$channelName.files[$index]"
            Assert-NinjaSlayerReleaseEqual ([string]$expectedFile.path) ([string]$actualFile.path) `
                "$channelName.files[$index].path"
            Assert-NinjaSlayerReleaseEqual ([long]$expectedFile.length) ([long]$actualFile.length) `
                "$channelName.files[$index].length"
            Assert-NinjaSlayerReleaseEqual ([string]$expectedFile.sha256).ToLowerInvariant() `
                ([string]$actualFile.sha256) "$channelName.files[$index].sha256"
        }
    }

    Assert-NinjaSlayerPropertySet $Attestation.workshop @('archive', 'files') 'Workshop release bundle'
    Assert-NinjaSlayerPropertySet $Attestation.workshop.archive @('name', 'length', 'sha256') `
        'Workshop release archive'
    $expectedWorkshopName = "NinjaSlayer-$Tag-workshop-universal.zip"
    Assert-NinjaSlayerReleaseEqual ([string]$Attestation.workshop.archive.name) `
        $expectedWorkshopName 'workshop.archive.name'
    $workshopArchive = Read-NinjaSlayerWorkshopBundleArchive `
        -Path (Join-Path $ArchiveDirectory $expectedWorkshopName) `
        -Compatibility $Compatibility
    Assert-NinjaSlayerReleaseEqual ([long]$Attestation.workshop.archive.length) `
        ([long]$workshopArchive.length) 'workshop.archive.length'
    Assert-NinjaSlayerReleaseEqual ([string]$Attestation.workshop.archive.sha256).ToLowerInvariant() `
        ([string]$workshopArchive.sha256) 'workshop.archive.sha256'
    $expectedWorkshopFiles = @(Get-NinjaSlayerWorkshopBundleFiles -Compatibility $Compatibility)
    if (@($Attestation.workshop.files).Count -ne $expectedWorkshopFiles.Count) {
        throw "workshop.files must contain exactly $($expectedWorkshopFiles.Count) entries."
    }
    for ($index = 0; $index -lt $expectedWorkshopFiles.Count; $index++) {
        $expectedFile = @($Attestation.workshop.files)[$index]
        $actualFile = @($workshopArchive.files)[$index]
        Assert-NinjaSlayerPropertySet $expectedFile @('path', 'length', 'sha256') `
            "workshop.files[$index]"
        Assert-NinjaSlayerReleaseEqual ([string]$expectedFile.path) ([string]$actualFile.path) `
            "workshop.files[$index].path"
        Assert-NinjaSlayerReleaseEqual ([long]$expectedFile.length) ([long]$actualFile.length) `
            "workshop.files[$index].length"
        Assert-NinjaSlayerReleaseEqual ([string]$expectedFile.sha256).ToLowerInvariant() `
            ([string]$actualFile.sha256) "workshop.files[$index].sha256"
    }
}
