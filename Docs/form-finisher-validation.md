# Form and finisher validation, 2026-09-14

Historical validation of the preceding candidate. Current feedback, sizing,
companion timing and hand anchors are recorded in [heavy-layout-validation.md](heavy-layout-validation.md).

Candidate: working tree based on `964aba97367ce1f793445347d115dbad0ed996c8`.
No installation, commit or upload was performed.

## Implementation

- Heavy attacks now use the complete native Bludgeon effect at the target base,
  at scene size, with its internal anticipation independent of damage timing.
  Bamboo uses the complete native dramatic stab; Alabama uses native fire-burst
  intensity and hit-spark size. Other borrowed effects retain their audited native
  layers and placements; requested custom Shiv art/tint and Iai art remain.
- Extra red finisher light is removed. Reverse finishers predict only known
  attacks and use one cubic visual approach per command. Koki approaches from its
  actual position over the existing Iai gate, with the camera following that gate.
  Ninja Slayer's forward finisher still places directly at impact.
- Shared form calibration sets full Naraku scale .3732865 and One Soul .1366402,
  preserves normal .33, shares the foot line and updates core/support/hand/shadow.
  Accepted upright layers remain unchanged. The delivered full/One Soul head
  hashes match the runtime assets.
- Form changes expose the new form below at most 128 rigid textured shards of
  the old live pose. Both Hell Tornado layers share the actual actor core as burst
  origin. The .083s burst/.25s fade is presentation-only and uses block-break audio.

## Automated verification

- Logic: 347 passed, 0 failed.
- Stable 0.107.1 and Preview 0.111.0: Debug/Release builds, 0 warnings/errors.
- Both hosts: Orb product contracts and RitsuLib contracts passed. These cover
  actual action gates, same-card hit counts/Hook cadence, all-form motion and hand
  tracking, layered Hell Tornado, shadow grounding, both approach directions,
  no rig reparenting, fixed layout roots and prediction-return action handoff.
- Godot pack export, repository consistency, compatibility-derived files,
  build boundaries and `git diff --check` passed.

Final Debug DLL SHA-256:

- Stable: `75a974e96f677e881f9f26023ccdda7468858a6031dcca03d076d70c3abde347`
- Preview: `9cb5ecb53661b48d88e67664ba94f605a246f5f0e6ffc457bc34506cf5ef786a`

Logs: `build/action-compat-validation/form-finisher-*`.

## Actual background recordings

All videos use the isolated game process on the background desktop at 1280x720,
60fps, without ComputerUse, foreground activation or synthetic gameplay frames.
Each directory retains frame/QPC timestamps, process-loopback audio, FMOD events,
motion samples and the waveform synchronization report.

| Directory under build/action-compat-validation | Duration | Maximum encoded audio residual |
| --- | ---: | ---: |
| run-B-form-finisher-forms-04 | 18.267s | 13.33ms |
| run-B-form-finisher-koki-02 | 5.400s | 14.67ms |
| run-B-form-finisher-reverse-02 | 5.367s | 24.67ms |

Each deliverable is `preview.mp4`. Forms includes native/mod heavy comparison on
small, large and raised targets, all forms, layered transitions, shuriken and a
forward finisher. Maximum shard count was 128; final count was zero. Koki and
reverse approach layout-root excursion was zero; final visual offset was zero.
The reverse recording predates only the shard-origin correction, which that
recording does not exercise. The other two use the final candidate.

Audio verification resolves all three FMOD slow-attack variants from the actual
project metadata, requires multiple waveform matches and checks the encoded
output against the same selected source. It does not change runtime sound or Bank.

## Existing runtime diagnostics

Builds and pack export are clean. Actual game logs are not empty: the Vulkan RGB
format-conversion warnings also occur in the native baseline. The existing
RitsuLib confirmation-dialog disconnect and Godot shutdown node-path/resource
messages also occur in the earlier special-heavy/menu recordings. Their signatures
were compared; this change does not claim to fix those external/previous issues.
RitsuLib failure-injection tests intentionally log errors before reporting success.
