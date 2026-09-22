# FMOD Bank Output

FMOD Studio builds to `desktop/NinjaSlayer.bank` (see `STS2_FModProject_Minimal-main/Metadata/Workspace.xml`).

The game loads `NinjaSlayer.bank` in **this folder** (not `desktop/`).

After `File → Build` in FMOD Studio:

1. Confirm `desktop/NinjaSlayer.bank` updated (size >> 27KB).
2. Copy to `NinjaSlayer.bank` here, **or** run `dotnet build` (auto-syncs from `desktop/` when present).
3. `File → Export GUIDs` → update `GUIDs.txt` in this folder.

Do not place `Master.bank` here. The mod registers only `NinjaSlayer.bank` and `GUIDs.txt`.

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
