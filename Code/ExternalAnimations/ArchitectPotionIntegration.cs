using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class ArchitectPotionIntegration
{
    private static readonly ConditionalWeakTable<NCombatRoom, HashSet<Task>> Throws = new();

    internal static DynamicPatchInfo[] CreatePatches()
    {
        Type? service = AccessTools.TypeByName("ThrowPotionsAtArchitect.ArchitectPotionThrowService");
        if (service == null) return [];
        MethodInfo throwing = AccessTools.Method(service, "ThrowAndConsume",
            [typeof(NPotionHolder), typeof(PotionModel), typeof(NCreature), typeof(Vector2)])
            ?? throw new MissingMethodException(service.FullName, "ThrowAndConsume");
        MethodInfo offering = AccessTools.Method(service, "ShouldOfferThrow", [typeof(PotionModel)])
            ?? throw new MissingMethodException(service.FullName, "ShouldOfferThrow");
        return
        [
            new("ninjaslayer_architect_potion_flight", throwing,
                prefix: new HarmonyMethod(typeof(ArchitectPotionIntegration), nameof(BeforeThrow)),
                postfix: new HarmonyMethod(typeof(ArchitectPotionIntegration), nameof(AfterThrow)),
                isCritical: true, description: "Finish in-flight Architect potions before execution."),
            new("ninjaslayer_architect_potion_offer", offering,
                postfix: new HarmonyMethod(typeof(ArchitectPotionIntegration), nameof(AfterOffer)),
                isCritical: true, description: "Stop accepting new potion throws after Continue.")
        ];
    }

    private static bool BeforeThrow(PotionModel potion, ref Task __result)
    {
        if (potion.Owner.Character is not INinjaSlayerCharacter
            || NCombatRoom.Instance?.GetNodeOrNull("NinjaSlayerArchitectExecution") == null) return true;
        __result = Task.CompletedTask;
        return false;
    }

    private static void AfterOffer(PotionModel? potion, ref bool __result)
    {
        if (potion?.Owner.Character is INinjaSlayerCharacter
            && NCombatRoom.Instance?.GetNodeOrNull("NinjaSlayerArchitectExecution") != null)
            __result = false;
    }

    private static void AfterThrow(PotionModel potion, ref Task __result)
    {
        if (potion.Owner.Character is not INinjaSlayerCharacter || NCombatRoom.Instance is not { } room) return;
        HashSet<Task> pending = Throws.GetValue(room, _ => []);
        Task flight = __result;
        pending.Add(flight);
        __result = CompleteThrow(flight, pending);
    }

    private static async Task CompleteThrow(Task flight, HashSet<Task> pending)
    {
        try { await flight; }
        finally { pending.Remove(flight); }
    }

    internal static Task WaitForThrows(NCombatRoom room, CancellationToken cancellationToken) =>
        Throws.TryGetValue(room, out HashSet<Task>? pending)
            ? Task.WhenAll(pending.ToArray()).WaitAsync(cancellationToken)
            : Task.CompletedTask;
}
