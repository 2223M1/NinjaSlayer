# FMOD Bank Output

FMOD Studio builds to `desktop/NinjaSlayer.bank` (see `STS2_FModProject_Minimal-main/Metadata/Workspace.xml`).

The game loads `NinjaSlayer.bank` in **this folder** (not `desktop/`).

After `File → Build` in FMOD Studio:

1. Confirm `desktop/NinjaSlayer.bank` updated (size >> 27KB).
2. Copy to `NinjaSlayer.bank` here, **or** run `dotnet build` (auto-syncs from `desktop/` when present).
3. `File → Export GUIDs` → update `GUIDs.txt` in this folder.

Do not place `Master.bank` here. The mod registers only `NinjaSlayer.bank` and `GUIDs.txt`.
