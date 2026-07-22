using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.CardPools;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

[RegisterCard(typeof(StatusCardPool))]
public sealed class BusyLine : NinjaSlayerStandaloneCardTemplate
{
    private static readonly NinjaSlayerCardSpec CardSpec = new(nameof(BusyLine), 1, CardType.Status, CardRarity.Status, TargetType.None, false, "IrcTerminal");


    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;

    // ponytail: reuse terminal card art until Busy Line gets dedicated art.

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Retain,
        CardKeyword.Exhaust
    ];

    public BusyLine() : base(CardSpec) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) => Task.CompletedTask;

    protected override void OnUpgrade() { }
}
