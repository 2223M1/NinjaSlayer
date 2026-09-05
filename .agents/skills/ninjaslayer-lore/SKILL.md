---
name: ninjaslayer-lore
description: Use the user's local Ninja Slayer novels and the project's versioned lore indexes to establish canon facts, chronology, characterization, relationships, character voice, techniques, equipment, recurring imagery, translation terminology, event-writing constraints, and evidence-backed card or content names. Use this skill before technical implementation whenever canon, flavor, naming, theme, dialogue, or narrative affects the Mod. It owns evidence and the creative brief, not generic prose polishing or RitsuLib integration.
---

# Ninja Slayer Lore

## Scope And Authority

This skill turns the private three-part novel corpus into compact, cited evidence and adaptation constraints.

It owns:

- Canon verification, chronology, identity, relationships, and terminology.
- Characterization and voice evidence.
- Evidence-backed mechanics, themes, event premises, flavor, and names.
- The boundary between source fact, adaptation inference, and authorized invention.

It does not own:

- Generic Chinese prose technique.
- RitsuLib, Godot, Patch, resource, or localization implementation.
- Claims about vanilla Slay the Spire 2 behavior.

Use the private extracted corpus only in small, relevant slices. Keep EPUB files and extracted prose out of Git.

## Locate And Validate The Corpus

Find the active repository containing `Docs/lore/sources.json` and treat it as `<repo-root>`. The canonical scripts live in `<repo-root>/.agents/skills/ninjaslayer-lore/scripts`.

If `.lore-cache/manifest.json` is absent or stale, rebuild it:

```powershell
python <repo-root>/.agents/skills/ninjaslayer-lore/scripts/build_corpus.py --repo-root <repo-root>
```

The default source directory is `%USERPROFILE%/Documents/忍者杀手/LocalBooks`; pass `--source-root` only when the books live elsewhere.

The search script validates the EPUB hashes and refuses a stale cache. Do not bypass that check or mix citations from different source hashes.

## Select A Task Mode

### Fact, chronology, identity, or translation

Use the narrow retrieval workflow below. Prefer one direct passage and one independent corroborating passage for consequential claims.

### Characterization, relationship, or voice

Read `references/creative-writing-and-naming.md`. Sample multiple scenes and distinguish narrator voice, direct speech, internal thought, and translator or formatter habits.

### Event, dialogue, flavor, or narrative localization

Read `references/creative-writing-and-naming.md`. Build a cited canon-and-voice brief before drafting. Determine whether the result is a canonical retelling, a canon-compatible interpolation, or an explicitly alternate/mod-original scene.

### Card, relic, power, event, technique, or keyword naming

Read `references/creative-writing-and-naming.md` and the current `Docs/card-catalog.md` plus relevant localization. Generate candidates in clearly labelled canon and coined tiers; never present a coined name as an original term.

### Mechanics or thematic design

Read the relevant `Docs/lore/` indexes and `design-evidence.md`. Connect each design inference to cited facts and explain the bridge. Lore evidence constrains theme and behavior; it does not settle balance, implementation, or numerical design.

## Retrieval Workflow

1. Read only the smallest relevant versioned sources:
   - `Docs/lore/index.md` for routing.
   - `aliases.json` for established equivalent forms.
   - The relevant section of `characters.md`, `techniques-and-equipment.md`, `design-evidence.md`, or `uncertainties.md`.
   - `library-index.md` only when chapter selection or source structure matters.
2. Convert the task into two to four concrete query families:
   - Exact entities, aliases, techniques, equipment, places, or episode titles.
   - Actions and relationship verbs that could demonstrate the claim.
   - Repeated objects, imagery, forms of address, or short source phrases relevant to voice or naming.
   - A book or episode restriction when chronology matters.
3. Run narrow searches, preferably with JSON output for grouping:

```powershell
python <repo-root>/.agents/skills/ninjaslayer-lore/scripts/search_lore.py `
  --query "茶道" --limit 24 --context 3 --json
