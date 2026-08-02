[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string]$Version,

    [string]$StableDataDir,
    [string]$PreviewDataDir,
    [string]$GodotExe,
    [string]$WorkshopUploadRoot,
    [string]$ReleaseNoteFile = 'Workshop\change-note.md',
    [string]$SettingsFile = 'build\fast-release\settings.json',

    [ValidateRange(60, 3600)]
    [int]$BudgetSeconds = 300,

    [switch]$Confirm,
    [switch]$DryRun,
    [switch]$Resume,
    [switch]$SaveSettings,
    [switch]$SkipGitHub,
    [switch]$SkipWorkshop,
    [switch]$CleanBuildCache,
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$publisher = Join-Path $PSScriptRoot 'Publish-FastRelease.ps1'
& $publisher @PSBoundParameters
