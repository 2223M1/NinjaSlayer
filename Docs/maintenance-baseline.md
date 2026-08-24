# Maintenance baseline

The staged maintainability work starts from commit `c85acf3b2f92b78710bad9616f0ed755fc212703` on 2026-07-22.

## Runtime contracts

- Supported game hosts are the rolling `stable` and `preview` entries in `eng/compatibility.json`. RitsuLib uses one exact compile baseline and minimum manifest dependency; players receive the current Workshop runtime.
- Workshop has one item and one universal bundle. Its top-level loader accepts only the two active host MVIDs, verifies the selected implementation SHA-256, associates that assembly with the active mod, and rejects unknown hosts instead of guessing a nearby version.
- Finisher capability owns attack interception, lethal protection, primary damage observation, post-card commit, and card-play cleanup. Presentation and Tornado cadence are separate optional capabilities.
- Enhanced lethal protection resolves the exact `Creature.LoseHpInternal(decimal, ValueProp)` target after the MVID loader selects the host implementation. A foreign transpiler, skipping Prefix, or result-replacing Harmony patch disables the enhancement and keeps the original attack path.
- Finisher search limits are 25,000 states and 8 ms; the active-time watchdog is 90 seconds.
- Transition owns one 30-second watchdog and must restore input, black screen, hover suppression, camera state, and loading state on every exit.
- Prepared safety clears afflictions only after pile-change hooks confirm the card left the draw pile, and independently repairs invalid state at run-load and combat boundaries. The required Patch targets the exact public `CardPileCmd.Draw(PlayerChoiceContext, decimal, Player, bool)` signature on both separately compiled hosts. Prepared FIFO positioning directly calls the identical public `CardPile.AddInternal(CardModel, int, bool)` and `RemoveInternal(CardModel, bool)` APIs in both hosts, with rollback and placement repair when the transaction fails.
- F2 feedback uses at most three 10-second attempts within a 35-second total budget. Only network errors, timeouts, HTTP 408/429, and 5xx responses retry; `Retry-After` is capped at 5 seconds. The transport never owns caller streams, while the Harmony replacement preserves the original method's close-on-every-exit contract.
- Feedback persistence uses a submission-scoped Durable Object lease, attempt-specific KV paths, and a SHA-256-bound completion marker. Expired attempts may be replaced, but stale writers and cleanup paths cannot overwrite or delete the winning attempt.
- Naraku has normal, new Naraku, fully released Naraku, and One Body One Soul visual policies; this roadmap does not change their abilities.
- Boss framing holds for at least 2 seconds; boss and finisher camera recovery each remain 0.2 seconds.

## Verification boundaries

- Public CI uses project assets and package references only; real game assemblies remain confined to protected runners.
- Private game references are allowed only in the protected contract and release environments.
- Project-owned compiler, nullable, analyzer, test, and Godot import warnings must remain at zero.
- `MSB3270` is the only explicitly suppressed external reference warning at this baseline; adding another suppression requires an allowlist entry and rationale.
- Automatic telemetry may contain only the consented RitsuLib run-history envelope and NinjaSlayer balance contribution. Local diagnostics are restricted to logs and user-initiated F2 feedback.

No card values, reward pools, Reporter Pass behavior, cinematic timing, or Workshop state are changed by this baseline document.
