#Requires -Version 7.0
#Requires -PSEdition Core

Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'file-hash.ps1')

function Get-NinjaSlayerVerifiedSpineExtension {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Compatibility,
        [Parameter(Mandatory)][string]$SourceDirectory,
        [scriptblock]$ValidateSource
    )

    $resolvedSource = (Resolve-Path -LiteralPath $SourceDirectory -ErrorAction Stop).Path
    $contracts = @($Compatibility.spineExtension.windowsFiles)
    if ($contracts.Count -ne 3) {
        throw 'The compatibility manifest must declare exactly three Windows Spine files.'
    }

    $results = [Collections.Generic.List[object]]::new()
    foreach ($contract in $contracts) {
        $name = [string]$contract.name
        $expectedHash = ([string]$contract.sha256).ToLowerInvariant()
        $source = Join-Path $resolvedSource $name
        $file = Get-Item -LiteralPath $source -Force -ErrorAction Stop
        if ($file.PSIsContainer) {
            throw "Spine input must be a file: $source"
        }
        if ($null -ne $ValidateSource) {
            & $ValidateSource $file $contract
        }
        $sourceHash = (Get-NinjaSlayerFileSha256 -Path $file.FullName).ToLowerInvariant()
        if ($sourceHash -ne $expectedHash) {
            throw "Spine extension hash mismatch for $name`: expected $expectedHash, got $sourceHash."
        }

        $results.Add([ordered]@{
            name = $name
            sha256 = $sourceHash
            sourcePath = $file.FullName
        })
    }

    return $results.ToArray()
}

function Copy-NinjaSlayerVerifiedSpineExtension {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Compatibility,
        [Parameter(Mandatory)][string]$SourceDirectory,
        [Parameter(Mandatory)][string]$DestinationDirectory,
        [scriptblock]$ValidateSource,
        [switch]$MarkDestinationReadOnly
    )

    $resolvedDestination = [IO.Path]::GetFullPath($DestinationDirectory)
    [IO.Directory]::CreateDirectory($resolvedDestination) | Out-Null
    $verified = @(Get-NinjaSlayerVerifiedSpineExtension `
        -Compatibility $Compatibility `
        -SourceDirectory $SourceDirectory `
        -ValidateSource $ValidateSource)

    foreach ($inputFile in $verified) {
        $destination = Join-Path $resolvedDestination ([string]$inputFile.name)
        Copy-Item -LiteralPath ([string]$inputFile.sourcePath) -Destination $destination -Force
        $destinationHash = (Get-NinjaSlayerFileSha256 -Path $destination).ToLowerInvariant()
        if ($destinationHash -ne [string]$inputFile.sha256) {
            throw "Copied Spine extension hash mismatch for $($inputFile.name)."
        }
        if ($MarkDestinationReadOnly) {
            (Get-Item -LiteralPath $destination -Force).IsReadOnly = $true
        }
    }

    $expectedNames = @($verified | ForEach-Object { [string]$_.name } | Sort-Object)
    $actualNames = @(Get-ChildItem -LiteralPath $resolvedDestination -File -Force |
        ForEach-Object Name | Sort-Object)
    if ($null -ne (Compare-Object $expectedNames $actualNames -CaseSensitive)) {
        throw "Spine destination must contain exactly the three declared files: $resolvedDestination"
    }

    return @($verified | ForEach-Object {
        [ordered]@{
            name = [string]$_.name
            sha256 = [string]$_.sha256
        }
    })
}
