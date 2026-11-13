using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Powers;
using STS2RitsuLib.Combat.HealthBars;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyOneMindOneBody()
    {
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var owner = combat.Player.Creature;
            var form = AddCard<OneMindOneBody>(combat, upgraded: upgraded);
            Require(form.EnergyCost.GetWithModifiers(CostModifiers.Local) == 3
                && form.Keywords.Contains(CardKeyword.Ethereal), "Both One Mind versions must remain three-cost and Ethereal.");
            await CardCmd.AutoPlay(Choice, form, null);
            Require(owner.GetPowerAmount<NarakuLifePower>() == (upgraded ? 10 : 7)
                && !owner.HasPower<KaratePower>(), "One Mind grants seven/ten life without creating real Karate.");
            await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), combat.Enemy);
            Require(combat.Enemy.CurrentHp == 1000 - 6 - (upgraded ? 10 : 7)
                && owner.GetPowerAmount<NarakuLifePower>() == (upgraded ? 9 : 6),
                "Naraku Life alone must trigger Karate and spend one life.");
        }
        using (var combat = new OrbCombat())
        {
            var owner = combat.Player.Creature;
            await PowerCmd.Apply<KaratePower>(Choice, owner, 5, owner, null);
            var karate = owner.GetPower<KaratePower>()!;
            await CardCmd.AutoPlay(Choice, AddCard<OneMindOneBody>(combat), null);
            await PowerCmd.ModifyAmount(Choice, owner.GetPower<NarakuLifePower>()!, -2, owner, null); // Keep the five-life mechanics fixture.
            Require(karate.Amount == 5 && karate.DisplayAmount == 10
                && karate.DynamicVars["AttackAmount"].IntValue == 10
                && karate.HoverTips.OfType<HoverTip>().Any(tip => tip.Description.Contains("10")),
                "Five actual Karate plus five life must display ten in the power and tooltip.");
            int displays = 0;
            karate.DisplayAmountChanged += () => displays++;
            var strike = AddCard<StrikeIronclad>(combat);
            using (KarateCombatPreviewContext.Enter(strike, combat.Enemy))
            {
                var forecast = new KarateHealthBarForecastSource().GetHealthBarForecastSegments(new(combat.Enemy));
                Require(forecast.Sum(segment => segment.Amount) == 10 && karate.Amount == 5
                    && owner.GetPowerAmount<NarakuLifePower>() == 5, "The health bar forecasts ten Karate damage without spending it.");
            }
            await CardCmd.AutoPlay(Choice, strike, combat.Enemy);
            Require(combat.Enemy.CurrentHp == 984 && karate.Amount == 5 && karate.DisplayAmount == 9
                && karate.DynamicVars["AttackAmount"].IntValue == 9
                && karate.HoverTips.OfType<HoverTip>().Any(tip => tip.Description.Contains("9"))
                && owner.GetPowerAmount<NarakuLifePower>() == 4 && displays > 0,
                "User example: Strike deals six plus ten, life becomes four, displayed Karate becomes nine, real Karate remains five.");

            await PowerCmd.Apply<NarakuLifePower>(Choice, owner, 3, owner, null);
            Require(karate.DisplayAmount == 12, "Gaining life must immediately increase displayed Karate.");
            int hp = owner.CurrentHp;
            await CreatureCmd.Damage(Choice, owner, 2, ValueProp.Unpowered, combat.Enemy);
            Require(owner.CurrentHp == hp && karate.DisplayAmount == 10 && karate.Amount == 5,
                "Absorbing damage must immediately lower borrowed Karate without consuming real Karate.");
            await CreatureCmd.GainBlock(owner, 3, ValueProp.Unpowered, null);
            await CreatureCmd.Damage(Choice, owner, 3, ValueProp.Move, combat.Enemy);
            Require(karate.DisplayAmount == 10, "Full block must preserve life and borrowed Karate.");
            await CreatureCmd.Damage(Choice, owner, 7, ValueProp.Unpowered, combat.Enemy);
            Require(owner.CurrentHp == hp - 2 && !owner.HasPower<NarakuLifePower>()
                && karate.DisplayAmount == 5 && karate.Amount == 5,
                "Damage beyond life must hit HP, never real Karate, and remove the borrowed display immediately.");
            await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), combat.Enemy);
            Require(combat.Enemy.CurrentHp == 973 && karate.Amount == 4 && karate.DisplayAmount == 4,
                "After life is exhausted, Karate resumes consuming actual stacks.");
            await PowerCmd.Apply<NarakuLifePower>(Choice, owner, 2, owner, null);
            await PowerCmd.Remove<OneMindOneBodyPower>(owner);
            Require(karate.DisplayAmount == 4 && owner.GetPowerAmount<NarakuLifePower>() == 2,
                "Removing One Mind must stop the contribution and retain both real resources.");
            await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), combat.Enemy);
            Require(combat.Enemy.CurrentHp == 963 && karate.Amount == 3
                && owner.GetPowerAmount<NarakuLifePower>() == 2,
                "Without One Mind, attacks must consume real Karate even when life remains.");
        }
        using (var combat = new OrbCombat())
        {
            var owner = combat.Player.Creature;
            await PowerCmd.Apply<KaratePower>(Choice, owner, 5, owner, null);
            await CardCmd.AutoPlay(Choice, AddCard<OneMindOneBody>(combat), null);
            await CardCmd.AutoPlay(Choice, AddCard<OneMindOneBody>(combat), null);
            Require(owner.GetPowerAmount<NarakuLifePower>() == 14
                && owner.GetPower<OneMindOneBodyPower>()!.DisplayAmount == 2,
                "Two base plays must grant fourteen life and show two conversion layers.");
            await PowerCmd.ModifyAmount(Choice, owner.GetPower<NarakuLifePower>()!, -9, owner, null);
            Require(owner.GetPower<KaratePower>()!.DisplayAmount == 15, "Two forms with five real Karate and five life must display fifteen.");
            await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), combat.Enemy);
            Require(combat.Enemy.CurrentHp == 979 && owner.GetPowerAmount<NarakuLifePower>() == 4
                && owner.GetPowerAmount<KaratePower>() == 5 && owner.GetPower<KaratePower>()!.DisplayAmount == 13,
                "Two forms must deal fifteen bonus damage, spend one life, and lower displayed Karate by two without spending base Karate.");
            var second = combat.AddEnemy();
            await CreatureCmd.Damage(Choice, new[] { combat.Enemy, second }, 1, ValueProp.Move, owner);
            Require(combat.Enemy.CurrentHp == 965 && second.CurrentHp == 986
                && owner.GetPowerAmount<NarakuLifePower>() == 3 && owner.GetPower<KaratePower>()!.DisplayAmount == 11,
                "Two-layer AOE must use thirteen bonus damage on every target and spend only one life total.");
            await CardCmd.AutoPlay(Choice, AddCard<OneMindOneBody>(combat, upgraded: true), null);
            Require(owner.GetPowerAmount<NarakuLifePower>() == 13 && owner.GetPowerAmount<OneMindOneBodyPower>() == 3
                && owner.GetPower<KaratePower>()!.DisplayAmount == 44,
                "An upgraded third play must add ten life and one conversion layer, not ten conversion layers.");
            await PowerCmd.ModifyAmount(Choice, owner.GetPower<OneMindOneBodyPower>()!, -1, owner, null);
            Require(owner.GetPower<KaratePower>()!.DisplayAmount == 31
                && owner.GetPower<KaratePower>()!.DynamicVars["AttackAmount"].IntValue == 31,
                "Losing a conversion layer must refresh the Karate number and tooltip immediately.");
        }
        foreach (int multiplier in new[] { 1, 2 })
        {
            using var combat = new OrbCombat();
            var owner = combat.Player.Creature;
            await PowerCmd.Apply<OneMindOneBodyPower>(Choice, owner, multiplier, owner, null);
            await PowerCmd.Apply<NarakuLifePower>(Choice, owner, 1, owner, null);
            await PowerCmd.Apply<KaratePower>(Choice, owner, 2, owner, null);
            await CardCmd.AutoPlay(Choice, AddCard<TwinStrike>(combat), combat.Enemy);
            Require(combat.Enemy.CurrentHp == 986 - multiplier && owner.GetPowerAmount<KaratePower>() == 1
                && !owner.HasPower<NarakuLifePower>(),
                "Two hits must consume the last life first, then real Karate, using the current conversion multiplier.");
        }
        using (var combat = new OrbCombat())
        {
            var owner = combat.Player.Creature;
            await CardCmd.AutoPlay(Choice, AddCard<OneMindOneBody>(combat), null);
            await PowerCmd.ModifyAmount(Choice, owner.GetPower<NarakuLifePower>()!, -2, owner, null); // Keep the five-life mechanics fixture.
            await PowerCmd.Apply<GuardStancePower>(Choice, owner, 3, owner, null);
            await CardCmd.AutoPlay(Choice, AddCard<PalmThrust>(combat, upgraded: true), null);
            Require(combat.Enemy.CurrentHp == 970 && owner.Block == 9
                && owner.GetPowerAmount<NarakuLifePower>() == 2 && !owner.HasPower<KaratePower>(),
                "Naraku-only Karate must trigger Guard Stance per hit without generating actual Karate.");
            var second = combat.AddEnemy();
            await CreatureCmd.Damage(Choice, new[] { combat.Enemy, second }, 1, ValueProp.Move, owner);
            Require(combat.Enemy.CurrentHp == 967 && second.CurrentHp == 997 && owner.Block == 12
                && owner.GetPowerAmount<NarakuLifePower>() == 1, "AOE must spend one life for the whole wave.");
            var other = Player.CreateForNewRun<Ironclad>(UnlockState.all, 2);
            combat.State.AddPlayer(other);
            other.ResetCombatState();
            await PowerCmd.Apply<KaratePower>(Choice, other.Creature, 4, other.Creature, null);
            await CreatureCmd.Damage(Choice, second, 1, ValueProp.Move, other.Creature);
            Require(second.CurrentHp == 992 && other.Creature.GetPowerAmount<KaratePower>() == 3
                && owner.GetPowerAmount<NarakuLifePower>() == 1, "A teammate's Karate must not borrow or spend this player's life.");
        }
        using (var combat = new OrbCombat())
        {
            var owner = combat.Player.Creature;
            await CardCmd.AutoPlay(Choice, AddCard<OneMindOneBody>(combat), null);
            await PowerCmd.ModifyAmount(Choice, owner.GetPower<NarakuLifePower>()!, -2, owner, null); // Keep the five-life mechanics fixture.
            await PowerCmd.Apply<KaratePower>(Choice, owner, 5, owner, null);
            await CardCmd.AutoPlay(Choice, AddCard<AlabamaDrop>(combat), combat.Enemy);
            Require(combat.Enemy.CurrentHp == 965 && owner.GetPowerAmount<KaratePower>() == 5
                && owner.GetPowerAmount<NarakuLifePower>() == 4,
                "Alabama Drop must deal 5*5 base damage plus ten Karate damage; life must not enter its multiplier.");
            await CardCmd.AutoPlay(Choice, AddCard<Redouble>(combat), null);
            Require(owner.GetPowerAmount<KaratePower>() == 10 && owner.GetPower<KaratePower>()!.DisplayAmount == 14,
                "Redouble must double only real Karate.");
            await CardCmd.AutoPlay(Choice, AddCard<KarateBulwark>(combat), null);
            Require(owner.GetPowerAmount<PlatingPower>() == 10 && !owner.HasPower<KaratePower>()
                && owner.GetPowerAmount<NarakuLifePower>() == 4 && KaratePower.GetAttackAmount(owner) == 4,
                "Karate Bulwark must convert only real Karate to Plating and preserve the Naraku contribution.");
        }
        using (var combat = new OrbCombat(ninjaSlayer: true))
        {
            var owner = combat.Player.Creature;
            var run = RunState.CreateForTest([combat.Player]);
            AccessTools.Field(combat.State.GetType(), "<RunState>k__BackingField").SetValue(combat.State, run);
            await CardCmd.AutoPlay(Choice, AddCard<OneMindOneBody>(combat), null);
            await PowerCmd.ModifyAmount(Choice, owner.GetPower<NarakuLifePower>()!, -2, owner, null); // Keep the five-life mechanics fixture.
            var card = AddCard<StrikeIronclad>(combat);
            using (KarateCombatPreviewContext.Enter(card, combat.Enemy))
                Require(new KarateHealthBarForecastSource().GetHealthBarForecastSegments(new(combat.Enemy))
                    .Sum(segment => segment.Amount) == 5, "Naraku-only Karate must appear in the health bar forecast.");
            var play = new CardPlay { Card = card,
#if !NINJASLAYER_CHANNEL_STABLE
                Player = combat.Player,
#endif
                Target = combat.Enemy, ResultPile = PileType.Discard,
                Resources = new ResourceInfo { EnergySpent = 0, EnergyValue = 0, StarsSpent = 0, StarValue = 0 },
                IsAutoPlay = false, PlayIndex = 0, PlayCount = 1 };
            var command = DamageCmd.Attack(6)
#if NINJASLAYER_CHANNEL_STABLE
                .FromCard(card)
#else
                .FromCard(card, play)
#endif
                .Targeting(combat.Enemy);
            var product = typeof(OneMindOneBody).Assembly;
            var create = AccessTools.Method(product.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherAttackCommandAdapter", true)!, "CreateSpec");
            object spec = create.Invoke(null, [command, card, play, null, null])!;
            var evaluate = AccessTools.Method(product.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherForecast", true)!, "Evaluate");
            string Outcome() => evaluate.Invoke(null, new object?[] { owner, new[] { combat.Enemy }, spec, command, null })!.ToString()!;
            combat.Enemy.SetCurrentHpInternal(11);
            Require(Outcome() == "Guaranteed" && owner.GetPowerAmount<NarakuLifePower>() == 5,
                "A finisher must include the five borrowed Karate without spending life.");
            await PowerCmd.ModifyAmount(Choice, owner.GetPower<NarakuLifePower>()!, -1, owner, null);
            Require(Outcome() == "NotGuaranteed", "Losing life must invalidate the cached finisher forecast immediately.");
            await PowerCmd.Apply<OneMindOneBodyPower>(Choice, owner, 1, owner, null);
            Require(Outcome() == "Guaranteed", "A second form must immediately increase borrowed Karate in the finisher forecast.");
            command.WithHitCount(3);
            spec = create.Invoke(null, [command, card, play, null, null])!;
            combat.Enemy.SetCurrentHpInternal(36);
            Require(Outcome() == "Guaranteed", "Three hits with two forms and four life must deal 18 primary plus 8+6+4 Karate damage.");
            combat.Enemy.SetCurrentHpInternal(37);
            Require(Outcome() == "NotGuaranteed" && owner.GetPowerAmount<NarakuLifePower>() == 4,
                "Multi-hit forecasting must lower borrowed Karate by two per hit without mutating life.");
            var twin = AddCard<TwinStrike>(combat);
            using (KarateCombatPreviewContext.Enter(twin, combat.Enemy))
                Require(new KarateHealthBarForecastSource().GetHealthBarForecastSegments(new(combat.Enemy))
                    .Sum(segment => segment.Amount) == 14, "Two-hit health-bar preview must show 8+6 borrowed Karate damage.");
            command.WithHitCount(1);
            spec = create.Invoke(null, [command, card, play, null, null])!;
            combat.Enemy.SetCurrentHpInternal(11);
            await PowerCmd.ModifyAmount(Choice, owner.GetPower<OneMindOneBodyPower>()!, -1, owner, null);
            await PowerCmd.Apply<NarakuLifePower>(Choice, owner, 1, owner, null);
            await PowerCmd.Apply<ThornsPower>(Choice, combat.Enemy, 2, combat.Enemy, null);
            Require(Outcome() == "NotGuaranteed", "Thorns can spend borrowed Karate before the hit; the finisher must not promise a kill.");
            await CardCmd.AutoPlay(Choice, card, combat.Enemy);
            Require(combat.Enemy.CurrentHp == 2 && owner.GetPowerAmount<NarakuLifePower>() == 2,
                "Thorns must reduce five life to three before the three-damage Karate trigger spends one more life.");
        }
        GD.Print("PASS One Mind: 7/10 life, 3-cost Ethereal, one/two/three conversion layers, 10->9 and 15->13 display/damage, one-life multihit/AOE spending, real-stack card isolation, dynamic gains/losses/removal, Guard Stance, teammate isolation, descending multi-hit forecasts and Thorns.");
    }
}
