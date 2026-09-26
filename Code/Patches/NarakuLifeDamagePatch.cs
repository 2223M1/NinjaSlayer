using HarmonyLib;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Powers;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NarakuLifeDamagePatch : IPatchMethod
{
    private sealed record Absorption(int Amount);
    private static readonly ConditionalWeakTable<DamageResult, Absorption> Absorptions = new();

    internal static int AbsorbedBy(DamageResult result) =>
        Absorptions.TryGetValue(result, out var receipt) ? receipt.Amount : 0;

    public static string PatchId => "ninjaslayer_naraku_life_absorption";
    public static string Description => "Absorb final HP loss before lethal damage and finisher classification.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(Creature), nameof(Creature.LoseHpInternal), [typeof(decimal), typeof(ValueProp)])];

    [HarmonyPriority(Priority.First)]
    [HarmonyAfter("Loadout")]
    public static void Prefix(Creature __instance, ref decimal amount, out int __state)
    {
        decimal before = amount;
        if (__instance.GetPower<NarakuLifePower>() is { } life)
            amount = life.AbsorbHpLoss(amount);
        __state = (int)(before - amount);
    }

    [HarmonyPriority(Priority.Last)]
    public static void Postfix(Creature __instance, DamageResult __result, int __state)
    {
        if (__state <= 0) return;
        // The absorption already happened even when another mod supplies the HP-loss result.
        if (__result == null || !ReferenceEquals(__result.Receiver, __instance))
            throw new InvalidOperationException("Naraku absorption requires the receiver's final damage result.");
        Absorptions.Add(__result, new Absorption(__state));
    }
}
