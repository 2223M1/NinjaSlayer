# Ephemeral protected runners

This directory contains the trusted entry points for protected Contract, Release, and real-game Smoke jobs. Candidate source is compiled without evaluating its project, targets, NuGet configuration, tests, or workflows.

`eng/compatibility.json` defines the two rolling hosts. The launcher reads that manifest and accepts semantic paths only:

- `-RunnerPurpose Contract`: pass `GameDataDirectoryStable` and `GameDataDirectoryPreview`. The launcher copies both reference sets read-only and creates an isolated .NET 9 runtime. Run from elevated PowerShell 7 (`pwsh`) so the job can block non-loopback traffic from both dotnet and Godot while retaining the local Contract fixture.
- `-RunnerPurpose Release`: pass the same two data directories. The launcher also isolates the fixed-hash Spine GDExtension files used by `PackageMod`.
- `-RunnerPurpose Smoke`: pass `GameRootDirectoryStable`, `GameRootDirectoryPreview`, and `RitsuLibModDirectory`. The launcher isolates the same fixed-hash Spine GDExtension files for candidate packaging. The RitsuLib directory must be a complete current Workshop installation whose manifest and assembly are at least the pinned compile baseline. Run from elevated PowerShell 7 (`pwsh`).

Example:

```powershell
pwsh .\tools\private-contract\Start-EphemeralContractRunner.ps1 `
  -RunnerPurpose Contract `
  -RegistrationToken <short-lived-token> `
  -RunnerVersion <actions-runner-version> `
  -RunnerArchiveSha256 <official-sha256> `
  -GameDataDirectoryStable C:\hosts\stable\data_sts2_windows_x86_64 `
  -GameDataDirectoryPreview C:\hosts\preview\data_sts2_windows_x86_64
```

The runner version, registration token, and archive SHA-256 come from GitHub's **New self-hosted runner** page. `RunnerArchivePath` can point to a pre-downloaded official archive; its hash is still verified.

Every private input must match the MVID and runtime assembly list in the compatibility manifest. The launcher restores inherited environment variables and removes the runner, work directory, references, Spine inputs, RitsuLib copy, and isolated runtime after its single job. Contract and Smoke artifacts contain only text attestations, one per active channel.

| Attestation | Schema |
| --- | ---: |
| Contract | `4` |
| Smoke | `6` |
