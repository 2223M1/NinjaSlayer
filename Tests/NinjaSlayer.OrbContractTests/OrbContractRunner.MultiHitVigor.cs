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
}
