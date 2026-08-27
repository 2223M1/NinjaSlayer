---
name: sts2-original-code-reference
description: Use as secondary behavioral evidence when NinjaSlayer needs exact vanilla behavior that current RitsuLib tutorials and public APIs do not define. It is not a source of Mod architecture, generic implementation patterns, or speculative compatibility.
---

# STS2 Original Code Reference

## Scope

Original game source is a behavioral oracle, not a Mod architecture template. Invoke this skill only after the RitsuLib tutorial and public API fail to answer a precise behavior question.

Read `references/original-code-map.md` for active-version source roots and exact-query templates.

## Evidence Workflow

1. Find the repository root containing `eng/compatibility.json` and read the active stable and preview `gameApiVersion` values.
2. Locate the source export matching each active version. Do not silently substitute a different export.
3. Search for the exact type, member, signature, hook, serialized field, animation call, or audio call required by the task.
4. Extract only the relevant preconditions, command order, hook timing, player-observable side effects, animation and audio timing, and current serialization semantics.
5. Stop once the exact active-version evidence answers the behavior question. Cite the version and source path used.

Do not copy large source blocks into the skill, implementation, or final report.

## Host Differences

- Keep a proven stable/preview difference in the owning feature as a compile-time branch using the channel constants generated from `eng/compatibility.json`.
- Introduce a feature-local adapter only when it isolates one real external host API boundary. Current examples are `Code/ExternalAnimations/CreatureDeathInteractionAdapter.cs` and `Code/ExternalAnimations/FinisherAttackCommandAdapter.cs`.
- Keep an adapter beside its owning feature, expose only the operation that feature needs, and prefer a direct local branch when an adapter would only rename one call.
- Must not reintroduce a global compatibility facade, runtime feature registry, capability graph, shared compatibility state machine, or cross-feature adapter platform.

## Private API Order

When exact behavior depends on a private API, consider these options in order and stop at the first viable one:

1. Public RitsuLib API.
2. Feature-local Harmony patch.
3. Feature-local reflection.

Must not reintroduce a global private-access facade or general reflection platform.

## Compatibility Evidence Rules

- Must not reintroduce a shared method-body fingerprint platform, runtime host guessing, or silent compatibility fallback. Exact signature-based Patch targeting is distinct from fingerprinting.
- Must not reintroduce speculative historical-alignment paths. A producer-specific migration is in scope only when the task names the exact producer version and supplies a real fixture, and it must remain feature-local.
- Do not use best-fit analogies, arbitrary prefix searches, stale-source substitution, or hypothetical forward compatibility.
- If exact evidence is unavailable, report the gap instead of inventing behavior or architecture.

## Completion Report

State the active host version, exact source symbols inspected, extracted behavioral facts, any stable/preview difference, and the feature-local mechanism selected. Explicitly report missing evidence.
