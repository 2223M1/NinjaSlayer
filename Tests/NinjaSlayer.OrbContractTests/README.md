# Orb product contracts

Runs the candidate product DLL against the actual stable or preview host in
Godot's headless .NET 9 runtime. This project does not compile linked product
source. Run each channel sequentially because Godot uses one project output.

```powershell
./Tests/NinjaSlayer.OrbContractTests/Run-Contracts.ps1 `
  -Channel preview -NinjaSlayerAssemblyPath <candidate-dll> `
  -Sts2DataDir <exact-host-data-directory> -SourceRevision <full-candidate-sha> `
  -GodotPath <godot-mono-console-executable> -DotnetRoot <isolated-net9-runtime> `
  -LogPath <output-log>
```

The runner checks source metadata, product module identity and the active host
MVID, and logs the input DLL path and SHA-256. A timeout without the completion
marker is a failure. Final acceptance uses a clean committed candidate build;
metadata supplied to a dirty development build is not immutable SHA evidence.

Coverage includes temporary slot ownership, depletion beside another orb,
actual discard dispatch and recycling at 0/1/multiple stock, native double and
quadruple evoke command sequences, independent Starless Night chains, AOE
shuffle, full-slot replacement, saved orb properties, Hell Tornado's consumed
volley, last-enemy death, next-combat reset, current character starting inventory
and save/reload, and removed-character/relic load rejection without file mutation.
The removed-model inputs are synthetic, not a claim of historical save support.

Uses the host's TestMode and in-memory save store. Stock throw animation is
replaced; damage, powers, orb commands and event dispatch remain production code.
The content pack uses Entry's actual starting-deck configuration. This is not the
full Entry initialization or rendered FirstCombatRestart smoke: it does not test
menu navigation, animation, textures, sound, Steam, or a complete run restart.
Without the game resource pack, RitsuLib UI initialization reports missing
vanilla fonts; these tests do not make a visual-resource acceptance claim.

Run saves store player inventory and base orb slots, not the mid-combat orb queue.
The separate SavedProperties roundtrip therefore tests model data, while the
next-combat test verifies that temporary combat state is discarded.
