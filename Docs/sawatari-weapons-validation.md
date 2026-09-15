# 0.2.12 release candidate follow-up, 2026-09-15

Base `46d6d2c8fca6b4bb0f1e35f1ca7913d6a632d5d0` plus the combined local changes.
The following are development-DLL results, not immutable release-package proof.

- Darv uses the native Dusty Tome selection and grant flow to give One Body One
  Soul; Zazen Drink is registered in the event pool and remains Nancy's reward.
  Both host product contracts exercise 32 selections and actual acquisition.
- Intermission facing now refreshes the existing weapon rig when body FlipH
  changes. Sprite2D FlipH does not mirror child nodes automatically. The real
  stable event screenshot confirms both machetes remain aligned with the hands.
- Stable 0.107.1 with installed RitsuLib 0.6.1 passed 12 event checkpoints in
  `build/action-compat-validation/run-B-release-v0212-event-facing-03/` and all
  67 weapon/motion checkpoints in `run-B-release-v0212-final-motion/`.
- Both Release products, SmokeDrivers, full Orb contracts and RitsuLib contracts
  passed; 357 logic tests and repository validation passed. Exact DLL paths and
  logs are under `build/release-v0.2.12/`. Host MVIDs are
  stable `97f10687-c306-4798-ab75-8b9f23f34dfb` and
  preview `73b63ee0-6c0a-47bb-b0d1-b21f6d94222e`.
  Preview contracts are not preview real-game evidence.
- The console route uses native `act 3`, `room Monster`, `win`, then
  `event NINJA_SLAYER_EVENT_SAWATARI_EVENT`, awaiting each command. A normal
  encounter must exist before the event resolves its companion encounter.
  The console scenario verifies event lifecycle; the separate fixed encounter
  scenario retains its strict Finisher assertion.
- A third-party CancelPlayCard exception reported in the user's log has not been
  fixed by these changes. No general floating-card compatibility fix is claimed.

Earlier evidence below describes the same feature work before the final release
follow-up; references to no upload describe those earlier local checkpoints.
# Sawatari weapons — local validation, 2026-09-15

Base: `de42b0c94a24ec9e404f1977eec09e8589a186b4` plus uncommitted changes.
This is a local development delivery, not a GitHub or Workshop release.
Existing Dark Strike and FreeControl changes remain in the working tree.

## Selected B sound and directed Iron Wave

The selected production machete contact sound is now
`event:/sfx/enemy/enemy_attacks/vantom/vantom_dismember`, for both Sawatari's throw
and the player's return. SmokeDriver no longer swaps candidate sounds during
playback; its remaining BladeFeedbackB route records the production behavior.

The native Iron Wave card spawns `vfx_flying_slash` at the victim core. Its Spine
animation contains three left-to-right translation tracks starting roughly
1000-1150px to the left. That authored player-side trajectory was wrong for an
enemy Sawatari. Each instance now maps the three native translation curves from
the actual attacker core toward the victim core and reflects the artwork when
attacking left. Attachment scale, native colors, animation time and completion
cleanup remain unchanged. No damage or sound gate moved: the native visual starts
on the damage frame and then plays its own flight, as before.

Evidence: `build/sawatari-weapons/blade-direction-01/`. Stable/Preview Debug and
Release builds have zero warnings/errors; all 349 Logic tests and both full
Orb/RitsuLib suites pass. New actual Spine scene checks sample all three layers
in both directions, across high/low and near targets, under a translated and
nonuniformly scaled VFX container. They verify the attacker origin and trajectory
within .5px, native artwork scale, and native completion cleanup.

Repository, compatibility, build-boundary and `git diff --check` checks pass.
The full actual Stable run
`build/action-compat-validation/run-B-blade-direction-motion-01/` passes all 67
checkpoints, including allied/enemy direction, defenses, early death, target
removal and weapon lifecycle. Normal/Fast adjacent dual damage intervals are
.28147/.28328s (within one 60fps frame of the unchanged .29196s cycle); Instant
segments share one process frame. Sound, effect creation and damage frames match.

The background recording
`build/action-compat-validation/run-B-blade-direction-01/blade-preview.mp4`
passes 39 live checkpoints and shows the corrected enemy-to-player flight,
bamboo, arrow, selected-B throws/returns, Iai and Dark Counter. It is 18.85s at
1280x720/60fps. Native/debug-source and captured-PCM verification found 29 usable
audio anchors with maximum encoded residual 35.65ms. One Vantom contact has a
clear isolated source match; all four real playback calls are independently
counted. The initial voice-only mux check rejected an overlapping hurt voice;
the action-boundary calibration used for delivery passed without changing video
frames or audio pitch. No install, commit or upload was performed.

