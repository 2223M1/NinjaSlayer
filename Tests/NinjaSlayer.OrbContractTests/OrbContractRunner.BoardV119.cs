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
            for (int i = 0; i < 4; i++) AddCard<StrikeIronclad>(combat, PileType.Draw);
            await CardCmd.AutoPlay(Choice, AddCard<Kindle>(combat, upgraded: upgraded), null);
            Require(PileType.Hand.GetPile(combat.Player).Cards.OfType<StrikeIronclad>().Count() == (upgraded ? 3 : 2)
                && PileType.Draw.GetPile(combat.Player).Cards.OfType<BlackFlame>().Count() == 1
                && !PileType.Discard.GetPile(combat.Player).Cards.OfType<BlackFlame>().Any(),
                "Kindle must draw two/three cards before adding exactly one Black Flame to draw.");
        }
        using (var combat = new OrbCombat())
        {
            using var selector = CardSelectCmd.UseSelector(new SelectCards(options => options.OfType<DefendIronclad>().Take(1)));
            foreach (bool upgraded in new[] { false, true })
            {
                AddCard<DefendIronclad>(combat);
                await CardCmd.AutoPlay(Choice, AddCard<Rekindle>(combat, upgraded: upgraded), null);
                Require(!combat.Player.Creature.HasPower<NarakuLifePower>(),
                    "Playing Rekindle and transforming a card must not grant Naraku Life by itself.");
            }
            await CardCmd.AutoPlay(Choice, AddCard<DefendIronclad>(combat), null);
            Require(!combat.Player.Creature.HasPower<NarakuLifePower>(),
                "Playing a Skill must not trigger Rekindle.");
            await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), combat.Enemy);
            Require(combat.Player.Creature.GetPowerAmount<NarakuLifePower>() == 10,
                "Each of the two transformed flames must grant the stacked base and upgraded reward of five.");
        }
        GD.Print("PASS v1.19: single-hit Vigor and Kindle destination.");
    }
}
