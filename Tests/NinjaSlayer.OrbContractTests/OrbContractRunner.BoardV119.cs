using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Unlocks;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyBoardV119()
    {
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var owner = combat.Player.Creature;
            await CardCmd.AutoPlay(Choice, AddCard<DevourFlame>(combat, upgraded: upgraded), null);
            await CardCmd.AutoPlay(Choice, AddCard<DevourFlame>(combat), null);
            int amount = upgraded ? 5 : 4;
            foreach (var pile in new[] { PileType.Hand, PileType.Draw, PileType.Discard })
            {
                await NinjaSlayerCardCmd.AddGeneratedCard<BlackFlame>(combat.Player, pile);
                await NinjaSlayerCardCmd.AddGeneratedCard<Chado>(combat.Player, pile);
                await NinjaSlayerCardCmd.AddGeneratedCard<Wound>(combat.Player, pile);
            }
            Require(owner.GetPowerAmount<NarakuLifePower>() == 9 * amount,
                "Devour Flame must stack and trigger for each generated Status in every destination pile.");
            await NinjaSlayerCardCmd.AddGeneratedCard<StrikeIronclad>(combat.Player, PileType.Hand);
            var existing = AddCard<Wound>(combat, PileType.Draw);
            await CardPileCmd.Add(existing, PileType.Hand);
            await CardCmd.Discard(Choice, existing);
            await CardCmd.Exhaust(Choice, existing);
            var other = Player.CreateForNewRun(ModelDb.Character<Ironclad>(), UnlockState.all, 2);
            other.InitializeSeed("board119-other");
            combat.State.AddPlayer(other);
            other.ResetCombatState();
            await CardPileCmd.AddGeneratedCardToCombat(combat.State.CreateCard<Wound>(combat.Player), PileType.Discard, other);
            await CardPileCmd.AddGeneratedCardToCombat(combat.State.CreateCard<Wound>(combat.Player), PileType.Discard, null);
            Require(owner.GetPowerAmount<NarakuLifePower>() == 9 * amount && !owner.HasPower<StrengthPower>(),
                "Non-status generation, pile movement, exhaustion and other/null creators must not grant Devour Flame rewards.");
            while (PileType.Hand.GetPile(combat.Player).Cards.Count < CardPile.MaxCardsInHand) AddCard<Wound>(combat);
            await NinjaSlayerCardCmd.AddGeneratedCard<Wound>(combat.Player, PileType.Hand);
            Require(owner.GetPowerAmount<NarakuLifePower>() == 10 * amount,
                "Full-hand overflow must retain one native generation reward.");
            other.PlayerCombatState!.AfterCombatEnd();
        }
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var attack = AddCard<PressTheAttack>(combat, upgraded: upgraded);
            await PowerCmd.Apply<VigorPower>(Choice, combat.Player.Creature, 7, combat.Player.Creature, null);
            await PowerCmd.Apply<StrengthPower>(Choice, combat.Player.Creature, 3, combat.Player.Creature, null);
            await CardCmd.AutoPlay(Choice, attack, combat.Enemy);
            Require(combat.Enemy.CurrentHp == 1000 - (upgraded ? 20 : 17)
                && !combat.Player.Creature.HasPower<VigorPower>(),
                "Press the Attack must apply Strength and Vigor to one hit, consuming Vigor once.");
        }
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            await CardCmd.AutoPlay(Choice, AddCard<LeftUppercut>(combat, upgraded: upgraded), combat.Enemy);
            Require(combat.Enemy.GetPowerAmount<VulnerablePower>() == (upgraded ? 2 : 1),
                "Left Uppercut must apply Vulnerable without a previous Attack.");
            for (int i = 0; i < 4; i++) AddCard<StrikeIronclad>(combat, PileType.Draw);
            await CardCmd.AutoPlay(Choice, AddCard<Kindle>(combat, upgraded: upgraded), null);
            Require(PileType.Hand.GetPile(combat.Player).Cards.OfType<StrikeIronclad>().Count() == (upgraded ? 3 : 2)
                && PileType.Discard.GetPile(combat.Player).Cards.OfType<BlackFlame>().Count() == 1
                && !PileType.Draw.GetPile(combat.Player).Cards.OfType<BlackFlame>().Any(),
                "Kindle must draw two/three cards before adding exactly one Black Flame to discard.");
        }
        using (var combat = new OrbCombat())
        {
            using var selector = CardSelectCmd.UseSelector(new SelectCards(options => options.OfType<DefendIronclad>().Take(1)));
            foreach (bool upgraded in new[] { false, true })
            {
                AddCard<DefendIronclad>(combat);
                await CardCmd.AutoPlay(Choice, AddCard<Rekindle>(combat, upgraded: upgraded), null);
                Require(!combat.Player.Creature.HasPower<NarakuLifePower>(),
                    "Neither the first nor a subsequent Rekindle may trigger its own Skill reward.");
            }
            await CardCmd.AutoPlay(Choice, AddCard<DefendIronclad>(combat), null);
            Require(combat.Player.Creature.GetPowerAmount<NarakuLifePower>() == 7,
                "A subsequent Skill must receive both base and upgraded Rekindle rewards.");
        }
        GD.Print("PASS v1.19: status generation, stacked Naraku Life, creator isolation, full hand, single-hit Vigor, unconditional Uppercut and Kindle destination.");
    }
}
