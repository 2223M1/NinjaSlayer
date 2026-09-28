# Yukano farewell and shuriken voice

The exhausted `YukanoCompanionRelic` uses `YukanoCombatAnimations.PlayFarewell`.
It no longer borrows Yamoto Koki's bow-and-exit animation. First arrival also uses the
roll, travelling right from outside the left screen edge to the combat slot.

- Reference: episode 18, approximately 700.2-700.9 seconds, 24000/1001 fps.
- Reverse the reference's clockwise roll: immediately travel left while turning
  counter-clockwise. No preparatory hold or motion blur.
- Smooth rigid rotation at one turn per three source frames (0.125125 seconds).
  Six turns take 0.75075 seconds in both Normal and Fast. Instant retires immediately.
- Only the visual anchor and hit core move; the layout root stays fixed. The exit
  distance includes the current screen position and full texture bounds. On completion,
  hide the actor and retire its companion slot. Entrance uses the action coordinator;
  post-combat departure uses a node-bound tween so CombatEnded cannot truncate it.
- `yukano_fast_bye` starts with both arrival and departure. `yukano_fast_attack` plays on each actual
  shuriken release, alongside the existing projectile sound. Arrow playback is unchanged.

## Audio build

The calibrated main `NinjaSlayer.bank` contains the Yukano voices and popup narrator.
Their existing GUIDs and gain are retained. No independent Yukano bank or duplicate
GUID registration is loaded.

## Focused recording

Use `tools/smoke-harness/theater/yukano-farewell.json` with `Invoke-TheaterPreview.ps1`.
The fixture starts the relic at one remaining combat, performs two real two-hit
shuriken attacks (Normal and Fast), then wins with a real Strike. The normal combat-end
hook must decrement the relic, play farewell, hide the actor and release its slot.
The script does not directly call the farewell animation.

## Player layout transition

Companion additions and retirements retain the native multiplayer slot destinations.
`NinjaSlayerAllyLayoutMotion` moves Ninja Slayer's layout root with a 0.25-second
smoothstep in Normal/Fast, including its health bar and attached visuals. Initial room
binding and Instant layout snap directly. Repeated native layout calls do not restart
the movement. If an attack or its return owns position, layout resumes after that
action from the actual returned position. The controller is owned by the creature node
and has no room-exit callbacks or detached asynchronous work.

`yukano-layout-overlap.json` additionally starts a real blocked Strike while Yukano
enters, to exercise the shared root ownership and final native destination.
