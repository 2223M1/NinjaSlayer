using NinjaSlayer.Content;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class ChadoStillnessRedesignV1 : RedesignV1CommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Breath", 2), new DynamicVar("Turns", 1)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [NinjaSlayerHoverTips.ChadoBreathing, HoverTipFactory.FromKeyword(CardKeyword.Retain), .. HoverTipFactory.FromCardWithCardHoverTips<ChadoEnergyRedesignV1>()];

    public ChadoStillnessRedesignV1()
        : base(nameof(ChadoStillnessRedesignV1), "Meditation", 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await ChadoBreathCmd.Apply(Owner, DynamicVars["Breath"].IntValue);
        await PowerCmd.Apply<ChadoRetainPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars["Turns"].IntValue,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade() => DynamicVars["Turns"].UpgradeValueBy(1);
}
