---
name: ninjaslayer-code-quality
description: Use only for NinjaSlayer architecture review, deletion-first refactoring, AI-slop removal, defensive-programming simplification, thin wrappers, compatibility layers, Patch transactions, lifecycle ownership, historical data, concurrency, performance infrastructure, and test-only abstractions. It is also the maintenance guard against reintroducing structures removed by the completed refactor.
---

# NinjaSlayer Code Quality

## Scope

This skill removes or prevents unjustified internal architecture while preserving player-observable behavior and real external contracts. It does not define RitsuLib integration, vanilla behavior, novel canon, or prose style; route those questions to the relevant domain skill.

The completed refactor is not a standing instruction to keep deleting. A no-change conclusion is valid when the surviving structure passes the evidence gates below.

## Task Modes

### Read-only review

- Establish the exact base and head or current reviewed SHA.
- Trace production callers and real runtime ownership.
- Record only evidence-backed findings. An empty deletion ledger is valid.
- Do not create a remediation plan merely to produce activity.

### Remediation or refactor

- Preserve the player-observable contract before changing structure.
- Delete the unsupported layer first.
- Add only the minimum code required by a real boundary, ownership rule, or tested behavior.
- Do not combine unrelated cleanup with the requested remediation.

### Maintenance guard

- Check that retired capability graphs, global compatibility facades, method-body fingerprint platforms, speculative historical paths, and global GC controls remain absent.
- Do not recreate a permanent “absence registry” or validator framework solely to prove those structures are absent.
- Reopen a completed area only when a current production caller, failure, regression, profiler result, host change, or explicit new requirement supplies fresh evidence.

## Required Workflow

1. State the player-observable behaviors and external contracts that must survive.
2. Trace every production caller of each file, type, method, validator, Patch group, compatibility path, and state owner under review. Separate production, test, tooling, and documentation references.
3. Classify each guard or failure path as a real trust boundary, expected operational failure, broken internal invariant, speculative compatibility, or test-only convenience.
4. Build a deletion ledger containing only structures that fail an evidence gate. Name every affected file, type, method, test, and validator. The ledger may be empty.
5. Apply the evidence gates. Delete first, then add only what a passing gate requires.
6. Keep lifecycle and Patch state with the feature that creates, observes, and disposes it. Keep Patch registration centralized in `Scripts/Entry.cs` through the RitsuLib `ModPatcher` API.
7. Update or delete tests that protect removed internals. Production abstractions must not exist solely to make tests easy to instantiate or mock.
8. Run the relevant build, behavioral, static, caller-search, and protected-host checks before completion.

## Evidence Gates

### Abstraction evidence

Keep an abstraction only when it represents a real external boundary, owns meaningful mutable state or policy, or serves multiple independent production callers.

A type name such as `Service`, `Manager`, `Registry`, `Coordinator`, `Adapter`, `Context`, or `Policy` is neither proof of value nor proof of waste. Judge the production role. Delete or inline a single-caller thin forwarder that adds no boundary, ownership, policy, or substantial behavior.

### Try-method evidence

Use `Try*` only for an expected, recoverable failure at a real boundary where the caller makes a meaningful alternate decision. Fix the producer or throw for broken internal invariants. Do not return `false`, `null`, an empty collection, or a default value to hide the defect.

### Supported-host evidence

The only active host targets are the rolling stable and preview entries in `eng/compatibility.json`.

Keep a proven difference compile-time and in the owning feature. Prefer a direct local branch; use a minimal adapter only for one real external API boundary. Do not reintroduce global compatibility facades, runtime capability graphs, feature-state registries, method-body fingerprint platforms, runtime host guessing, best-fit parsing, speculative historical alignment, or imaginary forward compatibility.

### Patch-transaction evidence

- Required gameplay Patches must install as one exact transaction or abort after verified rollback.
- An optional transaction may degrade only when every static and dynamic target has released the transaction's Harmony ownership.
- Aggregate counts are useful diagnostics, not independent proof that dynamic owners were removed.
- Keep the exact resolved dynamic targets on the failure path; do not build a general Patch capability registry to track them.

### Synchronization evidence

Require at least two production paths proven able to access the same mutable state concurrently. Without that evidence, remove `lock`, `Interlocked`, `Volatile`, concurrent containers, and synchronization wrappers rather than preserving them defensively.

### Performance evidence

Global or explicit GC control, including no-GC regions and forced collections, is prohibited without a measured production problem. Caches, pooling, batching, load limits, or process-wide switches require a profiler capture or benchmark that identifies the hot path and target.

A measured feature-local optimization may remain. Recheck the measurement after changing it; do not delete an optimization merely because its implementation is nontrivial.

### Catch-and-fallback evidence

Catch only an expected failure from a real boundary when the fallback is explicit, observable, and behaviorally required. Do not catch internal invariant failures, log-and-continue, or silently substitute defaults. If no justified fallback exists, let the failure surface.

### Historical-data evidence

Historical compatibility requires an identified producer version, a real fixture produced by it, and a feature-local migration. Delete best-fit parsers, arbitrary prefix matching, and unproven forward-compatibility branches.

## Deletion Rules

- Existing architecture is evidence to inspect, not precedent to preserve.
- A previous deletion is also not proof that every remaining layer is wrong.
- Do not replace a deleted layer with a synonymous facade, helper, coordinator, policy, context, contract, protocol, registry, manager, service, or adapter.
- Do not preserve an abstraction because tests instantiate or mock it.
- Do not move test-only seams into production.
- Use `IPatchMethod` and exact `ModPatchTarget` values for ordinary Patches. Use `IModPatches` only for cohesive sets installed and rolled back together.
- Prefer direct ownership and explicit feature-local flow over runtime discovery or defensive indirection.
- Do not add comments, validators, or reports that merely restate the skill without protecting a real behavior.

## Stop Conditions

Stop and report no code change when:

- Every candidate structure passes an evidence gate.
- The suspected defect exists only in a report, test double, stale branch, or name-based heuristic.
- Exact production callers or host inputs are unavailable and the missing evidence is material.
- The proposed deletion would remove a measured behavior, external boundary, or required ownership without a smaller proven replacement.

Do not lower the evidence threshold because the user asked for another audit after a successful refactor.

## Verification

Select every command relevant to the changed boundary and report its exact result:

```powershell
node .\tools\sync-compatibility.mjs --check
node .\tools\validate-repository.mjs
dotnet build .\NinjaSlayer.csproj -c Release -v:minimal
dotnet test .\Tests\NinjaSlayer.LogicTests\NinjaSlayer.LogicTests.csproj -c Release -v:minimal
node .\tools\test-build-boundaries.mjs
git diff --check
```

A normal build uses the configured channel, normally `preview`; it is not dual-host proof. Changed Patch targets, host APIs, private signatures, or compile-time branches require protected stable and preview contracts against the exact active inputs. Report unavailable checks as `NOT RUN`.

## Completion Report

For a read-only review, report the reviewed SHA/range, production callers inspected, empty or populated deletion ledger, surviving high-risk structures, and exact checks.

For a modification, report every deleted and changed file, type, method, test, and validator. Give production C# file, type-declaration, and physical-line counts before and after only when code changed. Confirm that retired capability, compatibility-facade, fingerprint, speculative-history, and global-GC structures remain absent. List surviving reflection, dynamic Patch, synchronization, fallback, host-branch, historical-data, and performance structures with the concrete evidence for retaining each one.
