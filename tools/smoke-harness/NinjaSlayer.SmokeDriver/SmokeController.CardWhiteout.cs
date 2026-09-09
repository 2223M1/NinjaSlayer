using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Pooling;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using NinjaSlayer.Cards.RedesignV1;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task VerifyTransformPoolReuse(ICombatState combat, Player player)
    {
        var previous = SaveManager.Instance.PrefsSave.FastMode;
        foreach (FastModeType speed in Enum.GetValues<FastModeType>())
        {
            SaveManager.Instance.PrefsSave.FastMode = speed;
            var model = combat.CreateCard<DefendNinjaSlayerRedesignV1>(player);
            PileType.Discard.GetPile(player).AddInternal(model);
            var card = NCard.Create(model)!;
            var ui = NCombatRoom.Instance!.Ui;
            ui.AddChild(card);
            card.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            card.Position = new Vector2(850, 400);
            var shine = NCardTransformShineVfx.Create(card, model, [])!;
            var overlay = (Control)AccessTools.Field(typeof(NCardTransformShineVfx), "_overlay").GetValue(shine)!;
            _ = TaskHelper.RunSafely(shine.PlayAnimation());
            await WaitUntilAsync(() => overlay.SelfModulate.A >= 0.99f, "Transform overlay never reached white.");
            ulong cardId = card.GetInstanceId();
            card.QueueFreeSafely();
            await WaitFrames(3);
            var next = combat.CreateCard<StrikeNinjaSlayerRedesignV1>(player);
            PileType.Discard.GetPile(player).AddInternal(next);
            var reused = NCard.Create(next)!;
            Require(reused.GetInstanceId() == cardId, "The test must exercise the same pooled NCard.");
            ui.AddChild(reused);
            reused.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            reused.Position = new Vector2(850, 400);
            await WaitFrames(8);
            _tree.Root.GetViewport().GetTexture().GetImage().SavePng(
                Path.Combine(Path.GetDirectoryName(_configuration.CheckpointPath)!, $"transform-pool-reuse-{speed}.png"));
            Require(FindDescendant<NCardTransformShineVfx>(reused) is null,
                "A cancelled transform left its white overlay attached to a reused card.");
            reused.QueueFreeSafely();
            await WaitFrames(3);

            var handCard = combat.CreateCard<DefendNinjaSlayerRedesignV1>(player);
            await CardPileCmd.Add(handCard, PileType.Hand);
            var transformed = (await CardCmd.TransformTo<BlackFlameRedesignV1>(handCard))!.Value.cardAdded;
            CardCmd.Upgrade(transformed);
            await WaitUntilAsync(() => FindDescendant<NCardTransformShineVfx>(ui) is null,
                $"A completed hand-card transform left a shine overlay at {speed} speed.");
            await CardCmd.Exhaust(new BlockingPlayerChoiceContext(), transformed);
            _checkpoints.Write("card.transform-pool-reuse", data: new JsonObject
                { ["speed"] = speed.ToString(), ["whiteOverlayRemaining"] = false, ["completedTransformUpgradedAndExhausted"] = true });
        }
        SaveManager.Instance.PrefsSave.FastMode = previous;
    }
}
