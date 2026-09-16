# Local Steam save diagnostics

Standalone stable 0.107.1 probe. It has no RitsuLib or NinjaSlayer build dependency
and is not part of the product or Workshop package. See
`Docs/save-attribution-2026-09-16.md` for results and limitations.

Build from the repository root:

```powershell
dotnet build tools/save-probe/SaveProbe.csproj -c Release "-p:Sts2DataDir=$PWD/build/aim-validation/reference/stable"
```

Only after that succeeds, run a group using an unused attempt name:

```powershell
tools/save-probe/Run-Probe.ps1 -Group baseline -Attempt 06
tools/save-probe/Summarize.ps1
```

Groups are baseline, ritsu, ninja, no-ninja, full. All include instrumentation;
baseline is not a pristine unmodded test. The last two approximate the player's
mod set and currently lack ModLaunchManager. Inputs are the installed Steam game,
the manifest inventory in `build/save-attribution-20260916/installed-manifests.json`,
and the freshly built probe. Ninja groups use the installed Workshop release.

The launcher stages game files, redirects local application data, and uses the
probe to namespace native Steam storage under a unique diagnostic prefix.
Actual write requests are guarded and logged. It does not alter the native write
tasks or callback pump. Never run without the namespace probe against live saves.
Steam must be running normally; diagnostic startup exits if its callback pump
has already stopped. Runs use the normal desktop, an offscreen window and muted
audio; no independent Steam callback pump is installed.

Each run fixes enemy HP/block, performs five attack and five status victories,
then returns to the menu. The 90-second fixture and six-minute process limits are
test failures, not product save timeouts. Output includes loaded versions, staged
DLL hashes, settings, JSONL timings, logs and a final screenshot. Diagnostic Steam
files are retained under their own prefixes; no automatic cleanup touches saves.
