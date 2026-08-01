[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Throws([scriptblock]$Action, [string]$Pattern) {
    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notmatch $Pattern) {
            throw "Expected error matching '$Pattern', received '$($_.Exception.Message)'."
        }
        return
    }
    throw "Expected error matching '$Pattern', but no error was thrown."
}

$global:NinjaSlayerFirewallTestState = [pscustomobject]@{
    Rules = @{}
    CreateCalls = [Collections.Generic.List[object]]::new()
    GetCalls = [Collections.Generic.List[string]]::new()
    RemoveCalls = [Collections.Generic.List[string]]::new()
    FailCreateAt = 0
    FailRemoveAt = 0
}

function global:New-NetFirewallRule {
    [CmdletBinding()]
    param(
        [string]$Name,
        [string]$DisplayName,
        [string]$Direction,
        [string]$Action,
        [string]$Program,
        [string[]]$RemoteAddress,
        [string]$Profile,
        [string]$Enabled
    )

    $state = $global:NinjaSlayerFirewallTestState
    $state.CreateCalls.Add([pscustomobject]@{
        Name = $Name
        Program = $Program
        RemoteAddress = [string[]]$RemoteAddress
    })
    if ($state.FailCreateAt -gt 0 -and $state.CreateCalls.Count -eq $state.FailCreateAt) {
        throw "Injected firewall creation failure $($state.FailCreateAt)."
    }
    $state.Rules[$Name] = [pscustomobject]@{ Name = $Name }
    return $state.Rules[$Name]
}

function global:Get-NetFirewallRule {
    [CmdletBinding()]
    param([string]$Name)

    $state = $global:NinjaSlayerFirewallTestState
    $state.GetCalls.Add($Name)
    if ($state.Rules.ContainsKey($Name)) {
        return $state.Rules[$Name]
    }
}

function global:Remove-NetFirewallRule {
    [CmdletBinding()]
    param([string]$Name)

    $state = $global:NinjaSlayerFirewallTestState
    $state.RemoveCalls.Add($Name)
    if ($state.FailRemoveAt -gt 0 -and $state.RemoveCalls.Count -eq $state.FailRemoveAt) {
        throw "Injected firewall removal failure $($state.FailRemoveAt)."
    }
    $state.Rules.Remove($Name) | Out-Null
}

function Reset-FirewallState([int]$FailCreateAt = 0, [int]$FailRemoveAt = 0) {
    $global:NinjaSlayerFirewallTestState = [pscustomobject]@{
        Rules = @{}
        CreateCalls = [Collections.Generic.List[object]]::new()
        GetCalls = [Collections.Generic.List[string]]::new()
        RemoveCalls = [Collections.Generic.List[string]]::new()
        FailCreateAt = $FailCreateAt
        FailRemoveAt = $FailRemoveAt
    }
}

. (Join-Path $PSScriptRoot '..\.github\scripts\process-network-isolation.ps1')

