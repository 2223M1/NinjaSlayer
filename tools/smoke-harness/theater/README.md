# Act 3 Promotional Theater

`promo-fight.json` drives native Fast-mode combat on the current workspace build.
It uses real cards, moves, damage, projectiles, voices, hurt reactions and death.
The separate SmokeDriver supplies actors, the friendly-fire target, camera cuts
and Sawatari's scripted handoff. It is never included in the published mod.

## Record

From the repository root, in PowerShell 7 Core:

Use the current production source checkout.
Do not maintain a separate theater source checkout or reuse old recording packages.
For routine edits, change `promo-fight.json`, then run the command below; no C#
changes are needed unless introducing a genuinely new action type.

```powershell
pwsh -NoProfile -File tools/smoke-harness/Invoke-TheaterPreview.ps1 `
  -OutputDirectory build/theater/promo-final
```

The launcher imports and exports the current project resources, compiles both
supported hosts, records source and audio-bank hashes, stages an isolated game copy,
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
`-PreviewDataDirectory`, `-GameRoot`, `-RitsuLibDirectory`, `-Godot` and
`-Python` when local paths differ. Builds and resource export are mandatory.
Omit `-OutputDirectory` for a unique timestamped output directory. Source changes
during preparation or capture invalidate the take; rerun after edits finish.

When disk space is limited, `-PreparationDirectory` may name an existing exited
copy under this project's `build/theater/preparation/`. The launcher checks its
game executable against the installed host and rejects a running copy. Resources
are still freshly exported and both Mod hosts rebuilt on every invocation; only
the isolated base game directory is reused. Recording outputs remain separate.

```powershell
# Two independent full takes.
pwsh -NoProfile -File tools/smoke-harness/Invoke-TheaterPreview.ps1 `
  -OutputDirectory build/theater/repeated -Repeat 2

# Replays every preceding cue, then captures only the requested interval.
pwsh -NoProfile -File tools/smoke-harness/Invoke-TheaterPreview.ps1 `
  -OutputDirectory build/theater/stab-edit `
  -FromCue dark_strike_closeup -ToCue alabama_counter

# Rehearsal reporting; the script still uses real HP and combat actions.
pwsh -NoProfile -File tools/smoke-harness/Invoke-TheaterPreview.ps1 `
  -OutputDirectory build/theater/rehearsal -Rehearsal
```

Output directories must be new. Every take contains `theater.mp4`, synchronous
24-bit `theater-audio.wav`, the unmodified video, raw captured audio, script copy,
timeline, actual HP changes, motion samples, completed-action coverage and hashes.
`audio-sync.json` contains per-take waveform matching against actual source audio;
there is no fixed guessed audio delay. Original capture times are preserved when
a frame is late. The verification report exposes both encoded FPS and measured
render FPS, including repeated frames.

## Edit

### Architect sequel

`architect-full-greeting-alabama.json` uses `continuePromo: true`: the driver
replays `promo-fight.json` in the same run before entering the native Architect
room, preserving actual remaining HP, max HP and relic state. Record only the
sequel with `-FromCue architect_room_full_greeting_alabama`. It uses the full
Boss greeting's native entrance and DOMO movie, then the formal Alabama
execution and exit; no calibration dialogue is inserted into the footage.
An unrecorded four-second black buffer separates the prior fight's visual and
audio tails. Forced melee selection is scoped to the native selection call and
does not invoke deck-removal/acquisition hooks or alter carried HP/relic state.

```powershell
pwsh -NoProfile -File tools/smoke-harness/Invoke-TheaterPreview.ps1 `
  -Script tools/smoke-harness/theater/architect-full-greeting-alabama.json `
  -FromCue architect_room_full_greeting_alabama
```

Each cue has a stable `id` and ordered `steps`. A step is an action, a `sequence`,
or `parallel` branches. Card steps use the host action queue. A branch starts its
next step only after the previous native operation completes. `delay` and `wait`
add intentional camera or staging holds; they do not truncate native actions.
`notBefore` is an optional absolute minimum cue start. `duration` is a minimum
whole-film duration, not a deadline that cuts off ongoing animation.
The full promotional take must remain below 40 seconds. Preserve Yukano's clear
rolling entrance, two shuriken rounds, arrow popup and native victory departure.
Overlap only non-close-up actions to save time; never cut an animation or speed
up the recording. The promotional fixture gives her relic one remaining combat
so the normal victory hook performs her departure.

| Action | Main fields |
| --- | --- |
| `card` | `card` C# type name, `energy`, `target`, `selection` indices; `charge` and `seconds` for Tornado drag |
| `move` | `actor`, `move`, `target`; Sawatari uses `bamboo`, `bow`, `dual`, `throw` |
| `roll_volley` | `count`; native draw/backflip followed by current discard-triggered shuriken |
| `knife_exchange` | `count`; throws and returns actual generated machete cards |
| `entrance` | `actor`: `koki` or `yukano` |
| `yukano_farewell` | `mode: "wait"` waits for native victory departure without adding an attack |
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
