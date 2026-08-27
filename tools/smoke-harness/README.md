# NinjaSlayer Real-Game Smoke Harness

This harness verifies runtime integration that RefLib and ABI contracts cannot cover. It is intentionally separate from the distributed mod.

The current automation controls the Windows client. macOS and Linux/Steam Deck validation uses the same scenario requirements as a manual release gate until platform-native harness runners exist.

## Scenario

The fresh process uses the original `AutoSlayer` for menu, reward, and map navigation while trusted Harmony patches select NinjaSlayer and replace only the first combat handler. The scenario:

1. Plays `ReadyBlade` and verifies Prepared is created.
2. Advances a turn and verifies Prepared does not remain on a card outside the draw pile.
3. Plays a non-lethal `TornadoFist` and verifies combat continues, the target survives, and the player returns to its combat position.
4. Instantiates Yamoto Koki's origami-missile scene and verifies its native node resolves as `SpineSprite`.
5. Exercises Dark Strike normal hit, full block, evasion, mixed sequential targets, injected hook failure, and lethal Thorns retaliation.
6. Reduces the final enemy to a deterministic lethal state, injects one Finisher presentation-construction failure, plays a three-hit `TornadoFist`, and verifies the observed session still commits exactly one death and releases all ownership.
7. Holds AutoSlay at the first map, saves, and exits with code `20`.
8. A second process clicks Continue, verifies the saved run and canonical character progress, abandons the run, returns to the main menu, and exits `0`.
9. A third fresh process uses Instant mode, executes a real Dark Ninja move against a one-HP Ninja Slayer, and verifies the reverse Finisher commits one death without leaving registry, camera, input-suppression, or black-overlay ownership.

Failures capture a screenshot when a viewport exists. JSONL checkpoints include only bounded player-observable assertions and identifiers; they do not expose mutable game objects. The attestation records the requested bundle SemVer, both the pinned RitsuLib compile baseline and the actual Workshop runtime version, plus the SHA-256 of the universal bundle's checksum manifest. Stable and preview attestations must carry the same bundle identity. The launcher also rejects loader, managed/native library, NinjaSlayer resource, and Spine errors found in the game logs.

## Isolation

`Invoke-NinjaSlayerSmoke.ps1` requires an elevated Windows host with:

- PowerShell 7 (`pwsh`); Windows PowerShell 5.1 is not supported.
- Both rolling Slay the Spire 2 hosts listed in `eng/compatibility.json`, installed on the same volume as `RUNNER_TEMP`.
- Godot 4.5.1 Mono and .NET 9.
- A complete current Workshop RitsuLib mod directory. Its manifest and assembly must be at least the pinned compile baseline; newer Workshop releases are expected and supported.
- An ephemeral GitHub Actions runner created with `-RunnerPurpose Smoke`.

The workflow builds both candidate implementations once and assembles one universal Workshop directory. The launcher validates and copies that exact directory, creates a hard-linked temporary game root without copying installed `mods`, stages exactly three mods, seeds a temporary settings file, redirects both Windows application-data roots, forces Steam off, and blocks outbound traffic for the temporary game executable and crash handler. A process-tree watchdog terminates either phase after five minutes. Cleanup removes firewall rules and the complete session tree.

Do not invoke the game manually from the staged directory and do not point the launcher at the real Mods directory. Successful and failed artifacts are written only to the explicit output directory.

Stable and preview are tested with the same top-level loader, manifest, PCK, and bundle file set. Only the implementation selected by the exact host MVID differs. A Contract pass cannot compensate for a loader or dependency that fails during type discovery before the mod loads.

The cross-platform release matrix freezes one candidate SHA-256 for Windows x64, macOS, and Linux x86_64/Steam Deck, with stable and preview on each platform. Every cell must verify mod and character registration, both custom encounters, Yamoto Koki origami-missile Spine instantiation, Dark Strike hit/block/evasion/sequential multi-target/retaliation-death paths, and logs free of native-library, import, MVID-selection, or managed-loader errors. A test performed after rebuilding or replacing any candidate file does not belong to the same matrix.

## GitHub Operation

1. Configure the `game-smoke` Environment with required approval.
2. Dispatch **Protected real-game smoke** with a full SHA already merged to `main` and the exact SemVer intended for its frozen Release bundle.
3. Approve the Environment.
4. From elevated PowerShell 7 (`pwsh`), register one ephemeral `ninjaslayer-smoke` runner with both game roots and the current RitsuLib Workshop directory.
5. Review the text-only attestation or sanitized failure evidence; the runner removes itself after one job.

The workflow and SmokeDriver come from protected `main`; the candidate checkout has no credentials. Both stable and preview `FirstCombatRestart` attestations are required for Release and Workshop publication.

## Periodic Full AutoSlay

Dispatch the same workflow with `mode=FullAutoSlay` for the periodic/manual advisory run. This mode forces NinjaSlayer selection, otherwise leaves the original AutoSlayer room and combat handlers intact, and verifies both tutorial and host-filtered unknown-room paths before AutoSlayer exits. Its one-hour default timeout and unrelated vanilla randomness make it unsuitable for every stable release.

Multiplayer smoke remains deferred until the single-player harness has stable field history.

## Sawatari Same-Combat Gate

Run `SawatariSameCombat` to verify the event's two decision pauses and duel in one real combat. The trusted SmokeDriver replaces only the first event returned by the host with Sawatari, kills all but one first-wave target, and uses a real single-hit Ninja Slayer Strike to observe normal Finisher completion before the intermission. It then requires the original `CombatState`, `NCombatRoom`, history, RNG, card piles, and powers to survive the intermission; requires exactly one duel combat-start banner without another `BeforeCombatStart`; and requires exactly one final `AfterCombatEnd`. The process exits as soon as these assertions pass, so unrelated AutoSlayer failures later in the seeded run cannot mask this gate.
