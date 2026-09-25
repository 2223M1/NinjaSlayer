using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

internal sealed class FinisherOrbPassivePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_finisher_orb_passive";
    public static string Description => "Associate native damaging orb passives with their ranged presentation.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(LightningOrb), nameof(OrbModel.Passive), [typeof(PlayerChoiceContext), typeof(Creature)]),
        new(typeof(GlassOrb), nameof(OrbModel.Passive), [typeof(PlayerChoiceContext), typeof(Creature)])
    ];
    public static void Prefix(OrbModel __instance, out FinisherRangedAction? __state) =>
        __state = __instance.Owner.Character is INinjaSlayerCharacter
            ? FinisherRangedAction.Begin(__instance.Owner.Creature) : null;
    public static void Postfix(ref Task __result, FinisherRangedAction? __state)
    {
        if (__state == null) return;
        __state.RestoreCaller();
        __result = Complete(__result, __state);
    }
    private static async Task Complete(Task task, FinisherRangedAction action)
    {
        using (action) await task;
    }
    public static Exception? Finalizer(Exception? __exception, FinisherRangedAction? __state)
    {
        if (__exception != null) __state?.Dispose();
        return __exception;
    }
}

internal sealed class FinisherOrbEvokePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_finisher_orb_evoke";
    public static string Description => "Associate each native damage evoke with its resolved target, without predicting future evokes.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(LightningOrb), nameof(OrbModel.Evoke), [typeof(PlayerChoiceContext)]),
        new(typeof(DarkOrb), nameof(OrbModel.Evoke), [typeof(PlayerChoiceContext)]),
        new(typeof(GlassOrb), nameof(OrbModel.Evoke), [typeof(PlayerChoiceContext)])
    ];
    public static void Prefix(OrbModel __instance, out FinisherRangedAction? __state) =>
        __state = __instance.Owner.Character is INinjaSlayerCharacter
            ? FinisherRangedAction.Begin(__instance.Owner.Creature) : null;
    public static void Postfix(ref Task<IEnumerable<Creature>> __result, FinisherRangedAction? __state)
    {
        if (__state == null) return;
        __state.RestoreCaller();
        __result = Complete(__result, __state);
    }
    private static async Task<IEnumerable<Creature>> Complete(Task<IEnumerable<Creature>> task, FinisherRangedAction action)
    {
        using (action) return await task;
    }
    public static Exception? Finalizer(Exception? __exception, FinisherRangedAction? __state)
    {
        if (__exception != null) __state?.Dispose();
        return __exception;
    }
}

internal sealed class FinisherShivVisualPatch : IPatchMethod
{
    private static readonly ConditionalWeakTable<NShivThrowVfx, Flight> Flights = new();
    public static string PatchId => "ninjaslayer_finisher_shiv_visual";
    public static string Description => "Capture the native Shiv projectile and its actual impact wait.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NShivThrowVfx), nameof(NShivThrowVfx.Create), [typeof(Vector2), typeof(Vector2), typeof(Color)])];

    public static void Postfix(NShivThrowVfx? __result, Vector2 throwerCenterPosition, Vector2 targetCenterPosition)
    {
        if (__result == null || FinisherRangedAction.Active is not { } action) return;
        Track(__result, action);
        AttachHead(__result, throwerCenterPosition, targetCenterPosition);
    }

    internal static NinjaSlayer.Code.Nodes.FinisherProjectileHead? AttachHead(
        NShivThrowVfx visual, Vector2 origin, Vector2 destination)
    {
        if (visual.GetNodeOrNull<NinjaSlayer.Code.Nodes.FinisherProjectileHead>("ContactHead") is { } existing)
            return existing;
        if (FinisherSessionRegistry.GetActiveSession() is not { IsRanged: true } session
            || session.FindImpactTarget(destination) is not { } target) return null;
        var trail = visual.GetNode<GpuParticles2D>("throw_container/vfx_dagger_spray_dagger");
        var head = new NinjaSlayer.Code.Nodes.FinisherProjectileHead
        {
            Name = "ContactHead", Texture = trail.Texture, Material = trail.Material,
            Scale = Vector2.One * 0.35f, Origin = origin, Target = target, ZIndex = 1
        };
        visual.AddChild(head);
        return head;
    }

