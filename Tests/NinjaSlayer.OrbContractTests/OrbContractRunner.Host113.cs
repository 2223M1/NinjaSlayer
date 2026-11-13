#if NINJASLAYER_CHANNEL_PREVIEW
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static readonly List<(Creature Target, string Path, string Method)> HostHitEffects = [];

    private static void ObserveHostHitEffect(Creature target, string path, MethodBase __originalMethod) =>
        HostHitEffects.Add((target, path, __originalMethod.Name));

    private static async Task VerifyHost113DamageEntries()
    {
        using var combat = new OrbCombat();
        var observer = new Harmony("NinjaSlayer.OrbContracts.Host113Damage");
        foreach (string method in new[] { nameof(VfxCmd.PlayOnCreature), nameof(VfxCmd.PlayOnCreatureCenter) })
            observer.Patch(AccessTools.Method(typeof(VfxCmd), method),
                postfix: new HarmonyMethod(typeof(OrbContractRunner), nameof(ObserveHostHitEffect)));
        try
        {
            var owner = combat.Player.Creature;
            await PowerCmd.Apply<StrengthPower>(Choice, owner, 100, owner, null);
            await PowerCmd.Apply<EvasionPower>(Choice, combat.Enemy, 1, combat.Enemy, null);
            HostHitEffects.Clear();
            var results = (await CreatureCmd.Damage(Choice,
                new[] {
                    new DamageTargetData(combat.Enemy) { hitVfx = "evaded-hit" },
                    new DamageTargetData(combat.Enemy) { hitVfx = "connected-hit", playVfxOnCreatureCenter = true }
                }, 7, ValueProp.Move, owner, null, null, ModifyDamageHookType.Cap)).ToArray();
            Require(results.Length == 1 && combat.Enemy.CurrentHp == 993
                && !combat.Enemy.HasPower<EvasionPower>(),
                "Duplicate targets must evade once; already-calculated damage must not apply Strength again.");
            Require(HostHitEffects.SequenceEqual(new[] { (combat.Enemy, "connected-hit", nameof(VfxCmd.PlayOnCreatureCenter)) }),
                "Evasion must preserve the surviving occurrence's hit effect and center flag.");

            await PowerCmd.Apply<KaratePower>(Choice, owner, 3, owner, null);
            await CreatureCmd.Damage(Choice, new[] { new DamageTargetData(combat.Enemy) },
                7, ValueProp.Move, owner, null, null, ModifyDamageHookType.Cap);
            Require(combat.Enemy.CurrentHp == 983 && owner.GetPowerAmount<KaratePower>() == 2,
                "The new damage entry must trigger exactly one Karate wave.");
            int hp = owner.CurrentHp;
            await PowerCmd.Apply<NarakuLifePower>(Choice, owner, 5, owner, null);
            await CreatureCmd.Damage(Choice, new[] { new DamageTargetData(owner) },
                7, ValueProp.Move, combat.Enemy, null, null, ModifyDamageHookType.Cap);
            Require(owner.CurrentHp == hp - 2 && !owner.HasPower<NarakuLifePower>(),
                "The new damage entry must consume Naraku before real HP.");
        }
        finally
        {
            observer.UnpatchAll(observer.Id);
            HostHitEffects.Clear();
        }
        GD.Print("PASS 0.113 damage entries: repeated-target Evasion, preserved hit metadata, modifier flags, Karate and Naraku.");
    }
}
#endif
