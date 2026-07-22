# NinjaSlayer

NinjaSlayer is a Slay the Spire 2 character mod built with Godot 4.5.1 Mono and RitsuLib. The repository contains the gameplay code, Godot resources, FMOD bank, telemetry/feedback Worker, and explicit packaging/release automation.

## Compatibility

| Component | Supported version |
|---|---|
| Slay the Spire 2 | `0.109.x` (`min_game_version`: `0.109.0`) |
| .NET target | `net9.0` |
| Godot | `4.5.1` Mono |
| RitsuLib | `0.4.58` or newer compatible release |
| Spine GDExtension | Godot 4.5.1 build committed under `addons/spine` |

Local distributable builds must compile against the `sts2.dll` and `0Harmony.dll` from the target game installation. `Book.StS2.RefLib 0.107.1` is used only as a best-effort CI compatibility check and does not lower the manifest's game requirement.

## Build Commands

Ordinary builds only compile. They do not export a PCK, install files, or upload Workshop content.

```powershell
dotnet restore .\NinjaSlayer.csproj
dotnet build .\NinjaSlayer.csproj --no-restore -v:minimal
```

Run dependency-free logic checks:

```powershell
dotnet run --project .\Tests\NinjaSlayer.LogicTests\NinjaSlayer.LogicTests.csproj -c Release
node .\tools\validate-repository.mjs
```

The checked-in `global.json` requires a .NET 9 SDK and prevents Godot 4.5.1 Mono from accidentally loading an incompatible .NET 10 MSBuild toolset.

Create and install a complete local package explicitly:

```powershell
dotnet msbuild .\NinjaSlayer.csproj -t:PackageMod -p:Configuration=Release
dotnet msbuild .\NinjaSlayer.csproj -t:InstallLocal -p:Configuration=Release
```

`PackageMod` exports `NinjaSlayer.dll`, versioned `NinjaSlayer.json`, `NinjaSlayer.pck`, and `SHA256SUMS` under `build/mods/NinjaSlayer`. `InstallLocal` packages first, copies those files into the game Mods directory, and verifies every copied file by SHA-256.

## Versions And Releases

- A clean exact `vX.Y.Z` tag produces version `X.Y.Z`.
- Normal commits produce `X.Y.Z-dev.N+gCOMMIT`; dirty trees include `.dirty`.
- GitHub Release automation accepts only strict `vX.Y.Z` tags whose commit belongs to `main`.
- The release workflow requires the `STS2_REFERENCE_BUNDLE_URL` secret to point to a private ZIP containing the target `sts2.dll` and `0Harmony.dll`.
- GitHub Releases never publish Steam Workshop content.

Local Workshop publishing is deliberately fail-closed:

```powershell
dotnet msbuild .\NinjaSlayer.csproj -t:PublishWorkshop `
  -p:Configuration=Release `
  -p:NinjaSlayerVersion=X.Y.Z `
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

This repository intentionally does not declare a license. Do not infer reuse rights from source availability.
