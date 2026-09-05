# Project simplification review

Baseline: `56b7b3b1ce43440d781ee97fdb97f52490b690ea` (PR #95).
The two external `NinjaSlayer-review-*-56b7b3b.md` reports were partial static
reviews. Their suggestions were checked against current callers and host
behavior; they were not treated as an executable specification.

## Approved behavior changes

- Retire unused legacy cards, Powers, tokens, Prepared queues and old Naraku
  and Chado workflows. Local copies live in ignored `.local-reference/legacy/`;
  compilation, registration and package export exclude that directory.
- Old runs are unsupported. Unknown model IDs fail without model substitution
  or save-file writes. Back up before abandoning a run, since the game's
  explicit abandon command deletes it. New runs retain native save behavior;
  restarting combat does not restore mid-combat orb stock.
- Keep NarakuEvent. Its deck condition accepts Black Flame producers and
  Naraku Life cards. Naraku Form selects half Naraku; the event relic grants
  the same Power at combat start with the full Naraku appearance.
- ZBR grants only 12 Naraku Life.
- Card titles use the native font. Remove every card copy of the misused
  character-body image and replace Naraku Form and Tornado Fist portraits with
  official animation stills. Current full/half Naraku character assets remain.

All other current card IDs and base/upgraded metadata are checked against the
86-card snapshot. This is regression evidence for the recorded properties,
not a claim that a snapshot executes every card interaction.

## Audit disposition

| Items | Disposition and reason |
| --- | --- |
| R01, R06, R07, R09, R15 | Legacy retirement removes the duplicated card base, Prepared transaction, token generation and old Tornado implementation. Sharing or repairing inactive flows would retain unwanted runtime code. |
| R02-R05 | Remove inactive Scry branches and proven duplicate Chado/stock guards. Preserve target liveness checks across animation waits. |
| R08 | Keep separate stock commit points for ordinary evoke, replacement, discard/shuffle and consumed volley. The shared shot remains local; no transaction manager or global chain state is introduced. |
| R10 | Remove Prepared lifecycle cleanup. Keep required Patch installation and exact static/dynamic ownership rollback; optional telemetry may degrade only after verified cleanup. |
| R11 | Share Great Uke's threshold between display and damage behavior; test 10, 10.5 and 11. |
| R12 | Name history reconstruction's incremental-update decision explicitly. Retire unused metrics and return-to-hand state; ChopStrike owns its destination override, with Exhaust taking precedence. |
| R13 | Split six card and two Power aggregates by named type. Replace the mixed Actions facade with current shared card commands and direct single-owner code. Preserve model identifiers. |
| R14 | Remove always-zero overrides after checking the native base implementation. |
| R16 | Remove unused parameters from current local commands and their callers. Retain host override signatures. |
| R17 | Replace behavioral source spelling checks with candidate-DLL metadata and product contracts. Keep static asset, localization, package, Patch-ID and build-boundary checks. Remove the private-host-member spelling map and obsolete file-absence registry. |
| R18 | Document actual ownership and settlement differences where needed; do not add blanket API-comment boilerplate. |

## Retained boundaries

- Finisher forecast, animation cancellation, Soar and Transition keep their
  existing ownership. Only unreachable legacy-card/Power forecast branches
  were removed. Core contracts cover Finisher isolation, deferred transitions,
  cancellation and exact Patch rollback; rendered smoke covers combat handoff.
- Reflection and dynamic patches remain at actual host/framework integration
  points, including saved-property registration and native orb scene binding.
  Required targets are 90 total, 56 critical on the tested host. Counts do not
  substitute for exact ownership checks.
- Feature-local cancellation leases, Transition completion/barriers, feedback
  and telemetry synchronization remain. Their producers include async
  continuations, cancellation callbacks and UI lifecycle callbacks; this round
  does not claim a new concurrency or performance optimization.
- Presentation may skip unavailable room/local-player nodes. Optional telemetry
  registration may warn and skip. Network/file failures retain their explicit
  user-facing handling; broken internal invariants are not converted into
  default gameplay values.
- Existing compile-time host branches and feature-local resource-load smoothing
  remain. No capability graph, global compatibility facade, method fingerprint
  platform, speculative save migration or global GC control was added.

## Runtime defects exposed during verification

- NarakuEvent lacked an asset profile and produced a blank event room.
- Native orb presentation assumed every orb scene used Spine. Initialize the
  Shuriken sprite before the native label/scale update.
- Native channel audio looked for a nonexistent generated debug MP3. Keep
  Shuriken stock acquisition silent through its own narrow Patch.

## Verification scope and evidence

Execution target for this round is Windows game **0.111.0**, MVID
`73b63ee0-6c0a-47bb-b0d1-b21f6d94222e`. API package: RitsuLib 0.5.12;
rendered game: installed RitsuLib 0.5.18. Stable and other platforms are
**NOT RUN**. Protected dual-host release gates remain in place; this work does
not authorize a release or Workshop publication.

Worktree verification passed: product and SmokeDriver builds, 320 LogicTests,
repository/compatibility/build boundaries, PowerShell/install/isolation and
release-artifact contracts, tutorial tests, core Patch/Finisher/Transition
contracts, orb/card/save contracts, and native two-process ENet card actions.
Multiplayer covers independent stock, multi-evoke, ChopStrike return/Exhaust,
Naraku ownership, Black Flame generation and identical final state/RNG results.
Save failure tests use synthetic unknown-model inputs and native save readers;
they are not historical compatibility fixtures.

Rendered smoke passed first combat, save/restart, reverse Finisher, half/full
Naraku, normal/rapid Hell Tornado rise and landing, all current card portraits
and Power icons, a complete AutoSlay run and Sawatari's same-combat transition.
The corrected card gallery is in local `smoke-presentation-7` evidence.
Pre-commit evidence is stamped with the baseline and is explicitly worktree
evidence. The PR records fresh committed-candidate results and SHA separately.

Production inventory across the same 15 source roots changed from **472 files,
61,460 physical lines, 937 type declarations** to **444 files, 54,153 lines,
757 type declarations**. Exact file/method edits are the Git diff against the
baseline; local `build/project-simplification/legacy-*.json` and
`final-inventory.json` retain the retirement and per-file inventories.

Residual: English ancient dialogue already lacks an `eng/ancients.json` file.
No translation was invented as part of this structural refactor.