    internal static void Track(NShivThrowVfx visual, FinisherRangedAction action)
    {
        if (Flights.TryGetValue(visual, out _)) return;
        var flight = new Flight(action);
        Flights.Add(visual, flight);
        action.Track(visual, flight.Arrival.Task);
        visual.TreeExiting += () => flight.Arrival.TrySetResult();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1068", Justification = "The native IL stack supplies duration and cancellation before the injected visual owner.")]
    internal static async Task Wait(float seconds, CancellationToken cancellation, bool ignoreCombatEnd, NShivThrowVfx visual)
    {
        await Cmd.Wait(seconds, cancellation, ignoreCombatEnd);
        if (!Flights.TryGetValue(visual, out Flight? flight)) return;
        if (!flight.Arrival.Task.IsCompleted)
        {
            visual.GetNodeOrNull<NinjaSlayer.Code.Nodes.FinisherProjectileHead>("ContactHead")?.Arrive();
            flight.Arrival.TrySetResult();
        }
        else if (flight.Action.Session is { } session) await session.Completion;
    }

    private sealed class Flight(FinisherRangedAction action)
    {
        internal FinisherRangedAction Action { get; } = action;
        internal TaskCompletionSource Arrival { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

internal sealed class FinisherShivTimingPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_finisher_shiv_impact";
    public static string Description => "Observe native Shiv timing without replacing its particle sequence.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets()
    {
        Type state = AccessTools.Method(typeof(NShivThrowVfx), "PlaySequence")
            .GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType;
        return [new(state, "MoveNext", [])];
    }
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source, MethodBase __originalMethod)
    {
        FieldInfo owner = AccessTools.Field(__originalMethod.DeclaringType, "<>4__this");
        MethodInfo wait = AccessTools.Method(typeof(Cmd), nameof(Cmd.Wait), [typeof(float), typeof(CancellationToken), typeof(bool)]);
        MethodInfo replacement = AccessTools.Method(typeof(FinisherShivVisualPatch), nameof(FinisherShivVisualPatch.Wait));
        int count = 0;
        foreach (CodeInstruction instruction in source)
        {
            if (instruction.Calls(wait))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction);
                yield return new CodeInstruction(OpCodes.Ldfld, owner);
                yield return new CodeInstruction(OpCodes.Call, replacement);
                count++;
            }
            else yield return instruction;
        }
        if (count != 2) throw new InvalidOperationException($"Expected two native Shiv waits, found {count}.");
    }
}

#if !NINJASLAYER_LEGACY_DAMAGE_API
internal sealed class FinisherNativeProjectilePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_finisher_native_projectile";
    public static string Description => "Use the preview host's existing projectile arrival callback for ranged impacts.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(MegaCrit.Sts2.Core.Nodes.Vfx.Utilities.NVfxProjectileHandler), "Create",
            [typeof(string), typeof(string), typeof(Vector2), typeof(Vector2), typeof(Callable)])];
    public static void Prefix(ref Callable endAction, out TaskCompletionSource? __state)
    {
        __state = null;
        if (FinisherRangedAction.Active == null) return;
        var arrival = __state = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Callable original = endAction;
        endAction = Callable.From(() =>
        {
            if (original.Delegate != null) original.Call();
            arrival.TrySetResult();
        });
    }
    public static void Postfix(Node2D? __result, TaskCompletionSource? __state)
    {
        if (__state == null) return;
        if (__result == null) { __state.TrySetResult(); return; }
        FinisherRangedAction.Active!.Track(__result, __state.Task);
        __result.TreeExiting += () => __state.TrySetResult();
    }
}
#endif
