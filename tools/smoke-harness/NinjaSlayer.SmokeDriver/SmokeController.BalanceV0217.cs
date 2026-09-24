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
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;
using STS2RitsuLib.Settings;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task VerifyBalanceV0217Live(string directory, CancellationToken ct)
    {
        using var manual = CardSelectCmd.SuspendSelectorForTest();
        var player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())!;
        var combat = CombatManager.Instance.DebugOnlyGetState()!;
        var choice = new BlockingPlayerChoiceContext();
        foreach (var relic in player.Relics.ToArray()) await RelicCmd.Remove(relic);
        foreach (var power in player.Creature.Powers.ToArray()) await PowerCmd.Remove(power);
        foreach (var enemy in combat.HittableEnemies)
        {
            enemy.SetMaxHpInternal(1000);
            enemy.SetCurrentHpInternal(1000);
            foreach (var power in enemy.Powers.ToArray()) await PowerCmd.Remove(power);
        }
        await ClearCards();
        var victim = combat.HittableEnemies.First();
        int hp = victim.CurrentHp;
        await PowerCmd.Apply<VigorPower>(choice, player.Creature, 7, player.Creature, null);
        var palm = await Add<PalmThrustRedesignV1>();
        CardCmd.Upgrade(palm);
        await CardCmd.AutoPlay(choice, palm, null);
        Require(combat.HittableEnemies.Sum(enemy => 1000 - enemy.CurrentHp) == 36
            && !player.Creature.HasPower<VigorPower>(), "Palm must use Vigor on all three hits.");
        _checkpoints.Write("v0217.live-palm-vigor");

        await ClearCards();
        await PowerCmd.Apply<StarlessNightRedesignPower>(choice, player.Creature, 1, player.Creature, null);
        await PowerCmd.Apply<FocusPower>(choice, player.Creature, 2, player.Creature, null);
        await (Task)AccessTools.Method(typeof(ShurikenOrb), "AddStock").Invoke(null, [choice, player, 10])!;
        var top = new List<CardModel>();
        for (int i = 0; i < 5; i++) top.Add(await Add<DefendIronclad>(PileType.Draw));
        var handCard = await Add<DefendIronclad>();
        var insight = await Add<TechniqueSearchRedesignV1>();
        int before = combat.HittableEnemies.Sum(enemy => enemy.CurrentHp);
        await CardCmd.AutoPlay(choice, insight, null);
        Require(top.Take(2).All(card => card.Pile?.Type == PileType.Hand && card.Keywords.Contains(CardKeyword.Sly))
            && !handCard.Keywords.Contains(CardKeyword.Sly), "Insight grants Sly only to directly drawn cards.");
        await Snapshot("insight-sly-draw");
        await CardCmd.Discard(choice, top.Take(2).ToArray());
        Require(PileType.Hand.GetPile(player).Cards.OfType<StrongShurikenTokenRedesignV1>().Count() == 1
            && before - combat.HittableEnemies.Sum(enemy => enemy.CurrentHp) == 16 && player.Creature.Block == 10,
            "Granted Sly must play both discards without additional Starless tokens.");
        await Snapshot("insight-after-discard");
        _checkpoints.Write("v115.live-insight-sly-stock-gain");
        var token = PileType.Hand.GetPile(player).Cards.OfType<StrongShurikenTokenRedesignV1>().Single();
        await CardCmd.AutoPlay(choice, token, victim);
        Require(token.Pile?.Type == PileType.Exhaust, "Snapshot Shuriken finishes its native throw in Exhaust.");
        await ClearCards();
        for (int i = 0; i < 5; i++) await Add<Wound>(PileType.Draw);
        await CardCmd.AutoPlay(choice, await Add<ShurikenDraw>(), null);
        await (Task)AccessTools.Method(typeof(ShurikenOrb), "AddStock").Invoke(null, [choice, player, 3])!;
        Require(PileType.Hand.GetPile(player).Cards.OfType<Wound>().Count() == 1
            && PileType.Hand.GetPile(player).Cards.OfType<StrongShurikenTokenRedesignV1>().Count() == 1,
            "Three additional layers trigger one draw and one snapshot token.");
        await Snapshot("stock-gain-powers");
        _checkpoints.Write("v115.live-stock-gain-replenishment");
        Task playing;

        await ClearCards();
        var exhausted = await Add<Wound>();
        await CardCmd.Exhaust(choice, exhausted);
        await CardCmd.Exhaust(choice, await Add<DefendIronclad>());
        playing = CardCmd.AutoPlay(choice, await Add<Excavate>(), null);
        await ChooseGrid([exhausted], "excavate");
        await playing;
        Require(PileType.Draw.GetPile(player).Cards.First() == exhausted, "Excavate did not put the selected exhausted card on top.");
        _checkpoints.Write("v0217.live-excavate");

        var narration = (ModSettingsValueBinding<NinjaSlayerSettingsData, bool>)AccessTools.Field(typeof(NinjaSlayerSettings), "_narration").GetValue(null)!;
        Require(narration.Read(), "Narration must default on in a new isolated profile.");
        var observer = new Harmony("NinjaSlayer.SmokeDriver.NarrationV0217");
        observer.Patch(AccessTools.Method(typeof(SfxCmd), nameof(SfxCmd.Play), [typeof(string), typeof(float)]),
            prefix: new HarmonyMethod(typeof(V0217NarrationObserver), nameof(V0217NarrationObserver.Prefix)));
        try
        {
            V0217NarrationObserver.Played.Clear();
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.PangbaiScaryEvent);
            narration.Write(false);
            narration.Save();
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.PangbaiDragonFlyingKickEvent);
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.NinjaSlayerFastAttackEvent);
            await WaitFrames(100);
            Require(V0217NarrationObserver.Played.SequenceEqual([NinjaSlayerAudio.NinjaSlayerFastAttackEvent]),
                "Narration toggle must suppress both queued and newly requested narration without muting character voice.");
            narration.Write(true);
            narration.Save();
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.PangbaiScaryEvent);
            await WaitFrames(100);
            Require(V0217NarrationObserver.Played.Contains(NinjaSlayerAudio.PangbaiScaryEvent), "Reenabled narration did not play.");
        }
        finally { observer.UnpatchAll(observer.Id); }
        _checkpoints.Write("v0217.live-narration-toggle");

        await ClearCards();
        foreach (var power in player.Creature.Powers.ToArray()) await PowerCmd.Remove(power);
        foreach (var type in new[] { typeof(Slaughter), typeof(Zanshin), typeof(ShurikenDraw), typeof(Excavate),
            typeof(ChopStrikeRedesignV1), typeof(KillingIntentRedesignV1), typeof(GiantShurikenRedesignV1) })
            await CardPileCmd.Add(combat.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(type)), player), PileType.Hand);
        await Snapshot("new-cards");
        await ClearCards();
        for (int i = 0; i < 3; i++) await Add<BlackFlameRedesignV1>();
        foreach (var enemy in combat.HittableEnemies) enemy.SetCurrentHpInternal(1);
        player.Creature.SetMaxHpInternal(200);
        player.Creature.SetCurrentHpInternal(200);
        PlayerCmd.EndTurn(player, canBackOut: false, actionDuringEnemyTurn: () => Task.CompletedTask);
        await WaitUntilAsync(() => !CombatManager.Instance.IsInProgress, "Black Flame did not end the battle.", ct);
        Require(player.Creature.CurrentHp == 196, $"Only the lethal Black Flame should self-damage, actual HP {player.Creature.CurrentHp}.");
        await Snapshot("black-flame-victory");
        _checkpoints.Write("v0217.live-black-flame-stops-after-win");
        _checkpoints.Write("balance-v0217.completed");

        async Task<T> Add<T>(PileType pile = PileType.Hand) where T : CardModel
        {
            var card = combat.CreateCard<T>(player);
            await CardPileCmd.Add(card, pile);
            return card;
        }
        async Task ClearCards()
        {
            foreach (var card in player.Piles.Where(p => p.IsCombatPile).SelectMany(p => p.Cards).ToArray())
                await CardPileCmd.RemoveFromCombat(card);
        }
        async Task Snapshot(string label)
        {
            await WaitFrames(45);
            _tree.Root.GetTexture().GetImage().SavePng(Path.Combine(directory, label + ".png"));
        }
        async Task ChooseGrid(CardModel[] selected, string label)
        {
            await WaitUntilAsync(() => UiHelper.FindFirst<NSimpleCardSelectScreen>(_tree.Root) != null, "Grid selection missing.", ct);
            var screen = UiHelper.FindFirst<NSimpleCardSelectScreen>(_tree.Root)!;
            await Snapshot(label);
            foreach (var card in selected)
            {
                var entry = screen.GetNode<NCardGrid>("%CardGrid").CurrentlyDisplayedCardHolders.Single(h => h.CardModel == card);
                entry.EmitSignal(NCardHolder.SignalName.Pressed, entry);
            }
            await UiHelper.Click(screen.GetNode<NConfirmButton>("%Confirm"));
            await WaitUntilAsync(() => !GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree(), "Grid selection did not close.", ct);
        }
    }
}

internal static class V0217NarrationObserver
{
    internal static readonly List<string> Played = [];
    public static void Prefix(string __0) => Played.Add(__0);
}
