# NinjaSlayer project instructions

## Original-novel evidence

- For original-lore questions or theme-driven card, relic, power, mechanic, flavor, and localization design, use `ninjaslayer-lore` before relying on model memory.
- Read the relevant `Docs/lore/` index, search the private `.lore-cache/` corpus, inspect only the needed context, and cite the chunk line. Seek a second passage for broad claims.
- Label conclusions as `原文事实`, `设计推论`, or `不确定`. Do not present a design interpretation as canon.
- Never commit EPUB files or extracted prose. If implementation is also requested, establish lore evidence first, then follow the RitsuLib and original-game-code skills.

## Skill routing

- Use `sts2-ritsulib-modding` for STS2/RitsuLib implementation, review, and
  debugging in this repository.
- When the local tutorial does not cover the required behavior, use
  `sts2-original-code-reference` before inventing a new implementation.
- Use `ninjaslayer-lore` first when canon, characterization, flavor, naming, or
  theme affects the result.
- Load `$gpt-image-2-style-library` only when the user explicitly requests
  style selection or prompt-contract calibration. Do not reload it during an
  established production batch.

## Card-art batches

- Generate exactly four candidate images for each card unless the user changes
  that requirement.
- Continue through the queued batch without pausing after every card or every
  four images unless a configured validation gate fails.
- A single coordinator owns the manifest, filenames, QA decisions, and
  `run-state.json`. Parallel workers must not write shared batch state.
- Use one production image engine per batch. Do not silently switch between
  bundled `imagegen` and the `$gpt-image` CLI pipeline.
