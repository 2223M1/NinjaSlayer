# Transition performance ablation

`Invoke-TransitionPerfMatrix.ps1` builds and runs three artifacts from one clean source revision:

| Variant | Load limit | Finalize batching |
| --- | --- | --- |
| `baseline` | on | on |
| `load-limit-off` | off | on |
| `finalize-off` | on | off |

The protected SmokeDriver samples `SceneTree.ProcessFrame` with `Stopwatch.GetTimestamp()` from
actual transition-video playback through the completed Neow reveal. This is the frame-time source
for p99. It requires `NGame.StartNewSingleplayerRun` to begin at the authored 0.2-second embark cue
while the video is still playing. It also records the first frame where the Neow room becomes
visible, the opaque-backdrop tail after video playback, and the real host `AssetLoadingSession`
queue/cache state. The driver rejects an artifact whose source revision or component metadata
differs from the requested matrix.

AutoSlayer normally makes `Cmd.Wait` complete immediately through `NonInteractiveMode`. During the
measured `NTransition.FadeOut` call only, the driver restores interactive wait semantics long enough
for the production Patch to create its real 0.2-second Godot timer, then restores AutoSlayer mode.

Use a byte-stable fresh character-select profile and a private game mirror. Evidence and build
outputs must live outside the repository. Example:

```powershell
pwsh -NoProfile -File .\tools\transition-perf\Invoke-TransitionPerfMatrix.ps1 `
  -Channel preview `
  -GameRoot C:\private\sts2-preview `
  -RitsuLibRoot C:\private\STS2-RitsuLib `
  -SpineExtensionDirectory C:\private\spine-windows `
  -InputSnapshotRoot C:\private\fresh-character-select-input `
  -EvidenceRoot C:\evidence\transition-perf `
  -DotNetRoot C:\private\dotnet-9 `
  -Runs 5
```

The script verifies the three Spine GDExtension inputs against `eng/compatibility.json`, installs
them only for packaging, and removes its owned copy afterward. It refuses a dirty worktree, an
occupied game process, a mismatched host MVID, an existing evidence directory, or any run without
`transition-perf.completed`. `matrix-summary.json` contains the verified Spine hashes, per-run
raw-data paths, and median run p99 for each variant.
