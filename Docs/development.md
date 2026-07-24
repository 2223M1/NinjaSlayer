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

Releases use `v0.1.x`, where `x` is `0` through `99` without leading zeroes. A clean exact tag produces the matching package version; work after that tag produces a development version for the next patch (for example, `v0.1.7` becomes `0.1.8-dev...`) so a local test build takes precedence over the installed Workshop release.

### Quick test release

Frequent player-test builds can use the local quick path. It packages and installs the mod, creates or verifies the matching tag, uploads an idempotent GitHub Release, stages the Workshop content, and invokes the local Workshop uploader:

```powershell
.\tools\release\Publish-QuickRelease.ps1 -Version 0.1.2 -Confirm
```

The quick path deliberately skips tests, Contract, Smoke, protected environments, and self-hosted runners. It still requires a clean `main` whose commit already matches `origin/main`, a valid `0.1.x` version, a successful package export, and valid package checksums. The release note comes from `Workshop/change-note.md`. Use `-SkipGitHub` or `-SkipWorkshop` to retry one destination after a partial external failure.

### Protected stable-candidate release

The release flow is:

1. Push the candidate commit to `main`.
2. Run **Protected game contract** for the exact commit with an ephemeral `Contract` runner.
3. Create and push the next `v0.1.x` tag.
4. Manually dispatch **GitHub Release**, approve it, and start an ephemeral `Release` runner.
5. Publish the matching GitHub Release to Workshop through the manual protected workflow or the guarded local target.

The local Workshop target is:

```powershell
dotnet msbuild .\NinjaSlayer.csproj -t:PublishWorkshop `
  -p:Configuration=Release `
  -p:NinjaSlayerVersion=0.1.x `
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