## Initial blade weight comparison (superseded above)

Dual machetes now use native flyingSlash (Iron Wave/Dash) and one heavyAttack MP3
per damage segment. Koki Iai uses that same MP3 once per Iai, while Dark Counter
uses Ironclad's native attack event. Enemy machete contact and the player's return
use Axe Ruby Raider's attack event; the Vantom alternative exists only in the
SmokeDriver preview entry. Slice was withdrawn and is not included.

Bamboo removes only its instance's lowercase `slash` particle before tree entry.
Native Flash/Sparks, materials, sizes, orientation and lifetime are retained; the
cached original scene is unchanged. No action, damage, timing or intent changed.

Fresh evidence: `build/sawatari-weapons/blade-feedback-01/`. Stable/Preview Debug
and Release product builds have zero warnings/errors; all 349 Logic tests and
both full Orb/RitsuLib suites pass. Repository, compatibility and build-boundary
checks pass. The full background Stable run
`build/action-compat-validation/run-B-blade-feedback-motion-02/` passes all 67
checkpoints, including both sides, Normal/Fast/Instant, block/evasion, lethal first
hit, target removal, pause/restore, four forms and original knife transfers.
Measured adjacent dual hits are .2828/.2845s (Normal/Fast), within one render frame
of the unchanged .291958s cycle. Instant hits remain on one process frame.
Player return contact, native sound and damage share the same frame.

Two actual 1280x720 60fps clips, with identical actions and only machete audio
swapped:

- A: `build/action-compat-validation/run-B-blade-feedback-A-04/blade-preview.mp4`,
  18.87s; 39 live checkpoints pass, 32 audio anchors, maximum encoded residual
  29.3ms.
- B: `build/action-compat-validation/run-B-blade-feedback-B-01/blade-preview.mp4`,
  18.85s; 39 live checkpoints pass, 29 audio anchors, maximum encoded residual
  34.0ms.

Both show bamboo, arrow, two dual attacks, two throws/returns, two Iai and two
counters. The final FMOD playback entry observes native calls that can bypass an
inlined SfxCmd; private-bank voices retain their separate RitsuLib observer.
Runtime sound counts assert one selected event and no old slash MP3 for Iai,
counter and player return. Damage/VFX event records remain beside each recording.

Audio verification includes MP3 source waveforms and silently rendered native
FMOD templates from the actual host banks, preserving authored event onset.
Ironclad's layered sound additionally uses spectrum/onset agreement. Captured
audio is aligned at the preview's action boundaries with 20ms crossfades, without
pitch resampling; the video contains only real recorded frames and an initial
trim. Identified PCM onsets are then tracked through final AAC with correlation
at least .8 and an unchanged 40ms maximum timing bound. `blade-audio-sync.json`
retains accepted/rejected anchors and per-sound coverage. All four A machete
contacts have source matches; B has one clear Vantom source match, while all four
actual B playback calls are independently counted. This does not claim every
overlapping sound can be isolated from the mix.

Validated Debug DLL SHA-256: Stable
`70C189A8694C2B79266021D5DDC7FA982B15614A9B978EB005582AF70B56321F`, Preview
`E80CF9F37DA1B88F94598B98202E09EF9FE7ABD5084F0C717F70518095FE6C22`.
PCK remains `D3405AF9C00AF8B304382AE47FEE018B1235A8261D4A6BECC9139AC0E3F721C8`.
Existing host/RitsuLib shutdown diagnostics remain as documented below. No local
installation, publication, foreground automation or synthesized animation was used.

## Previous weapon feedback revision

Bamboo now uses native bluntAttack on contact and the full dramaticStab scene at
the target core. Arrow release plays the native Crossbow Ruby Raider event, with
slash at arrival. Each dual thrust plays one slashAttack, including evasion;
contact slash VFX is omitted on evasion. Machetes play daggerThrow at release and
slashAttack/slash VFX at contact. Native volume, pitch, scene size and particle
parameters are retained. The player Token's existing feedback is not duplicated,
and StatusIntent(1) remains. No motion or damage waits were changed.

The live test exposed missing allied weapon initialization: native
Creature.AfterAddedToRoom only invokes Monster.AfterAddedToRoom on the enemy side.
The event now explicitly creates the ally's rig after setting its facing. A
separate real third-act event run verifies that production entry point. Target
removal during arrow flight is checked before querying IsHittable, avoiding a
native hook lookup on a creature that no longer belongs to combat.

