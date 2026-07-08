using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

[RegisterPower]
public sealed class EvasionPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.For(GetType());

    public override decimal ModifyDamageCap(Creature? target, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
    {
        if (target != Owner || Amount <= 0)
        {
            return decimal.MaxValue;
        }

        if (dealer is not { IsMonster: true } || !props.IsCardOrMonsterMove())
        {
            return decimal.MaxValue;
        }

        return 0m;
    }

    public override async Task AfterModifyingDamageAmount(CardModel? cardSource)
    {
        Flash();
        var audio = NinjaSlayerCombatAudioSet.For(Owner);
        NinjaSlayerCombatAudioSet.Play(audio.FastAttack);
        _ = FastAttackAnimation.Play(Owner, Owner.Player?.Character?.AttackAnimDelay ?? 0.15f, reverseDirection: true);
        await PowerCmd.Decrement(this);
    }
}
