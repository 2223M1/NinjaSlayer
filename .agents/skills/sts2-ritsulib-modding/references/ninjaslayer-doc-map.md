# NinjaSlayer STS2/RitsuLib Documentation Map

## Local Project

- Repository root: the directory containing `NinjaSlayer.csproj`.
- Local tutorial mirror: `Docs/tutorials.sts2modding.com`.
- RitsuLib docs: `Docs/tutorials.sts2modding.com/docs/04-ritsulib`.
- Current RitsuLib guide mirror: `Docs/sts2-ritsulib.ritsukage.com`.
- Card catalog: `Docs/card-catalog.md` tracks current card categories, descriptions, gameplay effects, costs, numbers, upgrade values, and dynamic formulas.

## Read Before Editing

Select the closest chapter and read its `index.md`. Paths below are relative to the repository root. For card description, behavior, cost, number, upgrade, or formula changes, also update the card catalog.

- Add a card: `Docs/tutorials.sts2modding.com/docs/04-ritsulib/04-01-add-card/index.md`
- Add a relic: `Docs/tutorials.sts2modding.com/docs/04-ritsulib/04-03-add-relic/index.md`
- Card properties, custom tags, starter cards: `Docs/tutorials.sts2modding.com/docs/04-ritsulib/04-04-card-properties/index.md`
- Add a power: `Docs/tutorials.sts2modding.com/docs/04-ritsulib/04-05-add-power/index.md`
- Add a potion: `Docs/tutorials.sts2modding.com/docs/04-ritsulib/04-06-add-potion/index.md`
- Add audio: `Docs/tutorials.sts2modding.com/docs/04-ritsulib/04-10-add-audio/index.md`
- Add a new character: `Docs/tutorials.sts2modding.com/docs/04-ritsulib/04-14-add-new-character/index.md`
- Add a card pool: `Docs/tutorials.sts2modding.com/docs/04-ritsulib/04-15-1-add-card-pool/index.md`
- Character animation: `Docs/tutorials.sts2modding.com/docs/04-ritsulib/04-15-2-character-animation/index.md`
- Add singleton hooks: `Docs/tutorials.sts2modding.com/docs/04-ritsulib/04-15-add-singleton/index.md`
- Patch system: `Docs/tutorials.sts2modding.com/docs/04-ritsulib/04-24-patch-system/index.md`
- Runtime content registry: `Docs/tutorials.sts2modding.com/docs/04-ritsulib/04-27-content-registry/index.md`
- Current Patch guide: `Docs/sts2-ritsulib.ritsukage.com/guide/patching-guide/index.md`

## External Fallback

If the local mirror is missing or clearly stale, use `https://tutorials.sts2modding.com/` as the source of truth and report the mismatch. Updating the mirror is a separate documentation-sync task.

## Build And Static Checks

From the repository root:

```powershell
dotnet restore .\NinjaSlayer.csproj
dotnet build .\NinjaSlayer.csproj --no-restore -c Release -v:minimal
dotnet test .\Tests\NinjaSlayer.LogicTests\NinjaSlayer.LogicTests.csproj -c Release -v:minimal
node .\tools\sync-compatibility.mjs --check
node .\tools\validate-repository.mjs
node .\tools\test-build-boundaries.mjs
git diff --check
```

The ordinary build uses the configured `NinjaSlayerHostChannel`, which defaults to `preview`. Run it for production C# or export-affecting resource changes. Run the logic tests for affected pure behavior, `test-build-boundaries.mjs` for project/build/package-boundary changes, and the repository checks for every completed change. Stable-and-preview Patch or API claims require the protected contract workflow with both exact game inputs.

Useful checks:

```powershell
rg -n "RegisterCard|RegisterRelic|RegisterPower|RegisterPotion|RegisterCharacter" . --glob "*.cs" --glob "!Tests/**" --glob "!tools/**" --glob "!build/**"
rg -n "IPatchMethod|IModPatches|RegisterPatch|RegisterPatches|ApplyRequiredPatcher" Code/Patches Scripts/Entry.cs --glob "*.cs"
rg -n "res://assets/|res://localization|card\.xx|relic\.xx|character\.xx" . --glob "!Docs/**" --glob "!.agents/**" --glob "!Tests/**" --glob "!tools/**" --glob "!build/**" --glob "!obj/**" --glob "!bin/**"
rg -n "FmodStudioDeferredBankRegistration|RegisterStudioGuidMappings" Scripts Content --glob "*.cs"
```

The legacy resource-path search is expected to return no matches; for that one check, `rg` exit code 1 means the repository contains none of the retired paths.
