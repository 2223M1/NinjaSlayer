using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.Standard;

public sealed class HellTornado : NinjaSlayerRareCard
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<SoarPower>(), HoverTipFactory.FromOrb<ShurikenOrb>()];

    public HellTornado()
        : base(nameof(HellTornado), 2, CardType.Skill, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<SoarPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
        if (ShurikenOrb.Find(Owner) is { } stock)
        {
            await ShurikenOrb.AddStock(choiceContext, Owner, stock.StackCount);
        }

        await PowerCmd.Apply<HellTornadoPower>(
            choiceContext,
            Owner.Creature,
            1,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade() => AddKeyword(CardKeyword.Retain);
}
