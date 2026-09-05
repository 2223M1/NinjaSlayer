using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class WasshoiRedesignV1 : RedesignV1UncommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new RepeatVar(2)];

    public WasshoiRedesignV1()
        : base(nameof(WasshoiRedesignV1), "NinjaGreeting", 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        IReadOnlyList<CardModel> cards = PileType.Draw.GetPile(Owner).Cards;
        if (cards.Count == 0)
        {
            return;
        }
        CardModel top = cards[0];

        if (top.Type == CardType.Attack)
        {
            var power = (WasshoiDuplicationPower)ModelDb.Power<WasshoiDuplicationPower>().ToMutable();
            power.Arm(top);
            await PowerCmd.Apply(
                choiceContext,
                power,
                Owner.Creature,
                DynamicVars.Repeat.IntValue - 1,
                Owner.Creature,
                this);
        }

        await CardCmd.AutoPlay(choiceContext, top, null);
    }

    protected override void OnUpgrade() => DynamicVars.Repeat.UpgradeValueBy(1);
}
