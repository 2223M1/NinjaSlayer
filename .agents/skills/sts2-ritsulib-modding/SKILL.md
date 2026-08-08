---
name: sts2-ritsulib-modding
description: Use when implementing, reviewing, or debugging Slay the Spire 2 mods that use RitsuLib, especially the NinjaSlayer project. Trigger for STS2 mod cards, relics, powers, potions, characters, card pools, localization, resources, animation, FMOD audio, Godot export/build issues, maintaining the NinjaSlayer card catalog, requests to match tutorials.sts2modding.com examples exactly, and cases where tutorial gaps should be filled by checking original game code.
---

# STS2 RitsuLib Modding

## Required Workflow

1. Find the active STS2 mod project by locating the repository root that contains `NinjaSlayer.csproj`.
2. Before changing code, identify the matching local tutorial chapter under `Docs\tutorials.sts2modding.com`.
3. Read the relevant `index.md` file completely enough to capture the example structure, imports, attributes, resource paths, registration method, and validation notes.
4. If the tutorial has example code for the task, write new code in the same pattern. Keep class shape, attributes, factory methods, resource path style, localization directory style, and registration style aligned with the example unless the installed API will not compile.
5. If the tutorial does not cover the behavior, use the `sts2-original-code-reference` skill and search the original game code for similar behavior before writing new logic.
6. If neither the tutorial nor original game code covers it cleanly, implement the smallest project-consistent solution and tell the user why no direct documented/original pattern was available.
7. When changing a card description, gameplay function, cost, damage, block, status stack amount, upgrade value, or dynamic formula, update `Docs/card-catalog.md` in the same task so the catalog matches the code and localization. If a card-adjacent change does not affect cataloged behavior or numbers, state that explicitly before finalizing.
8. Preserve RitsuLib dependency and automatic registration patterns. Do not replace RitsuLib functionality with unrelated fallback systems unless the user explicitly asks.

## Documentation Map

Use `references/ninjaslayer-doc-map.md` for the local paths, common chapters, and validation commands.

Common chapter targets:

- Cards: `04-ritsulib/04-01-add-card/index.md`
- Relics: `04-ritsulib/04-03-add-relic/index.md`
- Card properties and tags: `04-ritsulib/04-04-card-properties/index.md`
- Powers: `04-ritsulib/04-05-add-power/index.md`
- Potions: `04-ritsulib/04-06-add-potion/index.md`
- Audio: `04-ritsulib/04-10-add-audio/index.md`
- Characters: `04-ritsulib/04-14-add-new-character/index.md`
- Card pools: `04-ritsulib/04-15-1-add-card-pool/index.md`
- Character animation: `04-ritsulib/04-15-2-character-animation/index.md`
- Singletons/hooks: `04-ritsulib/04-15-add-singleton/index.md`
- Content registry: `04-ritsulib/04-27-content-registry/index.md`

## Project Conventions

- Use `NinjaSlayer`, not `Ninjaslayer`.
- Runtime resources live under `res://NinjaSlayer/...`.
- Localization lives under `NinjaSlayer/localization/{Language}/...`.
- Keep audio in the FMOD bank/event pipeline. Do not add direct WAV playback fallback unless explicitly requested.
- Keep generated or missing art replaceable through clearly named files under `NinjaSlayer/images/...`.
- Build from the project directory with `dotnet build .\NinjaSlayer.csproj --no-restore -v:minimal` unless restore is needed.

## Compliance Checks

Before finalizing code changes:

- Run `rg` to confirm the expected tutorial terms or API names are present in the modified files.
- Build the project when code or resources that affect export changed.
- For card code or localization changes, confirm `Docs/card-catalog.md` was updated, or state why the card catalog did not need changes.
- State any mismatch between local tutorial examples, installed RitsuLib API, and original game code.
