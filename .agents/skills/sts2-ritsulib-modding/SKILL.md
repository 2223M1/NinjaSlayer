---
name: sts2-ritsulib-modding
description: Use for NinjaSlayer feature implementation and bug fixes whose external integration depends on documented RitsuLib APIs, content or Patch registration, resources, localization, FMOD audio, the card catalog, or Godot build and export behavior. Architecture review and deletion-first refactoring belong to ninjaslayer-code-quality; canon, naming, characterization, and flavor belong to ninjaslayer-lore.
---

# STS2 RitsuLib Modding

## Scope

This skill owns the external integration shape between NinjaSlayer, RitsuLib, Godot, and the packaged Mod. It does not own internal architecture, novel canon, prose style, or speculative compatibility.

Use `references/ninjaslayer-doc-map.md` to locate the applicable local tutorial, installed package evidence, card catalog, resource conventions, and validation commands.

## Route Before Work

- If canon, characterization, relationships, flavor, naming, or theme affects the result, use `ninjaslayer-lore` first and treat its evidence brief as an input.
- If the requested deliverable is event prose, dialogue, flavor text, or substantial localization, establish lore evidence first. Use `$ninjaslayer-writing` only when the user explicitly invokes it; this skill remains responsible only for integration, resource shape, and validation.
- If the task is architecture review, ownership cleanup, compatibility simplification, or AI-slop removal, hand primary responsibility to `ninjaslayer-code-quality`.
- Use `sts2-original-code-reference` only when the installed public API and current tutorial do not establish one precise vanilla behavior.

## Evidence Workflow

1. Find the repository root containing `NinjaSlayer.csproj` and read the active channel definitions from `eng/compatibility.json` when host behavior is involved.
2. Identify the exact player-observable behavior and the external boundary that must implement it.
3. Inspect the installed RitsuLib API and the closest applicable current tutorial. The installed API wins when tutorial text and compiled signatures differ.
4. Trace the current production registration and caller path. Existing project code is evidence to understand, not a requirement to reproduce its internal layering.
5. Extract only the necessary external shape: base class, override or interface signature, attribute, registration call, Patch target, resource path, localization structure, audio event, and command order.
6. Implement the smallest direct change that satisfies that shape and the requested behavior.

Stop once authoritative evidence answers the external-contract question. Do not accumulate helpers, wrappers, or examples after the required shape is known.

## Feature And Bug-Fix Rules

### Feature implementation

- Keep gameplay ownership with the feature that creates and consumes it.
- Prefer public RitsuLib content, lifecycle, command, and registration APIs over new project-local infrastructure.
- Do not copy tutorial helper layers unless they independently represent a real boundary or policy in NinjaSlayer.
- When lore supplied the concept, preserve the classified canon facts and explicitly identified design inference. Do not turn a design inference into an asserted setting fact in localization or comments.

### Bug fix

- State the player-observable failure and the violated external contract.
- Fix the producer of the bad state. Do not translate an internal invariant failure into `null`, `false`, an empty collection, a default value, or a silent fallback.
- If the proposed repair starts rebuilding runtime gates, feature states, compatibility discovery, or a wrapper hierarchy, stop and route that work to `ninjaslayer-code-quality`.

### Architecture refactor

Stop architecture work and hand primary responsibility to `ninjaslayer-code-quality`. After that workflow identifies deletion and ownership changes, use this skill only to verify that surviving RitsuLib, Godot, resource, localization, and packaging contracts remain correct.

## Patch Registration And Transactions

- Define ordinary static Patches as `IPatchMethod` with `PatchId`, `Description`, `IsCritical`, exact `ModPatchTarget` values, and the required Harmony callback.
- Register Patches centrally in `Scripts/Entry.cs` through the RitsuLib `ModPatcher` API. Use `IModPatches` only for a cohesive set that is installed and rolled back together; do not wrap one Patch merely to create a group.
- Required gameplay Patches form one exact transaction. Any incomplete required installation must abort initialization after verified rollback.
- A genuinely optional presentation or telemetry area may use its own named transaction. It may degrade only after rollback has been verified and no static or dynamic Harmony ownership remains.
- Use `DynamicPatchInfo` only when the exact target cannot be expressed as a static `ModPatchTarget`. Preserve the resolved `OriginalMethod` targets through the failure path so rollback verification checks those targets directly; a zero aggregate count alone is not proof that dynamic owners were removed.
- Do not classify required gameplay behavior as optional merely to continue after failure.

## Content, Naming, And Localization

- Read the relevant current localization and `Docs/card-catalog.md` before adding or renaming cards, relics, powers, events, or keywords.
- Use `ninjaslayer-lore` for canon-derived names, character voice, episode references, and thematic claims. This skill only maps the approved text and design into the required game/resource structures.
- Preserve established identifiers separately from displayed text. Do not rename serialized IDs or resource paths merely to improve prose.
- When card behavior, displayed text, cost, numbers, upgrades, or formulas change, update `Docs/card-catalog.md` in the same task. Otherwise state why the catalog did not change.

## Project Rules To Preserve

- Use `NinjaSlayer`, not `Ninjaslayer`.
- Runtime resources live under `res://NinjaSlayer/...`.
- Localization lives under `NinjaSlayer/localization/{Language}/...`.
- Keep audio in the FMOD bank and event pipeline. Do not add direct WAV playback fallback unless explicitly requested.
- Keep replaceable art under clearly named paths in `NinjaSlayer/images/...`.
- Supported-host differences remain compile-time and feature-local. Do not reintroduce a global compatibility facade, runtime capability graph, feature-state registry, method-body fingerprint platform, or general reflection layer.

## Completion Checks

- Confirm the modified integration code contains the expected public API, signature, attribute, registration, Patch target, resource path, and localization shape.
- Use the exact command-selection rules in `references/ninjaslayer-doc-map.md`.
- A normal local build verifies only the configured channel, normally `preview`; do not report it as stable-and-preview proof.
- Changed Patch targets, host-dependent signatures, or compile-time host branches require the protected contracts against the exact active stable and preview inputs. Report unavailable inputs as `NOT RUN`.
- Check the card-catalog obligation and any lore evidence handoff.
- Report any mismatch among the tutorial, installed API, current production path, and implemented external shape.
