using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
using MegaCrit.Sts2.Core.Nodes.Vfx;
#endif
using MegaCrit.Sts2.Core.Nodes.Vfx.Cards;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.Lifecycle;

internal static class RapidCardPresentationContext
{
    private static readonly AsyncLocal<ScopeFrame?> Current = new();

    public static ScopeLease Begin(CardModel card)
    {
        ScopeFrame? previous = Current.Value;
        bool active = NinjaSlayerPatchCapabilities.RapidCardResolutionEnabled
            && card.Owner.Creature.Player?.Character is INinjaSlayerCharacter;
        Current.Value = new ScopeFrame(active, previous);
        CombatPresentationPacingScope.ScopeLease? pacing = active
            ? CombatPresentationPacingScope.Begin(CombatPresentationPacingPolicy.RapidCard)
            : null;
        return new ScopeLease(previous, pacing);
    }

    public static Task WaitUnlessActive(
        float fastDuration,
        float duration,
        bool ignoreFastMode = false,
        CancellationToken cancellationToken = default) =>
        IsActive
            ? Task.CompletedTask
            : Cmd.CustomScaledWait(fastDuration, duration, ignoreFastMode, cancellationToken);

    public static Task<bool> AwaitTweenUnlessActive(Tween tween, Node owner)
    {
        if (!IsActive)
        {
            return tween.AwaitFinished(owner);
        }

        TaskHelper.RunSafely(tween.AwaitFinished(owner));
        return Task.FromResult(true);
    }

#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
    public static async Task Exhaust(
        PlayerChoiceContext choiceContext,
        CardModel card,
        bool causedByEthereal,
        bool skipVisuals)
    {
        if (!IsActive || skipVisuals)
        {
            await CardCmd.Exhaust(choiceContext, card, causedByEthereal, skipVisuals);
            return;
        }

        NCard? cardNode = FindOrCreateCardNode(card);
        await CardCmd.Exhaust(
            choiceContext,
            card,
            causedByEthereal,
            skipVisuals: true);
        StartLegacyExhaust(cardNode);
    }
#else
    public static async Task<CardPileAddResult?> Exhaust(
        PlayerChoiceContext choiceContext,
        CardModel card,
        bool causedByEthereal,
        bool skipVisuals)
    {
        if (!IsActive || skipVisuals)
        {
            return await CardCmd.Exhaust(choiceContext, card, causedByEthereal, skipVisuals);
        }

        NCard? cardNode = FindOrCreateCardNode(card);
        CardPileAddResult? result = await CardCmd.Exhaust(
            choiceContext,
            card,
            causedByEthereal,
            skipVisuals: true);
        StopPlayPileTween(cardNode);
        if (cardNode != null && NCardExhaustQuickVfx.Create(cardNode) is { } exhaustVfx)
        {
            _ = TaskHelper.RunSafely(exhaustVfx.PlayAnimation());
        }

        return result;
    }
#endif

    public static async Task RemoveFromCombat(CardModel card, bool skipVisuals)
    {
        if (!IsActive || skipVisuals)
        {
            await CardPileCmd.RemoveFromCombat(card, skipVisuals);
            return;
        }

        NCard? cardNode = NCard.FindOnTable(card);
        await CardPileCmd.RemoveFromCombat(card, skipVisuals: true);
        if (card.Type == CardType.Power)
        {
            return;
        }

        if (cardNode != null && GodotObject.IsInstanceValid(cardNode))
        {
            StopPlayPileTween(cardNode);
            Tween tween = cardNode.CreateTween();
            tween.Parallel().TweenProperty(cardNode, "modulate:a", 0f, 0.15f);
            tween.Parallel().TweenProperty(cardNode, "scale", Vector2.Zero, 0.15f);
            tween.TweenCallback(Callable.From(cardNode.QueueFreeSafely));
        }
    }

    public static bool IsActive => Current.Value?.IsActive == true;

    public static void PreparePowerFly(CardModel card)
    {
        if (IsActive)
        {
            StopPlayPileTween(NCard.FindOnTable(card));
        }
    }

#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
    private static void StartLegacyExhaust(NCard? cardNode)
    {
        if (cardNode == null
            || !GodotObject.IsInstanceValid(cardNode)
            || NCombatRoom.Instance is not { } room)
        {
            return;
        }

        StopPlayPileTween(cardNode);
        if (NExhaustVfx.Create(cardNode) is { } exhaustVfx)
        {
            room.Ui.AddChildSafely(exhaustVfx);
        }

        Tween tween = cardNode.CreateTween().SetParallel();
        tween.TweenProperty(cardNode, "modulate:a", 0f, 0.15f);
        tween.TweenProperty(cardNode, "scale", Vector2.Zero, 0.15f);
        tween.Chain().TweenCallback(Callable.From(cardNode.QueueFreeSafely));
    }
#endif

    private static NCard? FindOrCreateCardNode(CardModel card)
    {
        NCard? node = NCard.FindOnTable(card);
        if (node != null || NCombatRoom.Instance is not { } room)
        {
            return node;
        }

        node = NCard.Create(card);
        if (node == null)
        {
            return null;
        }

        room.Ui.AddChildSafely(node);
        node.Position = (card.Pile?.Type ?? PileType.Play).GetTargetPosition(node);
        node.UpdateVisuals(PileType.Play, CardPreviewMode.Normal);
        return node;
    }

    private static void StopPlayPileTween(NCard? cardNode)
    {
        if (cardNode == null || !GodotObject.IsInstanceValid(cardNode))
        {
            return;
        }

        if (cardNode.PlayPileTween is { } tween && tween.IsValid())
        {
            tween.Kill();
        }

        cardNode.PlayPileTween = null;
    }

    internal sealed record ScopeFrame(bool IsActive, ScopeFrame? Previous);

    internal readonly struct ScopeLease(
        ScopeFrame? previous,
        CombatPresentationPacingScope.ScopeLease? pacing)
    {
        public void RestoreCallerContext()
        {
            pacing?.Dispose();
            Current.Value = previous;
        }
    }
}