Final product builds and contract evidence: `build/sawatari-weapons/feedback-03/`.
All four Stable/Preview Debug/Release builds have zero warnings/errors; 349 Logic
tests and both full Orb/RitsuLib suites pass. Both SmokeDriver host builds pass.
The resource pack is unchanged from revision 03. Native shutdown/RitsuLib
diagnostics described below still exist; this is not a clean runtime-log claim.

Actual Stable recordings, 1280x720 at 60fps with captured audio:

- `build/action-compat-validation/run-B-sawatari-feedback-04/weapons-preview.mp4`:
  continuous 17s excerpt containing all four attacks, without synthesized frames.
- `build/action-compat-validation/run-B-sawatari-feedback-04/preview.mp4`:
  full 58.45s recording; all 67 checkpoints pass. It covers Normal/Fast/Instant,
  block, evasion, right-facing allied attacks, lethal first hit, disappearing
  target, held-blade transfers and form/facing changes.
- `build/action-compat-validation/run-B-sawatari-feedback-event-01/`:
  all 11 checkpoints pass for the real allied event and duel transition.

Release audio shares the real projectile's creation frame; contact VFX/audio
shares damage hooks' frame. Core position is checked before hurt motion. Detailed
samples are in `sawatari-feedback.json` and `sawatari-timing.json` beside the full
recording. Arrow contacts measured .3385/.3507s in Normal/Fast; adjacent dual hits
measured .2998/.2833s against the unchanged .291958s source cycle. Instant dual
hits share the same process frame. Draw/flight timing checks allow two render
frame boundaries rather than imposing a narrower wall-clock interval.

Audio calibration now optionally includes the native blunt/slash/throw MP3s,
alongside FMOD voice source variants. The full export matches 37 accepted source
waveforms, with 36.6ms maximum encoded residual. The excerpt is separately checked
after encoding: 25 matches and 36.0ms maximum residual. Reports retain rejected
matches and counts per sound; arrow release is verified by its live FMOD event
timestamp, not by a separately extracted crossbow waveform.

Validated DLL SHA-256: Stable
`4F6C91F50FEBAC235FDCB9B43F8E4E5D053955A226A1FDBAC05D1E81B18EAAD0`, Preview
`A4FE3B414B77F3A6B5568F79D3B8939C7A56CAE3A7901DAA363E9FC5364FBD90`.
PCK SHA-256: `D3405AF9C00AF8B304382AE47FEE018B1235A8261D4A6BECC9139AC0E3F721C8`.
The isolated game's Stable DLL and PCK hashes match these candidates. Nothing was
installed, committed or published, and recording stayed on the inactive desktop.

## Machete revision 03

Actual transfers now adopt the receiving grip's reflection and remove inherited
skew, keeping local scale magnitudes and the existing .12s catch-turn tail.
The remaining blade also adopts the primary grip when the first card is used.
Machete is registered in TokenCardPool as Attack/Token; cost 2, damage 12,
Retain/Exhaust and generation restrictions are unchanged. StatusIntent(1) is
intentionally retained for the enemy's hand-clogging throw, per user instruction.

Fresh full Logic (349), both Orb/RitsuLib suites, all four product builds, resource
export, repository/build-boundary checks and diff validation passed. Evidence:
`build/sawatari-weapons/revision-03-final/`. Product builds have zero warnings/errors.
Actual Stable recording: `build/action-compat-validation/run-B-sawatari-machete-05/preview.mp4`
(45.83s, 1280x720, 60fps, AAC). All 45 checkpoints passed, including real two-knife
transfers/returns, full held-basis orientation, same-size flight, primary-grip
takeover, mirrored Instant catch, Token attack damage, and 24 form/facing/count
states. Native slash visuals and audio start on the damage frame after arrival;
VFX position is compared with the core before hurt motion starts, not afterward.
Audio calibration matched 16 source waveforms, with 11.3ms maximum residual.
The earlier two failed captures exposed test sampling assumptions (post-hurt core
position and Godot-renamed sprite nodes); no gameplay timing was changed for them.

Validated Debug DLL SHA-256: Stable `F201613482748192D74FF272B8CAD0BDC36FD498163E5B2BBC2403047E47F00A`,
Preview `28D4C28B3CE670B3A7AF9B29044CAB6D6DDF6008930818740CF2CFF43854EC95`.
Resource pack SHA-256: `D3405AF9C00AF8B304382AE47FEE018B1235A8261D4A6BECC9139AC0E3F721C8`.
The known RitsuLib settings-disconnect and native shutdown diagnostics remain.
This was an isolated background run, with no installation, commit or publication.

