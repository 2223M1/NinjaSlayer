using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using NinjaSlayer.Relics;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyBoardV115()
    {
        foreach (bool ninja in new[] { false, true })
        {
            using var combat = new OrbCombat(ninjaSlayer: ninja);
            var owner = combat.Player.Creature;
            for (int i = 0; i < 10; i++) AddCard<Wound>(combat, PileType.Draw);
            await PowerCmd.Apply<StarlessNightPower>(Choice, owner, 1, owner, null);
            await PowerCmd.Apply<FocusPower>(Choice, owner, 2, owner, null);
            var shield = AddCard<BladeBarrier>(combat);
            Require(!shield.ShouldGlowGold, "Barrier starts without a gain this turn.");
            await AddStock(combat.Player, 3);
            Require(PileType.Hand.GetPile(combat.Player).Cards.OfType<Wound>().Count() == 0 && combat.Tokens == 1
                && combat.Stock == 3 && shield.ShouldGlowGold, "Stock gain generates one token without drawing.");
            await AddStock(combat.Player, 2);
            Require(PileType.Hand.GetPile(combat.Player).Cards.OfType<Wound>().Count() == 0 && combat.Tokens == 2,
                "Replenishment generates one token without drawing.");
            await AddStock(combat.Player, 0);
            Require(combat.Tokens == 2, "Zero stock grants must not trigger.");
            Require(combat.Player.Piles.SelectMany(p => p.Cards).OfType<StrongShuriken>()
                .All(card => card.SnapshotDamage == 8), "Every token snapshots Focus at gain time.");
            for (int i = 0; i < 5; i++) await CardCmd.Discard(Choice, AddCard<Wound>(combat));
            Require(combat.Stock == 0 && combat.Tokens == 2 && shield.ShouldGlowGold, "Discard does not generate tokens or erase the turn's gain.");
            combat.State.RoundNumber++;
            combat.Player.PlayerCombatState!.IncrementTurnNumber();
            Require(!shield.ShouldGlowGold, "Gain glow expires on turn change.");
        }
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var existing = AddCard<DefendIronclad>(combat);
            var drawn = Enumerable.Range(0, upgraded ? 4 : 3).Select(_ => AddCard<DefendIronclad>(combat, PileType.Draw)).ToArray();
            var untouched = AddCard<Wound>(combat, PileType.Draw);
            await CardCmd.AutoPlay(Choice, AddCard<Adapt>(combat, upgraded: upgraded), null);
            Require(drawn.All(card => card.Pile?.Type == PileType.Hand && !card.Keywords.Contains(CardKeyword.Sly))
                && !existing.Keywords.Contains(CardKeyword.Sly) && !untouched.Keywords.Contains(CardKeyword.Sly),
                "Adapt draws three/four cards without granting Sly.");
            await CardCmd.AutoPlay(Choice, drawn[0], null);
            Require(drawn[0].Pile?.Type == PileType.Draw && PileType.Draw.GetPile(combat.Player).Cards[0] == drawn[0]
                && !combat.Player.Creature.HasPower<ReboundPower>(), "Adapt rebounds the next played card exactly once.");
            await CardCmd.AutoPlay(Choice, existing, null);
            Require(existing.Pile?.Type == PileType.Discard, "Adapt must not rebound a second card.");
        }
        using (var combat = new OrbCombat())
        {
            await PowerCmd.Apply<ResiliencePower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
            var directStatus = AddCard<Wound>(combat, PileType.Draw);
            var bonus = AddCard<DefendIronclad>(combat, PileType.Draw);
            var directSecond = AddCard<StrikeIronclad>(combat, PileType.Draw);
            await CardCmd.AutoPlay(Choice, AddCard<Adapt>(combat), null);
            Require(new CardModel[] { directStatus, directSecond, bonus }.All(card =>
                    card.Pile?.Type == PileType.Hand && !card.Keywords.Contains(CardKeyword.Sly)),
                "Drawing an existing Status does not trigger Resilience or grant Sly through Adapt.");
        }
        {
            using var combat = new OrbCombat();
            var owner = combat.Player.Creature;
            await PowerCmd.Apply<StarlessNightPower>(Choice, owner, 1, owner, null);
            for (int i = 0; i < 9; i++) AddCard<Wound>(combat);
            var draw = AddCard<DefendIronclad>(combat, PileType.Draw);
            await AddStock(combat.Player, 2);
            var token = combat.Player.Piles.SelectMany(p => p.Cards).OfType<StrongShuriken>().Single();
            Require(token.Pile?.Type == PileType.Hand && draw.Pile?.Type == PileType.Draw,
                "Stock gains use native full-hand handling.");
        }
        foreach (bool upgraded in new[] { false, true })
        foreach (bool attack in new[] { false, true })
        {
            using var combat = new OrbCombat();
            CardModel top = attack ? AddCard<Chop>(combat, PileType.Draw) : AddCard<DefendIronclad>(combat, PileType.Draw);
            var hammer = AddCard<NavyHammer>(combat, upgraded: upgraded);
            await CardCmd.AutoPlay(Choice, hammer, null);
            int count = upgraded ? 3 : 2;
            Require(top.Pile?.Type == PileType.Exhaust && hammer.Pile?.Type == PileType.Exhaust,
                "Hammer exhausts both itself and its fully resolved target, including a returning Chop.");
            if (attack) Require(combat.Enemy.CurrentHp == 1000 - 4 * count - (count - 1), "Hammer completes every repeat before exhaust.");
            else Require(combat.Player.Creature.Block == 5, "A non-attack is played only once.");
        }
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var other = combat.AddEnemy();
            await PowerCmd.Apply<VigorPower>(Choice, combat.Player.Creature, 7, combat.Player.Creature, null);
            var fist = AddCard<AntiAirBangBangFist>(combat, upgraded: upgraded);
            await CardCmd.AutoPlay(Choice, fist, null);
            Require(2000 - combat.Enemy.CurrentHp - other.CurrentHp == 2 * (7 + (upgraded ? 11 : 8))
                && !combat.Player.Creature.HasPower<VigorPower>(), "Random Anti-Air uses Vigor for the entire two-hit attack.");
        }
        using (var combat = new OrbCombat(ninjaSlayer: true))
        {
            var relic = ModelDb.Relic<NarakuUnleashedRelic>().ToMutable();
            combat.Player.AddRelicInternal(relic);
            await relic.BeforeCombatStart();
            Require(NinjaSlayerFormState.IsFullyReleasedNaraku(combat.Player.Creature)
                && !combat.Player.Creature.HasPower<NarakuFormPower>(), "Full form does not grant the card's damage trigger.");
            await CardCmd.AutoPlay(Choice, combat.Card(), combat.Enemy);
            Require(combat.Enemy.CurrentHp == 994, "Full relic alone adds no attack damage before generating a flame.");
            await Hook.AfterPlayerTurnStart(combat.State, Choice, combat.Player);
            Require(PileType.Hand.GetPile(combat.Player).Cards.OfType<BlackFlame>().Count() == 1, "Full form generates one flame each owner turn.");
            await CardCmd.AutoPlay(Choice, AddCard<NarakuForm>(combat), null);
            await CardCmd.AutoPlay(Choice, combat.Card(), combat.Enemy);
            Require(combat.Enemy.CurrentHp == 980, "Ordinary form and the relic-generated hand flame each trigger once.");
        }
        using (var combat = new OrbCombat())
        {
            var bag = ModelDb.Relic<DogushaToolPouchRelic>().ToMutable();
            combat.Player.AddRelicInternal(bag);
            for (int turn = 0; turn < 3; turn++)
            {
                await bag.BeforeHandDraw(combat.Player, Choice, combat.State);
                combat.Player.PlayerCombatState!.IncrementTurnNumber();
            }
            Require(combat.Stock == 3, "Tool bag grants stock every turn, not just the first.");
            var blanket = ModelDb.Relic<MentalBlanketRelic>().ToMutable();
            combat.Player.AddRelicInternal(blanket);
            await blanket.AfterPlayerTurnStart(Choice, combat.Player);
            Require(combat.Player.Creature.GetPowerAmount<NarakuLifePower>() == 2, "Blanket grants two Naraku Life.");
        }
        GD.Print("PASS board v1.15 stock-gain powers/glow, native Sly, Hammer exhaust, random Vigor, full-form separation and relic turns");
    }
}
