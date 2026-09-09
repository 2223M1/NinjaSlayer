using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.Lifecycle;

internal static class RapidCardPresentationContext
{
    private static readonly AsyncLocal<ScopeFrame?> Current = new();

    public static ScopeLease Begin(CardModel card)
    {
        ScopeFrame? previous = Current.Value;
        bool active = card.Owner.Creature.Player?.Character is INinjaSlayerCharacter;
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
