using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Cards.Standard;

[RegisterCard(typeof(NinjaSlayerCardPool))]
public sealed class KarateBulwark : NinjaSlayerStandaloneCardTemplate
{
    private static readonly NinjaSlayerCardSpec Spec = new(
        nameof(KarateBulwark),
        1,
        CardType.Skill,
        CardRarity.Rare,
        TargetType.Self,
        true);

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<KaratePower>(), HoverTipFactory.FromPower<PlatingPower>()];

    public KarateBulwark() : base(Spec) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        KaratePower? karate = Owner.Creature.GetPower<KaratePower>();
        int plating = NinjaSlayerCardRules.ResolveTurtleShellPlating(karate?.Amount ?? 0);
        if (karate != null)
        {
            await PowerCmd.Remove(karate);
        }

        if (plating > 0)
        {
            await PowerCmd.Apply<PlatingPower>(
                choiceContext,
                Owner.Creature,
                plating,
                Owner.Creature,
                this);
        }
    }

    protected override void OnUpgrade() => RemoveKeyword(CardKeyword.Exhaust);
}
