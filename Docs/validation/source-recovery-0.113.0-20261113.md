# Full project recovery for STS2 0.113.0

Followed the project's RitsuLib tutorial reference, [安装、看源码、修改](https://tutorials.sts2modding.com/docs/02-install-view-source-and-patch/), and downloaded [GDRE Tools v2.7.4](https://github.com/GDRETools/gdsdecomp/releases/tag/v2.7.4) from its official release. Godot Mono is the editor for the recovered project; GDRE performs extraction and decompilation.

- Tool installation: `../../Tools/STS2/gdsdecomp-2.7.4/` relative to the repository.
- Windows release archive SHA-256: `af24c563eee501effb194d83595de64696c32547b2246034dbfb891a24bfa0ad`, matching GitHub's published asset digest.
- Input game: installed `v0.113.0`, commit `16ac0fe3`; exact host MVID `ad562b4e-7352-4ca2-ae45-0ff44cd284bc` verified.
- Recovered project: `../Slay the Spire 2/Slay the Spire 2 v0.113.0/`.
- Source root matches the existing original-code-reference skill map: `src/Core/`.
- Editor entry: `project.godot`, Godot 4.5.1 Mono.
- GDRE full recovery exited successfully in 1 minute 10 seconds: 3,941 scripts decompiled; all 4,519 imported resources converted, with zero conversion failures. 1,593 resource conversions are marked lossy.
- Output is approximately 3.41 GiB and includes C#, GDScript, scenes, localization, materials, shaders and resources.
- Eight script paths lack recoverable implementations, with no same-named type in the release assembly. The obsolete `BottleSong.cs` path is accounted for by recovered `BottledSong.cs`; generated global-using directives do not form a runtime type. The full list and source hashes are in the recovered project's `RECOVERY-NOTES.md`.
- The former partial source directory was temporarily preserved as `Slay the Spire 2 v0.113.0-partial-20261113`, then removed after verifying that the full recovery contains its 19 C# source names and localization tables.

This is a local decompiled reference project, not the original authoring repository or a claim that the exported project builds/runs unchanged. No game code changes, game installation changes or publication were part of this recovery. Exported proprietary game content stays outside the mod repository.
