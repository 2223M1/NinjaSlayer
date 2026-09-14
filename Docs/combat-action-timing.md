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
| Hurt / default blocked brace | 0.30s / 0.20s |
| Dodge outbound / return | 0.08s / 0.14s |
| Facing half-turn | 0.15s |

An early visual peak holds until its gameplay gate. Genuine native waits and
special projectile/cinematic gates are preserved. Pausing a Tween is not treated
as cancellation; a hurt paused by Tornado B resumes its current frame.
Hurt, dodge and blocked brace retain their original visual durations in both
Normal and Fast. Flying Kick holds its maximum travel and kick stance through
drawing to hand capacity, Chado and genuine Hooks. Card completion starts its return;
draw backflips and shuriken throws may overlap that held stance.

## Special attacks

- Tornado keeps one approach/lift and the Whirlwind hit cadence: Normal
  0.15/0.50/0.85s; Fast 0.075/0.250/0.425s. Ordinary spin is 4800 degrees/s
  in both modes; empowered spin is 12000. B hitstop is 0.0175s per hit and
  0.025s on the last hit, without gameplay delay. Existing four-energy charge,
  zero-hit cleanup and finisher handling remain.
- Dark Ninja counter and Koki Iai use a slash-style 0.50/0.25/0s gameplay gate,
  following the native TriggerAnim scaling rule. Their 120px pow10 outbound spans
  that entire gate, followed by a concurrent 0.25s SmoothStep return in both
  Normal and Fast. This is a mod tuning choice, not a universal monster timing.
  Instant omits the visual Tween. Ninja Slayer's ordinary Slow is unchanged.
  These companion routes do not inherit the player's current kick/somersault card.
  Dark Ninja waits for the active hurt, including return, before starting counter.
  Koki's finisher uses the same 0.50/0.25/0s gate for a continuous cubic approach
  from its actual position to the close-range endpoint. The explicit Dark counter
  also predicts and starts the existing reverse approach. Both use 0.25s visual
  return after the cinematic, or after a failed lethal prediction.
- Enemy Sawatari and Dark Ninja use the shared 0.30s hurt: 28px recoil, 18-degree
  lean and the Ninja Slayer scale envelope. Only the visual rig/core moves; the
  combat root stays fixed. Allied Sawatari retains dodge behavior.
- Sawatari bamboo uses a six-frame cycle at the episode's 24000/1001 fps:
  0.25025s per stab in both modes. The previous whole-sprite trajectory is restored:
  progress 0, .327, .759, .904, 1, .473, 0, with impact at frame four.
  The complete sprite travels 126px and rotates around its core, without added
  compression. Body and bamboo share one transform; there is no separate weapon
  pivot, rotation, translation or layer. Overlapping hurt composes on the live attack pose
  and recovers onto that pose, without restoring a mid-attack snapshot. Render-frame
  overshoot carries between phases; actual damage/Hook waits are never removed.
- Counter uses native slash VFX and slash sound. Iai retains its petals and flash
  with one slash sound per sweep. Bamboo uses native dagger-throw sound and
  the complete native dramatic-stab scene at the target core, with default size,
  direction, particles and playback. Evasion omits contact; full block keeps it.

## Special heavy and separated Hell Tornado candidate

- Collapse Fist, Slaughter and Straight Ki alone use a planar forward somersault.
  The full turn and 120px horizontal approach share the Slow gate of 0.20/0.10/0s.
  Core height follows the live target while the body contour remains above ground.
  There is no squash or stretch. Like native Bludgeon, the complete heavy-blunt
  scene spawns at the target base with its authored size and 0.2s anticipation.
  The scene runs independently of damage. The 0.20s return adds a 70px arc in both
  Normal and Fast. A finisher turns at the directly assigned impact position and
  returns only after its cinematic.
- Planar somersault and head-orbit motion feed the same `SpinExposureRenderer`
  filtering, diffusion, radiance preservation and final output as cylindrical spin.
  Only the exposure geometry differs (affine planar samples versus axial projection).
  The planar path samples 129 actual poses across the same 42ms shutter. Shared
  filtering follows combined translation and angular velocity around the live pivot,
  with perpendicular edge diffusion and a gradually fading exposure tail,
  preserving the reference's circular bands and clear fixed head. Its intermediate
  capture follows screen-pixel resolution. The cylindrical path keeps its original
  projection, exposure weights, blur radii and filtering directions.
  The output replaces the body material; no sharp body or transparent ghost stack
  is drawn over it. Idle texture changes preserve motion history. Stopping restores
  the source material, while foreign hit/finisher materials retain ownership.
