using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class HardItOutRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<EvasionPower>(1)];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<EvasionPower>(), HoverTipFactory.FromCard<BlackFlameRedesignV1>()];

    public HardItOutRedesignV1()
        : base(nameof(HardItOutRedesignV1), "KarateWall", 1, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<EvasionPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars[nameof(EvasionPower)].BaseValue,
            Owner.Creature,
            this);
        await NinjaSlayerCardCmd.AddGeneratedCard<BlackFlameRedesignV1>(Owner, PileType.Hand);
        await NinjaSlayerCardCmd.AddGeneratedCard<BlackFlameRedesignV1>(Owner, PileType.Hand);
    }

    protected override void OnUpgrade() => DynamicVars[nameof(EvasionPower)].UpgradeValueBy(1);
}
