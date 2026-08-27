#Requires -Version 7.0
#Requires -PSEdition Core

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$matrixScript = Join-Path $repositoryRoot 'tools\transition-perf\Invoke-TransitionPerfMatrix.ps1'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    $matrixScript,
    [ref]$tokens,
    [ref]$parseErrors)
Require ($parseErrors.Count -eq 0) 'Transition performance script did not parse.'

$functionNames = @(
    'Test-IsChildPath',
    'Remove-ExperimentDirectory',
    'Get-ModsManifestLines',
    'Enter-IsolatedModsEnvironment',
    'Exit-IsolatedModsEnvironment',
    'Stage-Mod',
    'Assert-IsolatedModSet'
)
$definitions = $ast.FindAll(
    { param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -in $functionNames },
    $true)
Require ($definitions.Count -eq $functionNames.Count) 'Transition mods-isolation functions were not all found.'
. ([ScriptBlock]::Create(($definitions.Extent.Text -join "`n")))

$testRoot = Join-Path ([IO.Path]::GetTempPath()) `
    "NinjaSlayer-transition-isolation-test-$([Guid]::NewGuid().ToString('N'))"
$gameRoot = Join-Path $testRoot 'game'
$transitionPerfModsRoot = Join-Path $gameRoot 'mods'
$transitionPerfModsBackup = Join-Path $gameRoot '.ninjaslayer-transition-perf-mods-backup'
$transitionPerfModsEnvironmentOwned = $false
$transitionPerfOriginalModsExisted = $false
$transitionPerfOriginalModsManifest = @()
[IO.Directory]::CreateDirectory($gameRoot) | Out-Null

function Initialize-OriginalMods {
    [IO.Directory]::CreateDirectory((Join-Path $transitionPerfModsRoot 'UserMod\empty')) | Out-Null
    [IO.File]::WriteAllBytes(
        (Join-Path $transitionPerfModsRoot 'UserMod\payload.bin'),
        [byte[]](0, 1, 2, 127, 128, 255))
    [IO.File]::WriteAllText(
        (Join-Path $transitionPerfModsRoot 'root-note.txt'),
        "original`r`nbytes`n",
        [Text.UTF8Encoding]::new($false))
}

function Require-OriginalModsUnchanged([string[]]$ExpectedManifest, [string]$Label) {
    Require (Test-Path -LiteralPath $transitionPerfModsRoot -PathType Container) `
        "$Label did not preserve the original mods directory."
    $actual = @(Get-ModsManifestLines -Root $transitionPerfModsRoot)
    Require ($actual.Count -eq $ExpectedManifest.Count) "$Label changed the mods manifest count."
    Require (-not (Compare-Object -ReferenceObject $ExpectedManifest -DifferenceObject $actual)) `
        "$Label changed the mods byte manifest."
    Require (-not (Test-Path -LiteralPath (Join-Path $transitionPerfModsRoot 'NinjaSlayer-SmokeDriver'))) `
        "$Label retained SmokeDriver in the original mods directory."
}

function Require-OriginalRestored([string[]]$ExpectedManifest, [string]$Label) {
    Require-OriginalModsUnchanged -ExpectedManifest $ExpectedManifest -Label $Label
    Require (-not (Test-Path -LiteralPath $transitionPerfModsBackup)) `
        "$Label retained the fixed backup."
}

function New-ModSource([string]$Name) {
    $source = Join-Path $testRoot "sources\$Name"
    [IO.Directory]::CreateDirectory($source) | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $source "$Name.dll"),
        $Name,
        [Text.UTF8Encoding]::new($false))
    return $source
}

function Stage-FixtureMods([hashtable]$Sources, [string[]]$ExpectedNames) {
    foreach ($name in $ExpectedNames) {
        Stage-Mod -Source $Sources[$name] -Name $name
    }
    Assert-IsolatedModSet -ExpectedNames $ExpectedNames
}

