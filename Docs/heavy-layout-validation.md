# Heavy feedback and form layout validation, 2026-09-14

Working-tree candidate based on `964aba97367ce1f793445347d115dbad0ed996c8`.
No installation, commit or upload was performed. Earlier uncommitted work remains.

This records the preceding candidate. Koki/counter timing is superseded by
`iai-timing-validation.md`; form layout, hand anchors and ordinary player timing
remain unchanged.

## Recorded behavior

- Only Collapse Fist, Slaughter and Straight Ki call `WithHeavyBluntHitFx`.
  The seven ordinary heavy call sites use `bluntPath` and `bluntAttack` at the
  target core. Native acquired cards keep their own effects.
- Koki Iai and Dark Ninja counter share the ordinary 120px pow10 Slow outbound:
  visual peak at .10s, gameplay gate .20/.10/0s, then concurrent .10s return.
  The route cannot inherit the enclosing player's kick or somersault classification.
  Dark Ninja still awaits the complete hurt and return before countering.
- Koki finishers keep the continuous close-range approach over .20/.10/0s and
  return in .10s after the cinematic. The counter's explicit damage path now starts
  the existing reverse-approach lease when the read-only forecast is lethal.
  Forward Ninja Slayer finishers still place directly at impact.
- Full Naraku uses scale .3732865 at (-18.430034,-177.44514). One Soul uses
  .112611514 at (50.61516,-157.23682), giving a 300px solid body. Both solid-body
  bounds center on X=0, exclude the scarf/smoke and share ground Y=-6.45.
  Their shadow centers use near-ground support, not a normal-form single-foot delta.
- Full Naraku's hand anchor is (1180,790), outside/above the screen-right wrist.
  Normal and half Naraku share the same calibration and (1754,430) fingertip.
  One Soul keeps (77,293). The existing transform/label and launch-origin paths
  continue to use these anchors; no raster was recut or resampled.
- Flying Kick holds maximum travel and kick stance through draw-to-full, Chado
  and real Hooks. Card completion starts recovery; independent draw/throw motion
  can still overlap. Its obsolete explicit early-return branch was removed.

## Verification

- Logic: 349 passed, zero failed.
- Stable 0.107.1 / Preview 0.111.0, Debug and Release: all four builds passed with
  zero warnings and zero errors.
- Both hosts' Orb product contracts and RitsuLib contracts passed. The added
  active-kick-scope test checks the companion's .10s pow10 visual peak, separate
  Normal/Fast gameplay gates, 120px travel, .10s recovery and no Instant Tween.
  Actual hurt-to-counter return ordering, mirrored/airborne hand transforms,
  separated Hell Tornado and continuous approach/return contracts also passed.
- Godot resource pack export, repository consistency, build boundaries,
  compatibility-derived files and `git diff --check` passed.

Debug DLL SHA-256:

- Stable: `eca2ba678d0c79a09f6a6a03ffde06854c4ad4f3dff971191d8e5f15bbd1f7bd`
- Preview: `1ee557df20dce24917648258c935e9f01791619180381ca3a60db5a072811ce0`

Logs and resource pack: `build/action-compat-validation/heavy-layout-*`.
Assemblies: sibling workspace `.sts2build/heavy-layout/{stable,preview}`.

## Actual background recordings

All clips use the final Stable DLL in an isolated game copy on an inactive desktop,
at 1280x720 and 60fps. There was no ComputerUse, foreground activation or synthetic
action compositing. Extracted PNG checks are frames from these actual recordings.
Each directory retains motion samples, sections, process-loopback audio, FMOD
events, timestamps and the source-waveform synchronization report.

| Directory under build/action-compat-validation | Duration | Maximum encoded audio residual |
| --- | ---: | ---: |
| run-B-heavy-layout-02 | 18.300s | 34.33ms |
| run-B-heavy-layout-koki-01 | 4.300s | 4.00ms |
| run-B-heavy-layout-reverse-01 | 7.417s | 0.03ms |

Each deliverable is `preview.mp4`. The main clip shows ordinary feedback, Koki Iai,
normal/half/full/One Soul layout, standing Dark Ninja as the height reference,
counter after hurt, Flying Kick drawing, and the full-form wrist launch.
Flying Kick held 120px and kick stance for all 39 sampled post-impact frames
(about .648s), until the hand reached ten cards, then returned after completion.
Koki's separate finisher clip retains a fixed layout root throughout. Reverse
counter samples progress continuously to -630px in the visual layer; the enemy
layout root stays fixed and its final visual offset returns to zero.
Other companion dodge/farewell motion is outside that layout-root assertion.

The first main recording was rejected because the newly spawned Dark Ninja's
default 99 Evasion prevented the intended hit. The final preview removes that
power only in scene setup and uses Dark Ninja's own music before its stance move.
Runtime evasion and music behavior were not changed.

## Existing diagnostics

Runtime logs are not empty. Native Vulkan conversion and shutdown resource/node
messages remain, as do the preceding candidate's RitsuLib dialog disconnect.
The reverse-death save-backup deletion and shadow `frame_pre_draw` disconnect
signatures also occur in `run-B-form-finisher-reverse-02`; they are not newly
introduced here and are not all classified as native issues. RitsuLib contract
failure-injection cases intentionally log errors before reporting success.
