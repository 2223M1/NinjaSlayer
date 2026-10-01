# FMOD Bank Output

FMOD Studio builds `desktop/NinjaSlayer.bank` and `desktop/NinjaSlayerEventMusic.bank`
(see `STS2_FModProject_Minimal-main/Metadata/Workspace.xml`).

The game loads both banks from **this folder**. Keep these copies synchronized
with the corresponding `desktop/` files.

After `File → Build` in FMOD Studio:

1. Build both NinjaSlayer banks and confirm their Desktop outputs updated.
2. Copy both banks here. An ordinary `dotnet build` does not synchronize them.
3. If event identities changed, export and update the corresponding `GUIDs.txt`
   or `EventMusicGUIDs.txt`. Gain changes alone do not require new GUID mappings.

Do not place `Master.bank` here. The mod uses the original game's master buses.

## 0.3.9 event organization and Tornado Fist

The September 29 user project organizes voices by actor and music by event.
Runtime paths follow the exported GUID mappings; existing event GUIDs and
the current greeting, bamboo, death and event-music hooks are retained.
The three old spin events are replaced by one `spin_attack` event using the
former intro GUID. Its `sustain` and `finish` parameters hold the loop and enter
the outro at the final impact. One combat-room child owns the FMOD instance,
pauses with combat, waits for native STOPPED, and releases it on room exit.
Short attacks continue using per-hit voice calls. No card effect changed.

`tools/fmod/restructure_20260929.js` and `.py` document the source-project edit.
The ordinary build consumes the checked-in banks; it does not run those tools.
The 0.3.9 SmokeDriver plays a real 30-hit card without recording video and
checks loop wraps, pause, outro and natural release on the supported hosts.

## 0.2.17 event transitions

The source project is `../STS2_FModProject_Minimal-main/STS2.fspro` relative to the
repository root. `tools/fmod/music-v0217.patch` records the three changed event XML
files under its `Metadata/Event/` directory. From that FMOD project directory,
`git apply --check <absolute-path-to-patch>` checks an old source tree before
applying it; `git apply --reverse --check` verifies an already updated tree.

Sawatari co-op, Sawatari battle and Dark Ninja battle disable beat quantization
on their transitions and use 0.12-second transition timelines with proportionally
scaled fade curves. Event IDs, source clips, markers and the original
`bus:/master/music` routing remain intact. Build the Desktop banks and export GUIDs,
then copy only NinjaSlayer's bank to the runtime folder as above. The 0.2.17 bank
was built with FMOD Studio and checked in the actual stable host with RitsuLib 0.6.2.

## 0.3.0 Sawatari outro

Apply `tools/fmod/sawatari-outro.patch` after `music-v0217.patch`. It replaces the
106 short Sawatari battle transition regions with four contiguous phase regions.
Each region stops 2 ms before its destination marker, so the Duel Outro cannot
re-enter its own transition. The co-op, decision and duel loops are conditional
on their own phase. Source clips, event IDs, music routing and 0.12-second fades
are unchanged; obsolete transition objects are removed.

Build with FMOD Studio 2.03.06:

```powershell
& 'C:/Program Files/FMOD SoundSystem/FMOD Studio 2.03.06/fmodstudiocl.exe' `
  -build -banks NinjaSlayer -platforms Desktop -export-guids `
  ../STS2_FModProject_Minimal-main/STS2.fspro
```

Copy the resulting Desktop bank to this runtime folder. Local stable regression
reproduced the old bank repeatedly jumping between 350.65 and 350.80 seconds;
the new bank plays the complete outro and reaches FMOD STOPPED. See
`Docs/sawatari-completion-validation-2026-09-22.md` for evidence and scope.

## 2026-09-28 Ninja Slayer voice cleanup

The current user-edited FMOD project remains the source of truth. Fifteen
Ninja Slayer source assets now use the matching restored recordings from the
2026-09-28 delivery, aligned to the existing edits and matched to their previous
loudness. Event volumes, routing, random variants and timing are retained.
At that refresh, the shared death clip received a separate cleaned asset for
Ninja Slayer, while Dark Ninja retained the original MP3 and its decoder padding.
The user's subsequent FMOD edits are preserved by the calibration below. At the
user's request, all three spin attack assets (intro, loop, outro) were restored
byte-for-byte from the pre-refresh originals. Their rebuilt FMOD renders also
match the original renders byte-for-byte.

`tools/fmod/ninja-slayer-clean-refresh-20260928.json` records each source match,
hash, alignment and level adjustment. One asset was already byte-identical;
five assets without a verified complete matching restoration were left intact.
Before the three-asset rollback, thirty-eight FMOD renders across eighteen events had a maximum loudness change
of 0.20 LU and no playback-length change. This is technical verification, not
exhaustive human listening. The local evidence directory in the manifest contains
the original user project metadata, assets and banks for recovery.

## 2026-09-28 narrator and Yamoto voice cleanup

Six narrator and nine Yamoto source assets use the matching restored recordings.
All FMOD metadata and other assets, including the restored spin attack clips and
music, remain unchanged. The narrator's tornado-punch clip retains its edited
pause and uses the voice-protected version to avoid syllable loss. Yamoto's
greeting uses a small latency-compensated peak limit after loudness matching.
Twenty-seven renders across eleven events measured at most 0.10 LU difference
and no playback-length change. Source hashes, alignment, levels and backup
locations are in `tools/fmod/yamoto-narrator-clean-refresh-20260928.json`.

