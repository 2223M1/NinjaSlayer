# NinjaSlayer Asset Manifest

This manifest lists the real assets still needed by the mod. Placeholder files are intentionally text-only so Godot does not try to import invalid PNG, WAV, or FMOD bank files.

## FMOD Bank

Place FMOD Studio export files here:

- `NinjaSlayer/audio/fmod/NinjaSlayer.bank`
- `NinjaSlayer/audio/fmod/GUIDs.txt`

Register events in FMOD Studio with these paths:

- `event:/NinjaSlayer/character/select`
- `event:/NinjaSlayer/character/transition`
- `event:/NinjaSlayer/character/attack`
- `event:/NinjaSlayer/character/cast`
- `event:/NinjaSlayer/character/death`
- `event:/NinjaSlayer/character/hurt`

Suggested source WAV names for `NinjaSlayer/audio/sources/`:

- `character_select.wav`
- `character_transition.wav`
- `character_attack.wav`
- `character_cast.wav`
- `character_death.wav`
- `character_hurt.wav`

## Character Images

Current character code references these existing files:

- `NinjaSlayer/images/characters/ninja_slayer/idle/NinjaSlayer_idle_0001.png` through `NinjaSlayer_idle_0022.png` (24 fps)
- `NinjaSlayer/images/characters/ninja_slayer/naraku_idle/NinjaSlayer_naraku_idle_0001.png` through `NinjaSlayer_naraku_idle_0022.png` (24 fps)
- `NinjaSlayer/images/characters/ninja_slayer/attack/attack_0001.png`
- `NinjaSlayer/images/characters/ninja_slayer/cast/cast_0001.png`
- `NinjaSlayer/images/characters/ninja_slayer/hit/hit_0001.png`
- `NinjaSlayer/images/characters/ninja_slayer/dead/dead_0001.png`
- `NinjaSlayer/images/characters/ninja_slayer/relaxed/relaxed_0001.png`
- `NinjaSlayer/images/characters/ninja_slayer/naraku.png`
- `NinjaSlayer/scenes/creature_visuals/ninja_slayer.tscn`

Use `NinjaSlayer/images/characters/ninja_slayer/` for future namespaced character UI replacements.

## Card Images

Place card portraits in `NinjaSlayer/images/cards/`:

- `BlackFlame.png`
- `BurningCard.png`
- `ChadoCard.png`
- `Chop.png`
- `DefendNinjaSlayer.png`
- `DragonTornado.png`
- `GiantShurikenCard.png`
- `GreatUke.png`
- `IrcTerminal.png`
- `KarateFinish.png`
- `KarateStraight.png`
- `KillingIntent.png`
- `Meditation.png`
- `NarakuWithin.png`
- `OneBodyOneSoul.png`
- `PerfectFit.png`
- `RedBlackFlame.png`
- `ShurikenBarrage.png`
- `ShurikenCard.png`
- `ShurikenThrow.png`
- `SmokeRead.png`
- `StraightKi.png`
- `StrikeNinjaSlayer.png`
- `TrueNameRead.png`

## Relic Images

Place relic images in `NinjaSlayer/images/relics/`. Each relic needs small, outline, and large files:

- `BlanketRelic.png`
- `BlanketRelic_outline.png`
- `BlanketRelic_large.png`
- `ChadoBreathingRelic.png`
- `ChadoBreathingRelic_outline.png`
- `ChadoBreathingRelic_large.png`
- `DeepChadoBreathingRelic.png`
- `DeepChadoBreathingRelic_outline.png`
- `DeepChadoBreathingRelic_large.png`
- `MaguroSushiRelic.png`
- `MaguroSushiRelic_outline.png`
- `MaguroSushiRelic_large.png`
- `PortableIrcTerminalRelic.png`
- `PortableIrcTerminalRelic_outline.png`
- `PortableIrcTerminalRelic_large.png`

## Potion Images

Place potion images in `NinjaSlayer/images/potions/`:

- `ZbrAmpoulePotion.png`
- `ZbrAmpoulePotion_outline.png`

## Power Icons

Power icons load through `Content/NinjaSlayerPowerAssets.For(...)` from `NinjaSlayer/images/powers/{PowerClassName}.png`. The project currently ships `OpeningPower.png` and `soar_power.png`; every other power falls back to the shared icon at runtime. Drop a `{PowerClassName}.png` into the directory to replace that fallback without changing code.

## Card Art Naming

Card portraits resolve from the card's class name (`res://NinjaSlayer/images/cards/{ClassName}.png`). The generic-named cards were renamed to meaningful English (e.g. `SkillBlue2`→`ReadyBlade`, `AttackGold2`→`StunStrike`); their PNG + `.import` files were renamed to match. New cards just need a `{ClassName}.png`.
