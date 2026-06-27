using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Models;

namespace NinjaSlayer.Content;

[RegisterSingleton]
public sealed class NinjaSlayerCombatFeedback : HookedSingletonModel
{
    private bool _lowHealthLinePlayed;

    public NinjaSlayerCombatFeedback() : base(HookType.Combat)
    {
    }

    public override Task BeforeCombatStart()
    {
        _lowHealthLinePlayed = false;
        return Task.CompletedTask;
    }

    public override Task AfterCombatEnd(CombatRoom room)
    {
        _lowHealthLinePlayed = false;
        return Task.CompletedTask;
    }

    public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target.Player?.Character is not NinjaSlayerCharacter)
        {
            return;
        }

        TryPlayLowHealthVoice(target, result);

        if (!props.IsPoweredAttack())
        {
            return;
        }

        if (result.UnblockedDamage > 0)
        {
            return;
        }

        if (result.BlockedDamage > 0)
        {
            await CreatureCmd.TriggerAnim(target, "BlockedHit", 0.2f);
        }
    }

    private void TryPlayLowHealthVoice(Creature target, DamageResult result)
    {
        if (_lowHealthLinePlayed || result.UnblockedDamage <= 0)
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
}
