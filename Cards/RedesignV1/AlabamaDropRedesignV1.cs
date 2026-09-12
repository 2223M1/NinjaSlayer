using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards.RedesignV1;

public sealed class AlabamaDropRedesignV1 : RedesignV1RareCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new CalculationBaseVar(0),
        new ExtraDamageVar(5),
        new CalculatedDamageVar(ValueProp.Move)
            .WithMultiplier(static (card, _) => card.Owner.Creature.GetPowerAmount<KaratePower>()),
        new DynamicVar("Dazed", 3)
    ];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<KaratePower>(), HoverTipFactory.FromCard<Dazed>()];

    public AlabamaDropRedesignV1()
        : base(nameof(AlabamaDropRedesignV1), "AlabamaDrop", 3, CardType.Attack, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        bool resolved = false;
        async Task ResolveImpact()
        {
            if (resolved)
            {
                return;
            }

            resolved = true;
            await DamageCmd.Attack(DynamicVars.CalculatedDamage)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
                .FromCard(this)
#else
                .FromCard(this, cardPlay)
#endif
                .WithNoAttackerAnim()
                .Targeting(cardPlay.Target!).Execute(choiceContext);
        }

        await AlabamaDropAnimation.Play(Owner.Creature, cardPlay.Target!, ResolveImpact);
        await ResolveImpact();
        for (int index = 0; index < DynamicVars["Dazed"].IntValue; index++)
        {
            await NinjaSlayerCardCmd.AddGeneratedCard<Dazed>(Owner, PileType.Draw);
        }
    }

    protected override void OnUpgrade() => DynamicVars.ExtraDamage.UpgradeValueBy(2);
}
