# Boss greeting modes

## Runtime contract

- `BriefBossGreetingEnabled` lives in the existing local global settings file.
  `null` is an unchosen legacy/default preference, `false` is an explicit full
  greeting, and `true` enables brief greetings. Automatic first-completion
  writes explicitly save the data store; manual choices are never overwritten.
- The setting is available on the main-menu and run/combat pause surfaces.
- Brief greetings use a rigid, grounded 18-degree visual bow: down in 0.2s,
  hold until 0.7s, recover by 1.0s. Boss response starts at 0.5s. Duration is
  `max(2.0s, 0.5s + calibrated boss duration)`, independent of combat speed.
- Full greetings can transition to brief on host Space. Existing boss response
  progress is retained. Brief greetings cannot be skipped with another Space.
- The greeting gates the entire native `Hook.BeforeCombatStart` dispatch, not a
  late singleton hook. Native deck initialization stays outside the gate.
- Native reliable room-bound messages coordinate Ready, Start, Shorten, Done,
  and Release. Host mode does not write client preferences. Disconnected peers
  leave the barrier; released rooms can answer a reconnect without replay.
- Greeting-covered rooms consume the pending special entrance without playing
  it a second time. Existing save-reload suppression is retained.

## Focused verification (2026-09-25)

- Settings logic tests: 6 passed.
- Stable/Preview Debug product builds and smoke driver: zero warnings/errors.
- Native two-process ENet tests passed on both host versions. Covered conflicting
  modes, host-only Space, spoofed client messages, duplicates, stale room
  messages, completion barriers, released-room rejoin, and disconnect cleanup.
  Evidence: `build/boss-greeting/network-{stable,preview}-persisted/`.
- Actual isolated game runs checked disk persistence, one opening relic hook,
  unchanged initialized deck/RNG, and identical opening HP/cards after reload.
  Full natural completion and first Space-switch both persisted null -> true;
  manual false remained false after switching during an active boss response.
- Normal Kaiser response completed in 2.267s; Instant sleeping-boss preview
  held the bow while the native pause menu was open, then resumed. These extra
  recordings passed runtime assertions but failed the 40ms audio gate and are
  not synchronized deliverables.

## Preview delivery

Files are under `build/boss-greeting/`; each delivered directory includes
`theater.mp4`, aligned `theater-audio.wav`, the input script, timeline, motion
samples, capture/build metadata, audio alignment evidence, and verification.
Calibration attacks after the greeting are real gameplay, not composited media.

| Take | Duration | Boss response after short start | Greeting end | Audio residual | Captured/encoded frames |
| --- | ---: | ---: | ---: | ---: | ---: |
| `brief-persisted` | 7.567s | 0.517s | 2.019s | 36.65ms | 346/454 |
| `switch-persisted` | 9.183s | 0.515s | 2.019s after Space | 24.65ms | 430/551 |
| `full-persisted` | 20.050s | Full timeline | 14.564s | 23.31ms | 869/1203 |

All videos are 1920x1080, encoded at 60fps. Rendering was about 59fps, but
background capture repeated 23.79%/21.96%/27.76% of output frames respectively;
these are not 60 unique captured frames per second. The separate manual-false
run passed persistence/reload checks, but its 41.6ms audio residual exceeded the
delivery threshold. No threshold was relaxed.

## Reproduce

### Head-clearance follow-up

The native speech bubble uses `TalkPos` rather than detecting PNG head pixels.
Its tail/shadow extend about 64px below that anchor. Ninja Slayer now positions
the anchor 76px above the current solid-body contour, excluding the scarf and
using the current form/pose. Intent markers for Koki, Yukano, and Sawatari were
recalibrated without changing hitboxes or health-bar layout. Sawatari uses the
same bamboo marker on both sides; the Act 3 dual-blade rig retains extra
clearance above its upright blades, independent of its combat side.

`theater/overhead.json` exercises real intent nodes over their floating cycle,
all four player forms and both facings. After the user's request for tighter
native-like spacing, ally anchors were lowered by 6px (Koki), 12px (Yukano), and
40px (bamboo Sawatari), without moving speech bubbles. Over a complete 2s bob
cycle, recorded minimum icon/label-to-head gaps were 7.78px, 11.64px, and 9.98px
respectively; dual-blade Sawatari retained 49.98px to clear the blades. The
compact layout test requires a positive 4px margin instead of the earlier
conservative 8px margin. Evidence: `build/overhead/layout-04/`.

### Commands

Run from the repository root, using an existing current resource pack:

```powershell
dotnet test Tests/NinjaSlayer.LogicTests/NinjaSlayer.LogicTests.csproj --filter FullyQualifiedName~NinjaSlayerSettingsTests
pwsh -NoProfile -File tools/smoke-harness/Invoke-TheaterPreview.ps1 -Script tools/smoke-harness/theater/greeting-brief.json -OutputDirectory build/boss-greeting/new-take -ResourcePack build/boss-greeting/NinjaSlayer.pck -DebugAudioDirectory '<host debug_audio directory>'
```

Use `greeting-switch.json`, `greeting-response-switch.json`, `greeting-full.json`,
`greeting-crab.json`, or `greeting-sleep-pause.json` for the other focused paths.
The launcher uses an isolated profile and background Windows desktop. It does
not install into the user's game or capture the foreground desktop.
