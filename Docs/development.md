# Development and release guide

## Compatibility

| Component | Supported version |
|---|---|
| Slay the Spire 2 | `0.109.x` (`min_game_version`: `0.109.0`) |
| .NET target | `net9.0` |
| Godot | `4.5.1` Mono |
| RitsuLib | `0.4.62` |
| CI reference API | `Book.StS2.RefLib 0.109.0-beta` |

Local distributable builds compile against `sts2.dll` and `0Harmony.dll` from the target game installation. CI uses RefLib for public compilation checks; protected contracts use isolated real-game references.

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
dotnet msbuild .\NinjaSlayer.csproj -t:PackageMod -p:Configuration=Release
dotnet msbuild .\NinjaSlayer.csproj -t:InstallLocal -p:Configuration=Release
```

`PackageMod` exports `NinjaSlayer.dll`, `NinjaSlayer.json`, `NinjaSlayer.pck`, and `SHA256SUMS` under `build/mods/NinjaSlayer`. `InstallLocal` copies the verified package to the configured game Mods directory.

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

For an explicit combined GitHub and Workshop release from a clean `main`, use:

```powershell
.\tools\release\Publish-QuickRelease.ps1 -Version 0.1.2 -Confirm
```

`Publish-QuickRelease.ps1 -SkipGitHub` delegates to the Workshop-only path and therefore performs no GitHub operation. `-SkipWorkshop` retains the GitHub-only recovery path.

### Protected stable-candidate release

The release flow is:

1. Push the candidate commit to `main`.
2. Run **Protected game contract** for the exact commit with an ephemeral `Contract` runner.
3. Create and push the next stable SemVer tag.
4. Manually dispatch **GitHub Release**, approve it, and start an ephemeral `Release` runner.
5. Publish the matching GitHub Release to Workshop through the manual protected workflow or the guarded local target.

The local Workshop target is:

```powershell
dotnet msbuild .\NinjaSlayer.csproj -t:PublishWorkshop `
  -p:Configuration=Release `
  -p:NinjaSlayerVersion=MAJOR.MINOR.PATCH `
  -p:PublishWorkshopConfirmed=true
```

It requires a clean exact matching tag and the configured local uploader. GitHub Workshop publication uses the `workshop-production` environment and the existing Release artifact.

## Protected runners

`tools/private-contract/Start-EphemeralContractRunner.ps1` starts the short-lived Windows runner used by Contract, Release, or Smoke workflows. Supply the runner purpose, short-lived registration token, exact runner version, and official archive SHA-256 shown by GitHub.

The runner exposes read-only isolated game references, does not upload private binaries, and removes its work directory after completion. Detailed Contract setup is in [tools/private-contract/README.md](../tools/private-contract/README.md).

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
