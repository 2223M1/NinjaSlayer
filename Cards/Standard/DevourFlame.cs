using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Content;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.Standard;

public sealed class DevourFlame : NinjaSlayerUncommonCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [.. HoverTipFactory.FromCardWithCardHoverTips<BlackFlame>(),
            HoverTipFactory.FromPower<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>()];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>(2)];
    public DevourFlame()
        : base(nameof(DevourFlame), 1, CardType.Power, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<DevourFlamePower>(choiceContext, Owner.Creature,
            DynamicVars["StrengthPower"].BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade() => DynamicVars["StrengthPower"].UpgradeValueBy(1);
}
