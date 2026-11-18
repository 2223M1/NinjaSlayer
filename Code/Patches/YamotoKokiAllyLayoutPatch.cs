using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
#if NINJASLAYER_CHANNEL_PREVIEW
using MegaCrit.Sts2.Core.Models.Monsters;
#endif
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
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

    public static void Prefix(ref List<NCreature> creatureNodes,
        out Dictionary<NCreature, Vector2> __state)
    {
        __state = creatureNodes
            .Where(node => node.IsInsideTree() && node.Entity.Player?.Character is INinjaSlayerCharacter)
            .ToDictionary(node => node, node => node.Position);
        if (!creatureNodes.Any(node => IsCompanion(node.Entity))) return;

        // Only the native layout input changes, never combat/player/pet enumeration.
        List<Creature> insertionOrder = creatureNodes[0].Entity.CombatState!.Creatures.ToList();
        creatureNodes = creatureNodes
            .Where(node => node.Entity.Monster is not OrigamiMissileMonster
#if NINJASLAYER_CHANNEL_PREVIEW
                && !(node.Entity.Monster is Wuppy && LocalContext.IsMe(node.Entity.PetOwner))
#endif
                && (!IsCompanion(node.Entity) || IsCompanionAnchor(node.Entity)))
            .OrderBy(node => LocalContext.IsMe(node.Entity) ? 0
                : IsCompanionAnchor(node.Entity) ? 1 : node.Entity.IsPlayer ? 2 : 3)
            .ThenBy(node => IsCompanionAnchor(node.Entity) ? insertionOrder.IndexOf(node.Entity) : 0)
            .ToList();
        foreach (NCreature node in creatureNodes) node.Visuals.Modulate = Colors.White;
    }

    public static void Postfix(List<NCreature> creatureNodes, Dictionary<NCreature, Vector2> __state)
    {
        int companions = creatureNodes.Count(node => IsCompanionAnchor(node.Entity));
        foreach (var (node, previousPosition) in __state)
            NinjaSlayerAllyLayoutMotion.Ensure(node).OnLayout(previousPosition, companions);
#if NINJASLAYER_CHANNEL_PREVIEW
        if (creatureNodes.Any(node => IsCompanion(node.Entity))) PositionLocalWuppy(creatureNodes);
#endif
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

    internal static bool IsCompanion(Creature creature) =>
        creature.Monster is YamotoKokiMonster or ForestSawatariMonster or YukanoMonster;

    private static bool IsCompanionAnchor(Creature creature) =>
        FriendlyCompanionTargeting.IsFriendlyCompanion(creature)
        && !CompanionIntentLifecycle.HasRetired(creature);

    private static bool IsLayoutAnchor(Creature creature) => creature.IsPlayer || IsCompanionAnchor(creature);

#if NINJASLAYER_CHANNEL_PREVIEW
    private static void PositionLocalWuppy(List<NCreature> nodes)
    {
        var owner = nodes.FirstOrDefault(node => LocalContext.IsMe(node.Entity));
        if (owner?.Entity.Pets.FirstOrDefault(p => p.Monster is Wuppy && p.IsAlive)?.GetCreatureNode() is not { } pet) return;
        pet.Position = new Vector2(pet.Position.X, owner.Position.Y);
        pet.PositionWuppy(isCombatStart: false);
    }
#endif

    internal static void Reflow(NCombatRoom room)
    {
        List<NCreature> allies = room.CreatureNodes
            .Where(node => node.Entity.IsPlayer || node.Entity.Side == CombatSide.Player && node.Entity.PetOwner != null)
            .ToList();
        Creature? player = allies.FirstOrDefault(node => node.Entity.IsPlayer)?.Entity;
        if (player?.CombatState is not { } combat) return;
        // Sandpit's native creature-added hook already includes pets in its pull.
        // Rebuilding absolute player slots here would undo the encounter's position.
        if (allies.Any(node => NinjaSlayerAllyLayoutMotion.IsPulledBySandpit(node.Entity))) return;
#if NINJASLAYER_CHANNEL_PREVIEW
        // Also preserve the local pet on the reflow that removes the last companion.
        bool noAnchors = !allies.Any(n => IsCompanionAnchor(n.Entity));
        if (noAnchors) allies.RemoveAll(n => n.Entity.Monster is Wuppy && LocalContext.IsMe(n.Entity.PetOwner));
#endif
        foreach (NCreature node in allies) node.Visuals.Modulate = Colors.White;
        NCombatRoom.PositionPlayersAndPets(allies,
            combat.Encounter?.GetCameraScaling() ?? room.SceneContainer.Scale.X, combat.Encounter?.FullyCenterPlayers ?? false);
#if NINJASLAYER_CHANNEL_PREVIEW
        if (noAnchors) PositionLocalWuppy(allies);
#endif
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

    public static void Prefix(NCombatRoom __instance, object __0, out Dictionary<NCreature, Vector2>? __state)
    {
        var changed = __0 is NCreature node ? node.Entity : (Creature)__0;
        __state = null;
        if (changed.Side != CombatSide.Player || (!changed.IsPlayer && changed.PetOwner == null)) return;
        if (!YamotoKokiAllyLayoutPatch.IsCompanion(changed)
            && !__instance.CreatureNodes.Any(n => FriendlyCompanionTargeting.IsFriendlyCompanion(n.Entity))) return;
        __state = __instance.CreatureNodes
            .Where(node => FriendlyCompanionTargeting.IsFriendlyCompanion(node.Entity)
                && NinjaSlayerAllyLayoutMotion.IsPulledBySandpit(node.Entity))
            .ToDictionary(node => node, node => node.Position);
    }

    public static void Postfix(NCombatRoom __instance, Dictionary<NCreature, Vector2>? __state)
    {
        if (__state == null) return;
        // AddCreature places all existing pets beside their owner. Preserve our
        // companions' established spacing before Sandpit's native added hook
        // applies the same pull to the player and their pets.
        foreach (var (node, position) in __state) node.Position = position;
        YamotoKokiAllyLayoutPatch.Reflow(__instance);
    }
}
