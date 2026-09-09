# Reload and tooltip fixes (0.1.52)

## Product changes

- Boss greeting no longer calls `SaveRun` after combat setup. The host owns the
  checkpoint; the RitsuLib run-loaded event suppresses the resumed room's greeting
  without persisting combat RNG, Pantograph healing, or boss visit counters.
  Leaving the room clears that suppression. Existing greeting save fields remain
  readable. No new save format or global lifecycle registry is introduced.
- Black Flame relies on native Ethereal exhaustion. Its explicit second exhaust
  was removed, so Return Return Return receives one event per exhausted card.
- Rapid-card exhaustion now uses native `CardCmd.Exhaust`, including native pile
  UI notifications. The custom silent-exhaust redirection and VFX helpers were
  deleted. Prepared, Finisher, and Transition ownership is unchanged.
- Custom dynamic variables use RitsuLib tooltip factories. Cards and relics
  explicitly expose relevant generated-card previews. The shared Chado tip and
  exhausting Chop preview each have multiple production callers.
- Both opening relics apply native Retain to their generated instances. Later
  tea remains ordinary; previews do not modify the canonical card. Chinese
  descriptions preserve the user's exact wording.
- Ninja Sense upgrades Scry from 1 to 3, keeping draw at 1. The independent card
  metadata fixture and all 92 base/upgraded models are checked against the board.
- Character event keys and missing English relic/power entries are supplied.

## Verification scope

Tests exercise the candidate DLL, native host commands and event dispatch.
Opening tea, Black Flame end-turn exhaustion, bilingual descriptions, tooltips,
event text, Scry/Sly batching, nested choices, status autoplay, temporary stats,
and orb lifecycle are covered by `NinjaSlayer.OrbContractTests`.
The RitsuLib contracts retain required-patch rollback and Finisher/Transition
ownership checks. ENet uses two processes with real synchronized card actions.
The smoke harness runs in isolated Windows profiles and checks exhaust UI,
animations, run reload, A10 boss reload and full AutoSlay.

Local inputs are stable 0.107.1 (MVID
`97f10687-c306-4798-ab75-8b9f23f34dfb`) and preview 0.111.0
(`73b63ee0-6c0a-47bb-b0d1-b21f6d94222e`). The installed Windows game is stable.
Preview uses cached managed references with the stable resource pack for
contracts; this is not preview rendered gameplay evidence. Final local evidence
is in `build/reload-fixes`, with exact SHA and DLL hashes in contract logs and
package identity in smoke attestations. Development builds are not final SHA
attestations. Workshop publication records the final bundle separately under
`build/releases`.

## Additional mods

Combined smoke includes BaseLib 3.4.5, JmcModLib 1.9.0, LexNinja2 3.0.9,
LieRenTV 0.1.3, Isaac 2.3, Narrator manifest 0.2.0, CrashGuard 1.3.0 and
Flagellant 0.5.1. Current combination tests cover first-combat/reload/presentation
and A10 boss reload. They do not establish every event or character interaction.

The supplied September 8 log without NinjaSlayer already contains Narrator's
missing RitsuLib `SideTurnStartedEvent.get_CombatState` and BaseLib's missing
`NTreasureRoom._chestButton`. Narrator's error also appears in the combined run.
These are third-party API mismatches, not demonstrated NinjaSlayer conflicts;
no speculative compatibility shim or swallowed exception was added.

Old running saves remain unsupported. Loading an unsupported model must leave
the original file intact; users should back up or finish the old run and start
anew. Tests use synthetic removed-model inputs, not historical-save fixtures.
