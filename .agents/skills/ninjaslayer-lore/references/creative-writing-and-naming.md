# Creative Writing And Naming Workflow

Load this reference only for characterization, voice, event writing, flavor, dialogue, narrative localization, or naming. Simple fact lookup does not need it.

## 1. Build A Canon-And-Voice Brief

Before drafting, record internally:

### Canon frame

- Book part, episode, approximate chronology, and location.
- Whether the scene is a canonical retelling, canon-compatible interpolation, or alternate/mod-original material.
- Which facts the player must already know and which facts the cast can know at that point.

### Cast state

For every speaking or deciding character:

- Immediate goal.
- Fear, obligation, or competing motive.
- Current relationship with the other participants.
- Information they possess, misunderstand, conceal, or refuse to say.
- Physical condition and recent consequences that affect behavior.

### Evidence anchors

Use three to six strong source anchors for a substantial scene when the corpus supports them:

- One anchor for the event or relationship state.
- One or more anchors for each major character's voice or decision pattern.
- A second independent scene for broad characterization.
- A limiting or contrary passage when the character changes across the series.

A high match count inside one chunk remains one source scene.

### Invention boundary

Write two internal lists:

- **Must preserve**: established identity, chronology, relationship state, terminology, known abilities, prior consequences, and fixed names.
- **May invent**: connective actions, exact dialogue, minor setting detail, choice structure, and Mod-specific consequences that do not contradict the first list.

Everything invented remains `创作补全`.

## 2. Sample Character Voice Without Caricature

Separate these layers:

- Narrator voice.
- Direct dialogue.
- Internal thought.
- Interface, IRC, machine, or translated-text artifacts.
- One-off panic, injury, intoxication, comedy, or supernatural distortion.

For each important speaker, sample at least two scenes when available and record:

- Forms of address and politeness level.
- Sentence length and interruption pattern.
- Directness, evasiveness, understatement, and emotional concealment.
- Recurring exclamations, code-switching, technical vocabulary, and metaphors.
- What the speaker notices first under pressure.
- What the speaker would not know or would not voluntarily say.
- How the voice changes with status, injury, intimacy, anger, or time period.

Do not reduce a character to one catchphrase. A phrase such as a greeting, attack cry, or stock exclamation is surface evidence; goals, knowledge, and decision habits carry the voice through a full scene.

Translator punctuation and mixed-language texture may be useful style evidence, but they are not automatically character traits. Retain them only when the requested format or sampled voice supports them.

## 3. Write Events From Choices And Consequences

Choose one narrative mode explicitly.

### Canonical retelling

- Preserve the event order and outcome.
- Compress only what the game format requires.
- Do not add a choice that would contradict the canonical result unless the branch is explicitly non-canon.

### Canon-compatible interpolation

- Place the scene in a real gap.
- Give every participant a goal that fits the known relationship and knowledge state.
- Let the Mod invent the immediate problem and dialogue.
- End without changing a later canonical fact unless the user authorizes an alternate branch.

### Alternate or Mod-original scene

- State the divergence point internally.
- Preserve recognizable characterization unless the premise explicitly changes it.
- Mark new setting rules, relationships, or outcomes as `创作补全`.

Build the scene around a short causal sequence:

1. A concrete trigger changes the current situation.
2. Characters pursue incompatible or costly goals.
3. The player or protagonist makes a meaningful choice.
4. The choice changes resources, relationships, information, danger, or the next encounter.
5. The ending carries the consequence instead of explaining a moral.

Each paragraph or dialogue exchange must add an action, decision, discovery, relationship change, or consequence. Atmosphere supports those changes; it does not replace them.

## 4. Hand Off To `$ninjaslayer-writing`

When the user explicitly co-invokes `$ninjaslayer-writing`, pass it the canon-and-voice brief rather than raw search output.

Prefer its fiction and format guidance for:

- Event scenes.
- Character dialogue.
- Short flavor text.
- Choice labels and outcome text.
- Narrative localization.

Use its revision guidance only after a complete first draft exists.

The following constraints remain owned by `ninjaslayer-lore` and override generic prose cleanup inside the in-universe text:

- Fixed canon terms, names, forms of address, and displayed identifiers.
- Established code-switching and translation texture.
- Source-supported fragments, repetitions, interruptions, cries, and punctuation.
- Character knowledge, relationship state, emotional restraint, and invention boundary.
- UI length and localization format.

The writing pass may improve causal flow, paragraph purpose, action clarity, and non-generic dialogue. It may not make every speaker sound like smooth contemporary Mandarin or a Chinese internet essayist.

After revision, run a lore pass that checks:

- False canon attribution.
- Chronology and relationship drift.
- Voice convergence between characters.
- Generic anime threats, speeches, and moral summaries.
- Exposition that a character would not say.
- Decorative source mannerisms unsupported by the selected scenes.

## 5. Generate Evidence-Backed Names

### Read the current product vocabulary

Before naming, inspect:

- `Docs/card-catalog.md`.
- The relevant localization files.
- Existing class, model, serialized ID, and displayed-name distinctions.
- Related cards, relics, powers, keywords, and event options.

Avoid duplicate displayed names, near-duplicate concepts, and names that imply the wrong mechanic.

### Define the naming target

Record:

- Object type and UI length constraints.
- Mechanical action, condition, and payoff.
- Emotional or narrative register.
- Character, technique, episode, object, or motif that should anchor the name.
- Whether the user wants direct canon wording or an original but source-compatible name.

### Mine source material in layers

Search in this order:

1. Canonical technique, equipment, place, organization, or established phrase.
2. Episode-title vocabulary and recurring imagery.
3. Short dialogue or narration fragments whose speaker and context are understood.
4. Action-result pairs that describe the mechanic in the source's register.
5. Existing translation patterns for compounds, punctuation, English inserts, and forms such as `大·受身`.

Do not search only the abstract mechanic word. A card about recovering control may require searches for the character's breathing, posture, memory, impulse, teacher, and immediate aftermath.

### Produce three labelled tiers

- **原文名词／原文短语**: directly attested term or short phrase. Cite it.
- **原文压缩**: a transparent shortening or recombination of attested wording. Cite the source elements and explain the compression.
- **原创拟名**: a new Mod name built from established motifs or voice. Mark it as `创作补全`; do not claim that the novels use it.

A direct episode title is not automatically a good card name. Use it only when its context and mechanic align.

### Score and shortlist

Evaluate candidates against:

- Canon and thematic fit.
- Mechanical legibility.
- Character or faction voice.
- Distinctiveness within the current catalog.
- UI length and spoken rhythm.
- Translation stability across supported languages.
- Risk of false canon implication.

Generate enough candidates to expose different directions, then shortlist the strongest three. Do not pad the list with generic martial-arts, anime, cyberpunk, or Japanese-sounding filler.

For the final shortlist, provide:

| Candidate | Tier | Source or inference | Mechanical fit | Risk |
|---|---|---|---|---|

Keep explanations compact. If the user requests names only, use this evaluation internally and return only the names.

## 6. Flavor And Short Localization

For card flavor, tooltips, event choices, and short outcome text:

- Establish who is speaking or whether the line is narrator text.
- Use one concrete image, action, or consequence from the evidence brief.
- Preserve UI function. A choice label must signal the decision; flavor cannot hide the cost or outcome.
- Do not paste a long quote to create authenticity.
- Do not append an explanation after the source-supported image already carries the effect.
