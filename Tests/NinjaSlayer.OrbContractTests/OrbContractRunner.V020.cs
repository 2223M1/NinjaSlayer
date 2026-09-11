using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyV020()
    {
        foreach (var (hit, hp, life) in new[] { (15, 10, 5), (25, 5, 0), (30, 0, 0) })
        {
            using var combat = new OrbCombat();
            Creature player = combat.Player.Creature;
            player.SetCurrentHpInternal(10);
            await PowerCmd.Apply<NarakuLifePower>(Choice, player, 20, player, null);
            // Exercise the host's final loss/death boundary, including exact lethal loss.
            var result = player.LoseHpInternal(hit, ValueProp.Unpowered);
            Require(player.CurrentHp == hp && player.GetPowerAmount<NarakuLifePower>() == life,
                $"Naraku final loss {hit}: expected HP {hp}, life {life}.");
            Require(result.WasTargetKilled == (hp == 0), "Naraku absorption must precede the native lethal flag.");
        }
        foreach (bool buffer in new[] { false, true })
        {
            using var combat = new OrbCombat();
            Creature player = combat.Player.Creature;
            player.SetCurrentHpInternal(10);
            await PowerCmd.Apply<NarakuLifePower>(Choice, player, 20, player, null);
            if (buffer) await PowerCmd.Apply<BufferPower>(Choice, player, 1, player, null);
            await CreatureCmd.GainBlock(player, 5, ValueProp.Unpowered, null);
            _ = Hook.ModifyHpLost(combat.Player.RunState, combat.State, player, 15,
                ValueProp.Unpowered, combat.Enemy, null, HpLossHookPhase.All, out _);
            Require(player.GetPowerAmount<NarakuLifePower>() == 20, "Damage previews must not spend Naraku Life.");
            await CreatureCmd.Damage(Choice, player, 20, ValueProp.Unpowered, combat.Enemy);
            Require(player.CurrentHp == 10 && player.GetPowerAmount<NarakuLifePower>() == (buffer ? 20 : 5),
                "Block and Buffer must resolve before Naraku Life, including hits greater than real HP.");
            Require(!player.HasPower<BufferPower>(), "Buffer must prevent the hit before the shield is spent.");
            Require(player.GetPower<EvokeObserver>()!.Healing == 0, "Absorption must not trigger native healing notifications.");
        }
        using (var combat = new OrbCombat())
        {
            Creature player = combat.Player.Creature;
            player.SetCurrentHpInternal(10);
            await PowerCmd.Apply<NarakuLifePower>(Choice, player, 20, player, null);
            await PowerCmd.Apply<IntangiblePower>(Choice, player, 1, player, null);
            await CreatureCmd.Damage(Choice, player, 100, ValueProp.Unpowered, player);
            Require(player.CurrentHp == 10 && player.GetPowerAmount<NarakuLifePower>() == 19,
                "Intangible must cap self-damage before Naraku absorption.");
        }
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            await AddStock(combat.Player, 3);
            await PowerCmd.Apply<FocusPower>(Choice, combat.Player.Creature, 2, combat.Player.Creature, null);
            await PowerCmd.Apply<StarlessNightRedesignPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
            await CardCmd.AutoPlay(Choice, AddCard<OyeahThrowSword>(combat, upgraded: upgraded), null);
            Require(combat.Stock == (upgraded ? 2 : 1) && combat.Tokens == 3 && combat.Enemy.CurrentHp == 1000,
                "Oyeah Throw Sword must convert/consume the old stock before replenishing it.");
            var token = PileType.Hand.GetPile(combat.Player).Cards.OfType<StrongShurikenTokenRedesignV1>().First();
            Require(token.SnapshotDamage == 8, "Conversion must snapshot the six base damage and two Focus.");
            CardCmd.Upgrade(token);
            Require(token.DynamicVars.Damage.BaseValue == 12, "Strong Shuriken upgrade must add four damage.");
            var clone = (StrongShurikenTokenRedesignV1)token.CreateClone();
            Require(clone.SnapshotDamage == 8 && clone.DynamicVars.Damage.BaseValue == 12,
                "Card cloning must retain the snapshot and upgrade.");
            var restored = (StrongShurikenTokenRedesignV1)CardModel.FromSerializable(token.ToSerializable());
            Require(restored.SnapshotDamage == 8 && restored.DynamicVars.Damage.BaseValue == 12,
                $"Card serialization must retain the snapshot and upgrade: snapshot={restored.SnapshotDamage}, damage={restored.DynamicVars.Damage.BaseValue}, level={restored.CurrentUpgradeLevel}; saved={System.Text.Json.JsonSerializer.Serialize(token.ToSerializable(), new System.Text.Json.JsonSerializerOptions { IncludeFields = true })}.");
            await PowerCmd.Apply<FocusPower>(Choice, combat.Player.Creature, 8, combat.Player.Creature, null);
            await PowerCmd.Apply<StrengthPower>(Choice, combat.Player.Creature, 3, combat.Player.Creature, null);
            await CardCmd.AutoPlay(Choice, token, combat.Enemy);
            Require(combat.Enemy.CurrentHp == 985, "Snapshot token must gain Strength but not count Focus twice.");
            await PowerCmd.Apply<WeakPower>(Choice, combat.Player.Creature, 1, combat.Enemy, null);
            await PowerCmd.Apply<VulnerablePower>(Choice, combat.Enemy, 1, combat.Player.Creature, null);
            var nextToken = PileType.Hand.GetPile(combat.Player).Cards.OfType<StrongShurikenTokenRedesignV1>().First();
            await CardCmd.AutoPlay(Choice, nextToken, combat.Enemy);
            Require(combat.Enemy.CurrentHp == 973, "Snapshot tokens must still use native Strength, Weak and Vulnerable modifiers.");
        }
        using (var combat = new OrbCombat())
        {
            await AddStock(combat.Player, 8);
            foreach (var (upgraded, expectedLoss) in new[] { (false, 3), (false, 3), (true, 1), (false, 1) })
            {
                await CardCmd.AutoPlay(Choice, AddCard<BladeCycleRedesignV1>(combat, upgraded: upgraded), null);
                Require(combat.Player.Creature.GetPowerAmount<BladeCyclePower>() == expectedLoss,
                    "Repeated Blade Cycle must retain the best stock loss instead of adding its amounts.");
                int stock = combat.Stock;
                int hp = combat.Enemy.CurrentHp;
                await Hook.AfterShuffle(combat.State, Choice, combat.Player);
                int loss = combat.Player.Creature.GetPowerAmount<BladeCyclePower>();
                Require(combat.Stock == Math.Max(0, stock - loss) && combat.Enemy.CurrentHp == hp - stock * 6,
                    "Shuffle must fire all current stock before losing three (one upgraded).");
            }
        }
        using (var combat = new OrbCombat())
        {
            for (int index = 0; index < CardPile.MaxCardsInHand; index++) AddCard<DefendIronclad>(combat);
            await PowerCmd.Apply<StarlessNightRedesignPower>(Choice, combat.Player.Creature, 1, combat.Player.Creature, null);
            await AddStock(combat.Player, 2);
            await CardCmd.AutoPlay(Choice, AddCard<OyeahThrowSword>(combat, PileType.Discard), null);
            Require(combat.Tokens == 2 && combat.Stock == 1 && combat.Enemy.CurrentHp == 1000,
                "A full hand must not lose converted shots or prevent stock replacement.");
            Require(PileType.Discard.GetPile(combat.Player).Cards.OfType<StrongShurikenTokenRedesignV1>().Count() == 2,
                "Native generation must send overflow tokens to discard.");
        }
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            await CardCmd.AutoPlay(Choice, AddCard<PlaceholderBlueDefense01>(combat, upgraded: upgraded), null);
            Require(combat.Player.Creature.Block == (upgraded ? 10 : 7)
                && combat.Player.Creature.GetPowerAmount<ThornsPower>() == (upgraded ? 3 : 2),
                "Caltrops must grant its block and native Thorns.");
            AddCard<ChadoEnergyRedesignV1>(combat);
            AddCard<ChadoEnergyRedesignV1>(combat);
            int block = combat.Player.Creature.Block;
            await CardCmd.AutoPlay(Choice, AddCard<PlaceholderGoldDefense01>(combat, upgraded: upgraded), null);
            Require(combat.Player.Creature.Block - block == (upgraded ? 7 : 5),
                "Tea Guard grants only its base block when no tea has been exhausted.");
        }
        foreach (bool upgraded in new[] { false, true })
        {
            using var combat = new OrbCombat();
            var tea = AddCard<ChadoEnergyRedesignV1>(combat);
            for (int index = 0; index < 15; index++) AddCard<DefendIronclad>(combat, PileType.Draw);
            var kick = AddCard<DragonFlyingKickRedesignV1>(combat, upgraded: upgraded);
            await CardCmd.AutoPlay(Choice, kick, combat.Enemy);
            Require(combat.Enemy.CurrentHp == (upgraded ? 980 : 985)
                && PileType.Hand.GetPile(combat.Player).Cards.Count == CardPile.MaxCardsInHand
                && tea.DynamicVars.Energy.BaseValue == (upgraded ? 4 : 3) && kick.Pile?.Type == PileType.Exhaust,
                "Dragon Flying Kick must damage, draw to native hand capacity, then breathe and exhaust.");
        }
        using (var combat = new OrbCombat())
        {
            var relic = ModelDb.Relic<NinjaSlayer.Relics.BlanketRelic>().ToMutable();
            combat.Player.AddRelicInternal(relic);
            for (int turn = 0; turn < 3; turn++) await Hook.AfterPlayerTurnStart(combat.State, Choice, combat.Player);
            Require(combat.Player.Creature.GetPowerAmount<NarakuLifePower>() == 9,
                "Mental Blanket must grant three life on every owner turn without requiring a form.");
        }
        using (var combat = new OrbCombat())
        {
            Creature player = combat.Player.Creature;
            player.SetCurrentHpInternal(10);
            var observer = player.GetPower<EvokeObserver>()!;
            await PowerCmd.Apply<NarakuLifePower>(Choice, player, 20, player, null);
            await CreatureCmd.Damage(Choice, player, 15, ValueProp.Unpowered, combat.Enemy);
            Require(observer.Deaths == 0 && player.CurrentHp == 10, "The first shielded hit must not trigger death.");
            await CreatureCmd.Damage(Choice, player, 15, ValueProp.Unpowered, combat.Enemy);
            Require(player.IsDead && observer.Deaths == 1 && observer.Healing == 0,
                "The lethal second hit must dispatch death exactly once without healing.");
        }
        GD.Print("PASS v0.2.0 Naraku lethal boundary/prevention, converted stock, snapshot copy/save and +4 upgrade");
        GD.Print("PASS v0.2.0 repeated Blade Cycle, shuffle loss, full-hand conversion, block cards and Blanket turns");
    }
}
