# Knowledge layout and maintenance contract

## Private, generated layer

`.lore-cache/` is local-only and Git-ignored:

- `manifest.json`: extractor version, source SHA-256 values, OPF/spine/NCX counts, chapter metadata, and chunk inventory.
- `corpus/part1|part2|part3/*.md`: normalized source text, one paragraph per line.

Chunk names are stable for an unchanged source: `p1-c003-s02.md` means part 1, logical chapter 3, segment 2. A chunk begins with a JSON metadata comment, then a heading and one source paragraph per line. Cache text must never be copied wholesale into a tracked document.

The builder reads each EPUB through `META-INF/container.xml`, the root OPF manifest/spine, and the OPF's NCX. NCX anchors define logical chapters. If navigation is absent, spine documents become chapters. Segments are cut at paragraph boundaries near 8,000-12,000 characters.

## Versioned layer

`Docs/lore/` contains only compact, maintainable knowledge:

- `sources.json`: book IDs, titles, and filenames only.
- `library-index.md`: generated source hashes, extraction counts, chapter names, and chunk inventory.
- `aliases.json`: versioned search expansions.
- `characters.md`: design-relevant people and relationships.
- `techniques-and-equipment.md`: techniques, disciplines, weapons, and props.
- `design-evidence.md`: evidence translated into card/mechanic design constraints and opportunities.
- `uncertainties.md`: unresolved translation, identity, chronology, and evidence issues.

Every prose claim uses one of `[原文事实]`, `[设计推论]`, or `[不确定]`. Cite cache evidence as `[第一部｜章节名｜p1-c003-s02.md:42]`. Line numbers refer to the generated chunk and can change when its source EPUB changes; rebuild and refresh affected citations after a hash change.

## Alias rules

An alias group has a canonical form and search forms. Add only spellings or translations that refer to the same entity or concept. Put uncertain equivalences in `uncertainties.md`; do not use them as automatic search expansions until resolved.

Default search is Unicode case-insensitive substring matching with alias expansion. `--regex` accepts a Python regular expression and deliberately disables expansion so pattern meaning remains predictable.

## Rebuild and synchronization

Run `build_corpus.py --repo-root <repo>` after replacing an EPUB. The search script refuses a stale cache. Run `install_skill.ps1` after changing the canonical skill, then use `install_skill.ps1 -Check` to compare every installed file by SHA-256.
