# Orb product contracts

Runs the candidate product DLL against the actual stable or preview host in
Godot's headless .NET 9 runtime. This project does not compile linked product
source. Run each channel sequentially because Godot uses one project output.

```powershell
./Tests/NinjaSlayer.OrbContractTests/Run-Contracts.ps1 `
  -Channel preview -NinjaSlayerAssemblyPath <candidate-dll> `
  -Sts2DataDir <exact-host-data-directory> -HostPack <game-pck> -ProductPack <candidate-pck> `
  -SourceRevision <full-candidate-sha> `
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
The game resource pack supplies vanilla localization and fonts. The runner
formats all 92 base and upgraded cards in both languages and checks their
metadata against the approved fixture. These checks do not establish rendered
appearance; a preview resource pack used with the stable DLL is not stable
visual-resource evidence.

New-pool scenarios exercise native batch hand discard and Scry, nested Sly
choices, status autoplay/exhaust, accumulated Chado, Chop counters, Storm Fist,
damage-source filtering, temporary stats, copying, retention and turn expiry.
They also execute the native Black Flame turn-end lifecycle, check opening-only
tea retention, generated-card previews and character lines inside vanilla events.
The protected runner mounts the host pack for native selection and localization,
but reports tooltip presentation checks as NOT RUN without a product pack.
Local release acceptance supplies both packs through Run-Contracts.ps1;
gameplay and bilingual card formatting contracts run in both modes.
The separate multiplayer runner uses two real ENet processes and native card
actions, including local-only selectors whose choices cross the network.

Run saves store player inventory and base orb slots, not the mid-combat orb queue.
The separate SavedProperties roundtrip therefore tests model data, while the
next-combat test verifies that temporary combat state is discarded.
