# NinjaSlayer

NinjaSlayer is a Slay the Spire 2 character mod built with Godot 4.5.1 Mono and RitsuLib. The repository contains the gameplay code, Godot resources, FMOD bank, telemetry/feedback Worker, and explicit packaging/release automation.

## Compatibility

| Component | Supported version |
|---|---|
| Slay the Spire 2 | `0.109.x` (`min_game_version`: `0.109.0`) |
| .NET target | `net9.0` |
| Godot | `4.5.1` Mono |
| RitsuLib | `0.4.62` |
| CI reference API | `Book.StS2.RefLib 0.109.0-beta` |
| Spine GDExtension | Godot 4.5.1 build committed under `addons/spine` |

Local distributable builds must compile against the `sts2.dll` and `0Harmony.dll` from the target game installation. CI performs a blocking full-project compile against `Book.StS2.RefLib 0.109.0-beta`; final packages still use the real `0.109.x` game assemblies, and the reference package does not lower the manifest's game requirement.

## Build Commands

Ordinary builds only compile. They do not export a PCK, install files, or upload Workshop content.
Version resolution and delivery commands are isolated under `eng/`; the project
file itself owns only compilation settings and dependencies. CI runs the install,
checksum, Workshop staging, and fail-fast publication gates entirely in a
temporary directory.

```powershell
dotnet restore .\NinjaSlayer.csproj
dotnet build .\NinjaSlayer.csproj --no-restore -v:minimal
```

Run the public logic, architecture, and RitsuLib contract checks:

```powershell
dotnet test .\Tests\NinjaSlayer.LogicTests\NinjaSlayer.LogicTests.csproj -c Release
dotnet test .\Tests\NinjaSlayer.ArchitectureTests\NinjaSlayer.ArchitectureTests.csproj -c Release
node .\tools\validate-repository.mjs
node .\tools\test-build-boundaries.mjs
```

The RitsuLib Harmony contract requires an initialized Godot host and real game references. It is run by the protected workflow; locally, build its project and launch its `project.godot` with Godot 4.5.1 Mono in headless mode.

The checked-in `global.json` requires a .NET 9 SDK and prevents Godot 4.5.1 Mono from accidentally loading an incompatible .NET 10 MSBuild toolset.

Create and install a complete local package explicitly:

```powershell
dotnet msbuild .\NinjaSlayer.csproj -t:PackageMod -p:Configuration=Release
dotnet msbuild .\NinjaSlayer.csproj -t:InstallLocal -p:Configuration=Release
```

`PackageMod` exports `NinjaSlayer.dll`, versioned `NinjaSlayer.json`, `NinjaSlayer.pck`, and `SHA256SUMS` under `build/mods/NinjaSlayer`. `InstallLocal` packages first, copies those files into the game Mods directory, and verifies every copied file by SHA-256.
Packaging first refreshes the Debug editor assembly because Godot loads it before starting a Release export. Local `sts2.dll` and `0Harmony.dll` references remain copy-local so Godot can resolve public script types that implement game interfaces; the package allowlist still includes only the NinjaSlayer DLL, JSON, PCK, and checksum manifest. Godot-reported managed exceptions and `ERROR:` lines fail packaging even when the editor exits with code zero.
`StageWorkshop` is the non-uploading staging primitive used by build tests and
`PublishWorkshop`; invoking it directly never calls the Workshop uploader.

## Versions And Releases

- Releases use `v0.1.x`, where `x` is `0` through `99` without leading zeroes; for example, `v0.1.9` and `v0.1.10` are valid, while `v0.1.01` and `v0.1.100` are not.
- A clean exact supported tag produces the matching `0.1.x` package version.
- Normal commits produce `X.Y.Z-dev.N+gCOMMIT`; dirty trees include `.dirty`.
- GitHub Release automation accepts only supported `v0.1.x` tags whose commit belongs to `main`.
- A Release also requires a text-only attestation from the protected game-contract workflow for the exact tag commit.
- Strict Release packaging runs only on a one-job self-hosted runner labeled `ninjaslayer-release`. The launcher supplies temporary read-only copies of the local game references and Spine binaries; no game DLL is uploaded, cached, or downloaded by the workflow.
- Release publication is idempotent: rerunning a tag workflow replaces the matching ZIP asset when the Release already exists. The protected manual dispatch accepts an existing supported tag so an older failed run can be repaired using the trusted workflow currently on `main`.
- GitHub Releases never publish Steam Workshop content.

