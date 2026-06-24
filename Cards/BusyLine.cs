using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

[RegisterCard(typeof(NinjaSlayerCardPool))]
public sealed class BusyLine : ModCardTemplate
{
    private const int energyCost = 1;
    private const CardType type = CardType.Status;
    private const CardRarity rarity = CardRarity.Status;
    private const TargetType targetType = TargetType.None;
    private const bool shouldShowInCardLibrary = false;

    // ponytail: reuse terminal card art until Busy Line gets dedicated art.
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: "res://NinjaSlayer/images/cards/IrcTerminal.png"
    );

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Retain,
        CardKeyword.Exhaust
    ];

    public BusyLine() : base(energyCost, type, rarity, targetType, shouldShowInCardLibrary) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) => Task.CompletedTask;

    protected override void OnUpgrade() { }
}
