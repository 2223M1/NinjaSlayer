# Yukano First Arrow Popup

## Runtime

- Entry: `YukanoMonster.ARROW` through `YukanoCombatAnimations.PlayArrow`.
- `YukanoArrowPopup` owns a room-scoped, screen-space CanvasLayer. The authored 1920x1080 composition uses 0.6 scale, fitted uniformly to other viewport sizes.
- Opening: 5 frames at 24000/1001 fps. Film: the supplied 36-frame OGV. Closing: 4 frames, without a hold. Normal and Fast retain original speed.
- The movie signals the owning attack in `FramePreDraw` when `VideoStreamPlayer.StreamPosition` first crosses `24 * 1001 / 24000` seconds. The rigid launch pose is applied in that same callback. The resumed attack launches the projectile in its own execution context; the movie has no projectile or damage callback. Film and ordinary fallback join this single launch site. Flight remains 0.25 seconds; damage awaits actual arrival.
- The film tail is not owned by the attack's completed return tail. A subsequent action can start without truncating the popup.
- RitsuLib run slot `NinjaSlayer/yukano_arrow_popup` stores shared `Shown`. Visible start claims it. Instant and resource load failure do not. Cancellation after visible start does.
- Movie audio is embedded only. It follows master volume and background mute, not the BGM slider; no separate WAV or release sound is played.

## Original Assets

Source: `popup-frame-and-yukano-right-no-hold.zip`, directory `yukano-film/`.
Runtime assets are copied byte-for-byte: `runtime-atlas.png`, `interior-clip.png`, `clip.gdshader`, `animation.json`, `yukano-original.ogv`.

OGV SHA-256: `53ab1549562c0f08372becf46fa76a608f06b0251800f48c303b7df3e01d4383`.

The supplied JSON's legacy hold metadata is not a timing input. Only its ten atlas regions and source origins are used. Film placement and the supplied clipping shader are unchanged. No new art, recut, color adjustment, or audio replacement is involved.

## Focused Recording

After exporting current resources, run from the repository root:

```powershell
./tools/smoke-harness/Invoke-TheaterPreview.ps1 `
  -Script tools/smoke-harness/theater/yukano-popup-fast.json `
  -OutputDirectory build/theater/yukano-popup-fast-new-take `
  -ResourcePack build/yukano-popup-20260926/NinjaSlayer.pck
```

Additional fixtures: `yukano-popup-normal.json`, `yukano-popup-cancel.json`, `yukano-popup-invalid.json`, `yukano-popup-finisher.json`. Use a fresh output directory for each run. `-SkipBuild` is only appropriate after rebuilding current DLLs.

The Normal fixture immediately starts its second arrow while the original popup finishes. Fast also exercises Instant, shuriken, serialization through the native save manager, legacy saves and a new companion instance. Both enter another combat after capture to check run-wide persistence.

`motion.json` records decoder position, release render frame and real projectiles. `damage.json` records HP events. `audio-sync.json` measures native FMOD cues and the embedded movie waveform, using one shared capture offset. No individual sound is moved or remixed. `verification.json` reports actual capture and repeated-frame counts, in addition to the 60fps output encoding.

## Single-arrow demonstration and regression

Use `theater/yukano-popup-single.json` to demonstrate exactly one arrow. The
`normal` and `fast` fixtures deliberately issue separate first and second arrows
to test popup reuse; two flights in those recordings are not evidence that one
action fires twice. Do not present those fixtures as a single attack.

`Invoke-NinjaSlayerSmoke.ps1 -Mode CombatRegression -NoScreenshots` tests actual
arrow actions without recording video: first-use, ordinary, fast, pause/resume,
decoder fallback before/after release, cancellation, and target invalidation
before release. It separately records the primary
hit and the finisher's deferred death commit, so the latter is not miscounted as
a second attack. Use a universal candidate bundle with its production loader.
