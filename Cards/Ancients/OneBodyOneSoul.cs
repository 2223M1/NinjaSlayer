using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

public sealed class OneBodyOneSoul : NinjaSlayerStandaloneCardTemplate
{
    private static readonly NinjaSlayerCardSpec CardSpec = new(nameof(OneBodyOneSoul), 3, CardType.Power, CardRarity.Ancient, TargetType.Self, true);



    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Ethereal
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DynamicVar("NarakuLife", 30)
    ];

    public OneBodyOneSoul() : base(CardSpec) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<NarakuLifePower>(
            choiceContext,
            Owner.Creature,
            DynamicVars["NarakuLife"].BaseValue,
            Owner.Creature,
            this);

    protected override void OnUpgrade()
    {
        DynamicVars["NarakuLife"].UpgradeValueBy(10);
    }
}