## Motion revision 02

The current implementation supersedes the alternating wrist slash and the .65
player blade scale described by the earlier art review. Asset PNGs are unchanged.

- Bow draw lasts .20s, pulling the arrow back 24 actor pixels while a subdivided
  mesh moves the string center with fixed endpoints. Flight lasts .133s and starts
  in the draw completion callback, already aimed. The .12s string rebound overlaps
  flight. Normal/Fast share these visual times; they are mod timings, not native
  CrossbowRubyRaider parameters. No native crossbow reload is called.
- Dual machetes use rigid whole-body translation from episode 08 at 08:14.5.
  Background registration and 87-629 inlier features per sampled frame found less
  than .015 degrees of rotation. Seven source frames give a .291958s cycle, with
  impact after the two-frame thrust. The first/last vertical transition blends
  onto the current floor baseline; the body never sinks below that baseline.
  Body rotation/scaling and independent wrist slashes are not added.
- Only this source-driven dual sequence scopes out Damage presentation recovery.
  True damage, defenses, hooks, history and death remain serial. Other weapon
  attacks preserve native Damage recovery (.20/.10s).
- Knives retain the same sprite and world size throughout flight/catch. The .12s
  receiving-hand turn starts at contact and no longer blocks damage. Revision 03
  removes the sender's local reflection/skew at contact; the receiving grip owns
  handedness, and local scale magnitudes remain unchanged. New/reconstructed blades use the
  source hand's .8193301/.8120331 actor scale; form baseline scale is normalized.
- Weapon changes retract for .08s and emerge/settle over .16s, with hand-plane
  clipping and fixed knife dimensions. Bamboo and bow pass through the inner hand.
  Repeated intent refresh does not restart a transition; attack can start while
  it finishes. Initial binding and Instant select the final pose directly.
- Instant flight, catch, draw, dual and weapon layout do not await zero-time Tweens.
  Flight cancellation observes both the projectile and destination leaving the
  scene. Arrival completes directly on the Tween signal, without an extra frame
  of gameplay polling.

Source registration, four build logs, Logic/Orb/RitsuLib logs and exported pack:
`build/sawatari-weapons/revision-02/`. Final actual Stable capture:
`build/action-compat-validation/run-B-sawatari-motion-02/preview.mp4`.
All 43 checkpoints passed, including Normal/Fast/Instant dual damage, arrow
pullback/contact, hurt overlap, pause, same-sized round trip, 24 form/facing/count
combinations, disappearing destination and attack during a weapon transition.
Measured arrow contacts were .3500/.3339s (Normal/Fast); dual adjacent hits were
.2815/.2833s, within one 60fps frame of the source period. Instant hits shared the
same process frame. These are observed render-frame measurements, not idealized
constant-only assertions. Final audio matched 14 source cues, with 18.7ms maximum
residual after measured compensation.

The standalone ArchitectureTests project is absent (only old bin/obj remain).
Architecture validation uses the repository and build-boundary checks. Full Orb
contracts required updating the shadow fixture to mirror the newly offset body
origin as the live rig does; the original planted-shadow assertion remains.
The isolated game retains the pre-existing RitsuLib settings disconnect and native
shutdown diagnostics. No new animation exception or shader error was observed.
Preview host gameplay is covered by contracts, not a second full live recording.

## Gameplay

- Act one: opening arrow deals 14/16, then grants 2 Strength. Subsequent bamboo
  attacks keep their 2×4 base damage and native Strength scaling.
- Act three: 252/278 HP. Support attacks deal 12/14×2 to one enemy without
  throwing knives or gaining Strength. The duel opens with the same double hit.
  A double attack does not grant Strength and always schedules a throw next.
- Subsequent selection uses held weapons: two select double attack, one selects
  throw, none selects 2×4 bamboo. Throw deals 12/14, grants 3 Strength and gives
  the selected player one Machete, even when defenses prevent damage.
