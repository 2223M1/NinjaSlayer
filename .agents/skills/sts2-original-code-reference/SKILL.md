---
name: sts2-original-code-reference
description: Use when implementing, reviewing, or debugging Slay the Spire 2 mod behavior that is not directly covered by RitsuLib/tutorial documentation. Trigger for requests to follow original game code, compare against vanilla cards/relics/powers/characters/events/audio/animation, or find similar original implementations before writing mod code.
---

# STS2 Original Code Reference

## Required Workflow

1. Find the repository root containing `eng/compatibility.json`. Read the active stable and preview `gameApiVersion` values from that file, then locate matching original-code exports. They normally live under the sibling directory `../Slay the Spire 2/Slay the Spire 2 <version>/src`.
2. If an exact export is missing, use the newest available export only as an advisory reference and report the version mismatch. Never silently treat a stale export as the active host.
3. Search the original code before inventing behavior when the RitsuLib tutorial does not give an example for the specific feature.
4. Prefer the closest original implementation by gameplay behavior, not just by name. For example, use `Spite` for "lost HP this turn", `Shiv` for generated knife-like attacks, and `SelfFormingClayPower` or `TheGambitPower` for damage-received hooks.
5. Follow original command order, hook timing, animation trigger style, card tags, history queries, and model property conventions where compatible with mod code.
6. Do not copy large chunks of original code into a skill or final answer. Use local searches and cite file paths or class names as the implementation reference.
7. If original code relies on private/internal APIs or patterns that cannot be used from a mod, implement a compatible RitsuLib/project-local equivalent and state the difference.

## Reference Map

Read `references/original-code-map.md` for common paths and search commands.

## Search First

Use `rg` before opening broad directories:

```powershell
$compatibility = Get-Content -Raw eng/compatibility.json | ConvertFrom-Json
$sourceExports = Resolve-Path '../Slay the Spire 2'
$stableSource = Join-Path $sourceExports "Slay the Spire 2 $($compatibility.channels.stable.gameApiVersion)/src"
$previewSource = Join-Path $sourceExports "Slay the Spire 2 $($compatibility.channels.preview.gameApiVersion)/src"
rg -n "class Shiv|CardTag\.Shiv|NShivThrowVfx" $stableSource $previewSource
rg -n "DamageReceivedEntry|HappenedThisTurn|UnblockedDamage" $stableSource $previewSource
rg -n "TriggerAnim|WithAttackerAnim|AttackAnimDelay|CastAnimDelay" $stableSource $previewSource
```

Open only the specific matching files needed for the current task.
