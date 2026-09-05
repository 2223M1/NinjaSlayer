using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class AdversityCarapaceRedesignV1 : RedesignV1UncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<VigorPower>(6)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromCard<ChadoEnergyRedesignV1>(), HoverTipFactory.FromPower<VigorPower>()];

    public AdversityCarapaceRedesignV1()
        : base(nameof(AdversityCarapaceRedesignV1), "IronShirt", 1, CardType.Power, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<VitalityTeaPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars[nameof(VigorPower)].BaseValue,
            Owner.Creature,
            this);

    protected override void OnUpgrade() => DynamicVars[nameof(VigorPower)].UpgradeValueBy(2);
}
