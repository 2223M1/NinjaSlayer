using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class OyeahThrowSword : RedesignV1UncommonCard
{
    public OyeahThrowSword() : base(nameof(OyeahThrowSword), nameof(OyeahThrowSword), 2, CardType.Skill, TargetType.Self) { }
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Sly];
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(5, ValueProp.Move)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromOrb<ShurikenOrb>()];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        if (ShurikenOrb.Find(Owner) is { } orb)
            await orb.FireConsumedVolley(choiceContext, 1, this);
    }
    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3);
}
