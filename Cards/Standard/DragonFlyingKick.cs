using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Cards.Standard;

public sealed class DragonFlyingKick : NinjaSlayerRareCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [NinjaSlayerHoverTips.ChadoBreathing, .. HoverTipFactory.FromCardWithCardHoverTips<Chado>()];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(15, ValueProp.Move), new DynamicVar("Breath", 2)];

    public DragonFlyingKick()
        : base(nameof(DragonFlyingKick), 3, CardType.Attack, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
            .FromCard(this)
#else
            .FromCard(this, cardPlay)
#endif
            .WithDefectStrikeHitFx()
            .WithNoAttackerAnim()
            .AfterAttackerAnim(() => JumpAnimation.PlayFlyingKick(Owner.Creature))
            .Targeting(cardPlay.Target!)
            .ExecuteWithFinisher(choiceContext, this, cardPlay);
        await ChadoBreathCmd.Apply(choiceContext, Owner, DynamicVars["Breath"].IntValue);
        await CardPileCmd.Draw(choiceContext, Math.Max(0, CardPile.MaxCardsInHand - PileType.Hand.GetPile(Owner).Cards.Count), Owner);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(5);
        DynamicVars["Breath"].UpgradeValueBy(1);
    }
}
