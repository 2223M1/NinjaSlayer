# Combat action timing

Gameplay timing and the mod's single-image body motion are independent. The host's
`CreatureCmd.TriggerAnim` waits for the caller's gate; it does not generally double
the playback speed of a non-looping Spine track in Fast mode. Full Ironclad Spine
track durations are not damage gates or parameters for the mod's PNG lunge.

## Gameplay gates

Verified against Stable 0.107.1 and Preview 0.111.0 `CreatureCmd` and the native
AttackCommand. An explicit caller wait remains authoritative for ordinary Attack:
Fast uses `min(waitTime * 0.5, 0.25)`; Normal uses `waitTime`.

| Action | Normal | Fast | Instant |
| --- | ---: | ---: | ---: |
| Default Attack | 0.15s | 0.075s | 0s |
| Ordinary Slow | 0.20s | 0.10s | 0s |
| All five marked kicks, including Flying Kick | 0.25s | 0.125s | 0s |
| Default Cast | 0.25s | 0.125s | 0s |
| Same-card damage recovery | 0.20s | 0.10s | 0s |

Kicks use the user-selected gate, not a claimed native Kingly Kick timing. The
0.025s preparation turn is included in the first gate of each CardPlay. Lunge and
jump start after that turn. Later hits retain the stance and use their full gate;
replay prepares again. Kick hits are therefore 0.25/0.70/1.15s in Normal and
0.125/0.350/0.575s in Fast, before genuine Hook or choice delays.

Ordinary Slow never changes to an Attack gate merely because another card preceded
it. Cross-card visual recovery remains nonblocking; gameplay, Hooks, piles, History
and death processing remain serial. Finishers still place Ninja Slayer directly at
impact and retain exclusive ownership until their cinematic ends.

## PNG presentation

Unsupported Normal/Fast visual distinctions use the previous Fast value in both
modes. These are mod presentation choices, not copied native Spine parameters.
Instant omits these visual Tweens.

| Presentation | Normal and Fast |
| --- | ---: |
| Attack outbound | 0.075s |
| Ordinary Slow outbound | 0.10s |
| Ordinary return | 0.10s |
| Cast motion | 0.125s |
| Hop / jump motion | 0.14s / 0.35s |
| Draw backflip | 0.25s, no elastic scaling |
| Throw / release preparation | 0.167s / 0.083s |
| Hurt / default blocked brace | 0.15s / 0.10s |
| Dodge outbound / return | 0.04s / 0.07s |
| Facing half-turn | 0.15s |

An early visual peak holds until its gameplay gate. Genuine native waits and
special projectile/cinematic gates are preserved. Pausing a Tween is not treated
as cancellation; a hurt paused by Tornado B resumes its current frame.

## Special attacks

- Tornado keeps one approach/lift and the Whirlwind hit cadence: Normal
  0.15/0.50/0.85s; Fast 0.075/0.250/0.425s. Ordinary spin is 4800 degrees/s
  in both modes; empowered spin is 12000. B hitstop is 0.0175s per hit and
  0.025s on the last hit, without gameplay delay. Existing four-energy charge,
  zero-hit cleanup and finisher handling remain.
- Dark Ninja counter and Koki Iai use the supplied
  `ActsFromThePast/Animations/SlowAttackAnimation.cs` reference: 90px, 0.5s
  pow10 outbound, concurrent 0.5s SmoothStep return, identical Normal/Fast.
  This is a supplied mod animation reference, not a native Ironclad track.
  Dark Ninja waits for the active hurt, including return, before starting counter.
  Koki's finisher preparation also travels 90px; its return follows the cinematic.
- Enemy Sawatari and Dark Ninja use the shared 0.15s hurt: 28px recoil, 18-degree
  lean and the Ninja Slayer scale envelope. Only the visual rig/core moves; the
  combat root stays fixed. Allied Sawatari retains dodge behavior.
- Sawatari bamboo uses a six-frame cycle at the episode's 24000/1001 fps:
  0.25025s per stab in both modes, with impact at frame four. Render-frame
  overshoot carries between phases; actual damage/Hook waits are never removed.
  The sampled peak is 126px travel and 19.49 degrees of forward lean.
- Counter uses native slash VFX and slash sound. Iai retains its petals and flash
  with one slash sound per sweep. Bamboo uses native dagger-throw sound and
  only the dramatic-stab Flash/Sparks, recolored white/gold at 0.35 scale.
  Evasion omits contact sparks; full block keeps them. Native resources are cloned.

The episode's visual frames were examined. The source audio was not audibly
reviewed; the selected native effects are not claimed to reproduce its waveform.

## Verification

Logic tests cover caller gates and the native Fast cap. Actual Godot Orb contracts
cover kick preparation and each hit, visual return, all forms, backflip scale,
hurt pause/resume, full hurt-to-counter ordering, reference lunge peaks, bamboo
timestamps on both combat sides, and particle ownership/cleanup. RitsuLib contracts
cover host integration for both supported versions. Repository and build-boundary
checks cover the project's architecture rules.
