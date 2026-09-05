---
name: sts2-original-code-reference
description: Use only as a secondary behavioral oracle when NinjaSlayer needs exact vanilla Slay the Spire 2 behavior that current RitsuLib tutorials and installed public APIs do not define. It is not a source of Mod architecture, Ninja Slayer novel canon, prose style, naming, or speculative compatibility.
---

# STS2 Original Code Reference

## Scope

Original game source answers precise questions about vanilla behavior. It does not define how a Mod should be architected.

Do not use this skill for Ninja Slayer canon, characterization, event writing, flavor, or card naming; use `ninjaslayer-lore`. Do not invoke it merely because a nearby vanilla class looks convenient.

Read `references/original-code-map.md` for active-version source roots and exact-query templates.

## Invocation Gate

Before searching, write one concrete question internally:

- Which active host channel or channels are relevant?
- Which exact type, member, signature, hook, serialized field, animation call, audio call, or command sequence is unknown?
- Which player-observable behavior would the answer determine?
- Why did the current RitsuLib tutorial and installed public API not answer it?

If those questions cannot be answered, return to `sts2-ritsulib-modding` or `ninjaslayer-code-quality` instead of browsing original code broadly.

## Evidence Workflow

1. Find the repository root containing `eng/compatibility.json` and read the exact active stable and preview `gameApiVersion` values.
2. Locate the source export matching every relevant active version. Do not silently substitute a newer, older, or adjacent export.
3. Search for the exact symbol or signature first. Search behavioral analogues only when the exact symbol does not exist and the analogy is tied to the same observable contract.
4. Open the smallest relevant method, state type, or call chain. Trace callers only as far as needed to establish the behavior.
5. Extract the evidence fields below and stop when the precise question is answered.
6. Cite the host version, source path, type, and member used. Report an absent symbol or missing export as a gap rather than inventing a replacement.

## What To Extract

Record only facts that affect the requested behavior:

- Preconditions and ownership of mutable state.
- Command or hook order.
- Result, cancellation, exception, and cleanup semantics.
- Player-visible state changes, animation timing, audio timing, and feedback.
- Serialization fields, defaults, and current load/save behavior.
- Stable/preview differences that are actually present in the matching exports.

Do not copy broad class structure, helper hierarchies, defensive guards, caches, registries, or private-access patterns merely because vanilla uses them.

## Host Differences

- Keep a proven stable/preview difference in the feature that owns the behavior.
- Prefer a direct compile-time branch. Use a feature-local adapter only when it isolates one real external API boundary and has meaningful behavior beyond renaming a call.
- Do not turn two host signatures into a global compatibility facade, capability graph, feature registry, shared state machine, or cross-feature adapter platform.
- Recheck both active exports when a Patch target, private signature, async state machine, serialization field, or host branch changes.

## Private API Order

When exact behavior depends on a private API, consider these options in order and stop at the first viable one:

1. Public RitsuLib API.
2. Feature-local Harmony Patch.
3. Feature-local reflection.

Private access must remain beside the owning feature and expose only the operation that feature needs. Do not create a general reflection platform.

## Compatibility Evidence Rules

- Exact signature-based Patch targeting is allowed. Method-body fingerprints, IL hashes, metadata-token matching, runtime host guessing, and silent compatibility fallback are not.
- Historical compatibility requires an identified producer version and a real fixture produced by it. Keep the migration feature-local.
- Do not use arbitrary prefix searches, best-fit parsing, stale-source substitution, or hypothetical forward compatibility.
- A vanilla implementation can establish behavior, not justify a Mod abstraction by itself.

## Completion Report

State:

- Active host version or versions inspected.
- Exact source paths, types, and members.
- Extracted behavioral facts.
- Any proven stable/preview difference.
- The smallest feature-local Mod mechanism selected.
- Missing exports, unresolved behavior, or checks that were not possible.
