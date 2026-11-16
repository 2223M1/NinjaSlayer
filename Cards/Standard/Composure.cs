using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
#if NINJASLAYER_CHANNEL_STABLE
using StretchPower = NinjaSlayer.Powers.StretchPower;
#else
using StretchPower = MegaCrit.Sts2.Core.Models.Powers.StretchPower;
#endif

namespace NinjaSlayer.Cards.Standard;

public sealed class Composure : NinjaSlayerUncommonCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Ethereal];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<StretchPower>(2)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<ComposurePower>(), HoverTipFactory.FromPower<StretchPower>()];

    public Composure()
        : base(nameof(Composure), 1, CardType.Power, TargetType.Self) { }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        PowerCmd.Apply<ComposurePower>(
            choiceContext,
            Owner.Creature,
            DynamicVars["StretchPower"].BaseValue,
            Owner.Creature,
            this);

    protected override void OnUpgrade() => RemoveKeyword(CardKeyword.Ethereal);
}
