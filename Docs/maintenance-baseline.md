# Maintenance baseline

The staged maintainability work starts from commit `c85acf3b2f92b78710bad9616f0ed755fc212703` on 2026-07-22.

## Runtime contracts

- Supported game line: `0.109.x`; RitsuLib: `0.4.62`; public RefLib: `0.109.0-beta`.
- Finisher capability owns attack interception, lethal protection, primary damage observation, post-card commit, and card-play cleanup. Presentation and Tornado cadence are separate optional capabilities.
- Enhanced lethal protection is pinned to `Creature.LoseHpInternal` in the supported 0.109.x build (`MVID a49d3537-5a42-4dcd-9877-663e394f2b44`, metadata token `0x06008438`, IL SHA-256 `9c1b0e229a97c39866dcebe88c742175b9d41b27b2d507ed4ca31bfee4f61fc6`). A mismatch or a foreign skipping/result-replacing Harmony patch disables the enhancement and keeps the original attack path.
- Finisher search limits are 25,000 states and 8 ms; the active-time watchdog is 90 seconds.
- Transition owns one 30-second watchdog and must restore input, black screen, hover suppression, camera state, and loading state on every exit.
- Prepared gameplay currently filters normal draws and mirrors vanilla shuffle/history/hook ordering. Prepared cleanup must become independently installable before gameplay compatibility is broadened.
- Naraku has normal, new Naraku, fully released Naraku, and One Body One Soul visual policies; this roadmap does not change their abilities.
- Boss framing holds for at least 2 seconds; boss and finisher camera recovery each remain 0.2 seconds.

## Verification boundaries

- Public CI may use project assets and RefLib only.
- Private game references are allowed only in the protected contract and release environments.
- Project-owned compiler, nullable, analyzer, test, and Godot import warnings must remain at zero.
- `MSB3270` is the only explicitly suppressed external reference warning at this baseline; adding another suppression requires an allowlist entry and rationale.
- Automatic telemetry may contain only the consented RitsuLib run-history envelope and NinjaSlayer balance contribution. Local diagnostics are restricted to logs and user-initiated F2 feedback.

No card values, reward pools, Reporter Pass behavior, cinematic timing, or Workshop state are changed by this baseline document.
