# STS2 0.113.0 compatibility and card update

Local validation on 2026-11-13. These are working-tree development artifacts, not a frozen release. No Workshop upload, website deployment, or game-mod installation was performed.

## Host and dependency inputs

| Channel | Game API | Steam build | Host MVID | RitsuLib compile package |
| --- | --- | --- | --- | --- |
| stable | 0.107.1 | 23811903 | 97f10687-c306-4798-ab75-8b9f23f34dfb | STS2.RitsuLib.Compat.0.107.1 0.6.11 |
| preview | 0.113.0 | 26625880 | ad562b4e-7352-4ca2-ae45-0ff44cd284bc | STS2.RitsuLib 0.6.11 |

The installed RitsuLib Workshop runtime is 0.6.11. All shared modules and all three distributed host variants match `ritsulib-variants.manifest`. Final local contracts compile against NuGet and execute with the actual installed host-specific Runtime/facade plus Shared, Ui and Settings DLLs. NuGet and Workshop are separate builds; equal version numbers are not treated as equal binary hashes.

RitsuLib 0.6.11's `HookedSingletonModel` inherits native `RunModel` on 0.113.0. The mod continues using that framework contract. Test fixtures now bind the owning run and, on preview, native combat run models so HP hooks and replay callbacks follow the game lifecycle.

## Implemented changes

- Update host-specific death hooks, damage command targets, interaction APIs, feedback input, relic eligibility and event room generation for 0.113.0 while retaining 0.107.1 branches.
- Preserve 0.113.0 damage target metadata and modifier flags through Evasion, Naraku absorption and delayed finisher damage. Repeated occurrences of one target remain independent hits.
- Preserve Scry's draw-pile Sly discard behavior after the native 0.113.0 hand-only change.
- Remove custom singleplayer RNG patches and AutoAnthony integration, including packaging and tests for that integration.
- Rename the Chinese Sushi enchantment from 滋养 to 小啖 without changing its effect.
- Shorten Karate and One Mind, One Body text as requested. One Mind, One Body now grants 7/10 Naraku Life; repeated conversion layers and one-life spending remain intact.
- Assess grants 5/7 Stretch and Scry 4/5. Stable implements the current native Stretch behavior; preview uses native Stretch. Anti-Air Bang Bang Fist counts Stretch as a positive effect.
- Naraku's Silence option permanently removes one selected deck card on all four event pages.
- Tomoe Throw grants 11/14 block.
- Focused Blows deals fixed 6/9 damage and applies Aim for 2/3 turns. Aim uses native Accuracy icons, raises Shuriken damage by 50%, and prioritizes marked living targets for random Shuriken. Enemy turn end consumes one duration; hits do not.
- Strong Shuriken uses native multiplicative damage hooks for Aim. Aim and ordinary Vulnerable multiply to 2.25x, including upgraded and snapshotted cards. Native rounding occurs after all modifiers; preview and actual damage agree.
- Pair two presentation callbacks with tree entry/exit so repeated reparenting restores their subscriptions and no longer produces disconnect errors.

## Validation

- Both production Release builds and both smoke-driver builds: zero warnings, zero errors.
- Pure logic tests: 367 passed, zero failed or skipped.
- Repository consistency, generated compatibility files, build boundaries and PowerShell compatibility checks: passed.
- Both exact-host RitsuLib patch suites with installed runtime: passed, including required transactions, failure injection and rollback.
- Stable full product suite with installed runtime: passed. Includes card metadata, damage boundaries, Aim/Vulnerable/Focus/snapshots/upgrades, One Mind 7/10, Stretch, all four Naraku removal pages, free control, presentation, companions, telemetry and replay.
- Preview full product suite with installed runtime: passed, including the same gameplay/presentation suite and the 0.113.0 repeated-target/metadata/modifier-flag damage contracts.
- Platform-neutral PCK validation, loader variant contracts and packaged Box2D load/step: passed.

The native 0.107.1 and 0.113.0 `DamageVar.UpdateCardPreview` implementations retain decimal preview values. The Focused Blows regression compares against native Strike at the same 6/9 base damage, then verifies integer final HP loss; it does not truncate the internal preview before comparison.

## Limits

- Only the preview host resource PCK is available locally. Stable contracts use the exact stable game DLL and shared presentation resources from the installed preview PCK. This is not a complete native stable-client playthrough.
- Interactive Steam-client smoke and same-host real telemetry-save reload were not run. The latter has no supplied native save fixture.
- Third-party MinionLib combinations on 0.113.0 were not revalidated.
- The successful preview headless process reports three CanvasItem RIDs and ObjectDB instances at shutdown. This run does not establish a clean full-client shutdown or a leak-free client session.
- The local artifact source metadata identifies the starting commit and the version marks the tree dirty. Release preparation must freeze the final source, select a release version and rebuild before publication.

Detailed local logs and development DLLs are under `build/validation-0.113.0/`; compact final file hashes are recorded beside this report.
