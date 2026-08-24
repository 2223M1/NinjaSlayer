---
name: ninjaslayer-code-quality
description: Use only for NinjaSlayer architecture review, deletion-first refactoring, AI-slop removal, defensive-programming simplification, thin wrappers, compatibility layers, patch groups, lifecycle ownership, historical data, concurrency, performance infrastructure, and test-only abstractions.
---

# NinjaSlayer Code Quality

## Scope

This skill removes unjustified internal architecture while preserving player-observable behavior and real external contracts. It does not define RitsuLib integration shapes or vanilla behavior; use the domain evidence skills for those facts.

## Required Workflow

1. Write the player-observable behaviors that must survive before proposing structural work.
2. Trace every production caller of each file, type, method, validator, patch group, and compatibility path under review. Distinguish callers from tests and documentation references.
3. Classify each guard or failure path as a real trust boundary, expected operational failure, broken internal invariant, or speculative compatibility.
4. Create a deletion ledger naming every file, type, method, test, and validator to delete or update. Record the player behavior or external contract that replaces each structure-only assertion.
5. Apply the evidence gates below. Delete first, then add only the minimum code that passes a gate.
6. Keep lifecycle state with the feature that creates, observes, and disposes it. Keep patches feature-local and make patch-group ownership explicit.
7. Update or delete tests that only encode removed internals. Production abstractions must not exist solely to make tests convenient.
8. Run the relevant build, behavior tests, static checks, and caller search before completion.

## Evidence Gates

### Abstraction evidence

Keep an abstraction only when it represents a real external boundary, owns meaningful state or policy, or serves multiple independent production callers. Delete or inline a single-caller thin forwarder, wrapper, facade, adapter, service, manager, registry, protocol, contract, context, or policy that adds no such evidence.

### Try-method evidence

Use a `Try*` method only for an expected, recoverable failure at a real boundary where the caller makes a meaningful alternate decision. For broken internal invariants, fix the producer or throw. Do not return `false`, `null`, an empty collection, or a default value to hide the defect.

### Backward-compatibility evidence

Require an identified producer version and a real fixture produced by it. Stable/preview host differences must use the smallest feature-local compile-time adapter. Reject global compatibility facades, runtime capability graphs, feature registries, fingerprint platforms, best-fit parsing, and imaginary forward compatibility.

### Synchronization evidence

Require at least two production paths proven able to access the same mutable state concurrently. Without that evidence, remove `lock`, `Interlocked`, `Volatile`, concurrent containers, and synchronization wrappers rather than preserving them defensively.

### Performance evidence

Require a profiler capture or benchmark that identifies the measured hot path and target. Without it, remove GC control, no-GC regions, global caches, pooling, and process-wide performance switches. Any retained optimization must be feature-local and measured again after the change.

### Catch-and-fallback evidence

Catch only an expected failure from a real boundary when the fallback is explicit, observable, and behaviorally required. Do not catch internal invariant failures, log-and-continue, or silently substitute defaults. If no justified fallback exists, let the failure surface.

## Deletion Rules

- Existing architecture is evidence to inspect, not precedent to preserve.
- Do not replace a deleted layer with a synonymous new facade, helper, coordinator, policy, context, contract, protocol, registry, manager, service, or adapter.
- Do not preserve an abstraction because existing tests instantiate or mock it. Protect player behavior through the owning production entry point.
- Do not move test-only seams into production. Keep fixtures and doubles under `Tests/` unless a production boundary independently requires the interface.
- Prefer direct ownership and explicit feature-local flow over runtime capability discovery or defensive indirection.

## Completion Report

Report all deleted and modified files, types, methods, tests, and validators. Give production C# file, type-declaration, and physical-line counts before and after, including net changes. List every remaining high-risk capability, compatibility, fingerprint, fallback, synchronization, or performance structure and the concrete evidence for retaining it. Report exact build and test results and every check that could not run.
