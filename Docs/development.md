# Development and release guide

## Compatibility

<!-- compatibility:start -->
| Channel | Game API | RitsuLib compile package | Distribution |
|---|---|---|---|
| `stable` | `0.107.1` | `STS2.RitsuLib.Compat.0.107.1 0.5.1` | `public` |
| `preview` | `0.110.1` | `STS2.RitsuLib 0.5.1` | `beta` |

Only these two rolling channels are active. Runtime players receive the current RitsuLib Workshop build; `0.5.1` is the pinned, reproducible compile baseline and minimum manifest dependency. Protected builds use each channel's real game assemblies. No intermediate game host or RefLib approximation is a release target.
<!-- compatibility:end -->

All repository automation targets PowerShell 7 Core and must be invoked through `pwsh`. Windows PowerShell 5.1 is not supported.

## Validation

```powershell
dotnet test .\Tests\NinjaSlayer.LogicTests\NinjaSlayer.LogicTests.csproj -c Release
node .\tools\validate-repository.mjs
node .\tools\test-build-boundaries.mjs
```

The RitsuLib Harmony contract requires an initialized Godot host and real game references. It is executed by the protected contract workflow.

## Packaging

Ordinary `dotnet build` has no export, installation, or upload side effects.

```powershell
dotnet msbuild .\NinjaSlayer.csproj -t:PackageMod `
  -p:Configuration=Release `
  -p:NinjaSlayerHostChannel=preview `
  -p:Sts2DataDir=C:\path\to\preview\data_sts2_windows_x86_64
dotnet msbuild .\NinjaSlayer.csproj -t:InstallLocal `
  -p:Configuration=Release `
  -p:NinjaSlayerHostChannel=stable `
  -p:Sts2DataDir=C:\path\to\stable\data_sts2_windows_x86_64
```

Packaging and installation require an explicit channel and verify the selected `sts2.dll` MVID before export. `PackageMod` writes `NinjaSlayer.dll`, `NinjaSlayer.json`, `NinjaSlayer.pck`, and `SHA256SUMS` under `build/mods/<channel>/NinjaSlayer`. `InstallLocal` copies that verified package to the configured game Mods directory.

For an untagged build, `InstallLocal` uses the resolved version core with `+local.<commit>` build metadata. This keeps local testing at the same SemVer precedence as the corresponding Workshop release, so the game consistently selects the local package when both sources are installed. `PackageMod` and release publication retain their normal version semantics.

Local packages take precedence over the unlisted Workshop item. After switching the Steam branch between stable and preview, install the matching published package by host MVID instead of reusing the previous local DLL:

```powershell
pwsh .\tools\release\Install-CurrentHostRelease.ps1 -Version 0.1.30
```

The installer refuses unknown hosts and cross-channel archives, validates the published SHA-256 and all four package files, and atomically replaces `mods/NinjaSlayer`. Pass `-GameRoot` for a non-default Steam library or `-ArchivePath` to reuse an already downloaded official ZIP.

Godot loads a Debug editor assembly before Release export. The export-only build disables `ScriptPathAttribute` generation so the editor does not resolve game-dependent script types in its custom load context; the packaged Release assembly keeps normal script registration.

## Versions and releases

Releases use stable SemVer tags in the form `vMAJOR.MINOR.PATCH`, without leading zeroes. A clean exact tag produces the matching package version. Development builds use the next patch after the repository's highest stable release tag, including tags retained on archived history, so a cleaned main branch cannot regress below an already published version.

The three release entry points have distinct ownership:

| Entry point | Purpose |
| --- | --- |
| `Invoke-OneClickRelease.ps1` | Normal official release. Builds stable and preview, publishes GitHub, then uploads stable to Workshop. |
| `Publish-WorkshopQuickRelease.ps1` | Workshop-only player test or emergency upload. It does not publish GitHub. |
| `Publish-QuickRelease.ps1` | Optional protected audit path using Contract, Smoke, and protected artifacts. |

### Routine official release

The normal personal-project release path is local and targets completion within five minutes. Configure the two host directories once while performing a non-publishing dry run:

```powershell
pwsh .\tools\release\Invoke-OneClickRelease.ps1 `
  -Version 0.1.30 `
  -StableDataDir C:\path\to\stable\data_sts2_windows_x86_64 `
  -PreviewDataDir C:\path\to\preview\data_sts2_windows_x86_64 `
  -SaveSettings `
  -DryRun
