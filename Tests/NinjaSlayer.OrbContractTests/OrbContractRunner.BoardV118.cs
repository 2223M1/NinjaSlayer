using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyBoardV118()
    {
        MegaCrit.Sts2.Core.Context.LocalContext.NetId = 1;
        AccessTools.Property(typeof(MegaCrit.Sts2.Core.Runs.RunManager), "NetService").SetValue(
            MegaCrit.Sts2.Core.Runs.RunManager.Instance, new MegaCrit.Sts2.Core.Multiplayer.NetSingleplayerGameService());
        foreach (bool upgraded in new[] { false, true })
        foreach (int x in new[] { 0, 1, 3, 4, 5 })
        {
            using var combat = new OrbCombat();
            Creature second = combat.AddEnemy();
            combat.Player.PlayerCombatState!.GainEnergy(x);
            await PowerCmd.Apply<VigorPower>(Choice, combat.Player.Creature, 7, combat.Player.Creature, null);
            var tornado = AddCard<TornadoFist>(combat, upgraded: upgraded);
            await CardCmd.AutoPlay(Choice, tornado, null);
            int hits = x;
            int expected = 1000 - hits * ((upgraded ? 6 : 4) * (x >= 4 ? 2 : 1) + 7);
            Require(combat.Enemy.CurrentHp == expected && second.CurrentHp == expected,
                $"Tornado X={x}, upgraded={upgraded}: every native hit must include Vigor on every target.");
            Require(!combat.Enemy.HasPower<VulnerablePower>() && !second.HasPower<VulnerablePower>(), "Tornado retained old per-hit Vulnerable.");
            Require(combat.Player.Creature.HasPower<VigorPower>() == (x == 0), "Tornado must consume Vigor only after the complete attack.");
        }
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var kick = AddCard<HalfMoonCompassKick>(combat, upgraded: upgraded);
            Creature second = combat.AddEnemy();
            bool Playable() => (bool)AccessTools.Property(typeof(HalfMoonCompassKick), "IsPlayable").GetValue(kick)!;
            Require(!Playable(), "Half-Moon must be unplayable without a Chado in hand.");
            var tea = AddCard<Chado>(combat);
            using var selection = UseCardSelector(new SelectCards(options => options.OfType<Chado>().Take(1)));
            Require(Playable(), "Hand Chado must enable Half-Moon.");
            await CardCmd.AutoPlay(Choice, kick, null);
            int damage = upgraded ? 13 : 10;
            Require(tea.Pile?.Type == PileType.Exhaust && kick.Pile?.Type == PileType.Discard
                && combat.Enemy.CurrentHp == 1000 - damage && second.CurrentHp == 1000 - damage && kick.DynamicVars.Repeat.IntValue == 2,
                "Half-Moon must pay one Chado, hit each enemy once and go to discard with two hits ready.");
            var copy = (HalfMoonCompassKick)kick.MutableClone();
            await CardPileCmd.Add(kick, PileType.Hand);
            AddCard<Chado>(combat);
            await CardCmd.AutoPlay(Choice, kick, null);
            Require(combat.Enemy.CurrentHp == 1000 - 3 * damage && second.CurrentHp == 1000 - 3 * damage
                && kick.DynamicVars.Repeat.IntValue == 3 && copy.DynamicVars.Repeat.IntValue == 2,
                "Each copy must track its own hit count.");
            await kick.AfterSideTurnEnd(Choice, CombatSide.Player, [combat.Player.Creature]);
            Require(kick.DynamicVars.Repeat.IntValue == 3 && AddCard<HalfMoonCompassKick>(combat).DynamicVars.Repeat.IntValue == 1,
                "A turn boundary must preserve hits without changing fresh cards.");
            if (upgraded) { copy.DowngradeInternal(); Require(copy.DynamicVars.Repeat.IntValue == 2, "Downgrading must preserve accumulated hits."); }
            await CardPileCmd.Add(kick, PileType.Hand);
            AddCard<Chado>(combat);
            kick.ExhaustOnNextPlay = true;
            await CardCmd.AutoPlay(Choice, kick, null);
            var hits = CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>().Where(e => e.CardSource == kick).ToArray();
            Require(kick.Pile?.Type == PileType.Exhaust && kick.DynamicVars.Repeat.IntValue == 4
                && hits.Length == 12 && hits.All(h => h.Result.TotalDamage == damage)
                && second.CurrentHp == 1000 - 6 * damage && combat.Enemy.CurrentHp == second.CurrentHp,
                "Three plays must deal 1+2+3 separate hits per opponent and respect native Exhaust.");
        }
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            await CardCmd.AutoPlay(Choice, AddCard<NarakusMight>(combat, upgraded: upgraded), combat.Enemy);
            Require(combat.Enemy.CurrentHp == 1000 - (upgraded ? 10 : 8)
                && combat.Enemy.GetPowerAmount<VulnerablePower>() == (upgraded ? 3 : 2)
                && PileType.Hand.GetPile(combat.Player).Cards.OfType<BlackFlame>().Count() == 1
                && !combat.Player.Creature.HasPower<KaratePower>(), "Naraku's Might must attack, apply Vulnerable and generate a Black Flame.");
            await CardCmd.AutoPlay(Choice, AddCard<GrapplingHook>(combat, upgraded: upgraded), combat.Enemy);
            Require(combat.Enemy.GetPowerAmount<StrengthPower>() == -(upgraded ? 6 : 4)
                && combat.Enemy.GetPowerAmount<WeakPower>() == (upgraded ? 2 : 1), "Grappling Hook must reduce Strength without requiring Karate.");
        }
        using (var combat = new OrbCombat())
        {
            var kick = AddCard<HalfMoonCompassKick>(combat);
            AddCard<Chado>(combat);
            using var selection = UseCardSelector(new SelectCards(options => options.OfType<Chado>().Take(1)));
            await CreatureCmd.GainBlock(combat.Enemy, 15, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, null);
            await CardCmd.AutoPlay(Choice, kick, null);
            Require(combat.Enemy.CurrentHp == 1000 && combat.Enemy.Block == 5, "Half-Moon bypassed native Block.");
        }
        GD.Print("PASS v1.18: native Tornado X/Vigor/AOE, Half-Moon per-copy combat hits/discard/exhaust, Naraku attack and fixed Grappling Hook.");
    }
}
