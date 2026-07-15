---
name: ninjaslayer-lore
description: Search the user's local Ninja Slayer novels and the project's versioned lore indexes for canon facts, characters, relationships, plot context, techniques, equipment, translations, and thematic evidence. Use for original-text verification or when designing Ninja Slayer cards, relics, powers, mechanics, flavor, or localization from the novels. When a task also requires mod implementation, use this skill first for lore evidence, then use the RitsuLib or original-game-code skill for technical work.
---

# Ninja Slayer Lore

Use the private extracted corpus only in small, relevant slices. Keep novel text out of Git and distinguish source facts from design inference.

## Locate the project

Find the active NinjaSlayer repository containing `Docs/lore/sources.json`. Treat that directory as `<repo-root>`. The canonical scripts live in `<repo-root>/skills/ninjaslayer-lore/scripts`.

If `.lore-cache/manifest.json` is absent or stale, build it before searching:

```powershell
python <repo-root>/skills/ninjaslayer-lore/scripts/build_corpus.py --repo-root <repo-root>
```

The default source directory is `%USERPROFILE%/Documents/忍者杀手/LocalBooks`; pass `--source-root` only when the books live elsewhere.

## Retrieval workflow

1. Read `Docs/lore/design-evidence.md` for design work, plus `aliases.json` and the most relevant versioned index.
2. Search the corpus with a narrow name, action, technique, or object:

```powershell
python <repo-root>/skills/ninjaslayer-lore/scripts/search_lore.py --query "茶道" --limit 12 --context 2
```

Use `--regex` for a deliberate pattern search; it disables alias expansion.

3. Read only the returned lines and nearby context. Refine by `--book part1|part2|part3` when useful.
4. For broad or consequential claims, find a second passage or mark the conclusion as limited.
5. Cite the evidence as `[第一部｜章节名｜p1-c003-s02.md:42]`.

Never load an entire book into context. Never reproduce long passages; summarize and quote only the shortest phrase needed for verification.

## Classify conclusions

- **原文事实**: directly supported by a cited passage.
- **设计推论**: a design interpretation derived from cited facts; explain the bridge.
- **不确定**: translation, speaker, chronology, identity, or interpretation is not resolved by the checked evidence.

Do not convert a narrator's metaphor, character opinion, or one-off event into a universal setting rule without corroboration.

## Maintain knowledge

When a query reveals a durable alias or reliable design-relevant conclusion, update the smallest appropriate file under `Docs/lore/`. Do not copy extracted prose into versioned files. Keep every knowledge item classified and cited.

Read [references/knowledge-layout.md](references/knowledge-layout.md) before changing the corpus builder, citation format, aliases, or knowledge-file structure.
