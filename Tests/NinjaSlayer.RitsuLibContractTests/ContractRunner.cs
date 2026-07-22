using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
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
            VerifyFinalizerOrderingAndTypedState();
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

    private sealed class ContractState
    {
        public bool PostfixObserved { get; set; }
    }
}
