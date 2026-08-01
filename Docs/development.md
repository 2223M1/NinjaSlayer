# Development and release guide

## Compatibility

<!-- compatibility:start -->
| Channel | Game API | RitsuLib compile package | Distribution |
|---|---|---|---|
| `stable` | `0.107.1` | `STS2.RitsuLib.Compat.0.107.1 0.5.1` | `public` |
| `preview` | `0.110.0` | `STS2.RitsuLib 0.5.1` | `beta` |

Only these two rolling channels are active. Runtime players receive the current RitsuLib Workshop build; `0.5.1` is the pinned, reproducible compile baseline and minimum manifest dependency. Protected builds use each channel's real game assemblies. No intermediate game host or RefLib approximation is a release target.
<!-- compatibility:end -->

## Validation

```powershell
dotnet test .\Tests\NinjaSlayer.LogicTests\NinjaSlayer.LogicTests.csproj -c Release
dotnet test .\Tests\NinjaSlayer.ArchitectureTests\NinjaSlayer.ArchitectureTests.csproj -c Release
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

Godot loads a Debug editor assembly before Release export. The export-only build disables `ScriptPathAttribute` generation so the editor does not resolve game-dependent script types in its custom load context; the packaged Release assembly keeps normal script registration.

## Versions and releases

Releases use stable SemVer tags in the form `vMAJOR.MINOR.PATCH`, without leading zeroes. A clean exact tag produces the matching package version. Development builds use the next patch after the repository's highest stable release tag, including tags retained on archived history, so a cleaned main branch cannot regress below an already published version.

### Workshop-only quick test release

Frequent player-test builds can use the desktop shortcut or the Workshop-only script. It includes the current working tree, automatically selects the next local patch version, packages and installs the mod, stages Workshop content, and invokes the local uploader:

```powershell
.\tools\release\Invoke-OneClickRelease.ps1
# Or explicitly:
.\tools\release\Publish-WorkshopQuickRelease.ps1 -Confirm
```

This path deliberately skips tests, Contract, Smoke, protected environments, and self-hosted runners. It does not require a clean branch and performs no GitHub authentication, fetch, commit, tag, push, pull request, or Release operation. The next version is derived from local tags and completed Workshop markers under `build/releases`; a failed upload reuses the same version. The release note comes from `Workshop/change-note.md`.

To create or reuse a tag from clean `main` and dispatch the protected dual-host GitHub Release, use:

```powershell
.\tools\release\Publish-QuickRelease.ps1 -Version 0.1.2 -SkipWorkshop -Confirm
```

This command never builds or uploads a GitHub asset locally; the protected workflow owns both host archives and all attestations. Workshop publication is a separate operation after Release succeeds. `Publish-QuickRelease.ps1 -SkipGitHub` still delegates to the explicitly unsafe Workshop-only player-test path and performs no GitHub operation.

### Protected stable-candidate release

The release flow is:

1. Push the candidate commit to `main`.
2. Run **Protected game contract** for the exact commit with an ephemeral `Contract` runner.
3. Run **Protected real-game smoke** in `FirstCombatRestart` mode for the same commit with an ephemeral `Smoke` runner.
4. Create and push the next stable SemVer tag.
5. Manually dispatch **GitHub Release**, approve it, and start an ephemeral `Release` runner. The workflow creates one stable and one preview archive from the same source revision.
6. Dispatch **Publish Steam Workshop** for the required channel. The public item accepts only stable; preview remains blocked until its separate unlisted item id is recorded in `eng/compatibility.json`.

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
.\tools\Capture-GameHostContract.ps1 `
  -GameDirectory C:\path\to\game\data_sts2_windows_x86_64 `
  -Channel preview
# Review the candidate JSON and layout report, then:
.\tools\Capture-GameHostContract.ps1 `
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
