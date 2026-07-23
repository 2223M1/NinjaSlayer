# RunData compatibility fixtures

- `single-player-pre-greeting-v1.json` uses the state shape introduced by commit `ea3c6f9`, before boss-greeting room keys existed.
- `multiplayer-boss-greeting-v1.json` uses the state shape present at commit `a55e153` and includes two de-identified player slots plus invalid room-key residue for normalization coverage.
- Both envelopes follow the exact RitsuLib 0.4.62 player-slot format. The old registration omitted the C# `SchemaVersion` initializer, but RitsuLib's default was already `1` and its exporter still wrote `"schema": 1`.

No local save, Steam identifier, private game binary, or player-authored content is stored in these fixtures.
