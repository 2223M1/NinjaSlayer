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
6. Keep lifecycle and Patch state with the feature that creates, observes, and disposes it. Keep Patch registration centralized in `Scripts/Entry.cs` through the RitsuLib `ModPatcher` API.
7. Update or delete tests that only encode removed internals. Production abstractions must not exist solely to make tests convenient.
8. Run the relevant build, behavior tests, static checks, and caller search before completion.

## Evidence Gates

### Abstraction evidence

Keep an abstraction only when it represents a real external boundary, owns meaningful state or policy, or serves multiple independent production callers. Delete or inline a single-caller thin forwarder, wrapper, facade, adapter, service, manager, registry, protocol, contract, context, or policy that adds no such evidence.

### Try-method evidence

Use a `Try*` method only for an expected, recoverable failure at a real boundary where the caller makes a meaningful alternate decision. For broken internal invariants, fix the producer or throw. Do not return `false`, `null`, an empty collection, or a default value to hide the defect.

### Supported-host evidence

The only active host targets are the rolling stable and preview entries in `eng/compatibility.json`. Keep a proven difference compile-time and in the owning feature; prefer a direct local branch, and use a minimal adapter only for a real external API boundary. Must not reintroduce global compatibility facades, runtime capability graphs, feature-state registries, method-body fingerprint platforms, speculative historical alignment, best-fit parsing, or imaginary forward compatibility.

### Synchronization evidence

Require at least two production paths proven able to access the same mutable state concurrently. Without that evidence, remove `lock`, `Interlocked`, `Volatile`, concurrent containers, and synchronization wrappers rather than preserving them defensively.

### Performance evidence

Must not reintroduce global or explicit GC control, including no-GC regions and forced collections. Other caches, pooling, or process-wide performance switches require a profiler capture or benchmark that identifies the measured hot path and target. Any retained optimization must be feature-local and measured again after the change.

### Catch-and-fallback evidence

Catch only an expected failure from a real boundary when the fallback is explicit, observable, and behaviorally required. Do not catch internal invariant failures, log-and-continue, or silently substitute defaults. If no justified fallback exists, let the failure surface.

## Deletion Rules

- Existing architecture is evidence to inspect, not precedent to preserve.
- Do not replace a deleted layer with a synonymous new facade, helper, coordinator, policy, context, contract, protocol, registry, manager, service, or adapter.
- Do not preserve an abstraction because existing tests instantiate or mock it. Protect player behavior through the owning production entry point.
- Do not move test-only seams into production. Keep fixtures and doubles under `Tests/` unless a production boundary independently requires the interface.
- Use `IPatchMethod` and exact `ModPatchTarget` values for ordinary Patches. Use `IModPatches` only for cohesive sets installed and rolled back together. Must not reintroduce runtime Patch capabilities through a group.
- Prefer direct ownership and explicit feature-local flow. Must not reintroduce runtime capability discovery or defensive compatibility indirection.

## Verification

From the repository root, select every command relevant to the changed boundary and report its exact result:

```powershell
node .\tools\sync-compatibility.mjs --check
node .\tools\validate-repository.mjs
dotnet build .\NinjaSlayer.csproj -c Release -v:minimal
dotnet test .\Tests\NinjaSlayer.LogicTests\NinjaSlayer.LogicTests.csproj -c Release -v:minimal
node .\tools\test-build-boundaries.mjs
git diff --check
```

The ordinary build uses the configured channel, defaulting to `preview`; it is not dual-host proof. Run protected stable and preview contracts for changed Patch targets, host APIs, or compile-time host branches. If exact game inputs or protected execution are unavailable, report the missing check rather than substituting a nearby source or claiming compatibility.

## Completion Report

Report all deleted and modified files, types, methods, tests, and validators. Give production C# file, type-declaration, and physical-line counts before and after, including net changes. Confirm that retired capability, compatibility-facade, fingerprint, speculative-history, and GC-control structures remain absent. List surviving high-risk reflection, dynamic Patch, synchronization, fallback, host-branch, or performance structures and the concrete evidence for retaining each one. Report exact build and test results and every check that could not run.
