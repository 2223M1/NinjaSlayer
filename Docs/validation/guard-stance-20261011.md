# Guard Stance restoration and Battle Ready archive

Battle Ready is archived, not renamed. Its 1.0.22 card, power, stock-shot turn tracking, artwork and all 16 localization entries are preserved under `.local-reference/battle-ready-archive-20261011/`, with source revision `53ce85cdf30594f1ff232a2e48a741d0ed40318d` and file hashes. The archive is excluded from the build and Git as usual; that source revision retains the original tracked contents.

Guard Stance restores the original `GuardStance` and `GuardStancePower` model IDs and archived card artwork, at 1 energy and 1/2 Block per Karate wave. The existing Karate resolution calls native `CreatureCmd.GainBlock` with the archived unpowered Block behavior. Cross Guard gains native Retain on both variants.

The preliminary implementation passed stable product/ENet contracts and 371 logic tests. Those runs predate the final separate-model restoration and do not validate the published candidate. An optional bridge test assumed the Karate Patch was installed in its isolated fixture; it now checks the applied power amount, with trigger behavior covered by the main product fixture. At the user's explicit request, further gameplay, multiplayer, and re-download validation was stopped. Final release work is limited to required compilation/packaging and upload.
