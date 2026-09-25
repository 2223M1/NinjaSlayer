using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyBalanceV0217()
    {
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            await PowerCmd.Apply<VigorPower>(Choice, combat.Player.Creature, 7, combat.Player.Creature, null);
            await CardCmd.AutoPlay(Choice, AddCard<PalmThrustRedesignV1>(combat, upgraded: upgraded), null);
            Require(combat.Enemy.CurrentHp == 1000 - 12 * (upgraded ? 3 : 2)
                && !combat.Player.Creature.HasPower<VigorPower>(), "Palm Thrust must spend Vigor after all random hits.");
        }
        using (var combat = new OrbCombat())
        {
            var owner = combat.Player.Creature;
            await CardCmd.AutoPlay(Choice, AddCard<NinjaSlayer.Cards.OneBodyOneSoul>(combat), null);
            await CardCmd.AutoPlay(Choice, AddCard<NinjaSlayer.Cards.OneBodyOneSoul>(combat, upgraded: true), null);
            await PowerCmd.Apply<DexterityPower>(Choice, owner, 100, owner, null);
            await PowerCmd.Apply<FrailPower>(Choice, owner, 2, combat.Enemy, null);
            await PowerCmd.Apply<KaratePower>(Choice, owner, 4, owner, null);
            await CreatureCmd.Damage(Choice, new[] { combat.Enemy, combat.AddEnemy() }, 1, ValueProp.Move, owner);
            Require(owner.GetPowerAmount<NarakuLifePower>() == 7 && owner.GetPowerAmount<KaratePower>() == 3,
                "One Body grants seven Naraku Life once for an AOE Karate wave.");
            await CardCmd.AutoPlay(Choice, AddCard<PalmThrustRedesignV1>(combat, upgraded: true), null);
            Require(owner.GetPowerAmount<NarakuLifePower>() == 28 && !owner.HasPower<KaratePower>(), "Each multihit Karate wave grants Naraku Life separately.");
        }
        using (var combat = new OrbCombat())
        {
            var owner = combat.Player.Creature;
            await CardCmd.AutoPlay(Choice, AddCard<FlyingBladeDanceRedesignV1>(combat), null);
            await PowerCmd.Apply<DexterityPower>(Choice, owner, 100, owner, null);
            await PowerCmd.Apply<FrailPower>(Choice, owner, 2, combat.Enemy, null);
            await CardCmd.Discard(Choice, new[] { combat.Card(), combat.Card() });
            Require(owner.Block == 4, "Composure must ignore Dexterity and Frail like Feel No Pain.");
            await CardCmd.AutoPlay(Choice, AddCard<FlyingBladeDanceRedesignV1>(combat, upgraded: true), null);
            await CardCmd.Discard(Choice, new[] { combat.Card(), combat.Card() });
            Require(owner.Block == 12, "Base and upgraded Composure stack the same two unpowered block per discard.");
        }
        using (var combat = new OrbCombat())
        {
            await PowerCmd.Apply<StarlessNightRedesignPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
            await PowerCmd.Apply<FocusPower>(Choice, combat.Player.Creature, 2, combat.Player.Creature, null);
            await AddStock(combat.Player, 5);
            await CardCmd.Discard(Choice, new[] { combat.Card(), combat.Card(), combat.Card() });
            Require(combat.Tokens == 1 && combat.Enemy.CurrentHp == 976 && combat.Stock == 2,
                "A stock gain creates one token; three discards damage three times without generating more.");
            await CardCmd.Discard(Choice, combat.Card());
            Require(combat.Tokens == 1 && combat.Enemy.CurrentHp == 968, "Neither batched nor separate discards generate additional tokens.");
        }
        using (var combat = new OrbCombat())
        {
            await PowerCmd.Apply<StarlessNightRedesignPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
            await AddStock(combat.Player, 5);
            var sly = AddCard<ShurikenCreation>(combat, PileType.Draw);
            var outer = AddCard<DefendIronclad>(combat, PileType.Draw);
            var inner = AddCard<DefendIronclad>(combat, PileType.Draw);
            int selections = 0;
            using var selector = CardSelectCmd.UseSelector(new SelectCards(_ => ++selections == 1 ? [sly, outer] : [inner]));
            await ScryCmd.Execute(Choice, combat.Player, 2);
            Require(selections == 2 && combat.Tokens == 2 && combat.Enemy.CurrentHp == 982,
                "Nested Sly Scry generates another token only when its card gains stock.");
        }
        using (var combat = new OrbCombat())
        {
            var first = AddCard<StrikeIronclad>(combat, PileType.Draw);
            var second = AddCard<DefendIronclad>(combat, PileType.Draw);
            var kept1 = AddCard<Wound>(combat, PileType.Draw);
            var kept2 = AddCard<Wound>(combat, PileType.Draw);
            combat.Player.Creature.GetPower<EvokeObserver>()!.DrawOnDiscard = true;
            using var selector = CardSelectCmd.UseSelector(new SelectCards(_ => [first, second]));
            var result = await ScryCmd.Execute(Choice, combat.Player, 2);
            Require(result.Discarded == 2 && first.Pile?.Type == PileType.Discard && second.Pile?.Type == PileType.Discard
                && kept1.Pile?.Type == PileType.Hand && kept2.Pile?.Type == PileType.Hand,
                "Discard-triggered draws must not draw another selected Scry card before its discard.");
        }
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var old = AddCard<DefendIronclad>(combat);
            var wound = AddCard<Wound>(combat, PileType.Draw);
            var drawn = AddCard<StrikeIronclad>(combat, PileType.Draw);
            AddCard<Wound>(combat, PileType.Draw);
            AddCard<DefendIronclad>(combat, PileType.Draw);
            if (upgraded) AddCard<DefendIronclad>(combat, PileType.Draw);
            await CardCmd.AutoPlay(Choice, AddCard<Slaughter>(combat, upgraded: upgraded), combat.Enemy);
            Require(old.Pile?.Type == PileType.Hand && wound.Pile?.Type == PileType.Hand && drawn.Pile?.Type == PileType.Discard
                && combat.Player.Creature.GetPower<EvokeObserver>()!.Discarded == (upgraded ? 3 : 2),
                "Burning Blood discards only its directly drawn non-status cards, preserving the existing hand.");
            var retrieve = AddCard<Excavate>(combat, upgraded: upgraded);
            await CardCmd.Exhaust(Choice, wound);
            using var selector = CardSelectCmd.UseSelector(new SelectCards(options =>
            {
                Require(options.Contains(wound) && !options.Contains(drawn), "Excavate must select only exhausted cards.");
                return [wound];
            }));
            await CardCmd.AutoPlay(Choice, retrieve, null);
            Require(PileType.Draw.GetPile(combat.Player).Cards.First() == wound && retrieve.Pile?.Type == PileType.Discard,
                "Excavate places its selection on top without exhausting itself.");
        }
        foreach (bool echo in new[] { false, true })
        {
            using var combat = new OrbCombat();
            using var selector = CardSelectCmd.UseSelector(new SelectCards(_ => []));
            var strike = AddCard<ChopStrikeRedesignV1>(combat);
            if (echo) await PowerCmd.Apply<EchoFormPower>(Choice, combat.Player.Creature, 3, combat.Player.Creature, null);
            for (int play = 1; play <= 4; play++)
            {
                await CardCmd.AutoPlay(Choice, strike, combat.Enemy);
                Require(strike.Pile?.Type == (play <= 3 ? PileType.Hand : PileType.Discard),
                    $"Strike Strike play {play}, echo {echo}: native repeat series consumes only one return opportunity.");
            }
            var other = AddCard<ChopStrikeRedesignV1>(combat);
            await CardCmd.AutoPlay(Choice, other, combat.Enemy);
            Require(other.Pile?.Type == PileType.Hand, "Another physical card has an independent allowance.");
            combat.State.RoundNumber++;
            combat.Player.PlayerCombatState!.IncrementTurnNumber();
            await CardCmd.AutoPlay(Choice, strike, combat.Enemy);
            Require(strike.Pile?.Type == PileType.Hand, "Strike Strike replenishes returns next turn.");
        }
        using (var combat = new OrbCombat())
        {
            for (int i = 0; i < 3; i++) await CardCmd.AutoPlay(Choice, combat.Card(), combat.Enemy);
            await CardCmd.AutoPlay(Choice, AddCard<Zanshin>(combat), null);
            await CardCmd.AutoPlay(Choice, AddCard<Zanshin>(combat, upgraded: true), null);
            combat.State.RoundNumber++;
            combat.Player.PlayerCombatState!.IncrementTurnNumber();
            Require(combat.Player.Creature.GetPower<ZanshinPower>()!.ModifyHandDraw(combat.Player, 5) == 8,
                "Stacked Zanshin counts attacks played before the powers and adds three to the next normal draw.");
            combat.State.RoundNumber++;
            combat.Player.PlayerCombatState.IncrementTurnNumber();
            Require(combat.Player.Creature.GetPower<ZanshinPower>()!.ModifyHandDraw(combat.Player, 5) == 5,
                "Zanshin must not reward an earlier turn twice.");
        }
        GD.Print("PASS v0.2.17 Palm Vigor, unpowered guards, stock gain and nested Scry, Scry draw isolation, Burning Blood, Excavate, Strike returns and Zanshin");
    }
}
