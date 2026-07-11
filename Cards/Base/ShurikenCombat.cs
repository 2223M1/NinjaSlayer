using Godot;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards;

internal static class ShurikenCombat
{
    internal static bool HasSoarSpread(CardModel card) =>
        card.IsMutable && card.Owner != null && card.Owner.Creature.HasPower<NinjaSlayerSoarPower>();

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
            Creature? vfxTarget = cardPlay.Target ?? combatState?.HittableEnemies.LastOrDefault();
            return command
                .TargetingAllOpponents(combatState ?? throw new InvalidOperationException("Shuriken attacks require combat."))
                .WithHitVfxNode(_ => vfxTarget == null ? null : NShivThrowVfx.Create(card.Owner!.Creature, vfxTarget, Colors.Green));
        }

        ArgumentNullException.ThrowIfNull(cardPlay.Target);
        return command
            .Targeting(cardPlay.Target)
            .WithHitVfxNode(t => NShivThrowVfx.Create(card.Owner!.Creature, t, Colors.Green));
    }
}
