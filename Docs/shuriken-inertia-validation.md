# Held shuriken inertia candidate, 2026-09-14

Candidate based on `4f593bb912c1a96d1906803ee8d559b1d82f6a36`.
No installation, commit or upload was performed.

The existing Orb scene now contains two darker, opaque rear blades at 12/24
degrees. All three use the original front blade's size and center. Visible layers
follow actual stock with a three-layer cap. Only the blade artwork and contour
glow rotate; deformation inheritance and upright label transforms are unchanged.

One actual stock-wave release adds 1200 degrees/s, with exponential drag 20/s,
a cap of 1800 degrees/s and a stop threshold of 10 degrees/s. This produces about
60 degrees of travel in .25s, without reversing or resetting the angle. The exact
exponential integral makes travel independent of frame interval. A wave creates
its existing Shiv projectiles together and adds one impulse, regardless of target
count. Gain feedback, converted shots without a projectile and empty stock do not
spin the held blades. No gameplay model, wait, damage or sound was changed.

## Validation

- Stable/Preview Debug/Release: all four builds, zero warnings and zero errors.
- Logic: 349 passed. Both hosts' Orb product and RitsuLib contracts passed.
- Added real-scene contracts cover one/two/three-layer appearance, opaque equal
  sizing, both facings under scale/skew, fixed hand center, upright labels,
  30/60/144fps, Normal/Fast, consecutive impulses, cap, depletion and Instant.
- Resource export/headless scene loading, repository checks, build boundaries,
  compatibility-derived files and diff checks passed. The repository's previous
  blanket prohibition on Orb scene rotation was updated for the approved stack.
- Live-run telemetry save reload remains untested without a same-host fixture;
  this change adds no save fields. Existing runtime format/shutdown diagnostics
  and RitsuLib disconnect diagnostics remain.

Debug SHA-256:

- Stable: `04d57b52ae91038108ba922f453e7dc9f03068659f3b3dcbcb7212c10a7a33dd`
- Preview: `bcd341ce2a6fab3372c8506a4b9814fb6645694273146b482ff597fcf3f1c04e`
- Pack: `a7722383d4b249e9b892dfeeda4a9e0b671b6dc9a895e7006b0c9256e6297e64`

## Actual gameplay recording

`build/action-compat-validation/run-B-shuriken-inertia-01/preview.mp4`
contains 14.23s at 1280x720/60fps output: three-to-two-to-one-to-zero stock,
replenishment without spin, six consecutive throws, a two-target wave and
draw/backflip followed by throws. It runs the candidate in an isolated game on
an inactive desktop with process-loopback audio, without ComputerUse or synthetic
motion. Corrected encoded audio residual is at most 4ms.

Motion samples show a single turn settling at 59.62 degrees; gain-only velocity
stays zero. The isolated two-target wave starts at 1200 degrees/s, confirming one
impulse. Two newly created Orb nodes have an early FramePreDraw telemetry sample
before their later-registered visibility sync; the corresponding captured final
frame shows no blade or labels at zero stock. Layer visibility is governed by the
actual model count, whose consumption time is unchanged.

Logs/pack use `build/action-compat-validation/shuriken-inertia-*`; assemblies are
in sibling workspace `.sts2build/shuriken-inertia/{stable,preview}`. The recording
directory also retains frame samples, sections and audio synchronization evidence.
