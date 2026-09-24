using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Monsters;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class YamotoKokiAllyLayoutPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_yamoto_koki_ally_layout";
    public static string Description => "Use native player-slot layout for friendly companion pets.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NCombatRoom), nameof(NCombatRoom.PositionPlayersAndPets),
            [typeof(List<NCreature>), typeof(float), typeof(bool)])
    ];

    public static void Prefix(ref List<NCreature> creatureNodes)
    {
        if (!creatureNodes.Any(node => IsCompanion(node.Entity))) return;

        // Only the native layout input changes, never combat/player/pet enumeration.
        List<Creature> insertionOrder = creatureNodes[0].Entity.CombatState!.Creatures.ToList();
        creatureNodes = creatureNodes
            .Where(node => node.Entity.Monster is not YamotoKokiOrigamiMissile
                && (!IsCompanion(node.Entity) || IsCompanionAnchor(node.Entity)))
            .OrderBy(node => LocalContext.IsMe(node.Entity) ? 0
                : IsCompanionAnchor(node.Entity) ? 1 : node.Entity.IsPlayer ? 2 : 3)
            .ThenBy(node => IsCompanionAnchor(node.Entity) ? insertionOrder.IndexOf(node.Entity) : 0)
            .ToList();
        foreach (NCreature node in creatureNodes) node.Visuals.Modulate = Colors.White;
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo isPlayer = AccessTools.PropertyGetter(typeof(Creature), nameof(Creature.IsPlayer));
        MethodInfo isAnchor = AccessTools.Method(typeof(YamotoKokiAllyLayoutPatch), nameof(IsLayoutAnchor));
        int replaced = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            // Only the two direct grouping checks. Lambda checks and native
            // LocalContext/Osty handling retain real player identity.
            if (instruction.Calls(isPlayer))
            {
                instruction.opcode = System.Reflection.Emit.OpCodes.Call;
                instruction.operand = isAnchor;
                replaced++;
            }
            yield return instruction;
        }
        if (replaced != 2)
            throw new InvalidOperationException($"Expected two native layout grouping checks, found {replaced}.");
    }

    private static bool IsCompanion(Creature creature) =>
        creature.Monster is YamotoKokiMonster or SawatariMonster or YukanoMonster;

    private static bool IsCompanionAnchor(Creature creature) =>
        FriendlyCompanionTargeting.IsFriendlyCompanion(creature)
        && !CompanionIntentLifecycle.HasRetired(creature);

    private static bool IsLayoutAnchor(Creature creature) => creature.IsPlayer || IsCompanionAnchor(creature);

    internal static void Reflow(NCombatRoom room)
    {
        List<NCreature> allies = room.CreatureNodes
            .Where(node => node.Entity.IsPlayer || node.Entity.Side == CombatSide.Player && node.Entity.PetOwner != null)
            .ToList();
        Creature? player = allies.FirstOrDefault(node => node.Entity.IsPlayer)?.Entity;
        if (player?.CombatState is not { } combat) return;
        foreach (NCreature node in allies) node.Visuals.Modulate = Colors.White;
        NCombatRoom.PositionPlayersAndPets(allies,
            combat.Encounter?.GetCameraScaling() ?? room.SceneContainer.Scale.X, combat.Encounter?.FullyCenterPlayers ?? false);
        YamotoKokiAllyFacingController.Ensure(room).SyncNow();
        YamotoKokiOrigamiMissileOrbitController.Ensure(room).LayoutNow(snapNewMissiles: true);
    }
}

public sealed class YamotoKokiDynamicAllyLayoutPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_yamoto_koki_dynamic_ally_layout";
    public static string Description => "Refresh native companion slots after creatures enter or leave.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NCombatRoom), nameof(NCombatRoom.AddCreature)),
        new(typeof(NCombatRoom), nameof(NCombatRoom.RemoveCreatureNode))
    ];

    public static void Postfix(NCombatRoom __instance) => YamotoKokiAllyLayoutPatch.Reflow(__instance);
}
