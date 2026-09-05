using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class OnlyKarateRedesignV1 : RedesignV1RareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    public OnlyKarateRedesignV1()
        : base(nameof(OnlyKarateRedesignV1), nameof(OneBodyOneSoul), 1, CardType.Skill, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int current = Owner.Creature.GetPowerAmount<KaratePower>();
        return current > 0
            ? PowerCmd.Apply<KaratePower>(choiceContext, Owner.Creature, current, Owner.Creature, this)
            : Task.CompletedTask;
    }

    protected override void OnUpgrade() => AddKeyword(CardKeyword.Retain);
}
