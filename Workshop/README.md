# Steam Workshop metadata

- App ID: `2868840`
- Published file ID: `3776911445`
- Visibility: `public` (owner-confirmed on 2026-10-01)
- Dependency: RitsuLib Workshop item `3747602295`
- Preview image: `image.png` (must remain at or below Steam's 1 MiB limit)
- Detail-page first gallery image: `cover-wide.jpg` (1920×1080; the user's 2026-09-30 cover, JPEG compressed without cropping). Update the first image preview through Steam UGC; keep the square list preview `image.png`.

Credentials and Steam `config.vdf` are never stored in this directory. Publication is performed only by the manual `workshop.yml` workflow or `tools/release/Publish-WorkshopQuickRelease.ps1`; both upload the same universal stable/preview bundle shape. The PCK excludes build-only `addons/spine/**` and all native libraries, so the same frozen candidate can reuse the official platform extension on Windows x64, macOS, and Linux x86_64/Steam Deck. Keep the owner's public visibility setting; record actual host/platform validation separately rather than implying all platforms were tested.
