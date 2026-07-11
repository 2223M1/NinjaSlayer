using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.ExternalAnimations;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Models;

namespace NinjaSlayer.Content;

[RegisterSingleton]
public sealed class NinjaSlayerCombatFeedback : NinjaSlayerCombatSingletonTemplate
{
    private bool _lowHealthLinePlayed;

    public override async Task BeforeCombatStart()
    {
        _lowHealthLinePlayed = false;

        ICombatState? combatState = CombatManager.Instance.DebugOnlyGetState();
        if (combatState == null)
        {
            return;
        }

        foreach (Player player in combatState.Players)
        {
            if (!IsNinjaSlayer(player))
            {
                continue;
            }

            if (NinjaSlayerRunData.ConsumePendingAncientEntranceAnimation(player))
            {
                await AncientEntranceAnimation.Play(player);
            }
        }
    }

    public override Task AfterCombatEnd(CombatRoom room)
    {
        _lowHealthLinePlayed = false;
        return Task.CompletedTask;
    }

    public override async Task AfterPowerAmountChanged(
        PlayerChoiceContext choiceContext,
        PowerModel power,
        decimal amount,
        Creature? applier,
        CardModel? cardSource)
    {
        if (power.Owner.Player?.Character is not NinjaSlayerCharacter
            || power.GetTypeForAmount(amount) != PowerType.Debuff)
        {
            return;
        }

        await ShakeAnimation.Play(power.Owner);
    }

    public override Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target.Player?.Character is not NinjaSlayerCharacter)
        {
            return Task.CompletedTask;
        }

        TryPlayLowHealthVoice(target, result, dealer);

        if (!props.IsPoweredAttack())
        {
            return Task.CompletedTask;
        }

        if (result.UnblockedDamage > 0)
        {
            return Task.CompletedTask;
        }

        if (result.BlockedDamage > 0)
        {
            _ = CreatureCmd.TriggerAnim(target, "BlockedHit", 0.2f);
        }

        return Task.CompletedTask;
    }

    private void TryPlayLowHealthVoice(Creature target, DamageResult result, Creature? dealer)
    {
        if (_lowHealthLinePlayed || result.UnblockedDamage <= 0)
        {
            return;
        }

        if (ShouldSuppressLowHealthVoiceForWaterfallGiantExplode(dealer)
            && target.CurrentHp * 3 < target.MaxHp)
        {
            return;
        }

        if (target.CurrentHp * 3 >= target.MaxHp)
        {
            return;
        }

        _lowHealthLinePlayed = true;
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.PangbaiLowHealthEvent);
    }

    private static bool ShouldSuppressLowHealthVoiceForWaterfallGiantExplode(Creature? dealer)
    {
        if (dealer?.Monster is not WaterfallGiant giant)
        {
            return false;
        }

        return giant.NextMove.Id == "EXPLODE_MOVE";
    }
}
