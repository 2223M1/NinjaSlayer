---
name: sts2-ritsulib-modding
description: Use for NinjaSlayer feature implementation and bug fixes whose external integration depends on documented RitsuLib APIs, content registration, resources, localization, FMOD audio, the card catalog, or Godot build and export behavior. Architecture review and refactoring belong to ninjaslayer-code-quality.
---

# STS2 RitsuLib Modding

## Scope

This skill owns the external integration shape between NinjaSlayer, RitsuLib, Godot, and the mod package. It is not the primary skill for architecture review, compatibility cleanup, or internal refactoring.

Use `references/ninjaslayer-doc-map.md` to locate the relevant local tutorial, card catalog, resource conventions, and build command.

## Task Modes

### Feature implementation

1. Find the repository root containing `NinjaSlayer.csproj`.
2. Locate the exact public RitsuLib API or closest applicable tutorial chapter.
3. Extract only the required external shape: base class, override or interface signature, attribute, registration call, resource path, localization structure, and command order.
4. Implement the smallest direct feature that satisfies that shape and the requested player behavior.

### Bug fix

1. State the player-observable failure and identify the external RitsuLib contract involved.
2. Verify the exact API signature or tutorial rule, then fix the producer of the bad state.
3. Do not add a compatibility graph, silent fallback, or wrapper hierarchy to contain an internal invariant failure.

### Architecture refactor

Stop architecture work and hand primary responsibility to `ninjaslayer-code-quality`. After that workflow identifies the deletion and ownership changes, use this skill only to verify that surviving RitsuLib integration shapes remain correct.

## Source Authority And Stop Rule

- The installed public API and the applicable current tutorial are authoritative for RitsuLib integration facts.
- Stop searching as soon as the first authoritative source answers the specific external-contract question. Do not accumulate extra patterns or layers from additional examples.
- Use `sts2-original-code-reference` only when tutorials and public APIs do not establish an exact vanilla behavior needed by the feature.
- Tutorial helper classes and layering are examples, not architecture requirements. Copy the external contract, not the helper structure.
- Existing `GameCompatibility`, capability, fingerprint, `Policy`, registry, service, or adapter structures do not automatically become precedent for new code.
- Project consistency means consistent public behavior, naming, resources, and integration contracts; it does not require preserving current internal layering.

## Project Rules To Preserve

- Use `NinjaSlayer`, not `Ninjaslayer`.
- Preserve RitsuLib dependency and registration patterns when RitsuLib supplies the required feature.
- Runtime resources live under `res://NinjaSlayer/...` and localization under `NinjaSlayer/localization/{Language}/...`.
- Keep audio in the FMOD bank and event pipeline. Do not add direct WAV playback fallback unless explicitly requested.
- Keep replaceable art under clearly named paths in `NinjaSlayer/images/...`.
- When card behavior, text, cost, numbers, upgrades, or formulas change, update `Docs/card-catalog.md` in the same task. Otherwise state why no catalog update was needed.
- Build from the project root with `dotnet build .\NinjaSlayer.csproj --no-restore -v:minimal` unless restore is required.

## Completion Checks

- Confirm modified integration code contains the expected public API, signature, attribute, registration, and paths.
- Build when code or resources affecting export changed.
- Check the card catalog obligation for card or localization changes.
- Report any mismatch between the tutorial, installed RitsuLib API, and implemented external shape.
