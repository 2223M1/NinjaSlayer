using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.Standard;

public sealed class Training : NinjaSlayerUncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new KarateVar(2)];

    public Training()
        : base(nameof(Training), 2, CardType.Power, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<TrainingPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.Karate().BaseValue,
            Owner.Creature,
            this);

    protected override void OnUpgrade() => DynamicVars.Karate().UpgradeValueBy(1);
}
