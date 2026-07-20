using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerFinisherLethalDamagePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_finisher_lethal_damage";
    public static string Description => "Defer guaranteed lethal damage until the final finisher hit completes.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(Creature), nameof(Creature.LoseHpInternal), [typeof(decimal), typeof(MegaCrit.Sts2.Core.ValueProps.ValueProp)])];

    public static void Prefix(Creature __instance, ref decimal amount)
    {
        NinjaSlayerFinisherCinematic.TryProtectLethalDamage(__instance, ref amount);
    }
}

public sealed class NinjaSlayerFinisherDeathAnimationPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_finisher_death_animation";
    public static string Description => "Synchronize the final-hit freeze with enemy death animations.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NCreature), nameof(NCreature.StartDeathAnim), [typeof(bool)])];

    public static void Postfix(NCreature __instance)
    {
        NinjaSlayerFinisherCinematic.NotifyDeathAnimationStarted(__instance);
    }
}