## 2026-09-28 FMOD gain calibration

This is the September 28 baseline; the September 29 revision below supersedes
its gain targets while preserving its source cleanup and restoration.

The latest user project contains 60 events: six music events and 54 character
events with 90 sound instances. Calibration changes only 60 event master gains
and 61 SingleSound gains. All 217 source asset hashes, event identities, random
playlists, pitch, timing, transitions, routing and DSP structures are unchanged.
The three restored spin source files remain byte-identical and share one gain
adjustment. No compression, limiting or ducking was added.

Fresh preview 0.111.0 vanilla renders establish music targets of -28.2 LUFS for
narrative scenes and -20.3 LUFS for combat, using median short-term loudness in
stable playback windows. Music phases differ from these targets by at most
0.1 LU. Character calibration uses onset-aligned active 50 ms RMS windows with
10 ms hops, grouped by purpose; the largest target difference is 0.235 dB and
the largest within-event variant spread is 0.008 dB. Single SFX true peaks stay
at or below -2.2 dBTP; the busiest FMOD mix reaches -0.9 dBTP without clipping.

Both banks were rebuilt and copied to this folder and `desktop/`. Five actual
game scenes passed at standard volume and at the user's existing master-volume
setting, including all three event-music exits, Dark Ninja's first-turn music
transition and Sawatari's cooperation/choice/duel phases. This is automated
verification, not a claim of human listening approval.

`tools/fmod/loudness-calibration-20260928.json` records gains, measurements,
backups and bank hashes. The sibling delivery directory
`../deliveries/FMOD-LOUDNESS-20260928` contains the full pre-change metadata and
banks, guarded restore script, individual render evidence, six A/B samples and
the Chinese acceptance report. The source metadata diff is also recorded in
`tools/fmod/loudness-calibration-20260928.patch`.

## 2026-09-29 gain and listening revision

Music calibration excludes brief high-level passages from the normal-level
statistic for both vanilla and custom tracks. The retained-window reference
medians are -29.6 LUFS for narrative and -20.6 LUFS for combat. Dark Ninja's
prelude and Sawatari's choice phase now match their battle material's normal
level, with phase gains increased by 7.6 dB and 7.5 dB respectively.

Character material is raised by 2 dB relative to the September 28 calibration.
One Sawatari bamboo-attack variant is raised by 1 dB to retain peak headroom.
The character transition is raised by 8.8 dB; select is raised by 9.14 dB to
match its measured active level, within 0.01 dB. The spin attack's three parts
share the same 2 dB increase. Only event/instrument gain values change; source
audio, DSP, playback structure, identifiers and routing remain intact.

`tools/fmod/loudness-calibration-20260929.json` records the measurements, gain
changes, exclusions, bank hashes and backup location. Its sibling `.patch`
contains the FMOD gain diff. Local delivery evidence is under
`../deliveries/FMOD-LOUDNESS-20260929`. The listening package there excludes
vanilla menu music and includes all 90 character sounds, nine music phases,
and actual game recordings with vanilla combat sounds and act-music
transitions. Automated checks and user listening acceptance are reported
separately; the package includes a timestamp index for feedback.

## 2026-09-29 second listening revision

User listening feedback adjusts the September 29 gains: ordinary character
sounds decrease by 1 dB; all Mod music increases by 1 dB, with Yamoto's event
music increased by 3 dB total. The long greeting (`ninja_slayer_slow_domo`)
increases by 2 dB total and the enemy-burst `ninja_slayer_ninja_soul` by 4 dB
total. These totals are relative to the previous delivery. Select and
transition are cutscene cues and retain their previous gains unchanged.

This revision changes 58 event master gains only. The 217 source files,
instrument gains, random playlists, pitch, timing, routing and DSP remain
unchanged. The previous normal-music measurement method still excludes brief
climaxes; these new increments reflect the user's listening preferences.
The manifest and gain patch are
`tools/fmod/loudness-calibration-20260929-r2.json` and its sibling `.patch`.
Local evidence and the updated listening package are in
`../deliveries/FMOD-LOUDNESS-20260929-R2`, including focused A/B comparisons of
the long greeting, soul explosion and Yamoto music. Automated verification
does not imply human listening approval.

## 2026-09-29 character and narrator revision

Relative to the second listening revision, 50 character events decrease by
1 dB and the two narrator events increase independently by 2 dB. Select,
transition and all music retain their gains. Only 52 event master gains change;
instrument gains, source files and every other playback property stay intact.
The NinjaSlayer bank is rebuilt; NinjaSlayerEventMusic remains byte-identical.

`tools/fmod/loudness-calibration-20260929-r3.json` and its sibling `.patch`
record this revision. `../deliveries/FMOD-LOUDNESS-20260929-R3` contains the
backup, 90 variant renders, five FMOD mix/transition checks, guarded restore
script and separate character/narrator/cutscene listening files. This gain-only
revision does not repeat the earlier full game-scene automation.
