using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
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
        foreach (bool upgraded in new[] { false, true })
        foreach (int energy in new[] { 0, 2 })
        foreach (bool chemicalX in new[] { false, true })
        {
            using var combat = new OrbCombat();
            if (chemicalX)
                combat.Player.AddRelicInternal(ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.ChemicalX>().ToMutable());
            await PlayerCmd.SetEnergy(energy, combat.Player);
            await CardCmd.AutoPlay(Choice, AddCard<TeaStormRedesignV1>(combat, upgraded: upgraded), null);
            int breaths = 2 * (energy + (chemicalX ? 2 : 0)) + (upgraded ? 1 : 0);
            Require(!combat.Player.Piles.SelectMany(p => p.Cards).OfType<ChadoEnergyRedesignV1>().Any(),
                "Long Breath must wait until the next owner turn.");
            await PlayerCmd.SetEnergy(9, combat.Player);
            await Hook.BeforeSideTurnStart(combat.State, CombatSide.Enemy, [combat.Enemy]);
            Require(combat.Player.Creature.GetPowerAmount<PourTeaNextTurnPower>() == breaths,
                "Enemy turn must preserve captured X and delayed breathing.");
            await Hook.BeforeSideTurnStart(combat.State, CombatSide.Player, [combat.Player.Creature]);
            var tea = combat.Player.Piles.SelectMany(p => p.Cards).OfType<ChadoEnergyRedesignV1>().SingleOrDefault();
            Require((tea?.DynamicVars.Energy.IntValue ?? 0) == breaths
                && !combat.Player.Creature.HasPower<PourTeaNextTurnPower>(),
                "Long Breath uses native X modifiers and 2X/2X+1 exactly once.");
        }
        using (var combat = new OrbCombat())
        {
            await CardCmd.AutoPlay(Choice, AddCard<RecycledBladesRedesignV1>(combat), null);
            using var selector = CardSelectCmd.UseSelector(new SelectCards(_ => []));
            await ScryCmd.Execute(Choice, combat.Player, 2);
            Require(combat.Stock == 0, "Empty Scry must not award stock.");
            AddCard<DefendIronclad>(combat, PileType.Draw);
            await ScryCmd.Execute(Choice, combat.Player, 2);
            Require(combat.Stock == 1, "Nonempty Scry awards stock even when no card is selected.");
        }
        foreach (int layers in new[] { 0, 1, 2 })
        foreach (int flameCount in new[] { 0, 1, 2 })
        foreach (int amplification in new[] { 0, 6, 8 })
        {
            using var combat = new OrbCombat();
            if (amplification > 0) await PowerCmd.Apply<BurnBurnBurnPower>(Choice, combat.Player.Creature, amplification, combat.Player.Creature, null);
            if (layers > 0)
                for (int i = 0; i < layers; i++)
                    await CardCmd.AutoPlay(Choice, AddCard<NarakuFormRedesignV1>(combat), null);
            for (int i = 0; i < flameCount; i++) AddCard<BlackFlameRedesignV1>(combat);
            var attack = AddCard<StrikeIronclad>(combat);
            int ownerHp = combat.Player.Creature.CurrentHp;
            await CardCmd.AutoPlay(Choice, attack, combat.Enemy);
            int extra = (layers + flameCount) * (4 + amplification);
            Require(combat.Enemy.CurrentHp == 1000 - 6 - extra,
                $"Naraku {layers}, hand flames {flameCount}: each Naraku layer and hand flame is independently amplified.");
            var burns = CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>()
                .Where(entry => entry.Receiver == combat.Enemy && entry.CardSource != attack).ToArray();
            Require(burns.Length == layers + flameCount
                && burns.All(entry => entry.Result.TotalDamage == 4 + amplification)
                && combat.Player.Creature.CurrentHp == ownerHp,
                "Each virtual and physical flame must be a separate damage callback without attack-trigger self-damage.");
            Require(attack.Pile?.Type == PileType.Discard
                && combat.Player.Piles.SelectMany(p => p.Cards).OfType<BlackFlameRedesignV1>().Count() == flameCount,
                "Naraku must not exhaust attacks or generate flames.");
        }
        using (var combat = new OrbCombat())
        {
            await CardCmd.AutoPlay(Choice, AddCard<OneBodyOneSoul>(combat), null);
            await CardCmd.AutoPlay(Choice, AddCard<OneBodyOneSoul>(combat, upgraded: true), null);
            await ChadoBreathCmd.Apply(Choice, combat.Player, 4);
            Require(combat.Player.Creature.GetPowerAmount<KaratePower>() == 0,
                "One Body must no longer award Karate on breathing.");
            await ChadoBreathCmd.Apply(Choice, combat.Player, 1);
            Require(combat.Player.Creature.GetPowerAmount<KaratePower>() == 0, "Further breathing must not grant Karate.");
            int hp = combat.Player.Creature.CurrentHp;
            var flame = AddCard<BlackFlameRedesignV1>(combat);
#if NINJASLAYER_CHANNEL_STABLE
            await flame.OnTurnEndInHandWrapper(Choice);
#else
            await (Task)AccessTools.Method(typeof(CombatManager), "ResolveTurnEndCardEffects")
                .Invoke(CombatManager.Instance, [flame, Choice, Task.CompletedTask])!;
#endif
            Require(combat.Player.Creature.CurrentHp == hp - 4 && combat.Enemy.CurrentHp == 996
                && flame.Pile?.Type == PileType.Exhaust, "Soul no longer prevents Black Flame self-damage.");
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
            var tea = AddCard<ChadoEnergyRedesignV1>(combat, PileType.Draw);
            await CardCmd.AutoPlay(Choice, AddCard<TeaTeaRedesignV1>(combat), null);
            var later = combat.State.CreateCard<ChadoEnergyRedesignV1>(combat.Player);
            await CardPileCmd.AddGeneratedCardToCombat(later, PileType.Hand, combat.Player);
            Require(tea.Keywords.Contains(CardKeyword.Retain) && later.Keywords.Contains(CardKeyword.Retain),
                "Meditation grants native Retain to existing and future tea.");
            using var selector = CardSelectCmd.UseSelector(new SelectCards(_ => []));
            await CardCmd.AutoPlay(Choice, AddCard<TonyRetention>(combat), null);
            Require(combat.Player.Creature.Block == 4 && tea.Pile?.Type == PileType.Draw,
                "Macaco now grants four Block and scries without changing Tea Retain.");
        }
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var shield = AddCard<ShurikenGenerationRedesignV1>(combat, upgraded: upgraded);
            Require(!shield.ShouldGlowGold, "Shuriken Barrier starts inactive.");
            await PowerCmd.Apply<StarlessNightRedesignPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
            await AddStock(combat.Player, 1);
            await CardCmd.Discard(Choice, combat.Card());
            Require(combat.Stock == 0 && shield.ShouldGlowGold, "Gain qualification persists after the last orb is removed.");
            await CardCmd.AutoPlay(Choice, shield, null);
            Require(combat.Player.Creature.Block == (upgraded ? 22 : 16), "Barrier grants two native block instances.");
        }
        GD.Print("PASS v0.2.3 Naraku stacking, Soul, Scry planning, discard counters, native Retain and conditional defenses");
    }
}
