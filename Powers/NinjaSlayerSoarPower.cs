using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

[RegisterPower]
public sealed class NinjaSlayerSoarPower : ModPowerTemplate
{
    private const float RiseDistance = 220f;
    private const float RiseDuration = 0.3f;
    private const string DamageDecreaseKey = "DamageDecrease";

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.For(GetType());

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DynamicVar(DamageDecreaseKey, 50)
    ];

    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target != Owner)
        {
            return 1m;
        }

        if (!props.IsPoweredAttack())
        {
            return 1m;
        }

        return DynamicVars[DamageDecreaseKey].BaseValue / 100m;
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

    private static async Task AutoPlayDeckShuriken(PlayerChoiceContext choiceContext, Player player)
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

                if (creature.HasPower<NarakuPower>())
                {
                    SpinComboAudio.PlayNarakuSlowAttack(creature);
                }

                await CardCmd.AutoPlay(choiceContext, shuriken, target, AutoPlayType.Default, skipXCapture: false, !first);
                SoarSpinAnimation.EnsureAirborneSpin(creature);
                first = false;
            }
        });
    }
}
