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

All current card classes resolve to dedicated class-name portraits. `Docs/placeholder-assets.json` contains no outstanding card-art aliases.

Standard card portraits use `1000x760` and Ancient card portraits use `606x852`. `KarateStraight.png` remains at its restored source size of `1438x1093`, and the unchanged Shuriken Token `ShurikenCard.png` remains at `1439x1093`, as explicit source-art exceptions.

## Relic, Potion, Ancient, And Enchantment Images

Relic assets live in `NinjaSlayer/images/relics/` and use `{RelicClassName}.png`, `{RelicClassName}_outline.png`, and `{RelicClassName}_large.png`. Potion assets live in `NinjaSlayer/images/potions/` and use `{PotionClassName}.png` plus `{PotionClassName}_outline.png`.

All current relic classes use dedicated class-name icon sets.

`ZbrAmpoulePotion.png` is a dedicated `256x256` transparent potion icon and `ZbrAmpoulePotion_outline.png` is its pure-white silhouette. Its Naraku contents follow the burning VFX palette: black `#000000`, bright violet `#ff31ff`, and deep violet `#2b00ff`.

Nancy Lee uses dedicated Ancient presentation assets:

- `NinjaSlayer/images/ancients/NancyLeeMapIcon.png` and `NancyLeeMapIcon_outline.png` at `278x278`
- `NinjaSlayer/images/ancients/NancyLeeRunHistoryIcon.png` and `NancyLeeRunHistoryIcon_outline.png` at `128x128`

Her authoritative identity sheet is archived at `../art-production/output/ancillary-art/references/NancyLee-character-reference.webp`; generated Nancy assets preserve its blonde high bun, long side lock, paired dark hair sticks, cyan eyes, and black/navy armored ninja suit with cyan piping.

`NinjaSlayer/images/enchantments/BlackFlameEnchantment.png` is a dedicated `64x64` transparent icon using the same black and vivid-violet Naraku palette.

## Power Icons

Power icons resolve through `Content/NinjaSlayerPowerAssets.For(...)` from `NinjaSlayer/images/powers/{PowerClassName}.png`. Every concrete mod Power class has a dedicated 256x256 transparent PNG; no shared fallback icon remains. `IaiPower.png` is the dedicated Iai icon.

## Monster And Event Images

Dark Ninja uses two dedicated full-body poses on the same `733x649` transparent combat canvas:

- `NinjaSlayer/images/monsters/dark_ninja_standing.png` is the complete standing pose used by the Glory event and the first player turn.
- `NinjaSlayer/images/monsters/dark_ninja.png` is the combat pose used after Dark Ninja begins its first move.

`NinjaSlayer/images/monsters/dark_ninja_blade_glow.png` is the project-local white additive mask used to reveal the curved blade charge from the blade base to the tip.

Dark Strike uses two project-local, pixel-aligned presentation layers derived from that same image:

- `NinjaSlayer/images/monsters/dark_ninja_character.png`
- `NinjaSlayer/images/monsters/dark_ninja_sword.png` (blade-only cutout, no hilt)

Both layers remain `733x649`, share the full image origin, and recombine without an alignment offset. Runtime combat code must load these project resources rather than the external cutout workspace.
