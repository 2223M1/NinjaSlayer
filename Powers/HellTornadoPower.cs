using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class HellTornadoPower : NinjaSlayerPowerTemplate
{
    private const float RiseDistance = 220f;
    private const float RiseDuration = 0.3f;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    internal bool IsResolvingSoarAutoPlay { get; private set; }

    public override int ModifyCardPlayCount(CardModel card, Creature? target, int playCount)
    {
        if (!IsResolvingSoarAutoPlay || card.Owner.Creature != Owner)
        {
            return playCount;
        }

        if (!card.Tags.Contains(NinjaSlayerCardTags.Shuriken))
        {
            return playCount;
        }

        return playCount + 1;
    }

    public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        if (power == this && amount > 0)
        {
            Flash();
            await Task.WhenAll(
                ByrdRiseAnimation.Play(Owner, RiseDistance),
                SoarSpinAnimation.Accelerate(Owner, RiseDuration));
        }
    }

    public override async Task AfterAutoPrePlayPhaseEnteredEarly(PlayerChoiceContext choiceContext, Player player)
    {
        if (player != Owner.Player)
        {
            return;
        }

        Flash();
        await AutoPlayDeckShuriken(choiceContext, player);
        await SoarSpinAnimation.Decelerate(Owner, RiseDuration);
        await ByrdFallAnimation.Play(Owner, RiseDistance);
        NinjaSlayerCombatAnimations.StopSoarSpinAndReturnToIdle(Owner);
        await PowerCmd.Remove<SoarPower>(Owner);
        await PowerCmd.Remove(this);
    }

    public override Task AfterRemoved(Creature oldOwner)
    {
        if (SoarVisualState.IsAirborne(oldOwner))
        {
            SoarVisualState.ResetVisualsToGround(oldOwner);
            HopAnimation.SyncBasePosition(oldOwner, Vector2.Zero);
        }

        NinjaSlayerCombatAnimations.StopSoarSpinAndReturnToIdle(oldOwner);
        return Task.CompletedTask;
    }

    private async Task AutoPlayDeckShuriken(PlayerChoiceContext choiceContext, Player player)
    {
        ICombatState combatState = player.Creature.CombatState ?? throw new InvalidOperationException("Soar requires combat.");
        List<CardModel> shurikens = player.PlayerCombatState?.AllCards
            .Where(c => c.Tags.Contains(NinjaSlayerCardTags.Shuriken))
            .Where(c => c.Pile?.Type is PileType.Hand or PileType.Draw or PileType.Discard)
            .ToList() ?? [];
        if (shurikens.Count == 0)
        {
            return;
        }

        var creature = player.Creature;
        float perHitDuration = player.Character?.AttackAnimDelay ?? 0.15f;

        IsResolvingSoarAutoPlay = true;
        try
        {
            await SpinComboAudio.PlaySequence(creature, shurikens.Count, perHitDuration, async () =>
            {
                bool first = true;
                foreach (CardModel shuriken in shurikens)
                {
                    if (!combatState.IsLiveCombat() || !combatState.HittableEnemies.Any())
                    {
                        break;
                    }

                    Creature? target = player.RunState.Rng.CombatTargets.NextItem(combatState.HittableEnemies);
                    if (target == null)
                    {
                        continue;
                    }

                    if (NinjaSlayerFormState.IsFullyReleasedNaraku(creature))
                    {
                        SpinComboAudio.PlayNarakuSlowAttack(creature);
                    }

                    await CardCmd.AutoPlay(choiceContext, shuriken, target, AutoPlayType.Default, skipXCapture: false, !first);
                    SoarSpinAnimation.EnsureAirborneSpin(creature);
                    first = false;
                }
            });
        }
        finally
        {
            IsResolvingSoarAutoPlay = false;
        }
    }
}
