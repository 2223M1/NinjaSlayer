using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Lifecycle;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class HellTornadoRedesignPower : NinjaSlayerPowerTemplate
{
    private const float RiseDistance = 220f;
    private const float RiseDuration = 0.3f;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerAssetProfile AssetProfile =>
        NinjaSlayerPowerAssets.Named("HellTornadoPower");

    public override async Task AfterPowerAmountChanged(
        PlayerChoiceContext choiceContext,
        PowerModel power,
        decimal amount,
        Creature? applier,
        CardModel? cardSource)
    {
        if (power != this || amount <= 0)
        {
            return;
        }

        Flash();
        NinjaSlayerRapidAnimationCoordinator.CancelVisualTailForAction(Owner);
        Task presentation = Task.WhenAll(
            ByrdRiseAnimation.Play(Owner, RiseDistance),
            SoarSpinAnimation.Accelerate(Owner, RiseDuration));
        if (RapidCardPresentationContext.IsActive)
        {
            _ = TaskHelper.RunSafely(presentation);
        }
        else
        {
            await presentation;
        }
    }

    public override async Task AfterAutoPrePlayPhaseEnteredEarly(
        PlayerChoiceContext choiceContext,
        Player player)
    {
        if (player != Owner.Player)
        {
            return;
        }

        Flash();
        if (ShurikenOrb.Find(player) is { } stock)
        {
            await stock.FireConsumedVolley(choiceContext, 1, null);
        }

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
}
