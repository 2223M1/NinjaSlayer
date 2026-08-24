using Godot;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards;

internal static class ShurikenCombat
{
    internal static async Task PlayStockTokenAnimation(Player owner)
    {
        if (!LocalContext.IsMe(owner) || NCombatRoom.Instance is not { } room)
        {
            return;
        }

        ShurikenCard token = (ShurikenCard)ModelDb.Card<ShurikenCard>().ToMutable();
        token.Owner = owner;
        token.AfterCreated();

        NCard? node = NCard.Create(token);
        if (node == null)
        {
            return;
        }

        room.Ui.AddChildSafely(node);
        node.UpdateVisuals(PileType.Play, CardPreviewMode.Normal);
        node.Position = PileType.Hand.GetTargetPosition(node);
        Vector2 startPosition = node.GlobalPosition;
        room.Ui.AddToPlayContainer(node);
        node.GlobalPosition = startPosition;
        node.AnimCardToPlayPile();
        if (node.PlayPileTween is { } playTween)
        {
            await playTween.AwaitFinished(room);
        }

        Tween cleanup = node.CreateTween();
        cleanup.TweenInterval(0.6f);
        cleanup.TweenProperty(node, "modulate:a", 0f, 0.15f);
        cleanup.TweenCallback(Callable.From(node.QueueFreeSafely));
    }

    internal static async Task PlayStockThrowAnimation(Creature owner, Creature target)
    {
        await HopAnimation.Play(owner);
        NDebugAudioManager.Instance?.Play(TmpSfx.daggerThrow);
        target.GetVfxContainer()?.AddChildSafely(NShivThrowVfx.Create(owner, target, Colors.Green));
        await Cmd.CustomScaledWait(0.15f, 0.15f);
    }

    internal static async Task TriggerStockShot(
        PlayerChoiceContext choiceContext,
        Creature owner,
        Creature target,
        CardModel? source)
    {
        Player player = owner.Player
            ?? throw new InvalidOperationException("Shuriken stock requires a player owner.");
        await PlayStockTokenAnimation(player);
        await PlayStockThrowAnimation(owner, target);
        int bonus = owner.GetPower<ShurikenDamagePower>()?.Amount ?? 0;
        await Code.Compatibility.GameCompatibility.Damage.Deal(
            choiceContext,
            [target],
            RedesignV1Rules.ShurikenDamage(bonus),
            MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered
                | MegaCrit.Sts2.Core.ValueProps.ValueProp.Move,
            owner,
            source,
            null);
    }

    internal static bool HasSoarSpread(CardModel card) =>
        card.IsMutable && card.Owner != null && card.Owner.Creature.HasPower<HellTornadoPower>();

    internal static AttackCommand BuildAttackCommand(
        CardModel card,
        CardPlay cardPlay,
        DynamicVar damage,
        ICombatState? combatState)
    {
        var command = DamageCmd.Attack(damage.BaseValue)
            .FromCard(card, cardPlay)
            .WithNoAttackerAnim()
            .AfterAttackerAnim(() => HopAnimation.Play(card.Owner!.Creature))
            .WithHitFx(null, null, TmpSfx.daggerThrow);

        if (HasSoarSpread(card))
        {
            Creature? vfxTarget = cardPlay.Target!;
            if (vfxTarget == null && combatState?.HittableEnemies is { Count: > 0 } enemies)
            {
                vfxTarget = enemies[^1];
            }

            return command
                .TargetingAllOpponents(combatState ?? throw new InvalidOperationException("Shuriken attacks require combat."))
                .WithHitVfxNode(_ => vfxTarget == null ? null : NShivThrowVfx.Create(card.Owner!.Creature, vfxTarget, Colors.Green));
        }
        return command
            .Targeting(cardPlay.Target!)
            .WithHitVfxNode(t => NShivThrowVfx.Create(card.Owner!.Creature, t, Colors.Green));
    }
}
