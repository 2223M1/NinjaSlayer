using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.Standard;

public sealed class Relentless : NinjaSlayerRareCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(1)];

    public Relentless()
        : base(nameof(Relentless), 2, CardType.Power, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<RelentlessPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.Cards.BaseValue,
            Owner.Creature,
            this);

    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(1);
}
