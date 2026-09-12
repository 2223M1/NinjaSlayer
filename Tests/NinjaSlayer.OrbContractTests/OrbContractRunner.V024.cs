using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyV024()
    {
        foreach (bool upgraded in new[] { false, true })
        foreach (bool vulnerable in new[] { false, true })
        foreach (bool weak in new[] { false, true })
        foreach (int strength in new[] { 0, 3 })
        {
            using var combat = new OrbCombat();
            var owner = combat.Player.Creature;
            await PowerCmd.Apply<KaratePower>(Choice, owner, 4, owner, null);
            if (strength > 0) await PowerCmd.Apply<StrengthPower>(Choice, owner, strength, owner, null);
            if (weak) await PowerCmd.Apply<WeakPower>(Choice, owner, 1, combat.Enemy, null);
            if (vulnerable) await PowerCmd.Apply<VulnerablePower>(Choice, combat.Enemy, 1, owner, null);
            var card = AddCard<AlabamaDropRedesignV1>(combat, upgraded: upgraded);
            int main = (int)Math.Floor(((upgraded ? 28 : 20) + strength) * (weak ? .75m : 1m) * (vulnerable ? 1.5m : 1m));
            await CardCmd.AutoPlay(Choice, card, combat.Enemy);
            var hits = CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>()
                .Where(e => e.Receiver == combat.Enemy).Select(e => e.Result.TotalDamage).ToArray();
            Require(hits.SequenceEqual(new[] { main, 4 }) && owner.GetPowerAmount<KaratePower>() == 3,
                $"Alabama must deal normal main damage then four Karate and consume one: upgrade={upgraded}, vulnerable={vulnerable}, weak={weak}, strength={strength}; hits={string.Join(',', hits)}.");
            Require(PileType.Draw.GetPile(combat.Player).Cards.OfType<Dazed>().Count() == 3,
                "Alabama must retain its three Dazed.");
        }
        foreach (int hp in new[] { 5, 6, 8, 20, 24 })
        foreach (bool alabama in new[] { false, true })
        {
            using var combat = new OrbCombat();
            combat.AddEnemy();
            combat.Enemy.SetCurrentHpInternal(hp);
            await PowerCmd.Apply<KaratePower>(Choice, combat.Player.Creature, 4, combat.Player.Creature, null);
            var card = alabama ? (MegaCrit.Sts2.Core.Models.CardModel)AddCard<AlabamaDropRedesignV1>(combat)
                : AddCard<StrikeIronclad>(combat);
            await CardCmd.AutoPlay(Choice, card, combat.Enemy);
            var hits = CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>()
                .Where(e => e.Receiver == combat.Enemy).ToArray();
            Require(combat.Player.Creature.GetPowerAmount<KaratePower>() == 3
                && hits.Length == (hp <= (alabama ? 20 : 6) ? 1 : 2),
                "Lethal main damage consumes one Karate without hitting the dead target; bonus kills also consume once.");
        }
        using (var combat = new OrbCombat())
        {
            var second = combat.AddEnemy();
            combat.Enemy.SetCurrentHpInternal(1);
            await PowerCmd.Apply<KaratePower>(Choice, combat.Player.Creature, 4, combat.Player.Creature, null);
            await CreatureCmd.Damage(Choice, new[] { combat.Enemy, second }, 8, ValueProp.Move,
                combat.Player.Creature, AddCard<StrikeIronclad>(combat)
#if !NINJASLAYER_CHANNEL_STABLE
                , null
#endif
            );
            Require(second.CurrentHp == 988 && combat.Player.Creature.GetPowerAmount<KaratePower>() == 3,
                "A mixed lethal/live group wave shares four bonus damage and consumes just one stack.");
        }
        foreach (int flameCount in new[] { 1, 2, 3 })
        {
            using var combat = new OrbCombat();
            var second = combat.AddEnemy();
            await PowerCmd.Apply<BurnBurnBurnPower>(Choice, combat.Player.Creature, 3, combat.Player.Creature, null);
            for (int i = 0; i < flameCount; i++) AddCard<BlackFlameRedesignV1>(combat);
            for (int play = 0; play < 2; play++)
            {
                CombatManager.Instance.History.Clear();
                await CardCmd.AutoPlay(Choice, AddCard<TwinStrike>(combat), combat.Enemy);
                foreach (var enemy in new[] { combat.Enemy, second })
                {
                    var burns = CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>()
                        .Where(e => e.Receiver == enemy && e.CardSource is BlackFlameRedesignV1).ToArray();
                    Require(burns.Length == 1 && burns[0].Result.TotalDamage == flameCount * 4 + 3,
                        "Each independent multi-hit attack must merge all held flames into one amplified hit per enemy.");
                }
            }
        }
        using (var combat = new OrbCombat())
        {
            AddCard<BlackFlameRedesignV1>(combat);
            AddCard<BlackFlameRedesignV1>(combat);
            var observer = combat.Player.Creature.GetPower<EvokeObserver>()!;
            observer.NestedFlameAttack = AddCard<StrikeIronclad>(combat);
            await CardCmd.AutoPlay(Choice, AddCard<StrikeIronclad>(combat), combat.Enemy);
            var burns = CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>()
                .Where(e => e.CardSource is BlackFlameRedesignV1).ToArray();
            Require(observer.NestedFlameAttack == null && combat.Enemy.CurrentHp == 972
                && burns.Length == 2 && burns.All(e => e.Result.TotalDamage == 8),
                "An attack nested inside Black Flame damage gets its own merged wave without re-triggering the outer play.");
        }
        foreach (bool upgraded in new[] { false, true })
        foreach (int selectedCount in new[] { 0, 2 })
        {
            using var combat = new OrbCombat();
            var selected = Enumerable.Range(0, selectedCount).Select(_ => AddCard<DefendIronclad>(combat, PileType.Draw)).ToArray();
            var discern = AddCard<DecidedOutcomeRedesignV1>(combat, upgraded: upgraded);
            using var selector = CardSelectCmd.UseSelector(new SelectCards(_ => selected));
            await CardCmd.AutoPlay(Choice, discern, null);
            Require(discern.Pile?.Type == PileType.Exhaust && selected.All(card => card.Pile?.Type == PileType.Exhaust),
                "Discern and its selections must exhaust through native commands.");
            int energy = PileType.Hand.GetPile(combat.Player).Cards.OfType<ChadoEnergyRedesignV1>()
                .Sum(card => card.DynamicVars.Energy.IntValue);
            Require(energy == selectedCount, "Discern's own exhaust must not add to its breathing count.");
        }
        GD.Print("PASS v0.2.4: Alabama powered damage/upgrades/lethals, group Karate consumption, merged multi-hit hand flames, repeated plays and Discern native exhaust.");
    }
}
