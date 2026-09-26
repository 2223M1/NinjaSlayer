# Act 3 Promotional Theater

`promo-fight.json` drives native Fast-mode combat on the current workspace build.
It uses real cards, moves, damage, projectiles, voices, hurt reactions and death.
The separate SmokeDriver supplies actors, the friendly-fire target, camera cuts
and Sawatari's scripted handoff. It is never included in the published mod.

## Record

From the repository root, in PowerShell 7 Core:

```powershell
pwsh -NoProfile -File tools/smoke-harness/Invoke-TheaterPreview.ps1 `
  -OutputDirectory build/theater/promo-final
```

The launcher compiles both supported hosts, stages an isolated game copy,
disables telemetry/network access for that copy, and starts a new inactive
Windows desktop. It never switches the user's active desktop. The installed
game's mods, saves and settings are not changed. Music and ambience are muted;
gameplay sound and character voices remain enabled. The promotional script sets
`narrationEnabled: false` through the mod's existing setting in the isolated
profile; other scripts default to narration enabled. Process-specific WASAPI capture avoids
recording sound from the user's other applications.

The default `forward_plus` renderer uses Godot's asynchronous texture readback,
timestamped when requested, to avoid waiting for the GPU on every captured frame.
`-Renderer gl_compatibility` uses the synchronous path. Both render the actual
game scene and the same production animations. The theater recorder uses the
x264 ultrafast preset to leave CPU time for the game; it does not change game
speed. Native startup/exit warnings remain in the raw logs.

Local dependencies are .NET 9, the supported game references and installed game,
RitsuLib, ffmpeg/ffprobe, Python with NumPy/SciPy, and the existing `ssh localadmin`
loopback administrator alias. Supply `-StableDataDirectory`,
`-PreviewDataDirectory`, `-GameRoot`, `-RitsuLibDirectory`, `-ResourcePack` and
`-Python` when local paths differ. The resource pack must match the workspace's
resources. `-SkipBuild` is only for a previously built current candidate.

```powershell
# Two independent full takes.
pwsh -NoProfile -File tools/smoke-harness/Invoke-TheaterPreview.ps1 `
  -OutputDirectory build/theater/repeated -Repeat 2 -SkipBuild

# Replays every preceding cue, then captures only the requested interval.
pwsh -NoProfile -File tools/smoke-harness/Invoke-TheaterPreview.ps1 `
  -OutputDirectory build/theater/stab-edit -SkipBuild `
  -FromCue dark_strike_closeup -ToCue alabama_counter

# Higher HP for rehearsals; this deliberately prevents the final kill.
pwsh -NoProfile -File tools/smoke-harness/Invoke-TheaterPreview.ps1 `
  -OutputDirectory build/theater/rehearsal -Rehearsal -SkipBuild
```

Output directories must be new. Every take contains `theater.mp4`, synchronous
24-bit `theater-audio.wav`, the unmodified video, raw captured audio, script copy,
timeline, actual HP changes, motion samples, completed-action coverage and hashes.
`audio-sync.json` contains per-take waveform matching against actual source audio;
there is no fixed guessed audio delay. Original capture times are preserved when
a frame is late. The verification report exposes both encoded FPS and measured
render FPS, including repeated frames.

## Edit

Each cue has a stable `id` and ordered `steps`. A step is an action, a `sequence`,
or `parallel` branches. Card steps use the host action queue. A branch starts its
next step only after the previous native operation completes. `delay` and `wait`
add intentional camera or staging holds; they do not truncate native actions.
`notBefore` is an optional absolute minimum cue start. `duration` is a minimum
whole-film duration, not a deadline that cuts off ongoing animation.

| Action | Main fields |
| --- | --- |
| `card` | `card` C# type name, `energy`, `target`, `selection` indices; `charge` and `seconds` for Tornado drag |
| `move` | `actor`, `move`, `target`; Sawatari uses `bamboo`, `bow`, `dual`, `throw` |
| `roll_volley` | `count`; native draw/backflip followed by current discard-triggered shuriken |
| `knife_exchange` | `count`; throws and returns actual generated machete cards |
| `entrance` | `actor`: `koki` or `yukano` |
| `missiles` | Native two-missile summon and attack; `friendlyFire: true` redirects only the second missile |
| `apology` | Native smooth turn, Koki farewell, Yukano relocation |
| `camera` | `actor`, `zoom`, `height`, `seconds`; `wide` releases the camera lease |
| `takeover` | Mirrored Dark Strike, target hold, pullback, leap and kick |
| `form` | `normal`, `semi`, `full`, `soul`; uses actual powers/relics |
| `power` / `remove_power` | `actor`, power type name, `amount` |
| `clear_block` / `clear_air` | Removes fixture block or airborne powers through native commands |
| `aim` / `wait` | Native targeted drag, or `seconds` hold |

Common fields are `repeat`, `delay` and `covers` (completed-action labels).
Actors are `ninja`, `sawatari`, `dark`, `koki`, `yukano`; `enemy` resolves to the
current opponent. HP is set only when an actor enters. Final damage and death
must be achieved through the scripted attacks, not a late HP overwrite.

## Verify

```powershell
node tools/smoke-harness/theater/verify-theater.mjs build/theater/promo-final
node tools/smoke-harness/theater/verify-theater.mjs build/theater/stab-edit build/theater/promo-final
```

This checks resolution, output frame rate, measured sound alignment, completed
actions, actual final death, mirrored blade layers and Sawatari's impact hold.
It writes `verification.json`, `timeline.md` and `coverage.md`. With a reference
take, all captured cues must have identical before/after HP, powers, hand order,
shuriken stock and counter count. Rendering/particle RNG and subframe sampling
can differ, so videos are not claimed to be pixel-identical.

Changing JSON timing, repeats, card selection or camera parameters does not
require rebuilding. Adding a new action requires changing only the SmokeDriver.
