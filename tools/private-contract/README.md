# Ephemeral protected runners

This project is the trusted build entry used by the protected game-contract workflow. It compiles candidate C# sources against private game references without evaluating the candidate repository's project, targets, NuGet configuration, tests, or workflows.

Changes to dependencies or top-level source directories must update this harness on protected `main` before a later candidate can consume them. The harness never packages or runs candidate code and its output is deleted at the end of every workflow run.

`Start-EphemeralContractRunner.ps1` creates one of two purpose-specific, one-job Windows runners:

- `-RunnerPurpose Contract` registers `ninjaslayer-contract`. It must run from elevated PowerShell because the protected Contract enforces an outbound firewall rule. The launcher isolates `sts2.dll`, `0Harmony.dll`, and `GodotSharp.dll` as read-only files and creates a temporary .NET 9-only runtime root so Godot cannot roll Harmony forward to an unsupported .NET 10 runtime.
- `-RunnerPurpose Release` registers `ninjaslayer-release` and can run from a normal PowerShell session. It isolates the same game references plus the three fixed-hash Spine GDExtension DLLs used by strict `PackageMod` export. The GitHub Release workflow uploads only the final allowlisted mod ZIP.

Both modes require the short-lived registration token, exact runner version, and archive SHA-256 shown by GitHub's **New self-hosted runner** page. The host must have a .NET 9 SDK/runtime and Godot 4.5.1 Mono installed. For unreliable network environments, download the official runner archive first and pass its path with `RunnerArchivePath`; the required SHA-256 is still verified before extraction.

The launcher copies all private inputs to a temporary directory, marks them read-only, restores inherited environment variables, and removes the runner, work directory, references, Spine inputs, and isolated runtime after its single job. Neither mode stores game DLLs in GitHub Secrets, Actions caches, or artifacts.
