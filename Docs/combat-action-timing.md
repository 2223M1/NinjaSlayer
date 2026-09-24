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

- Full companions use native pets and move intents. Koki, Sawatari and Yukano
  share yellow native attack icons/particles and `CompanionIntentLifecycle`;
  summon, defend and heal retain native visuals. Hostile Sawatari is unchanged.
  `PositionPlayersAndPets` receives local player, live companions in combat
  insertion order, then other humans. Only its two layout grouping checks treat
  companions as independent anchors. The native roster, pet ownership, Osty
  placement and paper-crane orbit are unchanged.
- Companion logic awaits every impact, attack hook and required death operation.
  The last bamboo/dual-thrust return, Koki's final summon recovery and native
  intent fades may continue visually after input reopens. Registered visual tails
  are cancelled/restored by the next action, enemy turn, retirement or exit.
  Paper cranes retain their 0.16s flight and native-scaled 0.1s explosion lead-in;
  their owner hook serializes actual impacts/deaths, without awaiting lingering
  explosion particles. Target selection never runs in detached visual tasks.
- All three companions enter finishers through
  `FinisherEligibilityService.CreateCompanionSession` and the existing
  `FinisherSession` damage ledger, death commit and cleanup. This includes
  Sawatari's friendly bamboo/dual attacks and Yukano's arrow/shuriken attacks.
  Koki keeps its Iai approach, Sawatari reaches close range on its original
  bamboo/dual impact frame, and Yukano stays at its ranged position. Projectiles
  must arrive before damage. A guaranteed forecast only selects the presentation;
  actual confirmed lethal results still control death. Finishers that mutate
  combat state remain awaited through completion.

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
  with one slash sound per sweep. Bamboo uses native blunt-attack sound and
  the complete native dramatic-stab scene at the target core, with default size,
  direction, particles and playback. Evasion omits contact; full block keeps it.
- Sawatari arrows play the native crossbow attack event on release, with native
  slash VFX at the target core on arrival. Dual thrusts play one slash sound per
  hit window, including misses; only connected hits show contact VFX. Machetes
  play dagger-throw on release and slash sound/VFX on contact. All use original
  pitch/volume and native scene dimensions; audio and particles add no waits.

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

Dark Ninja's Dark Strike steals one permanent-deck card per target impact that
actually loses HP or Naraku Life. Full block, Buffer, evasion and zero damage do
not steal. Candidate cards are captured from draw/discard before damage, because
native player death clears combat piles; the native combat-card RNG is only used
after a successful damage receipt. Priority follows Thieving Hopper. Each stolen
card owns an independent native SwipePower and optional SpecialCardReward. Earlier
thefts all return when Dark Ninja dies. If lethal Thorns already removed the
attacker before the current hit completed, that final card uses the same native
reward path directly. Event combat offers these cards alongside one fixed
Beppin Fragment reward; each card and the relic can be claimed or skipped.

The local player's stolen cards form a compact fan at Dark Ninja's empty hand.
The same fan follows the detached attack body and full return body, restoring its
parent on interruption. The return still takes .6s; only its final .15s turns
through 180 degrees using the player character's smooth turn and exposure blur.
Health/status/intent nodes stay fixed. Death and room exit release the display.

Dark Ninja Iai requires a living actor still in this combat at entry, after hurt
recovery, before animation and before damage. A confirmed lethal hit in the
Finisher death ledger also disqualifies it even while the cinematic retains one
HP. Predictions do not disqualify a living actor. An actor already disqualified
before animation does not flash.

## Sawatari weapon motion

Normal/Fast share bow draw/flight (.20/.133s), knife body throw/release
(.167/.083s), knife flight (.25s), receiving-hand turn (.12s), and weapon
retraction/emergence (.08/.16s). These are mod presentation choices. The native
TriggerAnim rule does not globally double Spine playback, and native projectile
waits can be equal in both modes. No native crossbow reload is used here.

Dual machetes use the user-selected seven-frame episode cycle (.291958s in both
modes), with one true damage event per thrust and only repeated Damage visual
recovery skipped. Other attacks retain native .20/.10s recovery. Knife contact
releases gameplay before the receiving-hand turn ends; bow release and projectile
arrival use their actual animation callbacks. Instant applies the final state
without zero-duration Tweens. See [sawatari-weapons-validation.md](sawatari-weapons-validation.md)
for source registration and actual timing/capture evidence.

