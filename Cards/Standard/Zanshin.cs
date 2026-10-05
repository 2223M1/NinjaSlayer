using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.Standard;

public sealed class Zanshin : NinjaSlayerRareCard
{
    public Zanshin() : base(nameof(Zanshin), 1, CardType.Power, TargetType.Self) { }
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(2)];

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<ZanshinPower>(choiceContext, Owner.Creature, DynamicVars.Cards.BaseValue, Owner.Creature, this);

    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(1);
}
