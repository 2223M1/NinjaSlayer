# NinjaSlayer Real-Game Smoke Harness

This harness verifies runtime integration that RefLib and ABI contracts cannot cover. It is intentionally separate from the distributed mod.

## Scenario

The fresh process uses the original `AutoSlayer` for menu, reward, and map navigation while trusted Harmony patches select NinjaSlayer and replace only the first combat handler. The scenario:

1. Plays `ReadyBlade` and verifies Prepared is created.
2. Advances a turn and verifies Prepared does not remain on a card outside the draw pile.
3. Plays a non-lethal `TornadoFist` and verifies all X-attack ownership scopes return idle.
4. Reduces the final enemy to a deterministic lethal state, plays `TornadoFist`, and verifies a Finisher completes.
5. Holds AutoSlay at the first map, saves, and exits with code `20`.
6. A second process clicks Continue, verifies runtime ownership is idle, abandons the run, returns to the main menu, and exits `0`.

Failures capture a screenshot when a viewport exists. JSONL checkpoints include only bounded runtime health counters and capability states; they do not expose mutable game objects.

## Isolation

`Invoke-NinjaSlayerSmoke.ps1` requires an elevated Windows host with:

- PowerShell 7 (`pwsh`); Windows PowerShell 5.1 is not supported.
- Both rolling Slay the Spire 2 hosts listed in `eng/compatibility.json`, installed on the same volume as `RUNNER_TEMP`.
- Godot 4.5.1 Mono and .NET 9.
- A complete current Workshop RitsuLib mod directory. Its manifest and assembly must be at least the pinned compile baseline; newer Workshop releases are expected and supported.
- An ephemeral GitHub Actions runner created with `-RunnerPurpose Smoke`.

The launcher builds the candidate package, creates a hard-linked temporary game root without copying installed `mods`, stages exactly three mods, seeds a temporary settings file, redirects both Windows application-data roots, forces Steam off, and blocks outbound traffic for the temporary game executable and crash handler. A process-tree watchdog terminates either phase after five minutes. Cleanup removes firewall rules and the complete session tree.

Do not invoke the game manually from the staged directory and do not point the launcher at the real Mods directory. Successful and failed artifacts are written only to the explicit output directory.

Each host is tested with its own host-specific NinjaSlayer DLL and isolated game mirror. A Contract pass cannot compensate for a dependency that fails during type discovery before the mod loads.

## GitHub Operation

1. Configure the `game-smoke` Environment with required approval.
2. Dispatch **Protected real-game smoke** with a full SHA already merged to `main`.
3. Approve the Environment.
4. From elevated PowerShell 7 (`pwsh`), register one ephemeral `ninjaslayer-smoke` runner with both game roots and the current RitsuLib Workshop directory.
5. Review the text-only attestation or sanitized failure evidence; the runner removes itself after one job.

The workflow and SmokeDriver come from protected `main`; the candidate checkout has no credentials. Both stable and preview `FirstCombatRestart` attestations are required for Release and Workshop publication.

## Periodic Full AutoSlay

Dispatch the same workflow with `mode=FullAutoSlay` for the periodic/manual advisory run. This mode forces NinjaSlayer selection, otherwise leaves the original AutoSlayer room and combat handlers intact, and checks runtime ownership immediately before AutoSlayer exits. Its one-hour default timeout and unrelated vanilla randomness make it unsuitable for every stable release.

Multiplayer smoke remains deferred until the single-player harness has stable field history.