Weapon and inner-hand layering now uses local sibling order; flying knives use
the normal combat VFX layer. Both actors randomly select among occupied hands
when throwing and empty hands when catching. A single eligible hand is used
directly. Already-held knives keep their hand through pile changes, draws and
the other knife's transfer. Selection runs in the models using native Niche RNG,
not in rendering callbacks. The card's native saved HeldHand property preserves
the receiving hand; copies entering a hand claim an unoccupied slot. Playing a
card can throw either physical knife, retaining the same sprite throughout its
flight, catch and return. Damage, card exhaustion and weapon counts are unchanged.

## Sawatari encounter rules (2026-09-16)

Native A8 raises HP and A9 raises damage. Act one has 76/80 HP and cycles arrow,
bamboo, bamboo using three native move states. Arrow deals 14/16 and then grants
4 Plating; bamboo deals 2x4 and then grants 1 Strength. Act three has 280/300 HP:
dual machetes deal 8/10x2, throws deal 12/14 then grant 6 Vigor, and unarmed
bamboo deals 3/4x4 then grants 4 Strength. Native Vigor lasts for the entire next
attack and is consumed by its normal AfterAttack hook. Buffs require survival
after the attack but do not require damage to connect. Allied support uses the
same HP/damage and does not receive these enemy-only buffs.

First-combat death notifications only record presentation duration. Sawatari's
choice starts after the native CheckWinCondition completes, with no living
enemies, a surviving player and no native model requiring combat to continue.
The stable public check and preview private turn-state check are patched at
their respective native safe points. Living mercenary split spawns remain in
combat. Fogmog's summons retire through native death handling; any dead revival
models remaining in the enemy collection are removed without repeating death
hooks or rewards before Sawatari moves and the choice appears.

## Event relics (2026-09-16)

Sawatari's completed duel adds one fixed Bio-Bamboo RelicReward for each player
to the combat room's native ExtraRewards. The result-page button opens native
rewards; it does not obtain the relic. The Sawatari reward patch replaces ordinary
loot only when that room contains the fixed Bamboo reward. Native serialization
preserves the model, owner and parent event across a victory reload. The ordinary
loot branch does not add Bamboo. Dark Ninja adds one fixed Beppin Fragment reward
on entering its event combat, beside the native stolen-card returns. Both relics
can be taken or skipped; event Resume does not grant them again.

Bio-Bamboo counts the owner's native AfterCardPlayed attack callbacks. Every
second attack applies one native Plating; its saved remainder survives turns
and combats. Multi-hit attacks count once. Beppin Fragment grants seven Karate
on the first real or Naraku HP loss per combat, including self-damage. Prevention
and previews do not trigger it. Both are event-only relics with native counters,
flashes and hover tips.

The existing final-HP-loss Naraku patch associates actual absorbed amounts with
their DamageResult through weak references. Fragment reads that receipt. Only
Centennial Puzzle receives a local copy representing absorbed HP when the real
loss was zero; all other callbacks retain the original result. The native Puzzle
owns its draw, used flag and reset, so an overflowing hit cannot trigger twice.

Validation: [event-relics-validation-2026-09-16.md](event-relics-validation-2026-09-16.md).

## Waterfall Giant self-destruct (2026-09-16)

A completed native death command can revive a creature into a new phase. Finisher
commits each confirmed death command once and respects its result; a living
Waterfall Giant after SteamEruptionPower.AfterDeath is not a failed kill to retry.
Once submitted, that target no longer has a pending Finisher death for Iai gating.

The native knockout and preparation turn remain unchanged. Only the giant's
Erupt animation wait is replaced, in a NinjaSlayer party, by the existing boss
burst cue. The native AttackCommand then deals self-destruct damage while the
burst video and fragments are visible. Native damage modifiers, block, Naraku
absorption, attack/death hooks and the giant's final Kill remain native. Its final
death reuses that presentation for node removal instead of playing a second burst.
No new damage manager or save format is introduced. The integration belongs to
the existing optional boss presentation transaction; a disabled presentation
retains the original explosion and damage.

## Verification

Validation evidence is in [dark-strike-theft-validation.md](dark-strike-theft-validation.md).

Logic tests cover caller gates and the native Fast cap. Actual Godot Orb contracts
cover kick preparation and each hit, visual return, all forms, backflip scale,
hurt pause/resume, full hurt-to-counter ordering, reference lunge peaks, bamboo
timestamps on both combat sides, and particle ownership/cleanup. RitsuLib contracts
cover host integration for both supported versions. Repository and build-boundary
checks cover the project's architecture rules.