Create the `release-production` environment under repository **Settings > Environments** and require approval for it. This environment intentionally has no game-reference secrets. The required `sts2.dll`, `0Harmony.dll`, `GodotSharp.dll`, Godot executable, and Spine binaries remain on the release operator's machine. The launcher copies the private DLL inputs only into its ephemeral directory and invokes Godot from its installed location.

Use this sequence for each `v0.1.x` release:

1. Dispatch **Protected game contract** for the exact commit already on `main`, approve `game-contract`, then start an elevated `Contract` runner as described under [Protected Contract](#protected-contract).
2. After the Contract succeeds, create and push the supported tag, for example `git tag v0.1.1 <sha>` followed by `git push origin v0.1.1`.
3. Approve the queued **GitHub Release** job in `release-production`.
4. From a normal PowerShell session, start a fresh runner with `-RunnerPurpose Release`. Use the short-lived token, version, and archive SHA-256 from **Settings > Actions > Runners > New self-hosted runner**:

```powershell
.\tools\private-contract\Start-EphemeralContractRunner.ps1 `
  -RunnerPurpose Release `
  -RegistrationToken '<short-lived-token>' `
  -RunnerVersion '<runner-version>' `
  -RunnerArchiveSha256 '<official-archive-sha256>'
```

The release job validates the tag, `main` ancestry, Contract attestation, read-only inputs, and fixed Spine hashes before publishing the allowlisted ZIP. To repair an existing release, dispatch **GitHub Release** with its existing tag, approve `release-production`, and start a new `Release` runner; the existing ZIP asset is replaced.

Local Workshop publishing is deliberately fail-closed:

```powershell
dotnet msbuild .\NinjaSlayer.csproj -t:PublishWorkshop `
  -p:Configuration=Release `
  -p:NinjaSlayerVersion=0.1.x `
  -p:PublishWorkshopConfirmed=true
```

The command succeeds only on a clean exact matching tag. The manual GitHub Workshop workflow separately requires an existing Release, the confirmation text `PUBLISH_NINJASLAYER_3761570842`, approval through the `workshop-production` environment, and `STEAM_USERNAME` / `STEAM_CONFIG_VDF` secrets.

## Worker

The Worker lives in `Infrastructure/telemetry-worker`.

```powershell
cd .\Infrastructure\telemetry-worker
npm ci
npm test
npx wrangler deploy --dry-run
```

Production requires `POSTHOG_API_KEY` and `RATE_LIMIT_SALT` secrets, the two configured rate-limit bindings, and `FEEDBACK_KV`. Missing security bindings cause a `503` response. See `Docs/privacy.md` for data handling and retention.

Wrangler is pinned to `4.113.0`. Its local Miniflare dependency currently carries the upstream Sharp/libvips advisory documented in `Docs/dependency-security.md`; production dependencies are audited separately, and the project does not use `npm audit fix --force` to apply an incompatible downgrade.

This repository intentionally does not declare a license. Do not infer reuse rights from source availability.

## Protected Contract

The manual `Protected game contract` workflow accepts only a reviewed full SHA that is already on canonical `main`, then requires approval through the `game-contract` environment. Its workflow and build harness come from protected `main`; candidate project files, targets, tests, NuGet configuration, and workflows are never evaluated with the private game references mounted.

The job runs only on an ephemeral self-hosted Windows runner labeled `ninjaslayer-contract`. From an elevated Windows PowerShell session, call `tools/private-contract/Start-EphemeralContractRunner.ps1 -RunnerPurpose Contract` with the short-lived registration token, exact runner version, and archive SHA-256 shown by GitHub's **New self-hosted runner** page. The host must have a .NET 9 SDK/runtime and Godot 4.5.1 Mono installed. The optional `RunnerArchivePath` supports a pre-downloaded official archive when elevated network access is unreliable; its SHA-256 is still mandatory. The launcher creates an isolated read-only copy of `sts2.dll`, `0Harmony.dll`, and `GodotSharp.dll`, plus a temporary .NET 9-only runtime root for Godot; no private binary is uploaded to GitHub or placed in a cache.

The job compiles candidate source through `tools/private-contract`, runs the trusted RitsuLib contracts with outbound `dotnet` traffic blocked, uploads only a small JSON attestation, and removes the candidate checkout, build output, and isolated NuGet folder. The ephemeral launcher then removes its runner, work directory, and private reference copy. The workflow must not be converted to `pull_request_target` or given access to fork commits.