- Machete: 2-cost, 12-damage, non-upgradable Token attack with Retain and Exhaust.
  Sawatari's throw deliberately retains the native StatusIntent(1) indicator:
  this playable attack still serves as a hand-clogging burden in the encounter.
  Native Strength, Weak, Vulnerable, generation, overflow, autoplay and exhaustion
  apply. Playing at this combat's act-three Sawatari returns one weapon and
  updates intent immediately. Discard, direct exhaustion, transformation and
  another target do not return a weapon. Copies/replays may each return one,
  capped at two; cards have no additional persistent weapon ID.
  Flight runs in the attack's AfterAttackerAnim callback, before native hit
  feedback. WithHitFx uses vfx_attack_slash at the target core and slashAttack
  audio, followed immediately by real damage. Catch rotation remains a visual tail.
  Moving a remaining blade from the second grip to the first adopts the new
  grip's angle and reflection while preserving the transferred blade's size.
- Acts one/three use the same event ID and visited-event exclusion. The route
  only replaces eligible strong normal-monster encounters at unknown nodes.
- 93 registered cards; ordinary rewards remain 80 (20/35/25), with the same
  starter deck. Machete is excluded from reward/random-generation pools.

## Independent visual ownership

`SawatariWeaponVisuals` owns a common body attachment with two machete grips,
an independent bamboo sprite, bow/string sprites at their shared pivot, and
an independent arrow. The body texture does not change between weapons. A
single inner-fist layer changes depth: 1 for bow/bamboo, 30 for machetes. Enemy
blade depths remain 10/20. The existing foot marker is shared by all poses;
intent placement is adjusted once above the hat and stays outside body motion.

`SawatariWeaponVisuals.Flight` reparents the same sprite into the combat VFX
container with its global transform preserved. It moves continuously to the
receiving grip and reparents at contact, adopting its handedness without resizing;
only rotation settles afterward. Neither knife
nor arrow flight creates a replacement `NItemThrowVfx`. Losing the destination
or leaving the scene cancels the flight and releases its sprite.

`PlayerMacheteVisuals` associates native cards with held sprites locally. Hand to
Play keeps the existing blade until release; native pile callbacks remove
discarded/exhausted visuals. Copies, replays outside the hand and loaded card
state can create a missing visual. The monster still generates its native card
after damage and Strength; visual transfer does not reorder gameplay commands.
The player uses the existing pose synchronization, including Hell Tornado's body
transform. There is no new polling loop, global weapon registry, physics system,
saved ID or compatibility layer.

The discarded visual implementation consisted of baked bow/bamboo body swaps,
temporary player grip coordinates, separate flight sprites, and a redundant
hand-contents subscription. Their replacements live in the two owning nodes and
the shared flight partial. The now-unused extension to Yukano's projectile helper
was removed. Old composites and import records remain only in
`build/source-reconstruction/sawatari-weapons/reference/retired-runtime-composites/`.

## Approved art

Image processing is owned by task `01a09dd6-935c-7d43-a139-a2a3ab31154f`
(“抠图 (2)”). No new image processing was performed during integration.

- Enemy knife poses: `grip-revision-02/sawatari-weapon-rig.json`.
- Player grips: `player-grip-review-02/player-weapon-rig.json`, SHA-256
  `844cf27d6100d721a047a09d23b452e18da2958becb29d3157235a9eaec450ff`.
  All 223 locked PNG hashes were verified in the previous delivery. The approved
  grip positions, rotations 122.9726773°/142.9726773°, and depths 110/100 remain.
  Revision 02 supersedes the old 0.65 blade scale with the actual source size.
- Independent bow, string, arrow, bamboo, common body and hand:
  `independent-runtime-01/runtime-interface.json`. All nine asset hashes matched.
  The image task's reconstruction audit reports exact RGBA equality for the four
  approved rest states. Runtime uses its aligned crops at scale 1, with no second
  application of the source affine transform.
- Machete stays the same 285×51 image and handle pivot [236,17] throughout
  enemy hold, flight, player catch and return. Centered offset is [-93.5,8.5].

Bamboo is the approved visible fragment. Its hidden root has not been invented;
future poses that expose new pixels still need source artwork and visual review.

## Previous candidate evidence (before motion revision 02)

| Host | DLL relative to repository | SHA-256 |
| --- | --- | --- |
| stable 0.107.1 | `build/sawatari-weapons/stable/Debug/NinjaSlayer.dll` | `5bc90d539c320e540f79cd05e2d2cfb87056eb5b8dd9097595b5aaa2e738a760` |
| preview 0.111.0 | `build/sawatari-weapons/preview/Debug/NinjaSlayer.dll` | `c1352b4cb005649258ede72ca6d07cc526a2a32ed23249a3f81c1b9f004afa77` |

