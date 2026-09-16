using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.AutoSlay.Helpers;
using MegaCrit.Sts2.Core.Nodes.Relics;
using NinjaSlayer.Powers;
using NinjaSlayer.Relics;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task VerifyEventRelicsLive(string directory, CancellationToken ct)
    {
        SaveManager.Instance.MarkFtueAsComplete("obtain_relic_ftue");
        var player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())!;
        var state = CombatManager.Instance.DebugOnlyGetState()!;
        var choice = new BlockingPlayerChoiceContext();
        var bamboo = await RelicCmd.Obtain<BioBambooRelic>(player);
        var fragment = await RelicCmd.Obtain<BeppinFragmentRelic>(player);
        var puzzle = await RelicCmd.Obtain<CentennialPuzzle>(player);
        foreach (var relic in new MegaCrit.Sts2.Core.Models.RelicModel[] { bamboo, fragment })
        {
            var holder = UiHelper.FindAll<NRelicInventoryHolder>(_tree.Root).Single(node => node.Relic.Model == relic);
            holder.EmitSignal(Control.SignalName.MouseEntered);
            await WaitFrames(60);
            _tree.Root.GetTexture().GetImage().SavePng(Path.Combine(directory, relic.GetType().Name + "-tooltip.png"));
            holder.EmitSignal(Control.SignalName.MouseExited);
        }
        foreach (var card in PileType.Hand.GetPile(player).Cards.ToArray()) await CardPileCmd.RemoveFromCombat(card);
        await PowerCmd.Remove<KaratePower>(player.Creature);
        await PowerCmd.Remove<NarakuLifePower>(player.Creature);
        await PowerCmd.Remove<EvasionPower>(player.Creature);
        await PowerCmd.Remove<BufferPower>(player.Creature);
        if (player.Creature.Block > 0) await RemoveSmokeBlock(player.Creature);
        var enemy = state.HittableEnemies.First();
        enemy.SetMaxHpInternal(1000);
        enemy.SetCurrentHpInternal(1000);
        for (int i = 0; i < 2; i++)
        {
            var strike = state.CreateCard<StrikeIronclad>(player);
            await CardPileCmd.Add(strike, PileType.Hand);
            await CardCmd.AutoPlay(choice, strike, enemy);
            Require(bamboo.DisplayAmount == (i == 0 ? 1 : 0), "Rendered Bamboo counter differs from attacks played.");
            await WaitFrames(20);
            _tree.Root.GetTexture().GetImage().SavePng(Path.Combine(directory, $"bamboo-{i + 1}-attacks.png"));
        }
        Require(player.Creature.GetPowerAmount<PlatingPower>() == 1, "Bamboo did not grant native Plating.");
        for (int i = 0; i < 5; i++) await CardPileCmd.Add(state.CreateCard<DefendIronclad>(player), PileType.Draw);
        await PowerCmd.Apply<NarakuLifePower>(choice, player.Creature, 2, player.Creature, null);
        int hp = player.Creature.CurrentHp;
        int hand = PileType.Hand.GetPile(player).Cards.Count;
        var results = await CreatureCmd.Damage(choice, player.Creature, 2, ValueProp.Unpowered, enemy);
        Require(player.Creature.CurrentHp == hp && !player.Creature.HasPower<NarakuLifePower>()
            && results.Single().UnblockedDamage == 0 && player.Creature.GetPowerAmount<KaratePower>() == 7
            && puzzle.UsedThisCombat && PileType.Hand.GetPile(player).Cards.Count == hand + 3,
            "Rendered Naraku absorption did not trigger Fragment and native Puzzle exactly once.");
        await WaitFrames(30);
        _tree.Root.GetTexture().GetImage().SavePng(Path.Combine(directory, "naraku-relics-triggered.png"));
        _checkpoints.Write("event-relics.counter-and-naraku");
        await RelicCmd.Remove(bamboo);
        await RelicCmd.Remove(fragment);
        await RelicCmd.Remove(puzzle);
        await VerifySawatariCleanup(directory, ct);
        await VerifyDarkStrikeEventRewards(directory, label => _checkpoints.Write(label), ct);
        _checkpoints.Write("event-relics.completed");
    }
}
