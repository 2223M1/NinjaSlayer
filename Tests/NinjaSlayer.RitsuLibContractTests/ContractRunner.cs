using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.ExternalAnimations;
using STS2RitsuLib;
using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.RitsuLibContractTests;

public partial class ContractRunner : Node
{
    public override void _Ready()
    {
        try
        {
            VerifyOriginalLethalTargetFingerprint();
            VerifyFinalizerOrderingAndTypedState();
            VerifyRunOriginalContract();
            VerifyFinisherProtectionTransaction();
            VerifyCriticalRollback();
            GD.Print("NinjaSlayer RitsuLib contracts passed.");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PushError($"NinjaSlayer RitsuLib contract failed: {ex}");
            GetTree().Quit(1);
        }
    }

    private static void VerifyOriginalLethalTargetFingerprint()
    {
        Require(
            FinisherLethalTargetContract.TryValidate(out _, out _, out string reason),
            reason);
    }

    private static void VerifyFinalizerOrderingAndTypedState()
    {
        ContractPatch.Reset();
        ModPatcher patcher = CreatePatcher("finalizer-contract");
        patcher.RegisterPatch<ContractPatch>();
        try
        {
            Require(patcher.PatchAll(), "ModPatcher rejected the finalizer contract patch.");
            Require(ContractTarget.Execute(4, fail: false) == 8, "The original target result changed.");
            Require(ContractPatch.PrefixObserved, "Prefix was not called.");
            Require(ContractPatch.PostfixObserved, "Postfix was not called.");
            Require(ContractPatch.FinalizerObserved, "Finalizer was not called.");
            Require(ContractPatch.SharedStateObserved, "Typed __state was not shared across patch stages.");

            MethodInfo target = ResolveTarget();
            Patches info = Harmony.GetPatchInfo(target)
                ?? throw new InvalidOperationException("Harmony did not report the installed contract patch.");
            Patch prefix = info.Prefixes.Single(item => item.owner == patcher.PatcherId);
            Patch finalizer = info.Finalizers.Single(item => item.owner == patcher.PatcherId);
            Require(prefix.priority == 321, "Method-level Harmony priority was not preserved.");
            Require(prefix.before.Contains("contract.before"), "HarmonyBefore was not preserved.");
            Require(prefix.after.Contains("contract.after"), "HarmonyAfter was not preserved.");
            Require(finalizer.PatchMethod.Name == nameof(ContractPatch.Finalizer), "Finalizer registration is incorrect.");

            try
            {
                ContractTarget.Execute(5, fail: true);
                throw new InvalidOperationException("The original target exception was suppressed.");
            }
            catch (InvalidOperationException ex) when (ex.Message == "contract-target-failure")
            {
            }
            Require(ContractPatch.ExceptionFinalizerObserved, "Finalizer did not observe the original exception.");
        }
        finally
        {
            patcher.UnpatchAll();
        }
    }

    private static void VerifyCriticalRollback()
    {
        ModPatcher patcher = CreatePatcher("rollback-contract");
        patcher.RegisterPatch<ContractPatch>();
        patcher.RegisterPatch<MissingCriticalPatch>();
        Require(!patcher.PatchAll(), "A missing critical target did not fail the capability.");
        Patches? info = Harmony.GetPatchInfo(ResolveTarget());
        Require(!(info?.Owners.Contains(patcher.PatcherId) ?? false), "Critical failure left an earlier patch installed.");
    }

    private static void VerifyRunOriginalContract()
    {
        RunOriginalPatch.Reset();
        ModPatcher patcher = CreatePatcher("run-original-contract");
        patcher.RegisterPatch<RunOriginalPatch>();
        try
        {
            Require(patcher.PatchAll(), "ModPatcher rejected the __runOriginal contract patch.");
            Require(RunOriginalTarget.Execute(skipOriginal: false) == 17, "The run-original target result changed.");
            Require(RunOriginalPatch.ObservedOriginalRun, "Postfix did not observe __runOriginal=true.");
            Require(RunOriginalTarget.Execute(skipOriginal: true) == 0, "Skipped original did not retain its default result.");
            Require(RunOriginalPatch.ObservedOriginalSkip, "Postfix did not observe __runOriginal=false.");
            Require(RunOriginalPatch.SharedStateObserved, "Typed state was not shared when the original was skipped.");
        }
        finally
        {
            patcher.UnpatchAll();
        }
    }

