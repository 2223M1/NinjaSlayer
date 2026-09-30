using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.Standard;

public sealed class Composure : NinjaSlayerUncommonCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(2, ValueProp.Unpowered)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<ComposurePower>()];

    public Composure()
        : base(nameof(Composure), 1, CardType.Power, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<ComposurePower>(
            choiceContext,
            Owner.Creature,
            DynamicVars.Block.BaseValue,
            Owner.Creature,
            this);

    protected override void OnUpgrade() => AddKeyword(CardKeyword.Innate);
}
