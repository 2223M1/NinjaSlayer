# Dark Strike theft and return validation — 2026-09-15

Local candidate only; no commit, GitHub push or Workshop upload. Base is
`de42b0c94a24ec9e404f1977eec09e8589a186b4` plus the working-tree changes.
The pre-existing FreeControl changes remain included and untouched by this task.
The base SHA embedded in the DLL is not an assertion that these changes are committed.

## Behavior

- Each impact that loses real HP or Naraku Life steals one card. Absorption and
  overflow in the same hit count once. Full block, Buffer, evasion and zero damage
  do not steal. Healing does not erase a completed damage result. Dark Strike no
  longer applies Weak; its damage, healing and move cycle remain unchanged.
- Capture eligible draw/discard cards before damage, then choose with the native
  combat-card RNG only on actual HP loss. This also handles the native removal of
  a downed player's combat piles. Eligibility requires a permanent deck version;
  rarity/Imbued priority matches both supported Thieving Hopper implementations.
- Every stolen card has its own native SwipePower, preview and optional reward.
  All earlier thefts return when Dark Ninja dies. Lethal Thorns can kill and detach
  the attacker before the current stab finishes: only that final card uses the
  native SpecialCardReward construction directly. It does not replace earlier rewards.
- Players reclaim cards individually. Skipped cards stay out of the deck. Event
  combat offers the stolen cards before the existing two-relic victory flow.
- Native NCard displays form a compact fan at the empty hand, filtered to the local
  player. The fan follows both attack body layouts and restores its parent on
  interruption. Return remains .6s, with the shared smooth turn/exposure blur in
  its final .15s. Health, status and intent roots stay fixed.

## Candidate identity

All paths below are relative to the repository root. Individual changed C# file
hashes and all Debug/Release DLL hashes are saved in
`build/dark-strike-theft/candidate-evidence.json`.
The changed-C# snapshot SHA-256 is
`6281b89b450481a80d41c9c86b5b144f15ec51c13cfda6476983974b69c8e8e0`.

| Host | Exact host MVID | Candidate DLL |
| --- | --- | --- |
| stable 0.107.1 | `97f10687-c306-4798-ab75-8b9f23f34dfb` | `build/dark-strike-theft/stable/Debug/NinjaSlayer.dll` |
| preview 0.111.0 | `73b63ee0-6c0a-47bb-b0d1-b21f6d94222e` | `build/dark-strike-theft/preview/Debug/NinjaSlayer.dll` |

SHA-256:

```text
stable Debug: 2e4ffc954ebca7351f23ace8a49fcd13c6aced36ea6ffb1cf59123099f75fe2f
preview Debug: 351002972cc8e041785b52dd19f8a90b75c643e8a903cb1287e1274cbf9ed97e
NinjaSlayer.pck: 8ea8ee08c5482c34d6ca5bb7ede777fa3a29888c3fa36c3f39c2dce23f254318
```

The PCK is `build/dark-strike-theft/NinjaSlayer.pck`. Host references are under
`build/aim-validation/reference/{stable,preview}`. Integration uses RitsuLib 0.5.12.
Stable SmokeDriver is in `tools/smoke-harness/NinjaSlayer.SmokeDriver/bin/Debug/net9.0`;
preview SmokeDriver is in `build/dark-strike-theft/smoke-preview`. Each was built
with `NinjaSlayerAssemblyPath` pointing to the corresponding DLL above.

## Executed checks

- Both hosts: Debug/Release product builds, SmokeDriver builds, full Godot product
  contracts and RitsuLib integration contracts passed. No build warnings/errors.
- Logic tests: 349 passed, 0 failed, 0 skipped. Repository consistency,
  build-boundary, compatibility-generation and diff checks passed.
- Theft contracts execute actual DarkStrikeMove and native ThievingHopper move
  code. They cover HP/Naraku/overflow, defense gates, repeated theft, candidate
  exhaustion, hand/generated-card exclusion, priorities, fixed-seed repeated
  fixtures, exact upgraded cards, enchantment data, reward serialization/reload,
  individual take/skip, two-player ownership and downed-player return after native
  revival. Three earlier thefts plus a final lethal-Thorns theft reclaim all four
  exact permanent cards once.
- Windows stable 0.107.1 real-game run: Normal/Fast playback, three held cards,
  Naraku absorption, candidate exhaustion, forced detached-node destruction and
  cleanup, plus the existing Dark Strike impact regression suite passed.
- Actual Dark Ninja event UI: four stolen-card reward buttons appear; clicking one
  and skipping the other three returns only that one. The lethal-Thorns case
  presents all four, and clicking each restores all four. Each case then opens
  the existing two-relic reward screen with no repeated stolen-card reward.
- Recorded return frames show the projected scale crossing from +1 to -1 during
  the final .15s, followed by removal of the detached body/blur. The hand fan is
  visible at the empty hand and turns with the body; UI roots remain fixed.

Build/contract/check logs and the command script are in `build/dark-strike-theft/`.
The final real-game record is
`build/action-compat-validation/run-B-dark-strike-theft-regression/`:
`checkpoints.jsonl`, `preview.mp4`, `motion-frames.json`, `stolen-*.png`, and
`event-*-cards.png` / `event-*-relics.png`. It completed at checkpoint 22.

## Scope of evidence

Preview was built and exercised through product contracts, not a live preview
game. Two-player ownership was tested with native player/reward objects on both
hosts, not a two-process ENet session. The fixed-seed and reward reload checks are
product fixtures, not a mid-battle process restart. The stable real-game run used
only NinjaSlayer, RitsuLib and SmokeDriver in an isolated save directory. Godot
reported resource/node leaks during process shutdown; these logs are not claimed
to be warning-free. Gameplay assertions, event UI and exit checkpoints passed.