    private static void VerifyFinisherProtectionTransaction()
    {
        var combatState = new CombatState();
        bool contextIsCurrent = true;

        Creature failedTarget = CreateCreature(combatState, currentHp: 1, maxHp: 10);
        var failedLedger = new FinisherDamageLedger([failedTarget], 1, 1, combatState, () => contextIsCurrent);
        decimal failedAmount = 10m;
        Require(
            failedLedger.TryProtect(failedTarget, committing: false, ref failedAmount, out FinisherProtectionToken? failedToken),
            "Lethal protection did not create a token.");
        Require(failedTarget.CurrentHp == 2 && failedAmount == 1m, "The temporary 1 -> 2 HP bump was not applied.");
        failedLedger.FinalizeProtection(failedToken!);
        Require(failedTarget.CurrentHp == 1, "Finalizer did not roll back an intact temporary HP bump.");
        Require(failedLedger.DeferredDeaths.Count == 0, "An unconfirmed damage call registered a deferred death.");

        Creature confirmedTarget = CreateCreature(combatState, currentHp: 1, maxHp: 10);
        var confirmedLedger = new FinisherDamageLedger([confirmedTarget], 2, 1, combatState, () => contextIsCurrent);
        decimal confirmedAmount = 10m;
        Require(
            confirmedLedger.TryProtect(
                confirmedTarget,
                committing: false,
                ref confirmedAmount,
                out FinisherProtectionToken? confirmedToken),
            "Confirmed lethal protection did not create a token.");
        DamageResult result = confirmedTarget.LoseHpInternal(confirmedAmount, ValueProp.Move);
        confirmedLedger.Confirm(confirmedToken!, result, originalRan: true);
        confirmedLedger.FinalizeProtection(confirmedToken!);
        Require(confirmedTarget.CurrentHp == 1, "Confirmed protection changed the protected target HP.");
        Require(confirmedToken!.IsConfirmed, "The real DamageResult did not confirm its protection token.");
        Require(confirmedLedger.DeferredDeaths.SetEquals([confirmedTarget]), "Confirmed lethal damage was not deferred exactly once.");

        Creature postfixFailureTarget = CreateCreature(combatState, currentHp: 1, maxHp: 10);
        var postfixFailureLedger = new FinisherDamageLedger(
            [postfixFailureTarget],
            5,
            1,
            combatState,
            () => contextIsCurrent,
            (_, _) => throw new InvalidOperationException("injected-postfix-failure"));
        decimal postfixFailureAmount = 10m;
        Require(
            postfixFailureLedger.TryProtect(
                postfixFailureTarget,
                committing: false,
                ref postfixFailureAmount,
                out FinisherProtectionToken? postfixFailureToken),
            "Postfix-failure protection did not create a token.");
        DamageResult postfixFailureResult = postfixFailureTarget.LoseHpInternal(postfixFailureAmount, ValueProp.Move);
        try
        {
            postfixFailureLedger.Confirm(postfixFailureToken!, postfixFailureResult, originalRan: true);
            throw new InvalidOperationException("Injected Postfix failure did not propagate to the patch boundary.");
        }
        catch (InvalidOperationException ex) when (ex.Message == "injected-postfix-failure")
        {
        }
        postfixFailureLedger.FinalizeProtection(postfixFailureToken!);
        Require(postfixFailureTarget.CurrentHp == 1, "Postfix failure rolled a confirmed target back to its bumped HP.");
        Require(postfixFailureToken!.IsConfirmed, "Postfix failure lost the confirmed DamageResult state.");
        Require(
            postfixFailureLedger.DeferredDeaths.SetEquals([postfixFailureTarget]),
            "Postfix failure lost the confirmed deferred death.");

        Creature partiallyMutatedTarget = CreateCreature(combatState, currentHp: 1, maxHp: 10);
        var partiallyMutatedLedger = new FinisherDamageLedger(
            [partiallyMutatedTarget],
            3,
            1,
            combatState,
            () => contextIsCurrent);
        decimal partialAmount = 10m;
        Require(
            partiallyMutatedLedger.TryProtect(
                partiallyMutatedTarget,
                committing: false,
                ref partialAmount,
                out FinisherProtectionToken? partiallyMutatedToken),
            "Partial-mutation protection did not create a token.");
        partiallyMutatedTarget.SetCurrentHpInternal(1);
        partiallyMutatedLedger.FinalizeProtection(partiallyMutatedToken!);
        Require(partiallyMutatedTarget.CurrentHp == 1, "Finalizer overwrote HP changed by the original method.");

        Creature staleTarget = CreateCreature(combatState, currentHp: 1, maxHp: 10);
        var staleLedger = new FinisherDamageLedger([staleTarget], 4, 1, combatState, () => contextIsCurrent);
        decimal staleAmount = 10m;
        Require(
            staleLedger.TryProtect(staleTarget, committing: false, ref staleAmount, out FinisherProtectionToken? staleToken),
            "Stale-combat protection did not create a token.");
        contextIsCurrent = false;
        staleLedger.FinalizeProtection(staleToken!);
        Require(staleTarget.CurrentHp == 2, "Finalizer wrote HP into a stale combat.");
    }

