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
- `NinjaSlayer/images/characters/ninja_slayer/kill_idle/NinjaSlayer_kill_idle_0001.png` through `NinjaSlayer_kill_idle_0022.png` at 24 fps
- `NinjaSlayer/images/characters/ninja_slayer/naraku_idle/NinjaSlayer_naraku_idle_0001.png` through `NinjaSlayer_naraku_idle_0022.png` at 24 fps
- `NinjaSlayer/images/characters/ninja_slayer/attack/attack_0001.png`
- `NinjaSlayer/images/characters/ninja_slayer/cast/cast_0001.png`
- `NinjaSlayer/images/characters/ninja_slayer/dead/dead_0001.png`
- `NinjaSlayer/images/characters/ninja_slayer/relaxed/relaxed_0001.png`
- `NinjaSlayer/images/characters/ninja_slayer/naraku.png`
- `NinjaSlayer/scenes/creature_visuals/ninja_slayer.tscn`

Normal, Naraku, fully released Naraku, and One Body One Soul body resources and presentation policies are centralized in `Content/NinjaSlayerFormPresentation.cs`.

The normal `idle` sequence is the accepted `nin-static/runtime-24fps` output from the
`ninja-slayer-idle-mask-states-v5-hd-scarf` batch. The left-facing `kill_idle` sequence keeps those
exact HD body and scarf pixels and replaces only the shared `98x98` mask region with the accepted
static Kill mask from `ninja-slayer-idle-mask-states-v3`. The replacement contains `7,861` RGB
pixels, changes no Alpha, and leaves every pixel outside `x=1474..1571, y=310..407` untouched.
Both sequences remain `1800x1080`; the combat scene continues to render them at Scale `0.33`.

The semi-Naraku `naraku_idle` sequence is the accepted `runtime-22-frame-sequence` output from the
`ninja-slayer-semi-naraku-unified-upper-body-v3-true-smoke-alpha-fix-v3` batch. Its `2100x1080`
frames retain the existing centered overlay canvas and synchronized 22-frame timing; the wider
canvas contains the extended scarf and smoke while the body remains aligned to the normal form's
size, center, and foot baseline through the unchanged source Sprite transform.

Use `NinjaSlayer/images/characters/ninja_slayer/` for future character UI replacements.

## Combat Shadow Images

All custom ground shadows are project-local `510x96 RGBA` textures under
`NinjaSlayer/images/shadows/`. Their visible RGB is pure black, and runtime code does not load the
original atlas or use a Shader. Alpha bounds are recorded per texture because Koki's upper contour
and Sawatari's asymmetric cloak projection intentionally use different authored margins.

The hand-painted Alpha references were cropped from the recovered STS2 `0.110.0` atlases:

- Ironclad: `animations/characters/ironclad/ironclad.png`, `x=200, y=119, w=226, h=42`, Alpha max `101`
- Silent: `animations/characters/silent/silent.png`, `x=392, y=459, w=220, h=62`, Alpha max `100`
- Defect: `animations/characters/defect/defect.png`, `x=2, y=440, w=344, h=81`, Alpha max `89`
- Regent: `animations/characters/regent/regent.png`, `x=414, y=134, w=262, h=62`, Alpha max `68`

The final textures retain the existing scene dimensions and use only crops, affine resizing,
mirroring, contour trimming, and layered Alpha adjustment of those original brush masks. All
grounded shadows except Yukano match Ironclad's authored Alpha distribution. Yukano keeps a light
projection beneath the raised left leg while its grounded right-foot core uses the Ironclad
distribution:

- `ninja_slayer_shadow.png`: Ironclad-derived; Alpha bounds `x=4..505, y=1..94`; SHA-256 `067BA2089D131AD13C87C69E9156E6246CA8ADAB30FC187B1B4452C30CB59C96`
- `yamoto_koki_shadow.png`: Silent-derived with the upper-right rise trimmed; Alpha bounds `x=4..505, y=8..94`; SHA-256 `8724E9FED9759656F9C1C9145523FBEF79DDF3C222F1BBE1B72CC24D89966358`
- `dark_ninja_standing_shadow.png`: Ironclad-derived; Alpha bounds `x=4..505, y=1..94`; SHA-256 `1F3E6D12A38C2EE1EC3639A7387498876FBE636C5144E459CBE64C8137780BCB`
- `dark_ninja_combat_shadow.png`: Defect and Ironclad-derived; Alpha bounds `x=4..505, y=1..94`; SHA-256 `0B8FC40D35064CBC945A0166015C822FB91ED9CE1995B68CB1363EB93EC4211A`
- `sawatari_shadow.png`: Ironclad-calibrated Silent foot core plus a low-Alpha Regent cloak projection; Alpha bounds `x=94..505, y=1..94`; SHA-256 `D83C9F959B9B414A37486B8FC4AD21B30A95B7B2C9346D3B386106C4EAF09A8C`
- `yukano_shadow.png`: lighter Silent and Regent-derived projection with an Ironclad-calibrated grounded right-foot core; Alpha bounds `x=4..505, y=1..94`; SHA-256 `467657682D76BC8224FC405704C6A4D894CF2046148E69631408B5041CEE37CE`

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

Forest Sawatari currently uses `NinjaSlayer/images/monsters/sawatari.png`, a `461x537` transparent empty-hand standing placeholder with SHA-256 `359d03b2bef5eaeedfe6f7596eb2ed2938fe2c9f81f8bb3e504a7da73d832f20`. Both ally and enemy presentations use this one left-facing source; the ally is flipped at runtime. The event text intentionally retains the planned bamboo spear, firearm, and trap choreography until the final combat visuals replace this placeholder.

Yukano uses the complete character images that already include the yellow bow:

- `NinjaSlayer/images/monsters/yukano_closed.png` is the default `1074x1245` combat portrait, SHA-256 `B43A1D01B7623FD8666BA884DD56D02DDFDF5074C8BAA739F560463C7F3CBF1E`.
- `NinjaSlayer/images/monsters/yukano_open.png` is the same-size speaking portrait, SHA-256 `EA0F26DCFE68767C4005511B97046F69B15A983C2E0A57700DA3245DBA5E5426`.
- `NinjaSlayer/images/events/yukano_event.png` is the closed-mouth `1920x1080` event portrait, SHA-256 `E78C69F27FCFC9289DAE44E51D2A749839AC05B2506441A4F5F31B98489DEE91`.
- `NinjaSlayer/images/projectiles/yukano_red_shuriken.png` is the `209x208` shared combat and relic source, SHA-256 `0DBAE503153C90FF949DFC4AC8019596310233A4B061AD28F6D4121D522EEF24`.

`YukanoCompanionRelic.png`, `YukanoCompanionRelic_large.png`, and `YukanoCompanionRelic_outline.png` are centered standard-size derivatives of that red shuriken. Yukano's arrow is loaded at runtime from the host's `CrossbowRubyRaider` `arrow` atlas region; the original game atlas is not copied into the Mod.
