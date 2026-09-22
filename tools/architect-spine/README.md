# Architect Death Retarget

The build tool samples the native `skulking_colony` Spine 4.2.43 `die`
animation, including its path and physics constraints, at 120 Hz. It bakes
the result into Architect's private `ninjaslayer_soft_death` animation.
Runtime playback uses the native Spine track; no runtime bone rewriting or
soft-body collapse solver is involved.

The retarget keeps Architect's initial proportions and maps the source hip,
torso, detached head, limb paths and weighted arm meshes. Cape and sleeve
deformation is derived from those paths. Fourteen existing limb quads are
subdivided without changing their UVs or initial geometry. Existing animation
bytes and atlas bindings are preserved. The source events, coral fragments
and disappearance effect are not copied.

## Rebuild

Run from the repository root:

```powershell
node tools/architect-spine/author.mjs --check
node tools/architect-spine/author.mjs
```

The optional positional argument is the original game's PCK. The default is
the local Steam installation. `SOURCE.json` records source/output hashes,
mapping, sample times and source/target trajectory measurements.

## Playback

The source animation remains 3.666666746 seconds at 1x in Normal and Fast.
Production execution uses the same timeline as other bosses: register at death
onset, start whitening at 0.7 seconds, and burst at 0.9 seconds. It does not wait
for the full collapse to finish or speed up the clip. Fragment capture samples
the native pose at the shared 0.9-second cue, including its deformed mesh bounds.

## Isolated Preview

Export the current resource pack to a new local build path, then run:

```powershell
pwsh -NoProfile -File tools/smoke-harness/Invoke-TheaterPreview.ps1 `
  -Script tools/smoke-harness/theater/architect-death.json `
  -ResourcePack build/theater/architect-coral-content-v2.pck `
  -OutputDirectory build/theater/architect-coral-new-take
```

This uses a private desktop and isolated game copy. The comparison runs both
native animation tracks side by side, normalizing initial character height
without including Architect's pen/book. The second section calls the production
Architect cinematic. Only its final victory transition is suppressed by the
preview harness so the recording can finish. No production event/history
behavior is changed.

Comparison cleanup resets the entire native skeleton to setup pose before
restarting idle, clearing the death clip's unkeyed mesh deforms and constraint
values. `theater/architect-execution.json` records the execution independently
with a fresh Architect, without running the comparison first.

Recorded validation on 2026-09-22 (`architect-execution-fixed`):

- Stable 0.107.1 and Preview 0.111.0 Debug builds: zero warnings/errors.
- Native resource loaded; both comparison cleanup and fresh execution ran.
- Normal initial pose visually confirmed after comparison and in fresh execution.
- First sampled whiteout frame at clip time 0.735s; burst at clip time 0.900s.
- Source waveform audio alignment: maximum residual 16.7ms, three matched cues.
- 1920x1080, 60fps encoded; 708 unique captured frames over 14.017s
  (50.5 captured frames/s, 15.8% repeated frames). This is not a claim of
  60 unique captured frames/s.
- No full regression, installation, commit or upload was performed.
- Forwardable execution-only clip: `build/previews/architect-execution-20260922-fixed.mp4`.

The older `architect-coral-final` execution used the superseded full-clip-plus-
whiteout schedule and retained the comparison's death pose. Do not use it as
the current production preview.

The recording host still reports its existing startup/shutdown diagnostics
(including signal-disconnect and renderer/Sentry shutdown messages). These
were not treated as a clean-log assertion or modified in this task.
