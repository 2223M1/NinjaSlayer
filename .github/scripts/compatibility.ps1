Set-StrictMode -Version Latest

function Read-NinjaSlayerCompatibility {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    $manifest = Get-Content -LiteralPath $resolved -Raw -Encoding utf8 | ConvertFrom-Json
    if ([int]$manifest.schemaVersion -ne 1) {
        throw "Unsupported compatibility schema in $resolved."
    }
    $channelNames = @($manifest.channels.PSObject.Properties.Name)
    if ($channelNames.Count -ne 2 -or $channelNames[0] -ne 'stable' -or $channelNames[1] -ne 'preview') {
        throw 'Compatibility manifest must contain stable and preview, in that order.'
    }
    if ([string]$manifest.defaultBuildChannel -notin $channelNames) {
        throw 'Compatibility defaultBuildChannel is not active.'
    }
    if ([string]$manifest.defaultBuildChannel -ne 'preview') {
        throw 'Compatibility defaultBuildChannel must be preview.'
    }
    if ([string]$manifest.ritsuLibVersion -notmatch '^\d+\.\d+\.\d+$') {
        throw 'Compatibility ritsuLibVersion must be an exact SemVer core.'
    }
    foreach ($channelName in $channelNames) {
        $channel = $manifest.channels.$channelName
        if ([string]$channel.gameApiVersion -notmatch '^\d+\.\d+\.\d+$' -or
            [string]$channel.ritsuLibPackageId -notmatch '^STS2\.RitsuLib(?:\.Compat\.\d+\.\d+\.\d+)?$' -or
            [string]$channel.distributionChannel -notin @('public', 'beta') -or
            $null -eq $channel.runtimeAssemblies -or
            $null -eq $channel.compileFeatures -or
            $null -eq $channel.hostContract) {
            throw "Compatibility channel '$channelName' is incomplete."
        }
        $workshopItemId = $channel.workshopItemId
        if ($null -ne $workshopItemId -and [string]$workshopItemId -notmatch '^\d+$') {
            throw "Compatibility channel '$channelName' has an invalid Workshop item id."
        }
        if ([string]$channel.hostContract.assemblyVersion -notmatch '^\d+\.\d+\.\d+\.\d+$' -or
            [string]$channel.hostContract.moduleMvid -notmatch '^[0-9A-Fa-f-]{36}$') {
            throw "Compatibility channel '$channelName' has an invalid host contract identity."
        }
        foreach ($runtimeAssembly in @($channel.runtimeAssemblies)) {
            if ([string]$runtimeAssembly -notmatch '^[A-Za-z0-9_.-]+\.dll$') {
                throw "Compatibility channel '$channelName' has an invalid runtime assembly."
            }
        }
    }
    return $manifest
}

function Get-NinjaSlayerCompatibilityChannel {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Manifest,
        [Parameter(Mandatory)][ValidateSet('stable', 'preview')][string]$Channel
    )

    $value = $Manifest.channels.$Channel
    if ($null -eq $value) {
        throw "Compatibility channel '$Channel' is unavailable."
    }
    return $value
}

function Get-NinjaSlayerCompatibilitySha256 {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-NinjaSlayerGameModuleMvid {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$AssemblyPath)

    $resolved = (Resolve-Path -LiteralPath $AssemblyPath -ErrorAction Stop).Path
    $assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($resolved))
    return $assembly.ManifestModule.ModuleVersionId.ToString('D')
}
