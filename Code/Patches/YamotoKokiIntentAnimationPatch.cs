using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Monsters;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class YamotoKokiIntentUpdatePatch : IPatchMethod
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NIntent, OriginalMaterials> Originals = new();
    private static ShaderMaterial? _yellow;
    public static string PatchId => "ninjaslayer_yamoto_koki_intent_update";
    public static string Description => "Recolor native friendly attack intents and particles yellow.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NIntent), "UpdateVisuals", Type.EmptyTypes)
    ];

    public static void Postfix(NIntent __instance, AbstractIntent ____intent, Creature ____owner)
    {
        Sprite2D icon = __instance.GetNode<Sprite2D>("%Intent");
        CpuParticles2D particles = __instance.GetNode<CpuParticles2D>("%IntentParticle");
        if (____intent is AttackIntent && FriendlyCompanionTargeting.IsFriendlyCompanion(____owner))
        {
            if (!Originals.TryGetValue(__instance, out _))
                Originals.Add(__instance, new OriginalMaterials(icon.Material, particles.Material));
            _yellow ??= new ShaderMaterial
            {
                Shader = ResourceLoader.Load<Shader>("res://NinjaSlayer/shaders/friendly_attack_intent.gdshader")
            };
            icon.Material = _yellow;
            particles.Material = _yellow;
        }
        else if (Originals.TryGetValue(__instance, out OriginalMaterials? original))
        {
            icon.Material = original.Icon;
            particles.Material = original.Particles;
            Originals.Remove(__instance);
        }
    }

    private sealed record OriginalMaterials(Material? Icon, Material? Particles);
}

public sealed class YamotoKokiIntentGenerationPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_yamoto_koki_intent_generation";
    public static string Description => "Block stale companion intent writes after combat resolution.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NCreature), nameof(NCreature.UpdateIntent), [typeof(IEnumerable<Creature>)])
    ];

    public static bool Prefix(NCreature __instance, ref Task __result)
    {
        if (__instance.Entity.Side != CombatSide.Player
            || __instance.Entity.Monster is not (YamotoKokiMonster or SawatariMonster or YukanoMonster)
            || CompanionIntentLifecycle.IsActive(__instance.Entity))
        {
            return true;
        }

        CompanionIntentLifecycle.Invalidate(__instance.Entity);
        __result = Task.CompletedTask;
        return false;
    }
}

public sealed class YamotoKokiLastEnemyDeathIntentPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_yamoto_koki_last_enemy_intent_cleanup";
    public static string Description => "Hide companion intents when the final enemy starts dying.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NCreature), nameof(NCreature.StartDeathAnim), [typeof(bool)])
    ];

    public static void Prefix(NCreature __instance, bool shouldRemove)
    {
        Creature dying = __instance.Entity;
        ICombatState? combatState = dying.CombatState;
        if (!shouldRemove
            || dying.Side != CombatSide.Enemy
            || combatState == null
            || combatState.Enemies.Any(enemy => enemy != dying && enemy.IsAlive))
        {
            return;
        }

        CompanionIntentLifecycle.InvalidateCombat(combatState);
    }
}
