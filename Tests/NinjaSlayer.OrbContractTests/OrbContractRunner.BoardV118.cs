using Godot;
using MegaCrit.Sts2.Core.Combat;
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
            int hits = x >= 4 ? 2 * x : x;
            int expected = 1000 - hits * ((upgraded ? 6 : 4) + 7);
            Require(combat.Enemy.CurrentHp == expected && second.CurrentHp == expected,
                $"Tornado X={x}, upgraded={upgraded}: every native hit must include Vigor on every target.");
            Require(!combat.Enemy.HasPower<VulnerablePower>() && !second.HasPower<VulnerablePower>(), "Tornado retained old per-hit Vulnerable.");
            Require(combat.Player.Creature.HasPower<VigorPower>() == (x == 0), "Tornado must consume Vigor only after the complete attack.");
        }
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var kick = AddCard<HalfMoonCompassKick>(combat, upgraded: upgraded);
            bool Playable() => (bool)AccessTools.Property(typeof(HalfMoonCompassKick), "IsPlayable").GetValue(kick)!;
            Require(!Playable(), "Half-Moon must be unplayable without a Chado in hand.");
            var tea = AddCard<Chado>(combat);
            using var selection = CardSelectCmd.UseSelector(new SelectCards(options => options.OfType<Chado>().Take(1)));
            Require(Playable(), "Hand Chado must enable Half-Moon.");
            await CardCmd.AutoPlay(Choice, kick, null);
            int damage = upgraded ? 13 : 10;
            Require(tea.Pile?.Type == PileType.Exhaust && kick.Pile?.Type == PileType.Hand
                && combat.Enemy.CurrentHp == 1000 - damage && kick.DynamicVars.Damage.BaseValue == 2 * damage,
                "Half-Moon must exhaust one Chado, hit once, double itself and return.");
            var copy = (HalfMoonCompassKick)kick.MutableClone();
            AddCard<Chado>(combat);
            await CardCmd.AutoPlay(Choice, kick, null);
            Require(combat.Enemy.CurrentHp == 1000 - 3 * damage && kick.DynamicVars.Damage.BaseValue == 4 * damage
                && copy.DynamicVars.Damage.BaseValue == 2 * damage, "Half-Moon damage must belong to each copied entity.");
            await kick.AfterSideTurnEnd(Choice, CombatSide.Player, [combat.Player.Creature]);
            await copy.AfterSideTurnEnd(Choice, CombatSide.Player, [combat.Player.Creature]);
            Require(kick.DynamicVars.Damage.BaseValue == damage && copy.DynamicVars.Damage.BaseValue == damage,
                "Both original and copy must lose only their temporary damage on turn end.");
            AddCard<Chado>(combat);
            kick.ExhaustOnNextPlay = true;
            await CardCmd.AutoPlay(Choice, kick, null);
            Require(kick.Pile?.Type == PileType.Exhaust, "Native Exhaust must take priority over Half-Moon return.");
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
            using var selection = CardSelectCmd.UseSelector(new SelectCards(options => options.OfType<Chado>().Take(1)));
            await CreatureCmd.GainBlock(combat.Enemy, 15, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, null);
            await CardCmd.AutoPlay(Choice, kick, null);
            Require(combat.Enemy.CurrentHp == 1000 && combat.Enemy.Block == 5, "Half-Moon bypassed native Block.");
        }
        GD.Print("PASS v1.18: native Tornado X/Vigor/AOE, Half-Moon tea gating/copy/reset/exhaust, Naraku attack and fixed Grappling Hook.");
    }
}
