using Godot;
using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Orbs;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class ShurikenOrbChannelPatch : IPatchMethod
{
    private static readonly AccessTools.FieldRef<OrbQueue, Player> QueueOwner =
        AccessTools.FieldRefAccess<OrbQueue, Player>("_owner");

    public static string PatchId => "ninjaslayer_shuriken_orb_channel";
    public static string Description =>
        "Keep Ninja Slayer's Shuriken outside normal slot capacity while retaining native orb commands.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        AsyncTarget(typeof(OrbCmd), nameof(OrbCmd.Channel),
            [typeof(PlayerChoiceContext), typeof(OrbModel), typeof(Player)]),
        AsyncTarget(typeof(OrbQueue), nameof(OrbQueue.TryEnqueue), [typeof(OrbModel)]),
        new(typeof(OrbQueue), nameof(OrbQueue.RemoveCapacity), [typeof(int)]),
        AsyncTarget(typeof(OrbCmd), nameof(OrbCmd.EvokeLast),
            [typeof(PlayerChoiceContext), typeof(Player), typeof(bool)])
    ];

    private static ModPatchTarget AsyncTarget(Type type, string name, Type[] arguments)
    {
        MethodInfo method = AccessTools.DeclaredMethod(type, name, arguments)
            ?? throw new MissingMethodException(type.FullName, name);
        Type stateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            ?? throw new MissingMethodException(type.FullName, name + " state machine");
        return new(stateMachine, nameof(IAsyncStateMachine.MoveNext), []);
    }

    public static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        FieldInfo? incoming = AccessTools.DeclaredField(__originalMethod.DeclaringType, "orb");
        bool enqueue = __originalMethod.DeclaringType!.DeclaringType == typeof(OrbQueue);
        bool removeCapacity = __originalMethod.Name == nameof(OrbQueue.RemoveCapacity);
        MethodInfo capacity = AccessTools.PropertyGetter(typeof(OrbQueue), nameof(OrbQueue.Capacity));
        MethodInfo count = AccessTools.PropertyGetter(typeof(IReadOnlyCollection<OrbModel>), "Count");
        MethodInfo add = AccessTools.Method(typeof(List<OrbModel>), nameof(List<OrbModel>.Add));
        int capacityReads = 0, countReads = 0, insertions = 0, lastReads = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (incoming != null && instruction.Calls(capacity))
            {
                capacityReads++;
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                yield return new CodeInstruction(OpCodes.Ldfld, incoming);
                yield return CodeInstruction.Call(typeof(ShurikenOrbChannelPatch), nameof(ChannelCapacity));
            }
            else if ((incoming != null || removeCapacity) && instruction.Calls(count))
            {
                countReads++;
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(ShurikenOrbChannelPatch), nameof(NormalOrbCount));
                yield return instruction;
            }
            else if (enqueue && instruction.Calls(add))
            {
                insertions++;
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(ShurikenOrbChannelPatch), nameof(Enqueue));
                yield return instruction;
            }
            else if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Enumerable)
                && method.Name == nameof(Enumerable.Last) && method.IsGenericMethod
                && method.GetGenericArguments().SequenceEqual([typeof(OrbModel)]))
            {
                lastReads++;
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(ShurikenOrbChannelPatch), nameof(LastNormalOrShuriken));
                yield return instruction;
            }
            else yield return instruction;
        }

        if (incoming != null
            ? capacityReads != 2 || countReads != 1 || insertions != (enqueue ? 1 : 0)
            : lastReads != 1 || countReads != (removeCapacity ? 1 : 0))
            throw new InvalidOperationException($"Unexpected native orb command calls in {__originalMethod.DeclaringType}.{__originalMethod.Name}.");
    }

    private static int ChannelCapacity(OrbQueue queue, OrbModel incoming) =>
        queue.Capacity + (incoming is ShurikenOrb && QueueOwner(queue).Character is INinjaSlayerCharacter ? 1 : 0);

    private static int NormalOrbCount(IReadOnlyCollection<OrbModel> orbs) =>
        orbs.Count - orbs.Count(orb => orb is ShurikenOrb { UsesDedicatedSlot: true });

    private static void Enqueue(List<OrbModel> orbs, OrbModel incoming)
    {
        int dedicated = orbs.FindIndex(orb => orb is ShurikenOrb { UsesDedicatedSlot: true });
        if (incoming is ShurikenOrb && orbs.Any(orb => orb is ShurikenOrb))
            throw new InvalidOperationException("Shuriken stock must merge into the existing orb.");
        if (dedicated >= 0) orbs.Insert(dedicated, incoming);
        else orbs.Add(incoming);
    }

    private static OrbModel LastNormalOrShuriken(IEnumerable<OrbModel> orbs)
    {
        OrbModel last = orbs.Last();
        if (last is ShurikenOrb { UsesDedicatedSlot: true })
        {
            return orbs.LastOrDefault(orb => !ReferenceEquals(orb, last)) ?? last;
        }
        return last;
    }
}

public sealed class ShurikenOrbChannelSoundPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_shuriken_orb_channel_sound";
    public static string Description => "Keep stock acquisition silent instead of loading a nonexistent vanilla debug sound.";
    public static bool IsCritical => false;
    public static ModPatchTarget[] GetTargets() => [new(typeof(OrbModel), nameof(OrbModel.PlayChannelSfx), [])];
    public static bool Prefix(OrbModel __instance) => __instance is not ShurikenOrb;
}

public sealed class ShurikenOrbLayoutPatch : IPatchMethod
{
    private const double LayoutSeconds = 0.45;

    public static string PatchId => "ninjaslayer_shuriken_orb_layout";
    public static string Description =>
        "Keep Shuriken on Ninja Slayer's hand and lay out every later orb in vanilla slots.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NOrbManager), "TweenLayout", [])
    ];

    public static bool Prefix(
        NOrbManager __instance,
        List<NOrb> ____orbs,
        ref Tween? ____curTween)
    {
        if (__instance.GetParent() is not NCreature creatureNode
            || creatureNode.Entity.Player?.Character is not INinjaSlayerCharacter)
        {
            return true;
        }

        NOrb? shuriken = ____orbs.FirstOrDefault(orb => orb.Model is ShurikenOrb);
        if (shuriken == null)
        {
            return true;
        }

        // Keep native front-orb previews and controller navigation in gameplay order.
        ____orbs.Remove(shuriken);
        ____orbs.Add(shuriken);

        NOrb[] standardOrbs = ____orbs.Where(orb => !ReferenceEquals(orb, shuriken)).ToArray();
        ____curTween?.Kill();
        ____curTween = null;
        if (standardOrbs.Length == 0)
        {
            return false;
        }

        Tween tween = __instance.CreateTween().SetParallel();
        for (int index = 0; index < standardOrbs.Length; index++)
        {
            ShurikenOrbSlotPosition slot = ShurikenOrbLayoutMath.GetStandardPosition(
                standardOrbs.Length,
                index,
                __instance.IsLocal);
            Vector2 position = new(slot.X, slot.Y);
            tween.TweenProperty(standardOrbs[index], "position", position, LayoutSeconds)
                .SetEase(Tween.EaseType.InOut)
                .SetTrans(Tween.TransitionType.Sine);
        }

        ____curTween = tween;
        return false;
    }
}

public sealed class ShurikenOrbAddVisualPatch : IPatchMethod
{
    private static readonly AccessTools.FieldRef<NOrbManager, List<NOrb>> VisualOrbs =
        AccessTools.FieldRefAccess<NOrbManager, List<NOrb>>("_orbs");
    public static string PatchId => "ninjaslayer_shuriken_orb_add_visual";
    public static string Description => "Display the dedicated Shuriken without allocating a normal slot.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() => [new(typeof(NOrbManager), nameof(NOrbManager.AddOrbAnim), [])];

