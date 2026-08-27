# Transition performance ablation

`Invoke-TransitionPerfMatrix.ps1` builds and runs three artifacts from one clean source revision:

| Variant | Load limit | Finalize batching |
| --- | --- | --- |
| `baseline` | on | on |
| `load-limit-off` | off | on |
| `finalize-off` | on | off |

The protected SmokeDriver samples `SceneTree.ProcessFrame` with `Stopwatch.GetTimestamp()` from
character selection through the completed Neow reveal. This is the frame-time source for p99. It
also records the first frame where the Neow room becomes visible, the opaque-backdrop tail after
video playback, and the real host `AssetLoadingSession` queue/cache state. The driver rejects an
artifact whose source revision or component metadata differs from the requested matrix.

Use a byte-stable fresh character-select profile and a private game mirror. Evidence and build
outputs must live outside the repository. Example:

```powershell
pwsh -NoProfile -File .\tools\transition-perf\Invoke-TransitionPerfMatrix.ps1 `
  -Channel preview `
  -GameRoot C:\private\sts2-preview `
  -RitsuLibRoot C:\private\STS2-RitsuLib `
  -InputSnapshotRoot C:\private\fresh-character-select-input `
  -EvidenceRoot C:\evidence\transition-perf `
  -DotNetRoot C:\private\dotnet-9 `
  -Runs 5
```

The script refuses a dirty worktree, an occupied game process, a mismatched host MVID, an existing
evidence directory, or any run without `transition-perf.completed`. `matrix-summary.json` contains
the per-run raw-data paths and median run p99 for each variant.
