using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.AutoSlay.Helpers;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private static readonly Dictionary<OrbModel, bool> OrbPreviewObservations = [];
    private static void ObserveOrbPreview(NOrb __instance, bool isEvoking)
    {
        if (__instance.Model != null) OrbPreviewObservations[__instance.Model] = isEvoking;
    }

    private async Task VerifyOrbSlotsLive(string directory, CancellationToken ct)
    {
        var player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())!;
        var combat = CombatManager.Instance.DebugOnlyGetState()!;
        var room = NCombatRoom.Instance!;
        var manager = room.GetCreatureNode(player.Creature)!.OrbManager!;
        var queue = player.PlayerCombatState!.OrbQueue;
        var choice = new BlockingPlayerChoiceContext();
        foreach (var relic in player.Relics.ToArray()) await RelicCmd.Remove(relic);
        foreach (var power in player.Creature.Powers.ToArray()) await PowerCmd.Remove(power);
        foreach (var enemy in combat.HittableEnemies)
        {
            enemy.SetMaxHpInternal(10000);
            enemy.SetCurrentHpInternal(10000);
            foreach (var power in enemy.Powers.ToArray()) await PowerCmd.Remove(power);
        }
        foreach (var card in player.Piles.Where(pile => pile.IsCombatPile).SelectMany(pile => pile.Cards).ToArray())
            await CardPileCmd.RemoveFromCombat(card, false);
        var observer = new Harmony("NinjaSlayer.SmokeDriver.OrbPreview");
        var previewMethod = AccessTools.Method(typeof(NOrb), nameof(NOrb.UpdateVisuals));
        observer.Patch(previewMethod, postfix: new HarmonyMethod(typeof(SmokeController), nameof(ObserveOrbPreview)));
        try
        {
            await Stock(3);
            Require(queue.Capacity == 0, "Dedicated stock created a normal slot.");
            await Snapshot("dedicated-only");
            await OrbCmd.Channel<LightningOrb>(choice, player);
            Require(queue.Capacity == 1 && StockCount() == 3, "Ordinary channeling replaced dedicated stock.");
            Preview(OrbEvokeType.Front, queue.Orbs[0]);
            await Snapshot("normal-and-dedicated");
            int hp = TotalHp();
            await Play<Dualcast>();
            Require(hp - TotalHp() == 16 && StockCount() == 3, "Dualcast targeted stock while a normal orb existed.");
            Preview(OrbEvokeType.Front, queue.Orbs.Single());
            await Snapshot("empty-normal-fallback");
            hp = TotalHp();
            await Play<Quadcast>();
            Require(hp - TotalHp() == 72 && StockCount() == 0 && queue.Capacity == 1, "Quadcast did not fire all stock four times.");
            await Snapshot("stock-cleared");

            await Stock(3);
            await OrbCmd.AddSlots(player, 1);
            await OrbCmd.Channel<LightningOrb>(choice, player);
            await OrbCmd.Channel<FrostOrb>(choice, player);
            Preview(OrbEvokeType.All, queue.Orbs.ToArray());
            await Snapshot("shatter-preview");
            hp = TotalHp();
            int block = player.Creature.Block;
            await Play<Shatter>();
            Require(hp - TotalHp() == combat.HittableEnemies.Count * 7 + 16 + 36
                && player.Creature.Block == block + 10 && queue.Orbs.Count == 0 && queue.Capacity == 2,
                "Shatter mixed-orb damage, block or cleanup differed from the native contract.");
            await Snapshot("shatter-cleared");

            await Stock(2);
            await OrbCmd.Channel<LightningOrb>(choice, player);
            await OrbCmd.Channel<FrostOrb>(choice, player);
            await OrbCmd.Channel<DarkOrb>(choice, player);
            Require(StockCount() == 2 && queue.Orbs.First() is FrostOrb, "Full normal-slot replacement affected stock.");
            await Snapshot("normal-overflow");
            await OrbCmd.AddSlots(player, 20);
            for (int i = 0; i < 8; i++) await OrbCmd.Channel<LightningOrb>(choice, player);
            Require(queue.Capacity == 10 && queue.Orbs.Count == 11, "Ten slots plus dedicated stock did not render.");
            await Snapshot("ten-plus-dedicated");
            OrbCmd.RemoveSlots(player, 20);
            Require(queue.Capacity == 0 && StockCount() == 2 && queue.Orbs.Count == 1, "Shrinking removed dedicated stock.");
            await Snapshot("normal-slots-removed");
            var discarded = combat.CreateCard<DefendNinjaSlayerRedesignV1>(player);
            await CardPileCmd.Add(discarded, PileType.Hand);
            await CardCmd.Discard(choice, discarded);
            Require(StockCount() == 1, "Ordinary discard did not consume exactly one stack.");
            await OrbCmd.EvokeLast(choice, player);
            Require(StockCount() == 0 && queue.Capacity == 0, "Last evoke fallback did not clear the dedicated visual.");
            await Snapshot("all-cleared");
            await Stock(2);
            await Snapshot("stock-recreated");
            await OrbCmd.Channel<LightningOrb>(choice, player);
            await OrbCmd.Channel<FrostOrb>(choice, player);
            Require(queue.Capacity == 1 && StockCount() == 2, "Repeated channeling after recreation consumed stock.");
            await Snapshot("recreated-normal-overflow");
            await NGame.Instance!.ReturnToMainMenu();
            await WaitFrames(30);
            Require(!GodotObject.IsInstanceValid(manager) || !manager.IsInsideTree(), "Combat exit retained the orb manager.");
            _checkpoints.Write("orb-slots.combat-exit");
            var resume = NGame.Instance.MainMenu!.GetNode<NButton>("MainMenuTextButtons/ContinueButton");
            await WaitUntilAsync(() => resume.Visible && resume.IsEnabled, "Continue was not available after the native combat save.", ct);
            await UiHelper.Click(resume);
            await WaitUntilAsync(() => CombatManager.Instance.IsInProgress && NCombatRoom.Instance != null
                && LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())?.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
                "Native Continue did not restore the combat.", ct);
            await WaitFrames(120);
            NMapScreen.Instance?.Close(animateOut: false);
            player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())!;
            combat = CombatManager.Instance.DebugOnlyGetState()!;
            manager = NCombatRoom.Instance!.GetCreatureNode(player.Creature)!.OrbManager!;
            queue = player.PlayerCombatState!.OrbQueue;
            Require(queue.Capacity == 0 && queue.Orbs.Count == 0, "Native combat restart retained temporary orb state.");
            await Snapshot("native-continue-reset");
            await Stock(2);
            await OrbCmd.Channel<LightningOrb>(choice, player);
            Require(queue.Capacity == 1 && StockCount() == 2, "Normal/dedicated channeling failed after native Continue.");
            await Snapshot("native-continue-mixed");
            await NGame.Instance.ReturnToMainMenu();
        }
        finally
        {
            observer.Unpatch(previewMethod, HarmonyPatchType.All, observer.Id);
            OrbPreviewObservations.Clear();
        }

        int StockCount() => queue.Orbs.OfType<ShurikenOrb>().SingleOrDefault()?.StackCount ?? 0;
        int TotalHp() => combat.HittableEnemies.Sum(enemy => enemy.CurrentHp);
        Task Stock(int amount) => (Task)AccessTools.Method(typeof(ShurikenOrb), "AddStock").Invoke(null, [choice, player, amount])!;
        async Task Play<T>() where T : CardModel
        {
            T card = combat.CreateCard<T>(player);
            await CardPileCmd.Add(card, PileType.Hand);
            await CardCmd.AutoPlay(choice, card, null);
        }
        void Preview(OrbEvokeType type, params OrbModel[] expected)
        {
            OrbPreviewObservations.Clear();
            manager.UpdateVisuals(type);
            Require(queue.Orbs.All(orb => OrbPreviewObservations.TryGetValue(orb, out bool shown) && shown == expected.Contains(orb)),
                "Native orb previews did not match the actual selected orbs.");
        }
        async Task Snapshot(string name)
        {
            ct.ThrowIfCancellationRequested();
            await WaitFrames(45);
            var nodes = (List<NOrb>)AccessTools.Field(typeof(NOrbManager), "_orbs").GetValue(manager)!;
            Require(nodes.Count == queue.Capacity + (StockCount() > 0 ? 1 : 0)
                && nodes.Count(node => node.Model != null) == queue.Orbs.Count
                && queue.Orbs.All(orb => nodes.Count(node => ReferenceEquals(node.Model, orb)) == 1)
                && nodes.All(node => node.Position.IsFinite()), "Orb model/visual slots diverged at " + name);
            _tree.Root.GetTexture().GetImage().SavePng(Path.Combine(directory, "orb-" + name + ".png"));
            _checkpoints.Write("orb-slots." + name);
        }
    }
}
