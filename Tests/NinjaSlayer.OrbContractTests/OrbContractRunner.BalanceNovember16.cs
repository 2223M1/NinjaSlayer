using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Unlocks;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using NinjaSlayer.Relics;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyNovember16Balance()
    {
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var chop = AddCard<Chop>(combat, upgraded: upgraded);
            await CardCmd.AutoPlay(Choice, AddCard<CrossGuard>(combat), null);
            await CardCmd.AutoPlay(Choice, AddCard<Composure>(combat), null);
            await CardCmd.AutoPlay(Choice, chop, combat.Enemy);
            Require(chop.Pile?.Type == PileType.Hand && !combat.Player.Creature.HasPower<CrossGuardPower>(),
                "Cross Guard survives a Power and returns Chop/Chop+ to hand instead of draw top.");
            await CardCmd.AutoPlay(Choice, chop, combat.Enemy);
            Require(chop.Pile?.Type == PileType.Draw && PileType.Draw.GetPile(combat.Player).Cards[0] == chop,
                "The following Chop play restores its normal draw-top destination.");
        }
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var owner = combat.Player.Creature;
            await CardCmd.AutoPlay(Choice, AddCard<ChopStrike>(combat, upgraded: upgraded), combat.Enemy);
            Require(combat.Enemy.CurrentHp == 1000 - (upgraded ? 12 : 9) && !owner.HasPower<KaratePower>(),
                "Chop Strike deals 9/12 damage and delays its Karate until next turn.");
            await Hook.BeforeSideTurnStart(combat.State, CombatSide.Player, [owner]);
            Require(owner.GetPowerAmount<KaratePower>() == (upgraded ? 4 : 3) && !owner.HasPower<ChopStrikePower>(),
                "Chop Strike grants 3/4 Karate once at the next turn start.");
        }
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var relentless = AddCard<Relentless>(combat, upgraded: upgraded);
            Require(relentless.Keywords.Contains(CardKeyword.Retain) == upgraded,
                "Only upgraded Relentless has Retain.");
            var first = AddCard<DefendIronclad>(combat, PileType.Draw);
            var second = AddCard<DefendIronclad>(combat, PileType.Draw);
            await CardCmd.AutoPlay(Choice, relentless, null);
            await combat.Player.Creature.GetPower<RelentlessPower>()!.BeforeSideTurnEnd(Choice,
                CombatSide.Player, [combat.Player.Creature]);
            Require(first.Pile?.Type == PileType.Discard && second.Pile?.Type == PileType.Draw
                && combat.Player.Creature.Block == 5,
                "Both Relentless versions play exactly one top card at turn end.");
        }
        foreach (bool upgraded in new[] { false, true })
        foreach (CardType type in new[] { CardType.Attack, CardType.Skill, CardType.Power, CardType.Status })
        {
            using var combat = new OrbCombat();
            await CardCmd.AutoPlay(Choice, AddCard<TurningPoint>(combat, upgraded: upgraded), null);
            var draws = Enumerable.Range(0, 3).Select(_ => AddCard<DefendIronclad>(combat, PileType.Draw)).ToArray();
            CardModel generated = type switch
            {
                CardType.Attack => combat.State.CreateCard<StrikeIronclad>(combat.Player),
                CardType.Skill => combat.State.CreateCard<DefendIronclad>(combat.Player),
                CardType.Power => combat.State.CreateCard<Inflame>(combat.Player),
                _ => combat.State.CreateCard<Wound>(combat.Player)
            };
            await CardPileCmd.AddGeneratedCardToCombat(generated, PileType.Discard, combat.Player);
            Require(draws.Count(card => card.Pile?.Type == PileType.Hand) == (upgraded ? 2 : 1),
                $"Generating {type} triggers Turning Point's 1/2 draws even outside hand.");
            await CardPileCmd.Add(generated, PileType.Hand);
            await CardPileCmd.AddGeneratedCardToCombat(combat.State.CreateCard<Wound>(combat.Player), PileType.Discard, null);
            Require(draws.Count(card => card.Pile?.Type == PileType.Hand) == (upgraded ? 2 : 1),
                "Moving an existing card and generation without an owner do not trigger Turning Point.");
        }
        using (var combat = new OrbCombat())
        {
            var attack = AddCard<StrikeIronclad>(combat);
            string attackDescription = attack.GetDescriptionForPile(PileType.Hand, combat.Enemy);
            await CardPileCmd.Add(attack, PileType.Draw);
            var relic = ModelDb.Relic<NarakuUnleashedRelic>().ToMutable();
            combat.Player.AddRelicInternal(relic);
            var owner = combat.Player.Creature;
            var teammate = Player.CreateForNewRun<Ironclad>(UnlockState.all, 2);
            combat.State.AddPlayer(teammate);
            teammate.ResetCombatState();
            var otherAttack = combat.State.CreateCard<StrikeIronclad>(teammate);
            var otherSkill = combat.State.CreateCard<DefendIronclad>(teammate);
            await CardPileCmd.Add(otherAttack, PileType.Hand);
            await CardPileCmd.Add(otherSkill, PileType.Hand);
            await relic.BeforeCombatStart();
            Require(owner.GetPowerAmount<NarakuLifePower>() == 20 && !teammate.Creature.HasPower<NarakuLifePower>(),
                "Naraku Unleashed grants its owner exactly 20 life at combat start.");
            var skill = AddCard<DefendIronclad>(combat);
            var power = AddCard<Inflame>(combat);
            Require(Hook.ShouldPlay(combat.State, skill, out _, AutoPlayType.None), "Without hand attacks, Skills remain playable.");
            await CardPileCmd.Add(attack, PileType.Hand);
            var generated = combat.State.CreateCard<Chop>(combat.Player);
            await CardPileCmd.AddGeneratedCardToCombat(generated, PileType.Hand, combat.Player);
            Require(new CardModel[] { attack, generated, otherAttack, skill }.All(card =>
                    card.Keywords.ToHashSet().SetEquals(ModelDb.GetById<CardModel>(card.Id).Keywords))
                && new CardModel[] { attack, generated }.All(card =>
                    Hook.ShouldPlay(combat.State, card, out _, AutoPlayType.None))
                && attack.GetDescriptionForPile(PileType.Hand, combat.Enemy) == attackDescription,
                "Naraku Unleashed preserves card descriptions and keywords; existing and generated attacks can be played in either order.");
            Require(!Hook.ShouldPlay(combat.State, skill, out var preventer, AutoPlayType.None) && preventer == relic
                && !Hook.ShouldPlay(combat.State, power, out _, AutoPlayType.None)
                && Hook.ShouldPlay(combat.State, skill, out _, AutoPlayType.Default)
                && Hook.ShouldPlay(combat.State, otherSkill, out _, AutoPlayType.None),
                "Hand attacks block manual Skills/Powers only; autoplay and teammates remain unaffected.");
            var curse = AddCard<Enthralled>(combat);
            Require(Hook.ShouldPlay(combat.State, curse, out _, AutoPlayType.None)
                && !Hook.ShouldPlay(combat.State, attack, out _, AutoPlayType.None),
                "The native Enthralled curse retains priority without creating a deadlock with the relic.");
            await CardPileCmd.Add(curse, PileType.Discard);
            await CardPileCmd.Add(attack, PileType.Draw);
            await CardPileCmd.Add(generated, PileType.Discard);
            Require(Hook.ShouldPlay(combat.State, skill, out _, AutoPlayType.None),
                "Attacks outside hand do not block Skills.");
            await relic.AfterPlayerTurnStart(Choice, teammate);
            Require(!PileType.Hand.GetPile(combat.Player).Cards.OfType<BlackFlame>().Any(), "Teammate turns do not create flames.");
            await relic.AfterPlayerTurnStart(Choice, combat.Player);
            await relic.AfterPlayerTurnStart(Choice, combat.Player);
            Require(owner.GetPowerAmount<NarakuLifePower>() == 20
                && PileType.Hand.GetPile(combat.Player).Cards.OfType<BlackFlame>().Count() == 2,
                "Each owner turn adds one Black Flame without granting the starting life again.");
        }
        GD.Print("PASS November 16 balance: Cross Guard/Chop, delayed Chop Strike, Relentless, all generated card types and Naraku Unleashed life/attack priority/unchanged keywords/turns/ownership.");
    }
}
