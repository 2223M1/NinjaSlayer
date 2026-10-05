using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Cards.Standard;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static readonly List<AttackCommand> VigorAttacks = [];
    private static int _vigorAfterAttacks;
    private static int _vigorExtraHits;

    private static async Task VerifyMultiHitVigor()
    {
        await VerifyMultiHitFinisherForecast();
        var harmony = new Harmony("NinjaSlayer.OrbContracts.MultiHitVigor");
        harmony.Patch(AccessTools.Method(typeof(Hook), nameof(Hook.BeforeAttack)),
            prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(RecordVigorAttack)));
        harmony.Patch(AccessTools.Method(typeof(Hook), nameof(Hook.AfterAttack)),
            prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(RecordVigorAfterAttack)));
        harmony.Patch(AccessTools.Method(typeof(Hook), nameof(Hook.ModifyAttackHitCount)),
            postfix: new HarmonyMethod(typeof(OrbContractRunner), nameof(AddVigorHit)));
        var failures = new List<string>();
        try
        {
            foreach (bool storm in new[] { false, true })
            foreach (bool upgraded in new[] { false, true })
            foreach (int extra in new[] { 0, 1 })
            foreach (int block in new[] { 0, 1000 })
            {
                using var combat = new OrbCombat(ninjaSlayer: true);
                var second = combat.AddEnemy();
                AddCard<Chado>(combat, PileType.Exhaust);
                AddCard<Chado>(combat, PileType.Exhaust);
                var card = storm ? (MegaCrit.Sts2.Core.Models.CardModel)AddCard<StormFist>(combat, upgraded: upgraded)
                    : AddCard<DragonRoundhouseKick>(combat, upgraded: upgraded);
                await PowerCmd.Apply<StrengthPower>(Choice, combat.Player.Creature, 3, combat.Player.Creature, null);
                await PowerCmd.Apply<VigorPower>(Choice, combat.Player.Creature, 7, combat.Player.Creature, null);
                await CreatureCmd.GainBlock(combat.Enemy, block, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, null);
                await CreatureCmd.GainBlock(second, block, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, null);
                VigorAttacks.Clear();
                _vigorAfterAttacks = 0;
                _vigorExtraHits = extra;
                await CardCmd.AutoPlay(Choice, card, storm ? combat.Enemy : null);
                int hits = (storm ? 4 : 2) + extra;
                int damage = (storm ? (upgraded ? 6 + 2 * 4 : 4 + 2 * 3) : (upgraded ? 9 : 7)) + 3 + 7;
                var results = VigorAttacks.SelectMany(attack => attack.Results).ToArray();
                bool correct = VigorAttacks.Count == 1 && _vigorAfterAttacks == 1
                    && results.Length == hits && results.All(hit => hit.Count == (storm ? 1 : 2)
                        && hit.All(result => result.TotalDamage == damage))
                    && combat.Enemy.CurrentHp == 1000 - Math.Max(0, hits * damage - block)
                    && second.CurrentHp == 1000 - (storm ? 0 : Math.Max(0, hits * damage - block))
                    && !combat.Player.Creature.HasPower<VigorPower>();
                if (!correct) failures.Add($"{card.Id} upgraded={upgraded} extra={extra} block={block}: "
                    + $"Before/AfterAttack={VigorAttacks.Count}/{_vigorAfterAttacks}, "
                    + $"hits=[{string.Join(';', results.Select(hit => string.Join(',', hit.Select(r => r.TotalDamage))))}], expected {damage} x {hits}");
            }
            Require(failures.Count == 0, "Multi-hit Vigor regression:\n" + string.Join('\n', failures));

            // A new card play is a new attack; consumed Vigor must not leak into repeats.
            foreach (bool storm in new[] { false, true })
            {
                using var combat = new OrbCombat(ninjaSlayer: true);
                var other = combat.AddEnemy();
                var card = storm ? (MegaCrit.Sts2.Core.Models.CardModel)AddCard<StormFist>(combat)
                    : AddCard<DragonRoundhouseKick>(combat);
                _vigorExtraHits = 0;
                await PowerCmd.Apply<VigorPower>(Choice, combat.Player.Creature, 7, combat.Player.Creature, null);
                await CardCmd.AutoPlay(Choice, card, storm ? combat.Enemy : null);
                int afterFirst = combat.Enemy.CurrentHp;
                await CardCmd.AutoPlay(Choice, card, storm ? combat.Enemy : null);
                Require(afterFirst - combat.Enemy.CurrentHp == (storm ? 4 * 4 : 7 * 2),
                    "A later play incorrectly reused consumed Vigor.");

                // Fixed-target hits must not jump to another enemy after the target dies;
                // AOE hits must continue against the surviving enemy.
                combat.Enemy.SetCurrentHpInternal(1);
                int otherBefore = other.CurrentHp;
                await PowerCmd.Apply<VigorPower>(Choice, combat.Player.Creature, 7, combat.Player.Creature, null);
                await CardCmd.AutoPlay(Choice, card, storm ? combat.Enemy : null);
                Require(combat.Enemy.IsDead && other.CurrentHp == otherBefore - (storm ? 0 : 2 * (7 + 7))
                    && !combat.Player.Creature.HasPower<VigorPower>(),
                    "Early death changed native fixed/AOE target selection or Vigor cleanup.");
            }
            GD.Print("PASS multi-hit Vigor: Storm/Dragon base+upgrade, all hits/targets, Strength, full Block, hook-added hits, repeat plays and early death.");
        }
        finally
        {
            _vigorExtraHits = 0;
            VigorAttacks.Clear();
            harmony.UnpatchAll(harmony.Id);
        }
    }

    private static void RecordVigorAttack(AttackCommand __1) => VigorAttacks.Add(__1);
    private static void RecordVigorAfterAttack() => _vigorAfterAttacks++;
    private static void AddVigorHit(ref decimal __result) => __result += _vigorExtraHits;

    private static async Task VerifyMultiHitFinisherForecast()
    {
        var product = typeof(StormFist).Assembly;
        Type adapter = product.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherAttackCommandAdapter", true)!;
        Type forecast = product.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherForecast", true)!;
        var create = AccessTools.Method(adapter, "CreateSpec");
        foreach (bool upgraded in new[] { false, true })
        foreach (var canonical in new MegaCrit.Sts2.Core.Models.CardModel[]
                 { MegaCrit.Sts2.Core.Models.ModelDb.Card<StormFist>(), MegaCrit.Sts2.Core.Models.ModelDb.Card<DragonRoundhouseKick>(),
                     MegaCrit.Sts2.Core.Models.ModelDb.Card<PalmThrust>(), MegaCrit.Sts2.Core.Models.ModelDb.Card<AntiAirBangBangFist>() })
        {
            using var combat = new OrbCombat(ninjaSlayer: true);
            var run = MegaCrit.Sts2.Core.Runs.RunState.CreateForTest([combat.Player]);
            AccessTools.Field(combat.State.GetType(), "<RunState>k__BackingField").SetValue(combat.State, run);
            var card = combat.State.CreateCard(canonical, combat.Player);
            if (upgraded) card.UpgradeInternal();
            var play = new CardPlay { Card = card,
#if !NINJASLAYER_CHANNEL_STABLE
                Player = combat.Player,
#endif
                Target = combat.Enemy, ResultPile = PileType.Discard,
                Resources = new ResourceInfo { EnergySpent = 0, EnergyValue = 0, StarsSpent = 0, StarValue = 0 },
                IsAutoPlay = false, PlayIndex = 0, PlayCount = 1 };
            await PowerCmd.Apply<StrengthPower>(Choice, combat.Player.Creature, 3, combat.Player.Creature, null);
            await PowerCmd.Apply<VigorPower>(Choice, combat.Player.Creature, 7, combat.Player.Creature, null);
            int hits = card is AntiAirBangBangFist ? 3 : card.DynamicVars.Repeat.IntValue;
            decimal raw = card is StormFist ? card.DynamicVars.CalculatedDamage.Calculate(combat.Enemy) : card.DynamicVars.Damage.BaseValue;
            var command = DamageCmd.Attack(raw).WithHitCount(hits)
#if NINJASLAYER_CHANNEL_STABLE
                .FromCard(card)
#else
                .FromCard(card, play)
#endif
                ;
            if (card is DragonRoundhouseKick) command.TargetingAllOpponents(combat.State);
            else if (card is AntiAirBangBangFist) command.TargetingRandomOpponents(combat.State);
            else command.Targeting(combat.Enemy);
            // Baseline uses the old explicit-card producer, not a mock predictor.
            object spec = create != null ? create.Invoke(null, [command, card, play, null, null])!
                : AccessTools.Method(product.GetType("NinjaSlayer.Code.ExternalAnimations.FinisherAttackSpec", true)!, "FromCard")
                    .Invoke(null, [card, play, null, null, null])!;
            int hp = (int)(raw + 10m) * hits - 2;
            combat.Enemy.SetCurrentHpInternal(hp);
            await CreatureCmd.GainBlock(combat.Enemy, 2, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, null);
            object?[] args = [combat.Player.Creature, new[] { combat.Enemy }, spec, command, null];
            string outcome = AccessTools.Method(forecast, "Evaluate").Invoke(null, args)!.ToString()!;
            int resolved = (int)AccessTools.Property(args[4]!.GetType(), "ResolvedHits").GetValue(args[4])!;
            Require(outcome == "Guaranteed" && resolved == hits,
                $"{card.Id} upgraded={upgraded}: explicit finisher predicted {resolved} hits/{outcome}, expected {hits}/Guaranteed with Strength+Vigor+Block.");
            Require(combat.Player.Creature.GetPowerAmount<VigorPower>() == 7 && combat.Enemy.CurrentHp == hp,
                "Forecast consumed Vigor or changed real HP.");
            combat.Enemy.SetCurrentHpInternal(hp + 1);
            Require(AccessTools.Method(forecast, "Evaluate").Invoke(null,
                    new object?[] { combat.Player.Creature, new[] { combat.Enemy }, spec, command, null })!.ToString() == "NotGuaranteed",
                "A nonlethal combo was incorrectly predicted as an early finisher.");
        }
        GD.Print("PASS explicit multi-hit finisher: Storm/Dragon/Palm/AntiAir base+upgrade, native command hits/targeting, Strength+Vigor, Block, nonlethal boundary and read-only prediction.");
    }
}
