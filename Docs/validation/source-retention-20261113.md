# Source retention and supported hosts — 2026-11-13

The user's final instruction is to retain the official stable release and the latest preview, not the two highest version numbers. Active targets remain `stable` 0.107.1 and `preview` 0.113.0 in `eng/compatibility.json`. Intermediate preview 0.111.0 is unsupported. The briefly discussed 0.111.0/0.113.0 retarget was never applied.

## Source cleanup

All paths below are relative to `../Slay the Spire 2/` outside the mod Git repository.

| Export | Result | Files | Payload bytes |
| --- | --- | ---: | ---: |
| `Slay the Spire 2 v0.107.1` | Retained, unchanged | 19,263 | 1,278,633,635 |
| `Slay the Spire 2 v0.113.0` | Retained; recovery note updated | 30,739 | 3,659,846,448 |
| `Slay the Spire 2 v0.111.0` | Removed | 19,637 | 1,362,372,195 |
| `Slay the Spire 2 v0.113.0-partial-20261113` | Removed after full recovery | 802 | 13,213,308 |

Removed payload totals 1,375,585,503 bytes, approximately 1.281 GiB; this is logical file size, not a measurement of filesystem allocation. The shared `images` directory remains.

Before removal, resolved paths were verified inside the source root, with no Git metadata, worktrees, reparse points or active game/recovery/editor processes. Each directory was removed using native PowerShell and its explicit absolute path. All 19 partial C# source names and 783 localization paths exist in the complete 0.113.0 recovery.

The retired export's 1,973-byte GDRE report is preserved at `build/validation-0.113.0/source-retention/gdre-export-0.111.0.log`, SHA-256 `d9adcde739cd2c68f4f6f373971ad9959a400299b142608fb25e246127c87e46`. Its report records 3,591 decompiled scripts, seven unresolved scripts and 4,029 converted resources, with zero conversion failures. Shared host binaries under `Tools/STS2` and RitsuLib's own distributed variants were not removed; other mods may use them.

## Changed maintenance paths

- `AGENTS.md`, `.agents/skills/ninjaslayer-code-quality/SKILL.md` and `.agents/skills/sts2-original-code-reference/references/original-code-map.md`: record stable plus latest preview retention and retirement of intermediate previews.
- `tools/sync-compatibility.mjs` and generated `Docs/development.md`: state the same rolling two-channel policy and canonical source location.
- `Docs/maintenance-baseline.md`: replace the obsolete active preview reference with 0.113.0 and explicitly identify 0.111.0 as retired.
- `tools/localization/convert-traditional.mjs`: read the current preview source version from the compatibility manifest rather than hard-code the deleted 0.111.0 export.
- `tools/artifact-contract/Program.cs`, `TestLoaderContract` and `WriteVariantManifest`: use 0.107.1/0.113.0 fixtures and exercise rejection of 0.111.0 alongside malformed and unknown versions. Production loader selection already matches the manifest exactly and needed no change.
- `Docs/validation/source-recovery-0.113.0-20261113.md` and the recovered project's `RECOVERY-NOTES.md`: record removal of the temporary partial export.

No production C# code, gameplay behavior, card catalog, host branch or dependency version changed during this cleanup. No compatibility framework, runtime probing, reflection, fallback, synchronization or GC controls were introduced. Existing 0.107.1/0.113.0 feature-local branches remain necessary for the two retained hosts; their validation is recorded in `host-0.113.0-20261113.md`. Dated 0.111.0 reports remain historical evidence.

## Verification

- `node tools/sync-compatibility.mjs --check`: passed.
- `node tools/validate-repository.mjs`: passed.
- `node tools/test-build-boundaries.mjs`: passed.
- `dotnet run --project tools/artifact-contract/NinjaSlayer.ArtifactContract.csproj -c Release -- test-loader-contract`: passed, including explicit 0.111.0 rejection.
- `node --check tools/localization/convert-traditional.mjs`: passed. All eight required native Chinese glossary tables parse at the manifest-selected 0.113.0 source root. Conversion was not run, so approved localization text was not rewritten.
- `git diff --check`: passed; Git reported existing CRLF normalization warnings.
- Both retained source directories were recounted; the two removed directories are absent.

Earlier product and patch contract logs for the unchanged 0.107.1/0.113.0 pair both contain their success markers. These suites were not repeated for documentation, source cleanup and tooling-fixture edits. Their existing full-client and resource limitations remain applicable. No release upload or website deployment was performed.
