# STS2 Original Code Map

## Root Paths

- Active versions: `eng/compatibility.json` at the repository root.
- Source exports: normally `../Slay the Spire 2/Slay the Spire 2 <gameApiVersion>/src`.
- Use an older export only as an explicitly identified advisory reference when the active version has not been exported yet.
- Cards: `src\Core\Models\Cards`
- Powers: `src\Core\Models\Powers`
- Relics: `src\Core\Models\Relics`
- Potions: `src\Core\Models\Potions`
- Characters: `src\Core\Models\Characters`
- Card pools: `src\Core\Models\CardPools`
- Commands: `src\Core\Commands`
- Combat history: `src\Core\Combat\History`
- Value props: `src\Core\ValueProps`
- Nodes and VFX: `src\Core\Nodes`
- Audio proxies: `src\gdscript\audio_manager_proxy.gd`, `src\Core\Audio`
- Localization: `localization`
- Images and animations: `images`, `animations`

## Common Reference Classes

- Generated knife attack: `Shiv`, `BladeDance`, `Accuracy`
- Lost HP this turn: `Spite`
- Damage received tracking: `DamageReceivedEntry`, `CombatHistory`
- Damage received hooks: `SelfFormingClayPower`, `TheGambitPower`
- Temporary strength or temporary buffs: search `TemporaryStrength`, `Feed`, `Prepared`
- Card draw/discard selection: `Prepared`, `Reflex`, `ToolsOfTheTrade`
- Attack command and hit FX: `AttackCommand`, `DamageCmd.Attack`, `WithAttackerAnim`, `WithHitFx`
- Animation triggering: `CreatureCmd.TriggerAnim`, `CharacterModel.AttackAnimDelay`, `CharacterModel.CastAnimDelay`
- Card tags: `CardTag`, `CardTag.Shiv`
- Character asset profiles: search `CharacterAssetProfile`, `CharacterAssetProfiles`
- Card transform or reward pools: search `Transform`, `CardReward`, `CardPool`
- FMOD behavior: `FmodSfx`, `audio_manager_proxy.gd`

## Query Templates

```powershell
$compatibility = Get-Content -Raw eng/compatibility.json | ConvertFrom-Json
$sourceExports = Resolve-Path '../Slay the Spire 2'
$stableSource = Join-Path $sourceExports "Slay the Spire 2 $($compatibility.channels.stable.gameApiVersion)/src"
$previewSource = Join-Path $sourceExports "Slay the Spire 2 $($compatibility.channels.preview.gameApiVersion)/src"
rg -n "class <Name>|<ExactApi>|<Keyword>" $stableSource $previewSource
rg -n "DamageReceivedEntry|HappenedThisTurn|UnblockedDamage|BlockedDamage" $stableSource $previewSource
rg -n "WithAttackerAnim|WithHitFx|NShivThrowVfx|TriggerAnim" $stableSource $previewSource
rg -n "CardTag\.Shiv|CanonicalTags|Register|StartingDeck" $stableSource $previewSource
```

## Use Rules

- Match behavior first, then API shape.
- Prefer original command/hook sequencing when implementing game logic.
- For generated cards, compare cost, rarity, token/status properties, tags, target type, VFX, and draw/discard side effects.
- For text and localization, compare wording patterns in `localization`.
- For resources, compare dimensions and paths in `images`, `animations`, and `scenes`.
