using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Powers;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace NinjaSlayer.Cards.Standard;

public sealed class TurningPoint : NinjaSlayerUncommonCard
{
    public TurningPoint() : base(nameof(TurningPoint), 1, CardType.Power, TargetType.Self) { }
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(1)];
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<TurningPointPower>(choiceContext, Owner.Creature, DynamicVars.Cards.BaseValue, Owner.Creature, this);
    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(1);
}
