# Steam Workshop metadata

- App ID: `2868840`
- Published file ID: `3776911445`
- Visibility: `unlisted`
- Dependency: RitsuLib Workshop item `3747602295`
- Preview image: `image.png` (must remain at or below Steam's 1 MiB limit)

Credentials and Steam `config.vdf` are never stored in this directory. Publication is performed only by the manual `workshop.yml` workflow or `tools/release/Publish-WorkshopQuickRelease.ps1`; both upload the same universal stable/preview bundle shape. The PCK excludes build-only `addons/spine/**` and all native libraries, so the same frozen candidate can reuse the official platform extension on Windows x64, macOS, and Linux x86_64/Steam Deck. Visibility remains `unlisted` until that candidate passes stable and preview on all three platforms.
