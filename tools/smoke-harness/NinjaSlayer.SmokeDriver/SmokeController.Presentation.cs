using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;
using NinjaSlayer.Potions;
using NinjaSlayer.Powers;
using NinjaSlayer.Relics;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task VerifyCurrentPresentation(ICombatState combat, Player player, Creature target,
        CancellationToken cancellationToken)
    {
        await VerifyCardPresentation(combat, player);
        var choice = new BlockingPlayerChoiceContext();
        int originalHp = target.CurrentHp;
        int originalMaxHp = target.MaxHp;
        target.SetMaxHpInternal(1000);
        await CreatureCmd.SetCurrentHp(target, 1000);
        var visuals = NCombatRoom.Instance!.GetCreatureNode(player.Creature)!.Visuals;
        Node2D anchor = NinjaSlayerVisualRig.GetAirborneAnchor(visuals)!;

        // Applying the power outside a card exercises its awaited presentation path.
        var normalTornado = await PowerCmd.Apply<HellTornadoRedesignPower>(choice, player.Creature, 1, player.Creature, null);
        Require(SoarVisualState.IsAirborne(player.Creature) && anchor.Position.Y < -200,
            "Hell Tornado's normal application returned before rising.");
        await PowerCmd.Remove(normalTornado!);
        Require(!SoarVisualState.IsAirborne(player.Creature) && anchor.Position.IsZeroApprox(),
            "Removing Hell Tornado left the body airborne.");

        await PlayerCmd.SetEnergy(10, player);
        var stockCard = combat.CreateCard<PreparedShurikenRedesignV1>(player);
        await CardPileCmd.Add(stockCard, PileType.Hand);
        await CardCmd.AutoPlay(choice, stockCard, player.Creature);
        var tornado = combat.CreateCard<HellTornadoRedesignV1>(player);
        await CardPileCmd.Add(tornado, PileType.Hand);
        await CardCmd.AutoPlay(choice, tornado, player.Creature);
        Require(player.PlayerCombatState!.OrbQueue.Orbs.OfType<ShurikenOrb>().Single().StackCount == 2,
            "Hell Tornado did not double stock.");
        await WaitUntilAsync(() => anchor.Position.Y < -200, "Rapid Hell Tornado did not finish rising.", cancellationToken);
        await CapturePresentation("hell-tornado-airborne");
        var enemyTurn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PlayerCmd.EndTurn(player, canBackOut: false, actionDuringEnemyTurn: () =>
        {
            enemyTurn.TrySetResult();
            return Task.CompletedTask;
        });
        await WaitTaskAsync(enemyTurn.Task, "Hell Tornado enemy turn did not start.", DefaultTimeout);
        await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play
                && !player.Creature.HasPower<HellTornadoRedesignPower>(),
            "Hell Tornado did not finish its next-turn volley.", cancellationToken);
        Require(!SoarVisualState.IsAirborne(player.Creature) && anchor.Position.IsZeroApprox()
            && !player.Creature.HasPower<SoarPower>()
            && !player.PlayerCombatState!.OrbQueue.Orbs.OfType<ShurikenOrb>().Any(),
            "Hell Tornado did not release its stock, Soar and airborne visuals.");
        await CapturePresentation("hell-tornado-landed");
        _checkpoints.Write("hell-tornado.presentation-completed");

        var potion = await PotionCmd.TryToProcure<ZbrAmpoulePotion>(player);
        Require(potion.success, "ZBR could not be procured for smoke.");
        int lifeBefore = player.Creature.GetPowerAmount<NarakuLifePower>();
        int strengthBefore = player.Creature.GetPowerAmount<StrengthPower>();
        await potion.potion.OnUseWrapper(choice, player.Creature);
        Require(player.Creature.GetPowerAmount<NarakuLifePower>() == lifeBefore + 12
            && player.Creature.GetPowerAmount<StrengthPower>() == strengthBefore
            && NinjaSlayerFormState.GetPresentation(player.Creature).Kind == NinjaSlayerFormKind.Normal,
            "ZBR changed Strength or form, or granted the wrong Naraku Life.");
        await PlayerCmd.SetEnergy(10, player);
        var formCard = combat.CreateCard<NarakuFormRedesignV1>(player);
        await CardPileCmd.Add(formCard, PileType.Hand);
        await CardCmd.AutoPlay(choice, formCard, player.Creature);
        await WaitFrames(3);
        var overlay = FindDescendant<NarakuVisualOverlay>(visuals)!;
        Require(overlay.Visible && overlay.Texture.ResourcePath.StartsWith(NinjaSlayerFormPresentationCatalog.NarakuIdleTexturePrefix),
            "Naraku Form did not render the half-Naraku texture.");
        await CapturePresentation("naraku-half");
        await PowerCmd.Remove<NarakuFormRedesignPower>(player.Creature);
        var relic = await RelicCmd.Obtain<NarakuWithinRelic>(player);
        await relic.BeforeCombatStart();
        await WaitFrames(3);
        Require(overlay.Visible && overlay.Texture.ResourcePath == NinjaSlayerFormPresentationCatalog.FullyReleasedNarakuTexturePath
            && player.Creature.HasPower<NarakuFormRedesignPower>(),
            "The event relic did not render full Naraku with the current card's power.");
        await CapturePresentation("naraku-full");
        await PowerCmd.Remove<NarakuFormRedesignPower>(player.Creature);
        await RelicCmd.Remove(relic);
        await PowerCmd.Remove<NarakuLifePower>(player.Creature);
        await WaitFrames(3);
        Require(!overlay.Visible, "Normal form retained a Naraku overlay.");
        _checkpoints.Write("naraku.presentation-completed");
        target.SetMaxHpInternal(originalMaxHp);
        await CreatureCmd.SetCurrentHp(target, originalHp);
    }

    private async Task VerifyCardPresentation(ICombatState combat, Player player)
    {
        CardModel[] cards = typeof(NarakuFormRedesignV1).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(CardModel).IsAssignableFrom(type))
            .Select(type => ModelDb.GetById<CardModel>(ModelDb.GetId(type))).ToArray();
        foreach (var card in cards.Cast<ModCardTemplate>())
        {
            string path = card.AssetProfile.PortraitPath!;
            Texture2D texture = GD.Load<Texture2D>(path);
            Require(texture is not null && texture.GetWidth() > 0 && texture.GetHeight() > 0,
                $"Card portrait did not load: {path}.");
        }
        foreach (Type type in typeof(NarakuFormRedesignV1).Assembly.GetTypes()
                     .Where(type => !type.IsAbstract && typeof(PowerModel).IsAssignableFrom(type)))
        {
            PowerModel power = ModelDb.GetById<PowerModel>(ModelDb.GetId(type));
            Texture2D icon = power.Icon;
            Require(icon is not null && icon.GetWidth() > 0 && icon.GetHeight() > 0,
                $"Power icon did not load: {power.Id}.");
        }
        var layer = new CanvasLayer { Layer = 100 };
        _tree.Root.AddChild(layer);
        Vector2 viewportSize = _tree.Root.GetVisibleRect().Size;
        layer.AddChild(new ColorRect { Color = new Color("151b1d"), Size = viewportSize });
        var vanillaCard = combat.CreateCard<MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad>(player);
        NCard vanillaNode = NCard.Create(vanillaCard)!;
        layer.AddChild(vanillaNode);
        vanillaNode.Hide();
        Font vanillaFont = vanillaNode.GetNode<MegaLabel>("%TitleLabel").GetThemeFont(ThemeConstants.Label.Font);
        CardModel[] examples = [combat.CreateCard<StrikeNinjaSlayerRedesignV1>(player),
            combat.CreateCard<NarakuFormRedesignV1>(player), combat.CreateCard<TornadoFistRedesignV1>(player)];
        for (int index = 0; index < examples.Length; index++)
        {
            NCard node = NCard.Create(examples[index])!;
            layer.AddChild(node);
            node.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
            node.Position = new Vector2(viewportSize.X * (index + 1) / 4, viewportSize.Y / 2);
            Require(node.GetNode<MegaLabel>("%TitleLabel").GetThemeFont(ThemeConstants.Label.Font) == vanillaFont,
                "A mod card's title font differs from the vanilla card title font.");
        }
        await CapturePresentation("english-card-titles");
        layer.QueueFree();
        combat.RemoveCard(vanillaCard);
        foreach (CardModel card in examples) combat.RemoveCard(card);
        _checkpoints.Write("cards.presentation-validated", data: new System.Text.Json.Nodes.JsonObject { ["count"] = cards.Length });
    }

    private async Task CapturePresentation(string name)
    {
        await WaitFrames(90);
        await _tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string directory = Path.GetDirectoryName(_configuration.FailureScreenshotPath)!;
        Error result = _tree.Root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, name + ".png"));
        Require(result == Error.Ok, $"Could not capture {name}: {result}.");
    }
}
