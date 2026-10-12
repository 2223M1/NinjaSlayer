# Website synchronization for Workshop 1.0.25

The website now uses the catalog exported from the Steam-managed 1.0.25 Workshop download. All 12 files match the package uploaded from source `38c786e508d40c4f2a0d9368e4db49b05630d706`; the product changes are merged in PR #210 at `90660bcd05d8d4b1117a70cfc7e68946e835b4e3`.

The production catalog exporter runs in the headless preview host with its exact native localization. All 16 languages and actual packaged artwork are exported. Existing published version directories remain unchanged.

Current content includes archived Battle Ready, restored Guard Stance responding to any Karate trigger, native Retain on Cross Guard, and zero-energy Ninja Taunt with 13/16 Block. Dynamic values and upgraded descriptions come directly from the published models.

This synchronization does not rebuild or republish the mod and does not run additional gameplay tests. The package identity, public Workshop update note and exported catalog provenance are recorded in `release-1.0.25-catalog-evidence.json`.