```

The paths are saved in the ignored `build/fast-release/settings.json`. Keep old stable host references outside the repository in a durable private directory such as `../.sts2build/hosts/stable-0.107.1`; never point this setting at `.codex_tmp` or commit game binaries. Later releases require one non-interactive command:

```powershell
pwsh .\tools\release\Invoke-OneClickRelease.ps1 -Version 0.1.30 -Confirm
```

The command requires `main` to match `origin/main`, permits only local `AGENTS.md` and `.agents/` changes, and requires the tracked `Workshop/change-note.md` to differ from the previous SemVer release. It runs the fast repository checks and builds both host packages. It creates uncompressed ZIP containers because the exported PCK is already compressed, validates the exact four-file archives, installs the archive matching the active local host, then creates and pushes the tag, creates the GitHub Release, and uploads the exact stable archive contents through the local Workshop uploader. Stable and preview build caches are retained between releases. `-Resume` reuses archives bound to the same commit and compatibility manifest after an interrupted publication. `-DryRun` never changes the local game install, and `-SkipLocalInstall` explicitly disables the normal host-matched install.

Routine releases deliberately do not wait for Contract, Smoke, protected environments, attestations, pull requests, or a self-hosted Actions runner. Those checks remain available as optional audits. The five-minute budget covers local preparation and normal uploads; unusually slow GitHub or Steam network transfer can still exceed it without corrupting the completed release.

### Workshop-only player test or emergency upload

To upload an uncommitted working tree only to Workshop, use the separate player-test path:

```powershell
pwsh .\tools\release\Publish-WorkshopQuickRelease.ps1 -Confirm
```

This path automatically selects the next local patch version, stages and uploads stable, and performs no GitHub operation or local game installation. Use `Install-CurrentHostRelease.ps1` separately when the active game host needs a local package.

### Optional protected audit release

The original high-assurance path is retained for compatibility investigations or major host migrations:

1. Run **Protected game contract** for the exact commit with an ephemeral `Contract` runner.
2. Run **Protected real-game smoke** in `FirstCombatRestart` mode for the same commit with an ephemeral `Smoke` runner.
3. Dispatch the protected Release workflow with `Publish-QuickRelease.ps1`.
4. Publish its stable artifact through the protected Workshop workflow.

```powershell
pwsh .\tools\release\Publish-QuickRelease.ps1 -Version 0.1.30 -SkipWorkshop -Confirm
```

This optional path owns dual-host attestations and immutable protected artifacts, and is expected to take substantially longer than the routine local release.

The local Workshop target is:

```powershell
dotnet msbuild .\NinjaSlayer.csproj -t:PublishWorkshop `
  -p:Configuration=Release `
  -p:NinjaSlayerHostChannel=stable `
  -p:NinjaSlayerVersion=MAJOR.MINOR.PATCH `
  -p:PublishWorkshopConfirmed=true
```

It requires a clean exact matching tag, stable host references, and the configured local uploader. GitHub Workshop publication uses separate protected environments for public and preview items, downloads only the matching host archive, and revalidates its assembly metadata and manifest before upload.

## Host contract capture

`eng/compatibility.json` is the only handwritten source for active host versions, package ids, distribution channels, and host fingerprints. Update one channel from a read-only game installation with:

```powershell
pwsh .\tools\Capture-GameHostContract.ps1 `
  -GameDirectory C:\path\to\game\data_sts2_windows_x86_64 `
  -Channel preview
# Review the candidate JSON and layout report, then:
pwsh .\tools\Capture-GameHostContract.ps1 `
  -GameDirectory C:\path\to\game\data_sts2_windows_x86_64 `
  -Channel preview `
  -Apply
```

After reviewing the generated diff, run `node .\tools\sync-compatibility.mjs --check`. A host promotion replaces one of the two rolling channel entries; intermediate versions are not retained as active build targets.

## Protected runners

`tools/private-contract/Start-EphemeralContractRunner.ps1` starts the short-lived Windows runner used by Contract, Release, or Smoke workflows. Supply the runner purpose, short-lived registration token, exact runner version, and official archive SHA-256 shown by GitHub.

Contract and Release runners require both `GameDataDirectoryStable` and `GameDataDirectoryPreview`. Smoke requires both `GameRootDirectoryStable` and `GameRootDirectoryPreview`, plus the current Workshop RitsuLib installation. The launcher derives all expected versions, package ids, runtime assemblies, and MVIDs from `eng/compatibility.json`, exposes private inputs read-only, uploads no private binaries, and removes its work directory after completion. Detailed setup is in [tools/private-contract/README.md](../tools/private-contract/README.md).

## Real-game smoke

The **Protected real-game smoke** workflow runs a bounded single-player first-combat and process-restart scenario. `FullAutoSlay` is available as a longer advisory run. Both modes use a trusted test driver excluded from the shipping assembly and package.

Smoke setup, outputs, isolation boundaries, and troubleshooting are documented in [tools/smoke-harness/README.md](../tools/smoke-harness/README.md).

## Worker

```powershell
cd .\Infrastructure\telemetry-worker
npm ci
npm test
npx wrangler deploy --dry-run
```

Data handling is documented in [privacy.md](privacy.md), and the current development dependency exception is recorded in [dependency-security.md](dependency-security.md).
