[CmdletBinding()]
param(
    [string]$RepoRoot,
    [string]$Destination = (Join-Path $env:USERPROFILE '.codex\skills\ninjaslayer-lore'),
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
}
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
$Source = (Resolve-Path -LiteralPath (Join-Path $RepoRoot 'skills\ninjaslayer-lore')).Path
$Destination = [System.IO.Path]::GetFullPath($Destination)
$ExpectedSuffix = [System.IO.Path]::Combine('.codex', 'skills', 'ninjaslayer-lore')
if (-not $Destination.EndsWith($ExpectedSuffix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing unexpected skill destination: $Destination"
}

function Get-SkillHashes([string]$Root) {
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        return @{}
    }
    $result = @{}
    $fullRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    Get-ChildItem -LiteralPath $Root -File -Recurse | Where-Object {
        $_.FullName -notmatch '[\\/]__pycache__[\\/]'
    } | ForEach-Object {
        $relative = $_.FullName.Substring($fullRoot.Length).TrimStart('\', '/').Replace('\', '/')
        $result[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    return $result
}

function Compare-SkillHashes([hashtable]$Left, [hashtable]$Right) {
    $allKeys = @($Left.Keys) + @($Right.Keys) | Sort-Object -Unique
    return @($allKeys | Where-Object {
        -not $Left.ContainsKey($_) -or -not $Right.ContainsKey($_) -or $Left[$_] -ne $Right[$_]
    })
}

$sourceHashes = Get-SkillHashes $Source
if ($Check) {
    $differences = Compare-SkillHashes $sourceHashes (Get-SkillHashes $Destination)
    if ($differences.Count -gt 0) {
        Write-Error ("Installed skill differs: " + ($differences -join ', '))
    }
    Write-Host "[ok] Installed skill matches project source ($($sourceHashes.Count) files)."
    exit 0
}

$parent = Split-Path -Parent $Destination
New-Item -ItemType Directory -Path $parent -Force | Out-Null
$staging = Join-Path $parent ('ninjaslayer-lore.install-' + [guid]::NewGuid().ToString('N'))
try {
    Copy-Item -LiteralPath $Source -Destination $staging -Recurse -Force
    $stagingHashes = Get-SkillHashes $staging
    $copyDifferences = Compare-SkillHashes $sourceHashes $stagingHashes
    if ($copyDifferences.Count -gt 0) {
        throw "Staging copy hash mismatch: $($copyDifferences -join ', ')"
    }
    if (Test-Path -LiteralPath $Destination) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }
    Move-Item -LiteralPath $staging -Destination $Destination
}
finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}

$differences = Compare-SkillHashes $sourceHashes (Get-SkillHashes $Destination)
if ($differences.Count -gt 0) {
    throw "Installed skill hash mismatch: $($differences -join ', ')"
}
Write-Host "[ok] Installed ninjaslayer-lore to $Destination ($($sourceHashes.Count) files)."
