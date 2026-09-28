using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyCombatRegression()
    {
        foreach (PowerModel canonical in new PowerModel[]
                 { ModelDb.Power<InfestedPower>(), ModelDb.Power<StockPower>(), ModelDb.Power<SurprisePower>() })
        {
            using var combat = new OrbCombat(ninjaSlayer: true);
            var run = RunState.CreateForTest([combat.Player]);
            AccessTools.Field(combat.State.GetType(), "<RunState>k__BackingField").SetValue(combat.State, run);
            var product = typeof(NarakuLifePower).Assembly;
            var targeting = product.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherTargeting", true)!;
            var descriptor = product.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherActionForecastDescriptor", true)!;
            object forecast = Activator.CreateInstance(descriptor,
                [new Func<Creature, decimal>(_ => 10000m), ValueProp.Move, 3, Enum.Parse(targeting, "Random"),
                    null, null, false, null, null])!;
            var evaluate = AccessTools.Method(product.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherForecast", true)!, "EvaluateAction");
            string Outcome() => evaluate.Invoke(null,
                [combat.Player.Creature, new[] { combat.Enemy }, forecast, null])!.ToString()!;
            Require(Outcome() == "Guaranteed", "An ordinary lethal attack must still qualify for a finisher.");
            var power = canonical.ToMutable();
            await PowerCmd.Apply(Choice, power, combat.Enemy, 1, combat.Enemy, null);
            Require(Hook.ShouldStopCombatFromEnding(combat.State) && Outcome() == "NotGuaranteed",
                canonical.Id + " must prevent a premature finisher through the host continuation predicate.");
            await PowerCmd.Remove(power);
            Require(!Hook.ShouldStopCombatFromEnding(combat.State) && Outcome() == "Guaranteed",
                "The final phase must regain finisher eligibility after its native continuation source disappears.");
        }
        using (var combat = new OrbCombat(ninjaSlayer: true))
        {
            var owner = combat.Player.Creature;
            await PowerCmd.Apply<NarakuLifePower>(Choice, owner, 12, owner, null);
            var life = owner.GetPower<NarakuLifePower>()!;
            Require(life.IsVisible && life.DisplayAmount == 12, "Naraku Life must display its real stock.");
            int changed = 0;
            life.DisplayAmountChanged += () => changed++;
            await PowerCmd.Apply<NarakuLifePower>(Choice, owner, 3, owner, null);
            int hp = owner.CurrentHp;
            await CreatureCmd.Damage(Choice, [owner], 4, ValueProp.Unpowered, combat.Enemy, null
#if !NINJASLAYER_CHANNEL_STABLE
                , null
#endif
            );
            Require(life.Amount == 11 && life.DisplayAmount == 11 && owner.CurrentHp == hp && changed == 2,
                "Gain and absorption must update the same native displayed amount without hurting real HP.");
            await CreatureCmd.GainBlock(owner, 5, ValueProp.Unpowered, null);
            await CreatureCmd.Damage(Choice, [owner], 5, ValueProp.Move, combat.Enemy, null
#if !NINJASLAYER_CHANNEL_STABLE
                , null
#endif
            );
            Require(life.DisplayAmount == 11 && changed == 2, "Full block must not change Naraku Life's counter.");
            await CreatureCmd.Damage(Choice, [owner], 13, ValueProp.Unpowered, combat.Enemy, null
#if !NINJASLAYER_CHANNEL_STABLE
                , null
#endif
            );
            Require(life.DisplayAmount == 0 && !owner.HasPower<NarakuLifePower>() && owner.CurrentHp == hp - 2,
                "Exhausted Naraku Life must remove its native icon and apply overflow once.");
        }
        GD.Print("PASS native continuation veto for parasite, stock and gremlin phases; final-phase eligibility; visible Naraku Life and native count notifications.");
    }
}
