using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Cards;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyV023()
    {
        foreach (int layers in new[] { 0, 1, 2 })
        foreach (int flameCount in new[] { 0, 2 })
        {
            using var combat = new OrbCombat();
            await PowerCmd.Apply<BurnBurnBurnPower>(Choice, combat.Player.Creature, 3, combat.Player.Creature, null);
            if (layers > 0)
                for (int i = 0; i < layers; i++)
                    await CardCmd.AutoPlay(Choice, AddCard<NarakuFormRedesignV1>(combat), null);
            for (int i = 0; i < flameCount; i++) AddCard<BlackFlameRedesignV1>(combat);
            var attack = AddCard<StrikeIronclad>(combat);
            await CardCmd.AutoPlay(Choice, attack, combat.Enemy);
            int extra = (layers > 0 ? layers * 4 + 3 : 0) + flameCount * 7;
            Require(combat.Enemy.CurrentHp == 1000 - 6 - extra,
                $"Naraku {layers}, hand flames {flameCount}: one stacked bonus and independent hand flames.");
            Require(attack.Pile?.Type == PileType.Discard
                && combat.Player.Piles.SelectMany(p => p.Cards).OfType<BlackFlameRedesignV1>().Count() == flameCount,
                "Naraku must not exhaust attacks or generate flames.");
        }
        using (var combat = new OrbCombat())
        {
            await CardCmd.AutoPlay(Choice, AddCard<OneBodyOneSoul>(combat), null);
            await CardCmd.AutoPlay(Choice, AddCard<OneBodyOneSoul>(combat, upgraded: true), null);
            await ChadoBreathCmd.Apply(Choice, combat.Player, 4);
            Require(combat.Player.Creature.GetPowerAmount<KaratePower>() == 7,
                "One breathing effect must award seven Karate, regardless of breathing amount.");
            await ChadoBreathCmd.Apply(Choice, combat.Player, 1);
            Require(combat.Player.Creature.GetPowerAmount<KaratePower>() == 14, "Independent breathing must trigger again.");
            int hp = combat.Player.Creature.CurrentHp;
            var flame = AddCard<BlackFlameRedesignV1>(combat);
#if NINJASLAYER_CHANNEL_STABLE
            await flame.OnTurnEndInHandWrapper(Choice);
#else
            await (Task)AccessTools.Method(typeof(CombatManager), "ResolveTurnEndCardEffects")
                .Invoke(CombatManager.Instance, [flame, Choice, Task.CompletedTask])!;
#endif
            Require(combat.Player.Creature.CurrentHp == hp && combat.Enemy.CurrentHp == 996
                && flame.Pile?.Type == PileType.Exhaust, "Soul prevents only Black Flame self-damage, retaining enemy damage and exhaust.");
        }
        foreach (bool exhaust in new[] { false, true })
        {
            using var combat = new OrbCombat();
            await CardCmd.AutoPlay(Choice, AddCard<ComposeHaikuRedesignV1>(combat), null);
            await CardCmd.AutoPlay(Choice, AddCard<RecycledBladesRedesignV1>(combat), null);
            var kept = AddCard<DefendIronclad>(combat, PileType.Draw);
            var selected = AddCard<StrikeIronclad>(combat, PileType.Draw);
            using var selector = CardSelectCmd.UseSelector(new SelectCards(_ => [selected]));
            await ScryCmd.Execute(Choice, combat.Player, 2, exhaust);
            Require(kept.Pile?.Type == PileType.Draw && kept.Keywords.Contains(CardKeyword.Sly)
                && !selected.Keywords.Contains(CardKeyword.Sly), "Planning grants Sly only to the unselected snapshot.");
            Require(combat.Stock == 1, "Recycled Blades awards one stock per Scry, including the exhaust selection path.");
            await CardCmd.Discard(Choice, kept);
            Require(combat.Player.Creature.Block == 5 && combat.Stock == 0,
                "Granted native Sly must autoplay only when the kept card is actually discarded later.");
        }
        using (var combat = new OrbCombat())
        {
            var basic = AddCard<BattlefieldInsightRedesignV1>(combat);
            var upgraded = AddCard<BattlefieldInsightRedesignV1>(combat, upgraded: true);
            await CardCmd.AutoPlay(Choice, basic, null);
            await CardCmd.AutoPlay(Choice, upgraded, null);
            await CardCmd.AutoPlay(Choice, AddCard<FlyingBladeDanceRedesignV1>(combat), null);
            for (int i = 0; i < 8; i++) AddCard<DefendIronclad>(combat, PileType.Draw);
            for (int i = 0; i < 3; i++)
            {
                await CardCmd.Discard(Choice, combat.Card());
                if (i == 0) await Hook.AfterPlayerTurnStart(combat.State, Choice, combat.Player);
            }
            Require(PileType.Draw.GetPile(combat.Player).Cards.Count == 6
                && combat.Player.Creature.Block == 6,
                "Independent 3/2 discard counters carry across turns and Composure observes each discard.");
            Require(combat.Player.Creature.Powers.OfType<ScryDrawPower>().Count() == 2,
                "Mixed Insight upgrades must use native independent Power instances.");
        }
        using (var combat = new OrbCombat())
        {
            var tea = AddCard<ChadoEnergyRedesignV1>(combat, PileType.Draw);
            await CardCmd.AutoPlay(Choice, AddCard<TeaTeaRedesignV1>(combat), null);
            var later = combat.State.CreateCard<ChadoEnergyRedesignV1>(combat.Player);
            await CardPileCmd.AddGeneratedCardToCombat(later, PileType.Hand, combat.Player);
            Require(tea.Keywords.Contains(CardKeyword.Retain) && later.Keywords.Contains(CardKeyword.Retain),
                "Meditation grants native Retain to existing and future tea.");
            var target = AddCard<DefendIronclad>(combat);
            using var selector = CardSelectCmd.UseSelector(new SelectCards(options =>
            {
                Require(!options.Contains(later), "Macaco must exclude already-retained cards like native Snap.");
                return [target];
            }));
            await CardCmd.AutoPlay(Choice, AddCard<TonyRetention>(combat), null);
            target.EndOfTurnCleanup();
            Require(target.Keywords.Contains(CardKeyword.Retain) && ((CardModel)target.MutableClone()).Keywords.Contains(CardKeyword.Retain)
                && combat.Player.Creature.Block == 11, "Macaco Retain survives cleanup and native copying.");
        }
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var shield = AddCard<ShurikenGenerationRedesignV1>(combat, upgraded: upgraded);
            Require(!shield.ShouldGlowGold, "Shuriken Barrier starts inactive.");
            await PowerCmd.Apply<StarlessNightRedesignPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
            await AddStock(combat.Player, 1);
            await CardCmd.Discard(Choice, combat.Card());
            Require(combat.Stock == 0 && shield.ShouldGlowGold, "Conversion counts as an evoke after the last orb is removed.");
            await CardCmd.AutoPlay(Choice, shield, null);
            Require(combat.Player.Creature.Block == (upgraded ? 22 : 16), "Barrier grants two native block instances.");
            var tea = AddCard<ChadoEnergyRedesignV1>(combat);
            var guard = AddCard<PlaceholderGoldDefense01>(combat, upgraded: upgraded);
            Require(!guard.ShouldGlowGold, "Tea in hand alone does not satisfy Tea Guard.");
            await CardCmd.Exhaust(Choice, tea);
            Require(guard.ShouldGlowGold, "Actual tea exhaustion enables glow.");
            await CardCmd.AutoPlay(Choice, guard, null);
            Require(combat.Player.Creature.GetPowerAmount<KaratePower>() == (upgraded ? 4 : 3),
                "Tea Guard awards Karate after tea is exhausted.");
        }
        GD.Print("PASS v0.2.3 Naraku stacking, Soul, Scry planning, discard counters, native Retain and conditional defenses");
    }
}
