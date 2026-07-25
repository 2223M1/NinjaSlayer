# NinjaSlayer Asset Manifest

This manifest records the asset conventions used by the mod. Confirmed temporary art reuse is tracked separately in the machine-readable `Docs/placeholder-assets.json` inventory.

## FMOD Bank

Runtime FMOD exports live at:

- `NinjaSlayer/audio/fmod/NinjaSlayer.bank`
- `NinjaSlayer/audio/fmod/GUIDs.txt`

Source WAV files and FMOD Studio project files are development inputs and are not loaded directly by the game.

## Character Images

The character scene uses these namespaced resources:

- `NinjaSlayer/images/characters/ninja_slayer/idle/NinjaSlayer_idle_0001.png` through `NinjaSlayer_idle_0022.png` at 24 fps
- `NinjaSlayer/images/characters/ninja_slayer/naraku_idle/NinjaSlayer_naraku_idle_0001.png` through `NinjaSlayer_naraku_idle_0022.png` at 24 fps
- `NinjaSlayer/images/characters/ninja_slayer/attack/attack_0001.png`
- `NinjaSlayer/images/characters/ninja_slayer/cast/cast_0001.png`
- `NinjaSlayer/images/characters/ninja_slayer/hit/hit_0001.png`
- `NinjaSlayer/images/characters/ninja_slayer/dead/dead_0001.png`
- `NinjaSlayer/images/characters/ninja_slayer/relaxed/relaxed_0001.png`
- `NinjaSlayer/images/characters/ninja_slayer/naraku.png`
- `NinjaSlayer/scenes/creature_visuals/ninja_slayer.tscn`

Normal, Naraku, fully released Naraku, and One Body One Soul body resources and presentation policies are centralized in `Content/NinjaSlayerFormPresentation.cs`.

Use `NinjaSlayer/images/characters/ninja_slayer/` for future character UI replacements.

## Card Images

Card metadata is centralized in `NinjaSlayerCardSpec`. A card without an asset alias resolves to `NinjaSlayer/images/cards/{ClassName}.png`; an explicit `AssetName` resolves to the named shared portrait.

The foundational shared portraits include:

- `BlockCard.png`
- `BrewTea.png`
- `BurningCard.png`
- `ChadoCard.png`
- `Chop.png`
- `ComboFist.png`
- `IrcTerminal.png`
- `KarateFinish.png`
- `KarateStraight.png`
- `ShurikenBarrage.png`
- `ShurikenSpread.png`
- `ShurikenThrow.png`

New cards should use a `{ClassName}.png`. Temporary aliases must also be recorded in `Docs/placeholder-assets.json`; replacing an alias with dedicated art requires removing both its `AssetName` and inventory entry.

## Relic And Potion Images

Relic assets live in `NinjaSlayer/images/relics/` and use `{RelicClassName}.png`, `{RelicClassName}_outline.png`, and `{RelicClassName}_large.png`. Potion assets live in `NinjaSlayer/images/potions/` and use `{PotionClassName}.png` plus `{PotionClassName}_outline.png`.

The Nancy relics currently share the `PortableIrcTerminalRelic` icon set. Their target names are recorded in `Docs/placeholder-assets.json`.

## Power Icons

Power icons resolve through `Content/NinjaSlayerPowerAssets.For(...)` from `NinjaSlayer/images/powers/{PowerClassName}.png`. The project currently ships both `OpeningPower.png` and `soar_power.png`. A power without dedicated art falls back to `soar_power.png`; this fallback is recorded in `Docs/placeholder-assets.json`.
