using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.RedesignV1;
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
            await CardCmd.AutoPlay(Choice, AddCard<ShurikenDraw>(combat), null);
            await CardCmd.AutoPlay(Choice, AddCard<ShurikenDraw>(combat, upgraded: true), null);
            await PowerCmd.Apply<StarlessNightRedesignPower>(Choice, owner, 1, owner, null);
            await PowerCmd.Apply<FocusPower>(Choice, owner, 2, owner, null);
            var shield = AddCard<ShurikenGenerationRedesignV1>(combat);
            Require(!shield.ShouldGlowGold, "Barrier starts without a gain this turn.");
            await AddStock(combat.Player, 3);
            Require(PileType.Hand.GetPile(combat.Player).Cards.OfType<Wound>().Count() == 3 && combat.Tokens == 1
                && combat.Stock == 3 && shield.ShouldGlowGold, "One three-layer grant draws three from stacked powers and generates one token.");
            await AddStock(combat.Player, 2);
            Require(PileType.Hand.GetPile(combat.Player).Cards.OfType<Wound>().Count() == 6 && combat.Tokens == 2,
                "Replenishment triggers again once, not once per layer.");
            await AddStock(combat.Player, 0);
            Require(combat.Tokens == 2, "Zero stock grants must not trigger.");
            Require(combat.Player.Piles.SelectMany(p => p.Cards).OfType<StrongShurikenTokenRedesignV1>()
                .All(card => card.SnapshotDamage == 8), "Every token snapshots Focus at gain time.");
            await CardCmd.Discard(Choice, PileType.Hand.GetPile(combat.Player).Cards.OfType<Wound>().Take(5).ToArray());
            Require(combat.Stock == 0 && combat.Tokens == 2 && shield.ShouldGlowGold, "Discard does not generate tokens or erase the turn's gain.");
            combat.State.RoundNumber++;
            combat.Player.PlayerCombatState!.IncrementTurnNumber();
            Require(!shield.ShouldGlowGold, "Gain glow expires on turn change.");
        }
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var existing = AddCard<DefendIronclad>(combat);
            var drawn = Enumerable.Range(0, upgraded ? 3 : 2).Select(_ => AddCard<DefendIronclad>(combat, PileType.Draw)).ToArray();
            var untouched = AddCard<Wound>(combat, PileType.Draw);
            await CardCmd.AutoPlay(Choice, AddCard<TechniqueSearchRedesignV1>(combat, upgraded: upgraded), null);
            Require(drawn.All(card => card.Pile?.Type == PileType.Hand && card.Keywords.Contains(CardKeyword.Sly))
                && !existing.Keywords.Contains(CardKeyword.Sly) && !untouched.Keywords.Contains(CardKeyword.Sly),
                "Insight grants Sly only to directly drawn cards.");
            drawn[0].EndOfTurnCleanup();
            Require(((CardModel)drawn[0].MutableClone()).Keywords.Contains(CardKeyword.Sly), "Granted Sly survives turn cleanup and native copy.");
            await CardCmd.Discard(Choice, drawn);
            Require(combat.Player.Creature.Block == drawn.Length * 5, "Insight's native Sly cards play on discard.");
        }
        using (var combat = new OrbCombat())
        {
            await PowerCmd.Apply<StatusDrawPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
            var directStatus = AddCard<Wound>(combat, PileType.Draw);
            var bonus = AddCard<DefendIronclad>(combat, PileType.Draw);
            var directSecond = AddCard<StrikeIronclad>(combat, PileType.Draw);
            await CardCmd.AutoPlay(Choice, AddCard<TechniqueSearchRedesignV1>(combat), null);
            Require(directStatus.Keywords.Contains(CardKeyword.Sly) && directSecond.Keywords.Contains(CardKeyword.Sly)
                && bonus.Pile?.Type == PileType.Hand && !bonus.Keywords.Contains(CardKeyword.Sly),
                "Insight excludes cards drawn by a status-draw callback from its granted Sly.");
        }
        foreach (bool starlessFirst in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var owner = combat.Player.Creature;
            if (starlessFirst) await PowerCmd.Apply<StarlessNightRedesignPower>(Choice, owner, 1, owner, null);
            await PowerCmd.Apply<ShurikenDrawPower>(Choice, owner, 2, owner, null);
            if (!starlessFirst) await PowerCmd.Apply<StarlessNightRedesignPower>(Choice, owner, 1, owner, null);
            for (int i = 0; i < 9; i++) AddCard<Wound>(combat);
            var draw = AddCard<DefendIronclad>(combat, PileType.Draw);
            await AddStock(combat.Player, 2);
            var token = combat.Player.Piles.SelectMany(p => p.Cards).OfType<StrongShurikenTokenRedesignV1>().Single();
            Require(token.Pile?.Type == (starlessFirst ? PileType.Hand : PileType.Discard)
                && draw.Pile?.Type == (starlessFirst ? PileType.Draw : PileType.Hand),
                "Stock gains resolve powers in owner order and use native full-hand handling.");
        }
        foreach (bool upgraded in new[] { false, true })
        foreach (bool attack in new[] { false, true })
        {
            using var combat = new OrbCombat();
            CardModel top = attack ? AddCard<CommonChopRedesignV1>(combat, PileType.Draw) : AddCard<DefendIronclad>(combat, PileType.Draw);
            var hammer = AddCard<WasshoiRedesignV1>(combat, upgraded: upgraded);
            await CardCmd.AutoPlay(Choice, hammer, null);
            int count = upgraded ? 3 : 2;
            Require(top.Pile?.Type == PileType.Exhaust && hammer.Pile?.Type == PileType.Exhaust,
                "Hammer exhausts both itself and its fully resolved target, including a returning Chop.");
            if (attack) Require(combat.Enemy.CurrentHp == 1000 - 3 * count - (count - 1), "Hammer completes every repeat before exhaust.");
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
            var relic = ModelDb.Relic<NarakuWithinRelic>().ToMutable();
            combat.Player.AddRelicInternal(relic);
            await relic.BeforeCombatStart();
            Require(NinjaSlayerFormState.IsFullyReleasedNaraku(combat.Player.Creature)
                && !combat.Player.Creature.HasPower<NarakuFormRedesignPower>(), "Full form does not grant the card's damage trigger.");
            await CardCmd.AutoPlay(Choice, combat.Card(), combat.Enemy);
            Require(combat.Enemy.CurrentHp == 994, "Full relic alone adds no attack damage before generating a flame.");
            await Hook.AfterPlayerTurnStart(combat.State, Choice, combat.Player);
            Require(PileType.Hand.GetPile(combat.Player).Cards.OfType<BlackFlameRedesignV1>().Count() == 1, "Full form generates one flame each owner turn.");
            await CardCmd.AutoPlay(Choice, AddCard<NarakuFormRedesignV1>(combat), null);
            await CardCmd.AutoPlay(Choice, combat.Card(), combat.Enemy);
            Require(combat.Enemy.CurrentHp == 980, "Ordinary form and the relic-generated hand flame each trigger once.");
        }
        using (var combat = new OrbCombat())
        {
            var bag = ModelDb.Relic<PortableIrcTerminalRelic>().ToMutable();
            combat.Player.AddRelicInternal(bag);
            for (int turn = 0; turn < 3; turn++)
            {
                await bag.BeforeHandDraw(combat.Player, Choice, combat.State);
                combat.Player.PlayerCombatState!.IncrementTurnNumber();
            }
            Require(combat.Stock == 3, "Tool bag grants stock every turn, not just the first.");
            var blanket = ModelDb.Relic<BlanketRelic>().ToMutable();
            combat.Player.AddRelicInternal(blanket);
            await blanket.AfterPlayerTurnStart(Choice, combat.Player);
            Require(combat.Player.Creature.GetPowerAmount<NarakuLifePower>() == 2, "Blanket grants two Naraku Life.");
        }
        GD.Print("PASS board v1.15 stock-gain powers/glow, native Sly, Hammer exhaust, random Vigor, full-form separation and relic turns");
    }
}
