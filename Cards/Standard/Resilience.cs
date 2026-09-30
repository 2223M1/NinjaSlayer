using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Powers;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace NinjaSlayer.Cards.Standard;

public sealed class Resilience : NinjaSlayerUncommonCard
{
    public Resilience() : base(nameof(Resilience), 1, CardType.Power, TargetType.Self) { }
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(1)];
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<ResiliencePower>(choiceContext, Owner.Creature, DynamicVars.Cards.BaseValue, Owner.Creature, this);
    protected override void OnUpgrade()
    {
        DynamicVars.Cards.UpgradeValueBy(1);
        AddKeyword(CardKeyword.Innate);
    }
}
