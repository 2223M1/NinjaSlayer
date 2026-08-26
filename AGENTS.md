# NinjaSlayer project instructions

## Architecture constraints

- These constraints are non-negotiable. Refactor deletion-first: remove unnecessary layers before adding code, and do not treat existing internal abstractions as required precedent.
- Validate only real trust boundaries. When an internal invariant is broken, fix the producer or throw; do not translate it into `null`, `false`, an empty collection, a default value, or a silent fallback.
- Must not reintroduce runtime capability graphs or feature state machines, a global `GameCompatibility` facade, method-body fingerprint platforms, speculative historical-alignment paths, or global GC controls.
- Synchronization and performance infrastructure require demonstrated production concurrency or measurements. Keep supported-host differences compile-time and feature-local.
- Delete or inline a single-caller thin abstraction, never recreate a deleted layer under another name, and keep tests focused on player-observable behavior.

## Skill routing

- Use `sts2-ritsulib-modding` for ordinary feature implementation and bug fixes involving documented RitsuLib, Godot, content, resource, or Patch integration.
- Use `sts2-original-code-reference` only as a behavioral oracle when current tutorials and public APIs do not establish exact vanilla behavior; never use it as a Mod architecture template.
- Use `ninjaslayer-code-quality` for architecture review, deletion-first refactoring, AI-slop removal, compatibility simplification, and internal ownership changes.
- Use `ninjaslayer-lore` first whenever canon, characterization, flavor, naming, or theme affects the design.
- Load `$gpt-image-2-style-library` only when the user explicitly requests style selection or prompt-contract calibration. Do not reload it during an established production batch.

## Original-novel evidence

- For lore-driven work, read the relevant `Docs/lore/` index, search the private `.lore-cache/` corpus, cite the chunk line, and seek a second passage for broad claims.
- Label conclusions as `原文事实`, `设计推论`, or `不确定`. Do not present design interpretation as canon.
- Never commit EPUB files or extracted prose. Establish lore evidence before routing implementation to the appropriate code skill.

## Card-art batches

- Generate exactly four candidate images for each card unless the user changes that requirement.
- Continue through the queued batch without pausing after every card or every four images unless a configured validation gate fails.
- A single coordinator owns the manifest, filenames, QA decisions, and `run-state.json`. Parallel workers must not write shared batch state.
- Use one production image engine per batch. Do not silently switch between bundled `imagegen` and the `$gpt-image` CLI pipeline.