```

Use `--book part1|part2|part3` when useful. Use `--regex` only for a deliberate pattern; it disables alias expansion.
4. Group results by `(book_id, chunk)`. Multiple matching lines from one chunk are one scene, not independent corroboration.
5. Open the best complete chunk and enough adjacent context to understand who is speaking, what happened immediately before, what changes afterward, and whether the passage crosses a segment boundary. Do not infer a scene from an isolated clipped line.
6. Refine the search after reading the first scene. Search newly confirmed names, objects, address forms, actions, or episode titles rather than expanding with arbitrary synonyms.
7. For broad, character-defining, chronological, or design-critical claims, seek a second independent scene. Prefer evidence from a different episode or phase of the relationship. Include contrary or limiting evidence when present.
8. Build an internal evidence packet before answering or drafting:
   - Direct facts and citations.
   - Unresolved facts.
   - Character goals, knowledge, relationship state, and voice markers.
   - Recurring motifs and established terminology.
   - The explicit bridge to any design inference.
   - What may be invented and what must not be contradicted.

Never load an entire book into context. Never treat search ranking as truth; ranking only locates passages.

## Classify Every Conclusion

- **原文事实**: directly supported by a cited passage.
- **设计推论**: an adaptation or design interpretation derived from cited facts; explain the bridge.
- **创作补全**: newly written connective action, dialogue, event structure, or name authorized for the Mod but not asserted to occur in the novels.
- **不确定**: translation, speaker, chronology, identity, relationship, or interpretation is unresolved by the checked evidence.

Do not convert narrator metaphor, character opinion, unreliable perception, one-off comedy, translator punctuation, or a single combat feat into a universal setting rule.

## Co-Invocation With `$ninjaslayer-writing`

Do not copy or merge the generic writing skill into this skill. Keep the responsibilities separate:

1. `ninjaslayer-lore` retrieves and classifies evidence, fixes terminology, defines character knowledge and voice, and marks the boundary of invention.
2. `$ninjaslayer-writing`, when explicitly invoked by the user, turns that brief into readable scenes or localization and revises the draft.
3. `ninjaslayer-lore` performs the final canon, voice, terminology, and false-attribution check.

For event prose and dialogue, the lore brief and requested Ninja Slayer format take precedence over generic prose defaults. `$ninjaslayer-writing` must not:

- Naturalize or replace established names, forms of address, code-switching, technique terms, or deliberate translation texture.
- Remove punctuation, fragments, repetition, exclamations, or syntactic roughness that the evidence brief identifies as necessary to a speaker or source-faithful format.
- Invent personal history, knowledge, motives, relationships, or setting rules and present them as canon.
- Apply forum-post voice to in-universe narration or dialogue unless the user explicitly requests that form.

Use the writing skill's fiction, dialogue/format, and revision guidance as appropriate. Do not invoke it for fact-only research, evidence classification, or a short naming shortlist unless prose drafting is actually required.

## Technical Handoff

After lore evidence or writing is settled:

- Route RitsuLib, Godot, Patch, resources, localization files, and card-catalog integration to `sts2-ritsulib-modding`.
- Route exact vanilla behavior questions not answered by public APIs/tutorials to `sts2-original-code-reference`.
- Route architecture or ownership cleanup to `ninjaslayer-code-quality`.

Do not let a downstream technical skill silently revise the established canon classification, displayed name, character voice, or invention boundary.

## Maintain Compact Knowledge

When a query reveals a durable alias, relationship fact, technique fact, voice constraint, naming convention, or reliable design conclusion, update the smallest appropriate file under `Docs/lore/`.

- Add only information likely to save future searches.
- Keep prose compact and classified.
- Cite every factual item.
- Put uncertain equivalences in `uncertainties.md`, not automatic alias expansion.
- Do not create a new index or project-specific writing layer until repeated use proves the existing files cannot hold the material clearly.
- Never copy extracted prose into tracked files.

Read `references/knowledge-layout.md` before changing the corpus builder, citation format, alias schema, or knowledge-file structure.

## Output Rules

For research or design review, present the minimum evidence needed, classifications, citations, inference bridge, and unresolved points.

For a requested creative deliverable, keep the evidence packet internal unless the user asks for it. Deliver the requested text, then include only a compact canon note when it materially prevents confusion between `原文事实`, `设计推论`, and `创作补全`.

Cite evidence as `[第一部｜章节名｜p1-c003-s02.md:42]`. Quote only the shortest phrase necessary for verification and never reproduce long source passages.
