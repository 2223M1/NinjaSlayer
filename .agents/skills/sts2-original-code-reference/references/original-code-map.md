# STS2 Original Code Map

## Active Source Roots

- Read active versions from `eng/compatibility.json` at the repository root.
- Source exports normally live at `../Slay the Spire 2/Slay the Spire 2 v<gameApiVersion>/src`.
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
- Audio: `src\gdscript\audio_manager_proxy.gd` and `src\Core\Audio`
- Serialization: locate the exact model, serializer context, converter, or save-data type named by the task.

An export for a different version is not evidence of current behavior. Must not reintroduce speculative historical alignment; producer-specific historical evidence requires the exact version and a real fixture from it.

## Current Host-Difference Examples

- Prefer a compile-time branch inside the owning feature, as in `Code/Lifecycle/RapidCardPresentationContext.cs` and `Code/ExternalAnimations/ArchitectExecutionCinematic.cs`.
- `Code/ExternalAnimations/CreatureDeathInteractionAdapter.cs` isolates the stable manual interaction shutdown from preview's public death-interaction API.
- `Code/ExternalAnimations/FinisherAttackCommandAdapter.cs` isolates the stable card-play lookup from preview's `AttackCommand.CardPlay` API.
- These examples are feature boundaries, not precedent for a global host abstraction. Must not reintroduce the retired compatibility facade or adapter platform.

## Exact Query Templates

```powershell
$compatibility = Get-Content -Raw eng/compatibility.json | ConvertFrom-Json
$sourceExports = Resolve-Path '../Slay the Spire 2'
$stableSource = Join-Path $sourceExports "Slay the Spire 2 v$($compatibility.channels.stable.gameApiVersion)/src"
$previewSource = Join-Path $sourceExports "Slay the Spire 2 v$($compatibility.channels.preview.gameApiVersion)/src"

rg -n -F '<ExactTypeOrMember>' $stableSource $previewSource
rg -n -F '<ExactMethodSignatureFragment>' $stableSource $previewSource
rg -n -F '<ExactSerializedFieldOrKey>' $stableSource $previewSource
rg -n -F '<ExactAnimationOrAudioCall>' $stableSource $previewSource
```

Replace each placeholder with an identifier already established by the task, public API, runtime signature, or exact call site. Do not replace it with a broad keyword, arbitrary prefix, or guessed analogue.

## Evidence To Record

- Host version and exact source path.
- Preconditions and guard conditions.
- Command and hook order.
- Player-observable state changes and side effects.
- Animation and audio timing.
- Current serialization fields, keys, defaults, and ordering when serialization is in scope.

Do not copy the original inheritance tree, managers, registries, helpers, or service structure into the Mod unless a separate code-quality analysis proves that structure is required locally.