Host MVIDs: stable `97f10687-c306-4798-ab75-8b9f23f34dfb`, preview
`73b63ee0-6c0a-47bb-b0d1-b21f6d94222e`. Both use RitsuLib 0.5.12.
Resource pack: `build/sawatari-weapons/NinjaSlayer-entities.pck`, SHA-256
`472e8b22d3c67e3236f5b59741ffef8e5029ad4cfe5e291f16c7005f5eb6b962`.
The live game's copied stable DLL and PCK were hashed and matched these inputs.
The embedded base SHA alone does not identify the uncommitted source tree.

| Check | Result |
| --- | --- |
| Sequential stable/preview product builds | Passed, 0 warnings/errors |
| Both SmokeDriver builds against their candidate DLL | Passed |
| Both targeted Sawatari product contracts | Passed |
| Repository validator, compatibility sync, diff check | Passed |
| Build boundary tests | Passed |
| Final stable live run `run-B-sawatari-entities-04` | 50 passed checkpoints, 0 failed, native quit |
| Final recording audio calibration | Failed: maximum accepted residual 155.7 ms, threshold 40 ms |

Live evidence is under `build/action-compat-validation/run-B-sawatari-entities-04/`.
It covers arrow release from the existing node, both bow facings and the foot
anchor, damage/Strength, double-hit/throw/bamboo, both original knife instances
moving to player grips and back, 24 approved player form/facing/count combinations,
destination removal during flight, UI-anchor stability, and the act-three event's
support/intermission/duel/reward lifecycle. Screenshots were visually reviewed for
bow, bamboo and player grips. `video.mp4` is the original silent visual recording;
do not describe `preview.mp4` as audio-synchronized. All game checkpoints passed
before the separate audio postprocessor failed; its failure was not waived.

Contract logs: `build/sawatari-weapons/{stable,preview}-sawatari-contract.log`.
They verify both ascensions, defenses, native damage/exhaustion, immediate intent,
copied/repeated returns, overflow, other targets, seeded two-player selection and
SavedProperties/card serialization. An initial parallel build collided in Godot's
shared intermediate output and was rejected by the channel identity assertion.
The reported candidates were rebuilt sequentially and both identity checks pass.

Earlier full contracts, 349 logic tests and two-process ENet runs are recorded in
the previous task artifacts. They precede the independent-node rewrite and are
not fresh proof for these visual changes. Preview live gameplay and live combat
save/reload were not rerun. No new pure gameplay logic changed in this rewrite.
The isolated live log also contains a RitsuLib settings-dialog disconnect error
and Godot shutdown resource diagnostics; it is not an error-free log claim.

Whole production C# counts versus the clean base, including preceding local
Dark Strike/FreeControl/Sawatari work: files 481→488, lexical type declarations
829→836, physical lines 58,412→59,267 (`build/sawatari-weapons/source-counts.json`).
These are not isolated deltas for the visual rewrite. No runtime capability graph,
global compatibility facade, fingerprint platform, speculative save migration or
global GC control was added. This rewrite adds no production reflection, dynamic
patch, synchronization primitive or supported-host branch; existing gameplay
host branches and model serialization stay with their original owners.

## Immutable candidate package and smoke follow-up

Product candidate `ed3ee666e2a90f3171d42e2150f8ca89a60d0039`, version 0.2.12:
`build/release-v0.2.12/candidate/channels/{stable,preview}/package/NinjaSlayer/`.
Universal bundle validation passed. Stable real-game FirstCombatRestart passed
59 checkpoints across fresh, resume and reverse-finisher processes; the fixed
SawatariSameCombat scenario passed 12 checkpoints including its strict ordinary
Finisher assertion and intermission weapon alignment. Attestations are in
`candidate/smoke-constructor-FirstCombatRestart/` and
`candidate/smoke-constructor-SawatariSameCombat/` under the evidence root above.
Both runs loaded RitsuLib 0.6.2, which Steam installed during this session; the
preceding action-preview mirror still used 0.6.1.

The first packaged smoke run failed because its injected presentation failure
never reached the one-line factory. The real Finisher session completed its
three hits and single death. Moving the test-only Harmony injection to the
actual presentation constructor made the same product package pass the failure,
cleanup and restart checks. No production Finisher behavior changed. The updated
SmokeDriver is therefore newer than the product candidate SHA above.

The hidden Direct3D smoke windows emit swap-chain resize errors and shutdown
resource warnings. These runs verify gameplay/lifecycle; the earlier Vulkan
motion recording and visible intermission screenshot provide visual evidence.
