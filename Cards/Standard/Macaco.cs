using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.Standard;

public sealed class Macaco : NinjaSlayerRareCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<EvasionPower>(1)];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<EvasionPower>(), HoverTipFactory.FromCard<BlackFlame>()];

    public Macaco()
        : base(nameof(Macaco), 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<EvasionPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars[nameof(EvasionPower)].BaseValue,
            Owner.Creature,
            this);
        await NinjaSlayerCardCmd.AddGeneratedCard<BlackFlame>(Owner, PileType.Hand);
        await NinjaSlayerCardCmd.AddGeneratedCard<BlackFlame>(Owner, PileType.Hand);
    }

    protected override void OnUpgrade() => DynamicVars[nameof(EvasionPower)].UpgradeValueBy(1);
}
