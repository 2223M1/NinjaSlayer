# Finisher stationary UI, 2026-09-30

Local-only working-tree change based on released 0.3.8,
`318b214b17d20e89634935d5484f9df92f6175b4`, branch
`codex/finisher-stationary-ui`. No release, upload, or daily-game installation.
The DLL source metadata identifies the base SHA; this document and the local diff
identify the uncommitted candidate, not a clean release commit.

## Behavior and ownership

- Impact placement moves `NCreature.Visuals` instead of the creature layout root.
  The old `moveRoot` option is removed; Alabama and ordinary finishers use the
  same placement rule. Target/contact math and native damage timing are unchanged.
- Finisher recovery and the Architect's retained lunge, hop and walk-off use the
  existing `FinisherApproach` animation channel. Health and status UI stay in
  native layout space; body movement and cinematic camera rendering remain.
- Aether Reflux cinematic suspension transfers its displacement to that visual
  channel, then restores the visual baseline on disposal, including interruption
  after free control stops. No root transfer, UI counter-translation, reparenting,
  new manager, setting, or save field is introduced.
- Existing visual-only companion, hostile and ranged approaches remain in place.
  Card rules/specifications are unchanged.

## Automated verification

- 360 logic tests passed; repository consistency, build-boundary and whitespace checks passed.
- Stable 0.107.1 and preview 0.111.0 products and SmokeDriver built successfully.
- Full candidate DLL product contracts passed on both hosts, including mirrored
  contact/combo motion, target removal, recovery, actual packaged forms, external
  scales 0.65/1/1.4, stationary root/health/status UI, nested free-control takeover
  and stopping free control before cinematic disposal.
- Contract-process exit logs retain Godot RID/ObjectDB cleanup warnings; these
  are not recorded as clean shutdowns or as visual playtesting.

| Candidate | Host MVID | DLL SHA-256 |
| --- | --- | --- |
| stable 0.107.1 | `97f10687-c306-4798-ab75-8b9f23f34dfb` | `c92af710b6df719215d234c1a85e0a025d2065ff8d1cefea73c5f593b116bd6f` |
| preview 0.111.0 | `73b63ee0-6c0a-47bb-b0d1-b21f6d94222e` | `5d010a70ffaf443e689c49647b09e5542a5b6833cdf860b3bd1c0117ee0db49a` |

Compile dependency: RitsuLib 0.5.12. Local real-game dependency: RitsuLib 0.6.3.
Resources were not edited; the isolated candidate reuses the exact published
0.3.8 PCK (`3f944b92af4570d39ee74f6706d33a0cd812e774e338a9c2c6cb527a5f295cce`).

## Local real-game checks

Runs use preview 0.111.0 on the isolated background desktop,
with network blocked and separate saves. No screenshots or recordings.
The UI probe samples native transforms relative to combat layout, excludes
ancestor/camera shake, and requires visible body travel rather than accepting an
animation that simply does not move.

- Released DLL control (`baseline-preview-2`): the same lethal three-hit Tornado
  failed the layout-relative check. Root position changed from `(-480, 200)` to
  `(404.5, 200)` during 70 sampled frames.
- Initial candidate (`fixed-preview-2`): the three-hit finisher, one confirmed
  death, fallback presentation, save/restart and hostile reverse finisher passed.
  Its DLL preceded only the subsequent Architect/Alabama visual handoff repair.
- Final candidate (`final-preview`): layout-relative root/health/status checks,
  three-hit Tornado, one confirmed kill, fallback presentation, save/restart and
  hostile reverse finisher all passed (`attestation.json`: `passed`).
- Final candidate (`architect-preview-2`): native Continue, greeting, forced
  Alabama Drop, retained visual recovery and walk-off passed. Across 435 sampled
  frames the native root/health/status transforms stayed fixed while the body
  moved. The exit-start check rejects snapping back to the layout root; native
  victory count increased exactly once. Full run attestation: `passed`.

Initial global-transform probes are diagnostic only: global coordinates include
ancestor motion. They are excluded from the final pass/fail comparison. The first
Architect probe also included the separate room entrance slide; its result is
not execution evidence. The final fixture waits for entrance before sampling
Continue through greeting, Alabama execution, recovery, exit and one victory.

Not executed: stable graphical gameplay, multiplayer, real-game interruption at
every frame, or additional theater recordings. Contracts are not substituted for
those checks. The shared theater expectation was updated for a stationary root;
its recording scenario was not run.

Compact logs/checkpoints/attestations: `build/finisher-ui/validation.zip`.
Source-file hashes and final candidate hashes: `build/finisher-ui/evidence.json`.
The two candidate DLLs are retained under `build/finisher-ui/evidence/`.
Automatic approval rejected removal of this task's redundant channel packages
and staging with only `blocked by policy` as the reason. Approximately 655 MiB
of build copies remain; cleanup was not retried through another route. The daily installation
and original mixed worktree are untouched. No Git commit, push or Workshop upload.
