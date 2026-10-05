# Steam Workshop metadata

- App ID: `2868840`
- Published file ID: `3776911445`
- Visibility: `public` (owner-confirmed on 2026-10-01)
- Dependency: RitsuLib Workshop item `3747602295`
- Preview image: `image.png` (must remain at or below Steam's 1 MiB limit)
- Detail-page first gallery image: `cover-wide.jpg` (1920×1080; the user's 2026-09-30 cover, JPEG compressed without cropping). Update the first image preview through Steam UGC; keep the square list preview `image.png`.

Credentials and Steam `config.vdf` are never stored in this directory. Publication is performed only by the manual `workshop.yml` workflow or `tools/release/Publish-WorkshopQuickRelease.ps1`; both upload the same universal stable/preview bundle shape. The PCK excludes build-only `addons/spine/**` and all native libraries, so the same frozen candidate can reuse the official platform extension on Windows x64, macOS, and Linux x86_64/Steam Deck. Keep the owner's public visibility setting; record actual host/platform validation separately rather than implying all platforms were tested.

The local quick-release script accepts `-ValidationChannel stable|preview` for its rendered validation and catalog export (default `stable`). `-PreUploadValidationModes Release113,Release100` runs those isolated scenarios on the exact frozen bundle before uploading; the default empty list preserves existing callers that validated separately. This does not remove either implementation from the universal bundle or substitute for DLL contracts. A release validated on only one real client must record that limitation explicitly. The uploader receives the configured existing item ID explicitly; the Steam metadata tool uses the actual game client's data directory, which includes the native Steam API library.

## Language-specific descriptions

`workshop.json` keeps English in `description` (Steam's default language), and separate `schinese` and `japanese` entries in `localizedDescriptions`. Do not concatenate them. The title remains `忍者杀手 Ninja Slayer` in all languages. Tags remain `characters`, `Simplified Chinese`, `English`, and `Japanese`.

The official ModUploader has no localization fields; it uploads `description` to English. Both local release scripts therefore apply the localized fields after the content upload, using Steam's `SetItemUpdateLanguage` and `SetItemDescription`, and query each language again to verify. The SteamCMD workflow omits title/description and preserves the existing translations; it does not publish text edits. For a text-only change, run the metadata tool with Steam signed in to the item owner's account:

```powershell
dotnet run --project tools/workshop-metadata/WorkshopMetadata.csproj -c Release `
  '-p:Sts2DataDir=C:/path/to/Slay the Spire 2/data_sts2_windows_x86_64' -- `
  apply Workshop/workshop.json eng/compatibility.json
```

Use `inspect` for read-only snapshots or `verify` to compare remote fields to the local text without writing. The tool writes the same configured title in each language: a new localized description does not inherit the default title. It never uploads a game package or touches tags, visibility, dependencies, or images. It skips titles/descriptions that already match and submits metadata changes without a change note.

Steam documents `SetItemUpdateLanguage` for **title and description only**, not `SubmitItemUpdate`'s shared change note. Do not submit separate per-language updates with duplicate changelogs. Automatic game-package uploads continue to use exactly `我们修复了一些问题，增添了一些内容，调整了一些东西。`; detailed notes and historical edits remain with the owner.

References: [Steam UGC language fields](https://partner.steamgames.com/doc/api/ISteamUGC#SetItemUpdateLanguage), [change-note submission](https://partner.steamgames.com/doc/api/ISteamUGC#SubmitItemUpdate), [official uploader configuration](https://github.com/megacrit/sts2-mod-uploader/blob/main/src/ModConfig.cs).