    public static void Prefix(NOrbManager __instance, List<NOrb> ____orbs)
    {
        if (__instance.GetParent() is not NCreature { Entity.Player.Character: INinjaSlayerCharacter } creature)
            return;
        OrbQueue queue = creature.Entity.Player!.PlayerCombatState!.OrbQueue;
        if (queue.Orbs.Any(orb => orb is ShurikenOrb) && ____orbs.Count == queue.Capacity)
            __instance.AddSlotAnim(1);
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        int replaced = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Enumerable)
                && method.Name == nameof(Enumerable.LastOrDefault) && method.IsGenericMethod
                && method.GetGenericArguments().SequenceEqual([typeof(OrbModel)]))
            {
                replaced++;
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                yield return CodeInstruction.Call(typeof(ShurikenOrbAddVisualPatch), nameof(NewOrb));
            }
            else yield return instruction;
        }
        if (replaced != 1) throw new InvalidOperationException("Expected one native newly channeled orb lookup.");
    }

    private static OrbModel? NewOrb(IEnumerable<OrbModel> orbs, NOrbManager manager) =>
        manager.GetParent() is NCreature { Entity.Player.Character: INinjaSlayerCharacter }
            ? orbs.Single(orb => VisualOrbs(manager).All(node => !ReferenceEquals(node.Model, orb)))
            : orbs.LastOrDefault();
}

public sealed class ShurikenOrbRemoveSlotPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_shuriken_orb_remove_slot";
    public static string Description => "Remove normal slot visuals without deleting the dedicated Shuriken.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() => [new(typeof(NOrbManager), nameof(NOrbManager.RemoveSlotAnim), [typeof(int)])];
    public static void Prefix(List<NOrb> ____orbs)
    {
        NOrb? dedicated = ____orbs.FirstOrDefault(node => node.Model is ShurikenOrb { UsesDedicatedSlot: true });
        if (dedicated == null) return;
        ____orbs.Remove(dedicated);
        ____orbs.Insert(0, dedicated);
        // Native removal takes the tail; its final layout restores the dedicated node's order.
    }
}

public sealed class ShurikenOrbRemoveVisualPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_shuriken_orb_remove_visual";
    public static string Description => "Remove the dedicated empty visual after native Shuriken evocation.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() => [new(typeof(NOrbManager), nameof(NOrbManager.EvokeOrbAnim), [typeof(OrbModel)])];
    public static void Postfix(NOrbManager __instance, OrbModel orb)
    {
        if (orb is ShurikenOrb { UsesDedicatedSlot: true }) __instance.RemoveSlotAnim(1);
    }
}

public sealed class ShurikenOrbPreviewPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_shuriken_orb_preview";
    public static string Description => "Preview the dedicated Shuriken when all normal slots are empty.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() => [new(typeof(NOrbManager), nameof(NOrbManager.UpdateVisuals), [typeof(OrbEvokeType)])];
    public static void Postfix(List<NOrb> ____orbs, OrbEvokeType evokeType)
    {
        if (evokeType != OrbEvokeType.Front || ____orbs.Any(node => node.Model is not null and not ShurikenOrb)) return;
        ____orbs.FirstOrDefault(node => node.Model is ShurikenOrb { UsesDedicatedSlot: true })?.UpdateVisuals(true);
    }
}

public sealed class ShurikenMultiCastPreviewPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_shuriken_multicast_preview";
    public static string Description => "Preview Multi-Cast's actual first orb instead of every orb for Ninja Slayer.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() => [new(typeof(MultiCast), "get_OrbEvokeType", [])];
    public static void Postfix(MultiCast __instance, ref OrbEvokeType __result)
    {
        if (__instance.IsMutable && __instance.Owner?.Character is INinjaSlayerCharacter) __result = OrbEvokeType.Front;
    }
}
