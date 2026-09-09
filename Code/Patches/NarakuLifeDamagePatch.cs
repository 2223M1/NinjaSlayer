using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Powers;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NarakuLifeDamagePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_naraku_life_absorption";
    public static string Description => "Absorb final HP loss before lethal damage and finisher classification.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(Creature), nameof(Creature.LoseHpInternal), [typeof(decimal), typeof(ValueProp)])];

    [HarmonyPriority(Priority.First)]
    public static void Prefix(Creature __instance, ref decimal amount)
    {
        if (__instance.GetPower<NarakuLifePower>() is { } life)
            amount = life.AbsorbHpLoss(amount);
    }
}
