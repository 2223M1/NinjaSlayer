using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyBalanceV0216()
    {
        using (var combat = new OrbCombat())
        {
            await CardCmd.AutoPlay(Choice, AddCard<KarateTrainingRedesignV1>(combat), null);
            await CardCmd.AutoPlay(Choice, AddCard<KarateTrainingRedesignV1>(combat, upgraded: true), null);
            var sly = AddCard<ShurikenCreation>(combat, PileType.Draw);
            for (int i = 0; i < 9; i++) AddCard<DefendIronclad>(combat, PileType.Draw);
            int choices = 0;
            using var selector = CardSelectCmd.UseSelector(new SelectCards(options =>
            {
                choices++;
                if (choices == 1)
                {
                    Require(PileType.Hand.GetPile(combat.Player).Cards.Count == 5,
                        "Training must select after the normal hand draw.");
                    return [sly];
                }
                Require(choices <= 3, "Each Training must discard once, with one nested Scry.");
                return options.Take(1);
            }));
            var context = new HookPlayerChoiceContext(combat.Player, 1,
                MegaCrit.Sts2.Core.Entities.Multiplayer.GameActionType.CombatPlayPhaseOnly);
#if NINJASLAYER_CHANNEL_STABLE
            object[] args = [combat.Player, context];
#else
            object[] args = [AccessTools.Field(typeof(CombatManager), "_turnState").GetValue(CombatManager.Instance)!, combat.Player, context];
#endif
            await (Task)AccessTools.Method(typeof(CombatManager), "SetupPlayerTurn").Invoke(CombatManager.Instance, args)!;
            Require(choices == 3 && combat.Stock == 1
                && combat.Player.Creature.GetPowerAmount<KaratePower>() == 5
                && PileType.Hand.GetPile(combat.Player).Cards.Count == 3,
                $"Mixed Training: choices={choices}, stock={combat.Stock}, karate={combat.Player.Creature.GetPowerAmount<KaratePower>()}, hand={PileType.Hand.GetPile(combat.Player).Cards.Count}.");
        }
        using (var combat = new OrbCombat())
        {
            await CardCmd.AutoPlay(Choice, AddCard<KarateTrainingRedesignV1>(combat), null);
            using var selector = CardSelectCmd.UseSelector(new SelectCards(_ =>
                throw new InvalidOperationException("Empty-hand Training must not open a selector.")));
            foreach (var power in combat.Player.Creature.Powers.OfType<KarateTrainingPower>().ToArray())
                await power.AfterPlayerTurnStart(Choice, combat.Player);
            Require(combat.Player.Creature.GetPowerAmount<KaratePower>() == 2,
                "Empty-hand Training still grants Karate.");
        }
        using (var combat = new OrbCombat())
        {
            var owner = combat.Player.Creature;
            await PowerCmd.Apply<ThornsPower>(Choice, owner, 2, owner, null);
            await CardCmd.AutoPlay(Choice, AddCard<PlaceholderBlueDefense01>(combat), null);
#if NINJASLAYER_CHANNEL_STABLE
            await Hook.AfterTurnEnd(combat.State, CombatSide.Player, [owner]);
#else
            await Hook.AfterSideTurnEnd(combat.State, CombatSide.Player, [owner]);
#endif
            Require(owner.GetPowerAmount<ThornsPower>() == 5, "Caltrops must survive the player's turn end.");
#if NINJASLAYER_CHANNEL_STABLE
            await Hook.AfterTurnEnd(combat.State, CombatSide.Enemy, [combat.Enemy]);
#else
            await Hook.AfterSideTurnEnd(combat.State, CombatSide.Enemy, [combat.Enemy]);
#endif
            await CardCmd.AutoPlay(Choice, AddCard<PlaceholderBlueDefense01>(combat, upgraded: true), null);
            for (int enemyTurn = 2; enemyTurn <= 4; enemyTurn++)
            {
                int expected = enemyTurn <= 3 ? 9 : 6;
                int before = combat.Enemy.CurrentHp;
                await CreatureCmd.Damage(Choice, new[] { owner }, 1, ValueProp.Move, combat.Enemy, null
#if !NINJASLAYER_CHANNEL_STABLE
                    , null
#endif
                );
                Require(before - combat.Enemy.CurrentHp == expected,
                    "Native Thorns must retain each grant through its third enemy turn.");
#if NINJASLAYER_CHANNEL_STABLE
                await Hook.AfterTurnEnd(combat.State, CombatSide.Enemy, [combat.Enemy]);
#else
                await Hook.AfterSideTurnEnd(combat.State, CombatSide.Enemy, [combat.Enemy]);
#endif
                Require(owner.GetPowerAmount<ThornsPower>() == (enemyTurn == 2 ? 9 : enemyTurn == 3 ? 6 : 2),
                    "Caltrops grants must expire independently, preserving permanent Thorns.");
            }
            Require(!owner.HasPower<CaltropsDurationPower>(), "Expired Caltrops must remove its timer.");
        }
        GD.Print("PASS v0.2.16 Training after draw, mixed copies, Sly, empty hand; native Thorns and independent three-turn expiry");
    }
}
