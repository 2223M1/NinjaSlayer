# NinjaSlayer project instructions

## Architecture constraints

- These constraints are non-negotiable. Refactor deletion-first: remove unnecessary layers before adding code, and do not treat existing internal abstractions as required precedent.
- Validate only real trust boundaries. When an internal invariant is broken, fix the producer or throw; do not translate it into `null`, `false`, an empty collection, a default value, or a silent fallback.
- Do not introduce or retain runtime capability graphs or `Enabled`/`Degraded`/`Disabled` feature state machines.
- Do not introduce or retain a global `GameCompatibility` facade or a platform for method IL hashes, metadata tokens, async `MoveNext` fingerprints, or other method-body fingerprints.
- Historical compatibility requires an identified producer version and a real fixture. Do not add speculative compatibility or imaginary forward compatibility.
- Use `lock`, `Interlocked`, or similar synchronization only for production callers proven to execute concurrently.
- Add GC controls or global performance infrastructure only with profiler or benchmark evidence for the measured problem.
- Delete or inline a single-caller thin abstraction unless it is a demonstrated external boundary. Tests that only protect an internal layer do not justify keeping it.
- Tests should protect player-observable behavior. Update or remove structure-only tests when their internal abstraction is deleted.
- Never replace a deleted layer with a renamed equivalent.

## Skill routing

- Use `sts2-ritsulib-modding` for ordinary RitsuLib feature implementation and related bug fixes.
- Use `sts2-original-code-reference` only for exact vanilla behavior not covered by tutorials or public APIs.
- Use `ninjaslayer-code-quality` for architecture review, deletion-first refactoring, AI-slop removal, and compatibility simplification.
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
