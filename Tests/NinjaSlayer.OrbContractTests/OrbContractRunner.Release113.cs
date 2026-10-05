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
    private static async Task VerifyRelease113Cards()
    {
        foreach (bool upgraded in new[] { false, true })
        foreach (int stock in new[] { 0, 1, 3 })
        {
            using var combat = new OrbCombat();
            var jump = AddCard<SpiralJump>(combat, upgraded: upgraded);
            await PowerCmd.Apply<FocusPower>(Choice, combat.Player.Creature, 2, combat.Player.Creature, null);
            if (stock > 0) await AddStock(combat.Player, stock);
            await CardCmd.AutoPlay(Choice, jump, null);
            Require(combat.Enemy.CurrentHp == 1000 - (upgraded ? 12 : 8) - stock * 8 && combat.Stock == 0,
                "Spiral Jump must attack before consuming the existing stock once; empty stock is not replenished.");
            var moon = AddCard<Moonsault>(combat, upgraded: upgraded);
            Require(moon.EnergyCost.GetWithModifiers(CostModifiers.Local) == (upgraded ? 0 : 1) && moon.Keywords.Contains(CardKeyword.Retain),
                "Moonsault costs one/zero energy and retains in both versions.");
            if (stock > 0) await AddStock(combat.Player, stock);
            int hp = combat.Enemy.CurrentHp;
            await CardCmd.AutoPlay(Choice, moon, null);
            Require(combat.Enemy.CurrentHp == hp - stock * 16 && combat.Stock == 0,
                "Moonsault must fire every current layer twice and clear stock without replenishing.");
        }
        foreach (bool upgraded in new[] { false, true })
        foreach (PileType destination in new[] { PileType.Hand, PileType.Draw, PileType.Discard })
        {
            using var combat = new OrbCombat();
            await CardCmd.AutoPlay(Choice, AddCard<Resilience>(combat, upgraded: upgraded), null);
            await CardCmd.AutoPlay(Choice, AddCard<Resilience>(combat), null);
            await PowerCmd.Apply<DevourFlamePower>(Choice, combat.Player.Creature, 2, combat.Player.Creature, null);
            int draw = upgraded ? 3 : 2;
            for (int i = 0; i < draw * 3 + 2; i++) AddCard<StrikeIronclad>(combat, PileType.Draw);
            foreach (CardModel status in new CardModel[] { combat.State.CreateCard<Chado>(combat.Player),
                         combat.State.CreateCard<BlackFlame>(combat.Player), combat.State.CreateCard<Wound>(combat.Player) })
            {
                await CardPileCmd.AddGeneratedCardToCombat(status, destination, combat.Player);
                Require(PileType.Hand.GetPile(combat.Player).Cards.OfType<StrikeIronclad>().Count() == draw,
                    $"Resilience must draw {draw} for one generated Status in {destination} (upgraded={upgraded}).");
                foreach (var old in PileType.Hand.GetPile(combat.Player).Cards.ToArray())
                    await CardPileCmd.RemoveFromCombat(old);
            }
            Require(combat.Player.Creature.GetPowerAmount<NarakuLifePower>() == 6,
                "Devour Flame must remain independent of Resilience's generated-status draw.");
        }
        using (var combat = new OrbCombat())
        {
            await PowerCmd.Apply<ResiliencePower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
            for (int i = 0; i < 5; i++) AddCard<StrikeIronclad>(combat, PileType.Draw);
            var other = Player.CreateForNewRun<Ironclad>(UnlockState.all, 2);
            other.InitializeSeed("resilience-other");
            combat.State.AddPlayer(other);
            other.ResetCombatState();
            foreach (Player? creator in new Player?[] { other, null })
                await CardPileCmd.AddGeneratedCardToCombat(combat.State.CreateCard<Wound>(combat.Player), PileType.Discard, creator);
            var oldStatus = AddCard<Wound>(combat, PileType.Discard);
            await CardPileCmd.Add(oldStatus, PileType.Hand);
            await NinjaSlayerCardCmd.AddGeneratedCard<DefendIronclad>(combat.Player, PileType.Discard);
            Require(PileType.Hand.GetPile(combat.Player).Cards.Count == 1,
                "Other/null creators, existing Status movement and non-Status generation must not draw.");
            while (PileType.Hand.GetPile(combat.Player).Cards.Count < CardPile.MaxCardsInHand) AddCard<DefendIronclad>(combat);
            int drawCount = PileType.Draw.GetPile(combat.Player).Cards.Count;
            await NinjaSlayerCardCmd.AddGeneratedCard<Wound>(combat.Player, PileType.Hand);
            Require(PileType.Draw.GetPile(combat.Player).Cards.Count == drawCount
                && PileType.Discard.GetPile(combat.Player).Cards.OfType<Wound>().Any(),
                "Full-hand generation retains native overflow and cannot overdraw.");
            other.PlayerCombatState!.AfterCombatEnd();
        }
        GD.Print("PASS 1.0.13 cards: Spiral eight/twelve and one volley, Moonsault one/zero retained double volley, generated-status draw, stacking, creator isolation and full hand.");
    }
}
