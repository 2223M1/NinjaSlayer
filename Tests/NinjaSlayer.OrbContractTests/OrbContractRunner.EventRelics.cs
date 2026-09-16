using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using NinjaSlayer.Encounters;
using NinjaSlayer.Events;
using NinjaSlayer.Powers;
using NinjaSlayer.Relics;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static async Task VerifyEventRelics()
    {
        using (var combat = new OrbCombat())
        {
            var bamboo = (BioBambooRelic)ModelDb.Relic<BioBambooRelic>().ToMutable();
            combat.Player.AddRelicInternal(bamboo);
            Require(bamboo.Rarity == RelicRarity.Event && ModelDb.Relic<BeppinFragmentRelic>().Rarity == RelicRarity.Event,
                "Both duel relics must remain event-only.");
            await CardCmd.AutoPlay(Choice, combat.Card(), combat.Enemy);
            Require(bamboo.DisplayAmount == 1 && !combat.Player.Creature.HasPower<PlatingPower>(), "First attack must only advance the counter.");
            await CardCmd.AutoPlay(Choice, AddCard<DefendIronclad>(combat), null);
            Require(bamboo.DisplayAmount == 1, "Non-attacks must not advance the counter.");
            await bamboo.BeforeSideTurnStart(Choice, combat.Player.Creature.Side, [combat.Player.Creature], combat.State);
            await bamboo.AfterCombatEnd(null!);
            var restored = (BioBambooRelic)RelicModel.FromSerializable(bamboo.ToSerializable());
            Require(restored.DisplayAmount == 1, "Bamboo remainder must survive turn, combat and native save serialization.");
            combat.Player.RemoveRelicInternal(bamboo);
            combat.Player.AddRelicInternal(restored);
            await CardCmd.AutoPlay(Choice, combat.Card(), combat.Enemy);
            Require(restored.DisplayAmount == 0 && combat.Player.Creature.GetPowerAmount<PlatingPower>() == 1,
                "Second attack after reload must grant one Plating.");
            await CardCmd.AutoPlay(Choice, AddCard<TwinStrike>(combat), combat.Enemy);
            Require(restored.DisplayAmount == 1 && combat.Player.Creature.GetPowerAmount<PlatingPower>() == 1,
                "Multi-hit attack counts once.");
            await CardCmd.AutoPlay(Choice, combat.Card(), combat.Enemy);
            Require(combat.Player.Creature.GetPowerAmount<PlatingPower>() == 2, "Repeated pairs must continue granting Plating.");
        }

        foreach (string scenario in new[] { "real", "self", "absorbed", "depleted", "overflow", "blocked", "buffer", "evasion", "zero" })
        {
            using var combat = new OrbCombat();
            var player = combat.Player.Creature;
            var fragment = (BeppinFragmentRelic)ModelDb.Relic<BeppinFragmentRelic>().ToMutable();
            var puzzle = (CentennialPuzzle)ModelDb.Relic<CentennialPuzzle>().ToMutable();
            combat.Player.AddRelicInternal(fragment);
            combat.Player.AddRelicInternal(puzzle);
            for (int i = 0; i < 8; i++)
                await CardPileCmd.Add(combat.State.CreateCard<DefendIronclad>(combat.Player), PileType.Draw);
            if (scenario is "absorbed" or "depleted" or "overflow")
                await PowerCmd.Apply<NarakuLifePower>(Choice, player, scenario == "absorbed" ? 20 : scenario == "depleted" ? 2 : 1, player, null);
            if (scenario == "blocked") await CreatureCmd.GainBlock(player, 9, ValueProp.Unpowered, null);
            if (scenario == "buffer") await PowerCmd.Apply<BufferPower>(Choice, player, 1, player, null);
            if (scenario == "evasion") await PowerCmd.Apply<EvasionPower>(Choice, player, 1, player, null);
            int flashes = 0;
            fragment.Flashed += (_, _) => flashes++;
            // Previewing the same hit cannot spend life or activate either relic.
            _ = Hook.ModifyHpLost(combat.Player.RunState, combat.State, player, 2, ValueProp.Unpowered,
                combat.Enemy, null, HpLossHookPhase.All, out _);
            Require(!puzzle.UsedThisCombat && flashes == 0, "Preview activated life-loss relics.");
            var results = await CreatureCmd.Damage(Choice, player, scenario == "zero" ? 0 : 2,
                scenario == "evasion" ? ValueProp.Move : ValueProp.Unpowered, scenario == "self" ? player : combat.Enemy);
            bool triggers = scenario is not ("blocked" or "buffer" or "evasion" or "zero");
            Require(player.GetPowerAmount<KaratePower>() == (triggers ? 7 : 0)
                && puzzle.UsedThisCombat == triggers && PileType.Hand.GetPile(combat.Player).Cards.Count == (triggers ? 3 : 0),
                $"{scenario}: fragment and native Puzzle must each trigger once only on life loss.");
            if (scenario is "absorbed" or "depleted")
                Require(results.Single().UnblockedDamage == 0, "Centennial compatibility changed the shared damage result.");
            if (!triggers) continue;
            await CreatureCmd.Damage(Choice, player, 2, ValueProp.Unpowered, combat.Enemy);
            Require(flashes == 1 && player.GetPowerAmount<KaratePower>() == 7 && PileType.Hand.GetPile(combat.Player).Cards.Count == 3,
                "A second hit triggered either relic again.");
            await fragment.AfterCombatEnd(null!);
            await puzzle.AfterCombatEnd(null!);
            await CreatureCmd.Damage(Choice, player, 2, ValueProp.Unpowered, combat.Enemy);
            Require(flashes == 2 && player.GetPowerAmount<KaratePower>() == 14 && PileType.Hand.GetPile(combat.Player).Cards.Count == 6,
                "Life-loss relics did not reset for the next combat.");
        }
        foreach (RelicModel canonical in new RelicModel[] { ModelDb.Relic<BioBambooRelic>(), ModelDb.Relic<BeppinFragmentRelic>() })
        {
            using var f = new DarkStrikeFixture(multiplayer: true);
            var room = new CombatRoom(ModelDb.Encounter<DarkNinjaEncounter>().ToMutable(), f.Run)
            {
                ParentEventId = canonical is BioBambooRelic ? ModelDb.Event<SawatariEvent>().Id : ModelDb.Event<DarkNinjaEvent>().Id,
                ShouldResumeParentEventAfterCombat = false
            };
            room.MarkPreFinished();
            foreach (var player in f.Run.Players)
                room.AddExtraReward(player, new RelicReward(canonical.ToMutable(), player));
            var reloadedRoom = CombatRoom.FromSerializable(room.ToSerializable(), f.Run);
            foreach (var player in f.Run.Players)
            {
                var reward = reloadedRoom.ExtraRewards[player].OfType<RelicReward>().Single();
                Require(reward.Relic?.Id == canonical.Id && reward.Player == player
                    && !player.Relics.Any(relic => relic.Id == canonical.Id),
                    "Saved manual reward lost its model/owner or granted itself.");
                if (canonical is BioBambooRelic)
                {
                    var rewards = new RewardsSet(player).WithRewardsFromRoom(reloadedRoom);
                    Require(rewards.Rewards.Count == 1 && rewards.Rewards[0] == reward,
                        "Reloaded duel regenerated ordinary/random relic rewards.");
                }
                if (player == f.Combat.Player) await reward.SelectUnsynchronized();
                else reward.OnSkipped();
                Require(player.Relics.Count(relic => relic.Id == canonical.Id) == (player == f.Combat.Player ? 1 : 0),
                    "Native event relic reward ignored collection or skipping.");
            }
        }
        GD.Print("PASS event relics: attack counter/save, native Plating, real/Naraku life loss, prevention, native Puzzle, per-combat reset and two-player victory reload");
    }
}