- Hell Tornado orbits the torso and scarf around the fixed head in 0.1668333s per
  turn, with no Normal/Fast multiplier. Its live 42ms exposure records unwrapped
  angles so turns faster than 180 degrees per rendered frame retain their direction.
  The logical hit core follows the head; the held shuriken follows the orbiting hand.
  Ordinary action cleanup, including Alabama's takeover, preserves the split.
  Lifecycle cleanup restores source visibility and releases the exposure history.
- The delivered normal, normal-left, semi-Naraku and One Body One Soul layers are
  integrated. One Soul uses the final 1743x2712 texture, reference registration,
  preserved ground baseline and hand location. It takes presentation priority over
  Naraku and restores the underlying form on removal. Fully released Naraku uses
  the accepted 2026-09-14 upright material restoration, with 6.97381292 degrees
  counterclockwise baked into the 1254px runtime raster. Its revised 2826px
  head/body layers map at 0.5 with (-79.5,-79.5) translation and head pivot
  (1728.28622,1156.76662). No inverse rotation is applied. Internal form calibration
  shares the normal foot line at -6.45: normal scale .33, position (-166,-183);
  full Naraku .3732865 at (-18.430034,-177.44514); One Soul .112611514 at
  (50.61516,-157.23682). Their solid-body bounds center at X=0, excluding scarf
  and smoke. One Soul is 300px high, about 3% taller than standing Dark Ninja.
  The core, support contour, shadow and hand use this
  calibration. Normal/One Soul fingertip texture coordinates are (1754,430)
  and (77,293). Half Naraku shares the entire normal calibration, including its
  (1754,430) fingertip. Full Naraku uses (1180,790), outside/above the screen-right wrist.
  Alternate-form shadows center on near-ground body support; neither raster is
  rotated or resampled again.
  Both One Soul and full Naraku use the revised complete-hood head partitions,
  with normal source-over composition (body below head). This candidate is not
  a release artifact.

## Native effects and form changes

Held Shuriken stock draws one, two or at most three equal-size opaque blades at
the existing hand center. Rear blades are darker and offset by 12/24 degrees.
Each actual projectile release adds one angular impulse to the held blades and
contour glow, including an all-target wave only once. Gain/activation feedback
alone does not add inertia. A single impulse turns about 60 degrees and decays
over .25s in both Normal/Fast; faster releases add to the current velocity with
an 1800 degrees/s cap. Instant settles immediately without a Tween. Rotation is
local to the blade artwork, retaining the hand's full deformation and the native
upright labels. Stock layers and numbers still follow the actual model count;
zero stock hides the visual and clears its spin without delaying model removal.
Existing damage, projectile timing, texture, trajectory, sound and target rules
are unchanged.

Only Collapse Fist, Slaughter and Straight Ki use native heavyBlunt at the base;
the other seven mod heavy-feedback calls use bluntPath/bluntAttack at the core,
like ordinary attacks. Acquired native cards retain their own feedback.
Native heavy, slash, horizontal slash, dramatic stab, fire burst and hit sparks
retain their host placement, scene size and playback. The custom Iai scene keeps
Grand Finale's separate core/bottom layers. Shiv retains the explicitly selected
shuriken texture, yellow-orange tint, spin and current hand origin. Alabama uses
the native fire-burst intensity of .75 and default hit-spark size.

Finishers retain the dark backdrop, camera, hit feedback and death sequence;
the extra red radial light and its flash layer have been removed. Enemy approaches
use known targets and the already resolved hit count for read-only damage preview,
then move only the visual rig with EaseOutCubic during the existing animation gate.
One command approaches once and holds for its remaining hits. False predictions
return in .2s without swallowing subsequent actions. Unpredicted kills do not
teleport or delay damage. Random/custom callbacks are not evaluated in advance.

Visible combat form changes expose the new form immediately beneath up to 128
rigid textured triangles of the previous live pose, including both head/body layers
in Hell Tornado. Burst lasts .083s and disappearance .25s in Normal/Fast; Instant,
initial binding and loading show the final form directly. Same-frame changes
coalesce. The native block-break event plays once at .8 volume. No UI, shuriken or
shadow is copied; death/room cleanup releases all fragments.

The episode's visual frames were examined. The source audio was not audibly
reviewed; the selected native effects are not claimed to reproduce its waveform.

## Verification

Logic tests cover caller gates and the native Fast cap. Actual Godot Orb contracts
cover kick preparation and each hit, visual return, all forms, backflip scale,
hurt pause/resume, full hurt-to-counter ordering, reference lunge peaks, bamboo
timestamps on both combat sides, and particle ownership/cleanup. RitsuLib contracts
cover host integration for both supported versions. Repository and build-boundary
checks cover the project's architecture rules.