try {
    $sources = @{
        NinjaSlayer = New-ModSource 'NinjaSlayer'
        'STS2-RitsuLib' = New-ModSource 'STS2-RitsuLib'
        'NinjaSlayer-SmokeDriver' = New-ModSource 'NinjaSlayer-SmokeDriver'
    }
    $expectedNames = @('NinjaSlayer', 'STS2-RitsuLib', 'NinjaSlayer-SmokeDriver')

    Initialize-OriginalMods
    $originalManifest = @(Get-ModsManifestLines -Root $transitionPerfModsRoot)
    Enter-IsolatedModsEnvironment
    Require (Test-Path -LiteralPath $transitionPerfModsBackup -PathType Container) `
        'Isolation did not move the original mods directory to the fixed backup.'
    Stage-FixtureMods -Sources $sources -ExpectedNames $expectedNames
    [IO.File]::WriteAllText((Join-Path $transitionPerfModsRoot 'unexpected.dll'), 'unexpected')
    $exactSetRejected = $false
    try {
        Assert-IsolatedModSet -ExpectedNames $expectedNames
    }
    catch {
        $exactSetRejected = $_.Exception.Message -match 'differs from the isolated expected set'
    }
    Require $exactSetRejected 'Exact mod-set validation accepted an unexpected root file.'
    [IO.File]::Delete((Join-Path $transitionPerfModsRoot 'unexpected.dll'))
    Exit-IsolatedModsEnvironment
    Require-OriginalRestored -ExpectedManifest $originalManifest -Label 'success path'

    Enter-IsolatedModsEnvironment
    Stage-FixtureMods -Sources $sources -ExpectedNames $expectedNames
    $faultObserved = $false
    try {
        throw 'injected-transition-fault'
    }
    catch {
        $faultObserved = $_.Exception.Message -eq 'injected-transition-fault'
    }
    finally {
        Exit-IsolatedModsEnvironment
    }
    Require $faultObserved 'Injected exception was not observed.'
    Require-OriginalRestored -ExpectedManifest $originalManifest -Label 'exception path'

    Enter-IsolatedModsEnvironment
    Stage-FixtureMods -Sources $sources -ExpectedNames $expectedNames
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'pwsh.exe'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    foreach ($argument in @('-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 30')) {
        $startInfo.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($startInfo)
    $timeoutObserved = $false
    try {
        if (-not $process.WaitForExit(100)) {
            $timeoutObserved = $true
            $process.Kill($true)
            $process.WaitForExit()
            throw [TimeoutException]::new('injected-transition-timeout')
        }
    }
    catch [TimeoutException] {
        Require ($_.Exception.Message -eq 'injected-transition-timeout') 'Unexpected timeout failure.'
    }
    finally {
        if (-not $process.HasExited) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $process.Dispose()
        Exit-IsolatedModsEnvironment
    }
    Require $timeoutObserved 'Timeout fixture did not time out.'
    Require-OriginalRestored -ExpectedManifest $originalManifest -Label 'timeout path'

    [IO.Directory]::CreateDirectory($transitionPerfModsBackup) | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $transitionPerfModsBackup 'recovery-required.txt'),
        'do-not-overwrite')
    $backupRejected = $false
    try {
        Enter-IsolatedModsEnvironment
    }
    catch {
        $backupRejected = $_.Exception.Message -match 'backup already exists'
    }
    Require $backupRejected 'Existing fixed backup was not rejected.'
    Require-OriginalModsUnchanged -ExpectedManifest $originalManifest -Label 'backup refusal'
    Require ((Get-Content -Raw -LiteralPath (Join-Path $transitionPerfModsBackup 'recovery-required.txt')) `
            -eq 'do-not-overwrite') `
        'Backup refusal overwrote recovery data.'

    [IO.Directory]::Delete($transitionPerfModsBackup, $true)
    [IO.Directory]::Delete($transitionPerfModsRoot, $true)
    Enter-IsolatedModsEnvironment
    Stage-FixtureMods -Sources $sources -ExpectedNames $expectedNames
    Exit-IsolatedModsEnvironment
    Require (-not (Test-Path -LiteralPath $transitionPerfModsRoot)) `
        'Isolation created a mods path that did not originally exist.'
    Require (-not (Test-Path -LiteralPath $transitionPerfModsBackup)) `
        'No-original-mods path retained the fixed backup.'

    Write-Output 'Transition mods isolation tests passed.'
}
finally {
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ((Test-Path -LiteralPath $testRoot) -and
        (Test-IsChildPath -Path $testRoot -Root $temporaryRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
