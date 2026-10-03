# Workshop language fields — 2026-10-03

Base checkout: `d3dcd495523785104d0968935a537f11ee999840`, with existing local admin fixes preserved. Metadata-only update to Workshop `3776911445`; game package remains 1.0.4.

## Result

- English, Simplified Chinese (`schinese`), and Japanese descriptions now occupy separate Steam language fields. Existing wording, community links and donations links were preserved, not rewritten.
- All three titles are still `忍者杀手 Ninja Slayer`.
- Remote descriptions matched the old local combined description before the update. Afterward, each remote title/description exactly matched its configured text (normalizing CRLF only).
- Original title, content handle/size, public visibility, tags (`Characters,Simplified Chinese,english,japanese`), RitsuLib dependency and all image/video previews compared equal to the initial snapshot. No content-upload API was called; no game build or package download was needed.
- A second `apply` made zero updates. Public HTML for all three languages showed the complete respective description, F2 instructions, community/site links and no other language block.
- The first metadata pass exposed a Steam behavior: creating a localized description leaves its localized title empty instead of inheriting English. Verification caught it; the updater now always sets the same configured title alongside the description. Titles were corrected and checked against the initial snapshot.

## Update-note boundary

Steam documents `SetItemUpdateLanguage` for title/description only. `SubmitItemUpdate` exposes one shared change note, with no language parameter. This task did not rewrite historical notes or add release entries. The public changelog still began with the October 2 entry after the metadata edits.

Automatic package uploads retain exactly: `我们修复了一些问题，增添了一些内容，调整了一些东西。` Detailed notes remain manually maintained by the owner. No unsupported per-language changelog behavior is claimed.

## Local checks and publication wiring

- `tools/workshop-metadata/WorkshopMetadata.csproj`: Release build and real Steam `inspect`, `apply`, `verify`, and idempotent `apply` completed.
- Both local release scripts now apply and verify localization after the normal content upload, from the same frozen metadata. The external uploader staging JSON also uses the English default description, preventing a direct normal upload from restoring the old combined block.
- SteamCMD publication preserves localized fields by omitting title/description. Its visibility validation now accepts the already-configured public value `0`.
- PowerShell parsing, `node tools/validate-repository.mjs`, and `git diff --check` passed.
- No gameplay changes, game builds, runtime testing, GitHub push or new GitHub Release in this task. Unrelated dashboard edits remain local.
- Compact local snapshots and public HTML are in ignored `build/workshop-localization/`; the metadata tool is excluded from the Mod compilation and export.

References: [Steam update language](https://partner.steamgames.com/doc/api/ISteamUGC#SetItemUpdateLanguage), [Steam change-note submission](https://partner.steamgames.com/doc/api/ISteamUGC#SubmitItemUpdate), [official uploader config](https://github.com/megacrit/sts2-mod-uploader/blob/main/src/ModConfig.cs).
