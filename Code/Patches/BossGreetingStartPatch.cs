using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Code.ExternalAnimations;
using STS2RitsuLib.Patching.Models;
using System.Runtime.CompilerServices;

namespace NinjaSlayer.Code.Patches;

public sealed class BossGreetingStartPatch : IPatchMethod
{
    private sealed class Dispatch
    {
        internal bool CallingOriginal;
        internal Task Task = Task.CompletedTask;
    }
    private static readonly ConditionalWeakTable<ICombatState, Dispatch> Dispatched = new();
    public static string PatchId => "ninjaslayer_boss_greeting_start_gate";
    public static string Description => "Finish boss greetings before dispatching any combat-start hooks.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(Hook), nameof(Hook.BeforeCombatStart), [typeof(IRunState), typeof(ICombatState)])];

    public static bool Prefix(IRunState runState, ICombatState? combatState, ref Task __result)
    {
        if (combatState == null) return true;
        if (Dispatched.TryGetValue(combatState, out Dispatch? existing))
        {
            if (existing.CallingOriginal) return true;
            __result = existing.Task;
            return false;
        }
        if (!BossGreetingCinematic.IsPending(combatState)) return true;
        var dispatch = new Dispatch();
        Dispatched.Add(combatState, dispatch);
        __result = dispatch.Task = GreetThenStart(runState, combatState, dispatch);
        return false;
    }

    private static async Task GreetThenStart(IRunState runState, ICombatState combatState, Dispatch dispatch)
    {
        bool cancelled = false;
        try { await BossGreetingCinematic.TryPlay(combatState); }
        catch (OperationCanceledException) { cancelled = true; return; }
        finally
        {
            if (cancelled) BossGreetingCinematic.CancelDeferredBossBgm();
            else BossGreetingCinematic.PlayDeferredBossBgm();
        }
        if (CombatManager.Instance.DebugOnlyGetState() == combatState)
        {
            Task original;
            dispatch.CallingOriginal = true;
            try { original = Hook.BeforeCombatStart(runState, combatState); }
            finally { dispatch.CallingOriginal = false; }
            await original;
        }
    }
}