    private static Creature CreateCreature(ICombatState combatState, int currentHp, int maxHp)
    {
        var creature = (Creature)RuntimeHelpers.GetUninitializedObject(typeof(Creature));
        AccessTools.Field(typeof(Creature), "_maxHp").SetValue(creature, maxHp);
        AccessTools.Field(typeof(Creature), "_currentHp").SetValue(creature, currentHp);
        creature.CombatState = combatState;
        return creature;
    }

    private static MethodInfo ResolveTarget() => AccessTools.Method(
        typeof(ContractTarget),
        nameof(ContractTarget.Execute),
        [typeof(int), typeof(bool)])!;

    private static ModPatcher CreatePatcher(string capability) =>
        RitsuLibFramework.CreatePatcher(
            "NinjaSlayer.ContractTests",
            $"{capability}-{Guid.NewGuid():N}",
            capability);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static class ContractTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Execute(int value, bool fail)
        {
            if (fail)
            {
                throw new InvalidOperationException("contract-target-failure");
            }
            return value * 2;
        }
    }

    private static class RunOriginalTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Execute(bool skipOriginal) => 17;
    }

    private sealed class ContractPatch : IPatchMethod
    {
        private static ContractState? _prefixState;

        public static string PatchId => "contract_finalizer_state";
        public static bool IsCritical => true;
        public static ModPatchTarget[] GetTargets() =>
            [new(typeof(ContractTarget), nameof(ContractTarget.Execute), [typeof(int), typeof(bool)])];

        public static bool PrefixObserved { get; private set; }
        public static bool PostfixObserved { get; private set; }
        public static bool FinalizerObserved { get; private set; }
        public static bool SharedStateObserved { get; private set; }
        public static bool ExceptionFinalizerObserved { get; private set; }

        [HarmonyPriority(321)]
        [HarmonyBefore("contract.before")]
        [HarmonyAfter("contract.after")]
        public static void Prefix(out ContractState __state)
        {
            __state = new ContractState();
            _prefixState = __state;
            PrefixObserved = true;
        }

        public static void Postfix(ContractState __state)
        {
            PostfixObserved = true;
            __state.PostfixObserved = true;
            SharedStateObserved = ReferenceEquals(_prefixState, __state);
        }

        public static Exception? Finalizer(ContractState __state, Exception? __exception)
        {
            FinalizerObserved = true;
            SharedStateObserved |= ReferenceEquals(_prefixState, __state)
                && (__state.PostfixObserved || __exception is not null);
            ExceptionFinalizerObserved |= __exception is not null;
            return __exception;
        }

        public static void Reset()
        {
            _prefixState = null;
            PrefixObserved = false;
            PostfixObserved = false;
            FinalizerObserved = false;
            SharedStateObserved = false;
            ExceptionFinalizerObserved = false;
        }
    }

    private sealed class MissingCriticalPatch : IPatchMethod
    {
        public static string PatchId => "contract_missing_critical";
        public static bool IsCritical => true;
        public static ModPatchTarget[] GetTargets() =>
            [new(typeof(ContractTarget), "MethodThatMustNotExist")];
        public static void Prefix() { }
    }

    private sealed class RunOriginalPatch : IPatchMethod
    {
        private static RunOriginalState? _prefixState;

        public static string PatchId => "contract_run_original_state";
        public static bool IsCritical => true;
        public static ModPatchTarget[] GetTargets() =>
            [new(typeof(RunOriginalTarget), nameof(RunOriginalTarget.Execute), [typeof(bool)])];

        public static bool ObservedOriginalRun { get; private set; }
        public static bool ObservedOriginalSkip { get; private set; }
        public static bool SharedStateObserved { get; private set; }

        public static bool Prefix(bool skipOriginal, out RunOriginalState __state)
        {
            __state = new RunOriginalState();
            _prefixState = __state;
            return !skipOriginal;
        }

        public static void Postfix(bool __runOriginal, RunOriginalState __state)
        {
            ObservedOriginalRun |= __runOriginal;
            ObservedOriginalSkip |= !__runOriginal;
            SharedStateObserved |= ReferenceEquals(_prefixState, __state);
        }

        public static Exception? Finalizer(Exception? __exception, RunOriginalState __state)
        {
            SharedStateObserved |= ReferenceEquals(_prefixState, __state);
            return __exception;
        }

        public static void Reset()
        {
            _prefixState = null;
            ObservedOriginalRun = false;
            ObservedOriginalSkip = false;
            SharedStateObserved = false;
        }
    }

    private sealed class ContractState
    {
        public bool PostfixObserved { get; set; }
    }

    private sealed class RunOriginalState;
}