$sandbox = Join-Path ([IO.Path]::GetTempPath()) "ninjaslayer-firewall-tests-$([Guid]::NewGuid().ToString('N'))"
$workspace = Join-Path $sandbox 'workspace'
$programs = Join-Path $sandbox 'programs'
try {
    New-Item -ItemType Directory -Path $workspace, $programs -Force | Out-Null
    $programA = Join-Path $programs 'a.exe'
    $programB = Join-Path $programs 'b.exe'
    $workspaceProgram = Join-Path $workspace 'candidate.exe'
    Set-Content -LiteralPath $programA -Value 'a'
    Set-Content -LiteralPath $programB -Value 'b'
    Set-Content -LiteralPath $workspaceProgram -Value 'candidate'

    Reset-FirewallState
    $lease = New-NinjaSlayerProcessFirewallLease `
        -ProgramPath @($programA, $programA) `
        -RemoteScope NonLoopback `
        -RulePrefix 'NinjaSlayer-Test-Duplicate' `
        -ForbiddenRoot $workspace
    Assert-True ($lease.Programs.Count -eq 1) 'Duplicate executable paths must produce one firewall rule.'
    Assert-True ($lease.RuleNames.Count -eq 1) 'Duplicate executable paths must produce one planned rule name.'
    Assert-True ($lease.RemoteAddresses -contains '0.0.0.0-126.255.255.255') 'IPv4 non-loopback coverage is missing.'
    Assert-True ($lease.RemoteAddresses -contains '::2-ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff') 'IPv6 non-loopback coverage is missing.'
    Assert-True ($lease.RemoteAddresses -notcontains 'Any') 'NonLoopback mode must preserve loopback access.'
    Remove-NinjaSlayerProcessFirewallLease -Lease $lease
    Assert-True ($global:NinjaSlayerFirewallTestState.Rules.Count -eq 0) 'Successful cleanup left a firewall rule.'

    Assert-Throws {
        New-NinjaSlayerProcessFirewallLease `
            -ProgramPath (Join-Path $programs 'missing.exe') `
            -RemoteScope All `
            -RulePrefix 'NinjaSlayer-Test-Missing'
    } 'missing\.exe'
    Assert-Throws {
        New-NinjaSlayerProcessFirewallLease `
            -ProgramPath $workspaceProgram `
            -RemoteScope All `
            -RulePrefix 'NinjaSlayer-Test-Workspace' `
            -ForbiddenRoot $workspace
    } 'outside trusted and candidate workspaces'

    Reset-FirewallState -FailCreateAt 2
    Assert-Throws {
        New-NinjaSlayerProcessFirewallLease `
            -ProgramPath @($programA, $programB) `
            -RemoteScope All `
            -RulePrefix 'NinjaSlayer-Test-Partial'
    } 'Injected firewall creation failure 2'
    Assert-True ($global:NinjaSlayerFirewallTestState.Rules.Count -eq 0) 'Partial setup did not remove its first rule.'
    Assert-True ($global:NinjaSlayerFirewallTestState.GetCalls.Count -ge 2) 'Partial setup did not inspect every planned rule.'
    $checkedNames = @($global:NinjaSlayerFirewallTestState.GetCalls)
    $plannedNames = @($global:NinjaSlayerFirewallTestState.CreateCalls | ForEach-Object { [string]$_.Name })
    Assert-True ([Array]::IndexOf($checkedNames, $plannedNames[1]) -lt [Array]::IndexOf($checkedNames, $plannedNames[0])) `
        'Partial setup cleanup must inspect planned rules in reverse order.'

    Reset-FirewallState
    Assert-Throws {
        Invoke-NinjaSlayerProcessNetworkIsolation `
            -ProgramPath @($programA, $programB) `
            -RemoteScope All `
            -RulePrefix 'NinjaSlayer-Test-Action' `
            -Action { throw 'Injected isolated action failure.' }
    } 'Injected isolated action failure'
    Assert-True ($global:NinjaSlayerFirewallTestState.Rules.Count -eq 0) 'Action failure left a firewall rule.'
    $removed = @($global:NinjaSlayerFirewallTestState.RemoveCalls)
    $created = @($global:NinjaSlayerFirewallTestState.CreateCalls | ForEach-Object { [string]$_.Name })
    Assert-True ($removed.Count -eq 2 -and $removed[0] -eq $created[1] -and $removed[1] -eq $created[0]) `
        'Action failure cleanup must remove rules in reverse order.'
    Assert-True (@($global:NinjaSlayerFirewallTestState.CreateCalls[0].RemoteAddress) -contains 'Any') `
        'All mode must block every remote address.'

    Reset-FirewallState -FailRemoveAt 1
    $failedCleanupLease = New-NinjaSlayerProcessFirewallLease `
        -ProgramPath $programA `
        -RemoteScope All `
        -RulePrefix 'NinjaSlayer-Test-CleanupFailure'
    Assert-Throws {
        Remove-NinjaSlayerProcessFirewallLease -Lease $failedCleanupLease
    } 'Protected process firewall cleanup failed.*Injected firewall removal failure 1'
    $global:NinjaSlayerFirewallTestState.FailRemoveAt = 0
    Remove-NinjaSlayerProcessFirewallLease -Lease $failedCleanupLease
}
finally {
    Remove-Item -LiteralPath $sandbox -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item Function:\global:New-NetFirewallRule -ErrorAction SilentlyContinue
    Remove-Item Function:\global:Get-NetFirewallRule -ErrorAction SilentlyContinue
    Remove-Item Function:\global:Remove-NetFirewallRule -ErrorAction SilentlyContinue
    Remove-Variable NinjaSlayerFirewallTestState -Scope Global -ErrorAction SilentlyContinue
}

Write-Output 'Process network isolation tests passed.'
