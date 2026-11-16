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

`workshop.json` keeps English in `description` (Steam's default language), with the other 15 languages in `localizedDescriptions`. Do not concatenate them. The title remains `忍者杀手 Ninja Slayer` in all languages. Tags include `characters` and every language mapped in `languages.json`.

The official ModUploader has no localization fields; it uploads `description` to English. Both local release scripts therefore apply the localized fields after the content upload, using Steam's `SetItemUpdateLanguage` and `SetItemDescription`, and query each language again to verify. The SteamCMD workflow omits title/description and preserves the existing translations; it does not publish text edits. For a text-only change, run the metadata tool with Steam signed in to the item owner's account:

```powershell
dotnet run --project tools/workshop-metadata/WorkshopMetadata.csproj -c Release `
  '-p:Sts2DataDir=C:/path/to/Slay the Spire 2/data_sts2_windows_x86_64' -- `
  apply Workshop/workshop.json eng/compatibility.json
```

Use `inspect` for read-only snapshots or `verify` to compare remote fields to the local text without writing. The tool writes the same configured title in each language: a new localized description does not inherit the default title. It never uploads a game package or touches tags, visibility, dependencies, or images. It skips titles/descriptions that already match and submits metadata changes without a change note.

Steam documents `SetItemUpdateLanguage` for **title and description only**, not `SubmitItemUpdate`'s shared change note. Do not submit separate per-language updates with duplicate changelogs. Game-package uploads read the committed `Workshop/change-note.md` and freeze it with the candidate. `workshop.json.changeNote` mirrors that text; all publication paths must preserve Chinese, line breaks, quotes and backslashes instead of substituting a fixed sentence.

## Concise change notes

Write one Chinese line per player-visible change. Compare old and new numbers; `→` is base-to-upgrade and `+` means upgraded-only. Use `修复了用户反馈中xx的问题。` for a verified, released feedback fix, with a problem name of at most 10 characters. Describe additions, translation changes and renames in one sentence. Omit development, test and commit details. Do not call unresolved reports or third-party failures fixed.

Historical edits cover 1.0.0 onward. Match each original Workshop entry to its published version and evidence, keep an original/replacement record, edit through the owner's existing change-note page, and read it back. Preserve earlier entries and any entry that cannot be mapped reliably. Text-only maintenance does not upload a new package, create a release entry or bump the game version.

References: [Steam UGC language fields](https://partner.steamgames.com/doc/api/ISteamUGC#SetItemUpdateLanguage), [change-note submission](https://partner.steamgames.com/doc/api/ISteamUGC#SubmitItemUpdate), [official uploader configuration](https://github.com/megacrit/sts2-mod-uploader/blob/main/src/ModConfig.cs).

The Steam website separately offers language-specific historical note editing. Publish distinct notes for all 16 languages in `languages.json`; do not concatenate translations into one field. `changelog-translations.json` archives each version's approved Chinese source, translations, original entry ID, source revision and evidence. Keep one translated line per source change and preserve values, base/upgraded meanings, conditions and card-type restrictions. Use the names shipped in that version; for a rename affecting Chinese only, say so rather than inventing a rename in other languages. Earlier Chinese cleanup records remain in `changelog-history.json`.

After a single verified package upload, identify its existing announcement and open `https://steamcommunity.com/sharedfiles/editchangelogentry/3776911445/{entryId}/`. Save Simplified Chinese explicitly before replacing the English default, then use the language selector for every remaining supported language. Steam's numeric language values are English 0, German 1, French 2, Italian 3, Korean 4, Spanish 5, Simplified Chinese 6, Traditional Chinese 7, Russian 8, Thai 9, Japanese 10, Polish 12, Turkish 21, Brazilian Portuguese 22, Latin American Spanish 27, Indonesian 30. Read the current selector before using these values. Wait for saving to finish, reload, and verify both the selected language and exact text; a fallback warning is not a saved translation.

For historical maintenance, capture the existing IDs, order, dates and original text before editing. Record progress after each batch so resumed work skips verified fields. Preserve unmapped and earlier entries. Archive read-back hashes and the final entry-count/order check with the operation's validation evidence. This process edits the same announcements; never submit a package once per language or add replacement announcement entries.

The 1.0.0–1.2.8 backfill is complete: 39 announcements, 16 languages each, 624 saved and reloaded fields. `../Docs/validation/workshop-notes-16-languages-20261117.json` records the verification. All 96 existing entries retained their IDs, order, displayed publication dates and Chinese text; the 57 pre-1.0.0 entries were untouched.

## Discord release announcements

The user requested four-language Discord release announcements on 2026-11-17 and then requested immediate publication of 1.2.8. That release is already posted and verified in `Docs/validation/discord-1.2.8-evidence.json`. Continue with subsequent releases after the Workshop upload succeeds and is verified, alongside the website sync. The request authorizes these four release announcements without another posting-permission question.

- Translate the frozen `Workshop/change-note.md` for that version. Use card, relic, power and keyword names from the same release's `zhs`, `jpn`, `kor` and `eng` localization. Keep all changes and exact old/new values; `→` means base/upgraded values. Check conditions and card-type restrictions against the actual released behavior before publishing.
- Include the version and Workshop link. Send four clearly labeled language sections in the announcement channel. Split long messages at change boundaries when necessary, preserving language and version labels. Do not mention everyone or roles unless the user requests it.
- Record destination channel links and posted message links in the release evidence. Read back every message, check all four languages, and avoid duplicating an already recorded announcement on a resumed release. If sending is unavailable, retain the four drafts and report the unsent step accurately.
- Destination: [#announcements in NinjaSlayer | STS2 Mod](https://discord.com/channels/1555073106480070658/1555076253793525760). Send all four languages here in the order Chinese, Japanese, Korean, English. The user supplied a server invite on 2026-11-17; the signed-in server view confirms this is its announcement channel and exposes a message composer. Use this fixed channel link, not the expiring invite or `#start-here`.
- Sending method: use the authenticated Discord session in the Codex in-app browser. No webhook or bot setup is currently required. Recheck access at release time, verify the published messages, and retain their links in the release record. If a sending integration is added later, keep its credentials outside Git and release evidence.
