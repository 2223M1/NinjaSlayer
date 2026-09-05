using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class MomentumRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<FocusPower>(1)];

    public MomentumRedesignV1()
        : base(nameof(MomentumRedesignV1), "Momentum", 1, CardType.Power, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<MomentumRedesignPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars[nameof(FocusPower)].BaseValue,
            Owner.Creature,
            this);

    protected override void OnUpgrade() => DynamicVars[nameof(FocusPower)].UpgradeValueBy(1);
}
