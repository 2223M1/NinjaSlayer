# NinjaSlayer STS2/RitsuLib Documentation Map

## Local Project

- Repository root: the directory containing `NinjaSlayer.csproj`.
- Local tutorial mirror: `Docs/tutorials.sts2modding.com`.
- RitsuLib docs: `Docs/tutorials.sts2modding.com/docs/04-ritsulib`.
- Card catalog: `Docs/card-catalog.md` tracks current card categories, descriptions, gameplay effects, costs, numbers, upgrade values, and dynamic formulas.

## Read Before Editing

Select the closest chapter and read its `index.md`. For card description, behavior, cost, number, upgrade, or formula changes, also update the card catalog.

- Add a card: `docs\04-ritsulib\04-01-add-card\index.md`
- Add a relic: `docs\04-ritsulib\04-03-add-relic\index.md`
- Card properties, custom tags, starter cards: `docs\04-ritsulib\04-04-card-properties\index.md`
- Add a power: `docs\04-ritsulib\04-05-add-power\index.md`
- Add a potion: `docs\04-ritsulib\04-06-add-potion\index.md`
- Add audio: `docs\04-ritsulib\04-10-add-audio\index.md`
- Add a new character: `docs\04-ritsulib\04-14-add-new-character\index.md`
- Add a card pool: `docs\04-ritsulib\04-15-1-add-card-pool\index.md`
- Character animation: `docs\04-ritsulib\04-15-2-character-animation\index.md`
- Add singleton hooks: `docs\04-ritsulib\04-15-add-singleton\index.md`
- Runtime content registry: `docs\04-ritsulib\04-27-content-registry\index.md`

## External Fallback

If the local mirror is missing or clearly stale, use `https://tutorials.sts2modding.com/` as the source of truth and update the local mirror or note that the local mirror was not updated.

## Build And Static Checks

From the repository root:

```powershell
dotnet build .\NinjaSlayer.csproj --no-restore -v:minimal
```

Useful checks:

```powershell
rg "RegisterCard|RegisterRelic|RegisterPower|RegisterPotion|RegisterCharacter" .
rg "res://assets/|res://localization|card\.xx|relic\.xx|character\.xx" .
rg "FmodStudioDeferredBankRegistration|RegisterStudioGuidMappings" .
```
