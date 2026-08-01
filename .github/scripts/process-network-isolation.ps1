Set-StrictMode -Version Latest

$script:NinjaSlayerNonLoopbackRemoteAddresses = @(
    '0.0.0.0-126.255.255.255',
    '128.0.0.0-255.255.255.255',
    '::-::',
    '::2-ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff'
)

function Test-NinjaSlayerPathWithinRoot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Root
    )

    $normalizedRoot = $Root.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    if ($Path.Equals($normalizedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }
    $rootPrefix = "$normalizedRoot$([IO.Path]::DirectorySeparatorChar)"
    return $Path.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)
}

function Resolve-NinjaSlayerProtectedExecutable {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [string[]]$ForbiddenRoot = @()
    )

    $resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "Protected executable was not found: $resolved"
    }
    $fullPath = [IO.Path]::GetFullPath($resolved)
    foreach ($root in @($ForbiddenRoot)) {
        if ([string]::IsNullOrWhiteSpace($root)) {
            continue
        }
        $resolvedRoot = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $root -ErrorAction Stop).Path)
        if (Test-NinjaSlayerPathWithinRoot -Path $fullPath -Root $resolvedRoot) {
            throw "Protected executable must remain outside trusted and candidate workspaces: $fullPath"
        }
    }
    return $fullPath
}

function Remove-NinjaSlayerProcessFirewallLease {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Lease)

    $failures = [Collections.Generic.List[string]]::new()
    $ruleNames = @($Lease.RuleNames)
    for ($index = $ruleNames.Count - 1; $index -ge 0; $index--) {
        $ruleName = [string]$ruleNames[$index]
        try {
            if (@(Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue).Count -gt 0) {
                Remove-NetFirewallRule -Name $ruleName -ErrorAction Stop | Out-Null
            }
            if (@(Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue).Count -gt 0) {
                $failures.Add("Firewall rule remained after cleanup: $ruleName")
            }
        }
        catch {
            $failures.Add("Failed to remove firewall rule ${ruleName}: $($_.Exception.Message)")
        }
    }
    if ($failures.Count -gt 0) {
        throw "Protected process firewall cleanup failed. $($failures -join ' | ')"
    }
}

function New-NinjaSlayerProcessFirewallLease {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$ProgramPath,
        [Parameter(Mandatory)][ValidateSet('All', 'NonLoopback')][string]$RemoteScope,
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9._-]+$')][string]$RulePrefix,
        [string[]]$ForbiddenRoot = @()
    )

    $programs = [Collections.Generic.List[string]]::new()
    $seenPrograms = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($candidate in @($ProgramPath)) {
        $program = Resolve-NinjaSlayerProtectedExecutable -Path $candidate -ForbiddenRoot $ForbiddenRoot
        if ($seenPrograms.Add($program)) {
            $programs.Add($program)
        }
    }
    if ($programs.Count -eq 0) {
        throw 'At least one protected executable is required.'
    }

    $remoteAddresses = if ($RemoteScope -eq 'NonLoopback') {
        @($script:NinjaSlayerNonLoopbackRemoteAddresses)
    }
    else {
        @('Any')
    }
    $plans = [Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $programs.Count; $index++) {
        $plans.Add([pscustomobject]@{
            Name = "$RulePrefix-$index-$([Guid]::NewGuid().ToString('N'))"
            Program = $programs[$index]
        })
    }
    $lease = [pscustomobject]@{
        RuleNames = [string[]]@($plans | ForEach-Object { $_.Name })
        Programs = [string[]]@($programs)
        RemoteScope = $RemoteScope
        RemoteAddresses = [string[]]$remoteAddresses
    }

    try {
        foreach ($plan in $plans) {
            New-NetFirewallRule `
                -Name $plan.Name `
                -DisplayName $plan.Name `
                -Direction Outbound `
                -Action Block `
                -Program $plan.Program `
                -RemoteAddress $remoteAddresses `
                -Profile Any `
                -Enabled True | Out-Null
        }
        return $lease
    }
    catch {
        $setupFailure = $_
        try {
            Remove-NinjaSlayerProcessFirewallLease -Lease $lease
        }
        catch {
            throw "Protected process firewall setup failed: $($setupFailure.Exception.Message) Cleanup also failed: $($_.Exception.Message)"
        }
        throw $setupFailure
    }
}

function Invoke-NinjaSlayerProcessNetworkIsolation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$ProgramPath,
        [Parameter(Mandatory)][ValidateSet('All', 'NonLoopback')][string]$RemoteScope,
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9._-]+$')][string]$RulePrefix,
        [Parameter(Mandatory)][scriptblock]$Action,
        [string[]]$ForbiddenRoot = @()
    )

    $lease = $null
    try {
        $lease = New-NinjaSlayerProcessFirewallLease `
            -ProgramPath $ProgramPath `
            -RemoteScope $RemoteScope `
            -RulePrefix $RulePrefix `
            -ForbiddenRoot $ForbiddenRoot
        & $Action $lease
    }
    finally {
        if ($null -ne $lease) {
            Remove-NinjaSlayerProcessFirewallLease -Lease $lease
        }
    }
}
