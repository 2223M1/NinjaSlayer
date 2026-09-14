# Iai and counter timing candidate, 2026-09-14

Working-tree candidate based on `964aba97367ce1f793445347d115dbad0ed996c8`.
This supersedes only the Koki/counter timing in `heavy-layout-validation.md`.
No installation, commit or upload was performed.

## Behavior

- Koki Iai and Dark Ninja counter retain the 120px pow10 trajectory. Outbound
  and damage gate are now .50s Normal, .25s Fast and 0s Instant. This uses the
  host TriggerAnim scaling rule for an explicit .50s slash gate, not a universal
  monster attack duration or a claim about Spine playback speed.
- Both visual returns take .25s in Normal/Fast, overlapping damage recovery.
  Instant creates no motion Tween. Dark Ninja completes its existing hurt and
  return before starting the counter. Damage, target checks and Hook order remain.
- Koki's continuous finisher approach, camera lead and the explicit reverse-counter
  approach use the same gate. Their visual return is .25s after the cinematic or
  a failed lethal prediction. Other actors retain their previous approach recovery.
- Ninja Slayer ordinary Slow, kicks, Flying Kick draw hold, Yukano, bamboo,
  hurt/dodge, form calibration and shuriken anchors are unchanged. Normal and half
  Naraku still share the same fingertip calibration.

## Verification

- Logic: 349 passed, zero failed.
- Stable 0.107.1 and Preview 0.111.0 Debug/Release: all four builds passed with
  zero warnings and zero errors.
- Both Orb product and RitsuLib contracts passed. Deterministic stepping verifies
  the complete slash gate/outbound and .25s return while a player kick card is
  active, with a fixed UI root and no Instant Tween. Actual hurt-to-counter order,
  prediction-mismatch recovery and the existing movement/form contracts passed.
- Godot pack export and headless resource/scene loading passed. Repository
  consistency, build boundaries, compatibility-derived files and diff checks passed.
  The old ArchitectureTests directory contains only historical build residue;
  current architecture checks run through repository and build-boundary scripts.
- Live-run telemetry save reload was not run: the contract has no same-host save
  fixture. This candidate adds no save fields.

Logs/pack: `build/action-compat-validation/iai-timing-*`.
Assemblies: sibling workspace `.sts2build/iai-timing/{stable,preview}`.
Debug SHA-256:

- Stable: `5644728c80498ee3fc1122f5dc16fdd6719518c827994508688c24906d6e943e`
- Preview: `ca1900a19f519702737b22b07289b2251a58e4ffcae0acae16627d14b7064192`
- Pack: `ffddef2adc608eaf3ea79867d6ebe19c65e0dfdbb88f062dd7edc0e0f56aa686`

## Actual recordings

All videos use the candidate Stable assembly in an isolated game on an inactive
desktop, at 1280x720 with 60fps output and process-loopback audio. No ComputerUse,
foreground activation, synthetic motion or installation was used.

| Video under build/action-compat-validation | Duration | Content | Max encoded audio residual |
| --- | ---: | --- | ---: |
| run-B-iai-timing-01/preview.mp4 | 15.80s | Koki Normal/Fast twice each, then four actual hurt/counter exchanges | 24.67ms |
| run-B-iai-timing-koki-01/preview.mp4 | 4.65s | Fast ordinary Iai and lethal continuous approach | 1.67ms |
| run-B-iai-timing-reverse-01/preview.mp4 | 7.53s | Fast lethal Dark counter after complete hurt | 4.00ms |

Each directory contains sections, per-render-frame motion samples, audio event
and waveform synchronization evidence. Initial player attacks provide audio
reference cues. Game/render startup and first-use particle loading can still
produce frame hitches; 60fps output is not a guarantee of uninterrupted 60fps rendering.

The captured ordinary Koki motion reaches about 120px at 2.04s (Normal section
starts 1.55s) and 4.20s (Fast section starts 3.96s), with recovery complete near
2.32s and 4.45s. The UI root remains -670 during these actions. Dark counter
travels on the visual rig while its root stays 480. Its lethal approach progresses
from 0 through -108.59, -203.92 and subsequent positions to -630px; the cinematic
holds that endpoint, then restores the rig to zero. Koki's lethal approach likewise
starts at zero, continuously reaches its close-range endpoint and returns to zero.
Companion layout during enemy replacement/player death is separate from attack travel.

Existing Vulkan format, shutdown resource/node and RitsuLib disconnect diagnostics
remain. Reverse-death cleanup also retains the previously recorded backup/shadow
disconnect diagnostics; these are not all attributed to the unmodified host.
