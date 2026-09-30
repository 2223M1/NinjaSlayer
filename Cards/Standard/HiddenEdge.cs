using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.Standard;

public sealed class HiddenEdge : NinjaSlayerUncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new PowerVar<FocusPower>(3), new PowerVar<DexterityPower>(1)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<DexterityPower>(), HoverTipFactory.FromPower<FocusPower>()];

    public HiddenEdge()
        : base(nameof(HiddenEdge), 1, CardType.Power, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<FocusPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars[nameof(FocusPower)].BaseValue,
            Owner.Creature,
            this);
        await PowerCmd.Apply<DexterityPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
    }

    protected override void OnUpgrade() => DynamicVars[nameof(FocusPower)].UpgradeValueBy(2);
}
