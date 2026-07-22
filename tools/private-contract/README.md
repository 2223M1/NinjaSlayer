# Private contract harness

This project is the trusted build entry used by the protected game-contract workflow. It compiles candidate C# sources against private game references without evaluating the candidate repository's project, targets, NuGet configuration, tests, or workflows.

Changes to dependencies or top-level source directories must update this harness on protected `main` before a later candidate can consume them. The harness never packages or runs candidate code and its output is deleted at the end of every workflow run.

The protected workflow runs only on an ephemeral self-hosted Windows runner carrying the `ninjaslayer-contract` label. Start one from an elevated PowerShell session with `Start-EphemeralContractRunner.ps1`, using the short-lived registration token, exact runner version, and archive SHA-256 shown by GitHub's **New self-hosted runner** page. The launcher verifies that archive, copies only `sts2.dll`, `0Harmony.dll`, and `GodotSharp.dll` into a temporary read-only directory, and removes the runner, work directory, NuGet cache, and references after its single job.
