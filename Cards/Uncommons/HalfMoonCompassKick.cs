using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

public sealed class HalfMoonCompassKick : NinjaSlayerCardTemplate
{
    private const int energyCost = 0;
    private const CardType type = CardType.Attack;
    private const CardRarity rarity = CardRarity.Uncommon;
    private const TargetType targetType = TargetType.AllEnemies;
    private const bool shouldShowInCardLibrary = true;

    // ponytail: reuse sweep-kick art until this card gets dedicated art.
    public override CardAssetProfile AssetProfile => NinjaSlayerCardAssets.Named("SweepKick");

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(4, ValueProp.Move),
        new DynamicVar("ChadoDamage", 8)
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromCard<ChadoCard>(),
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust)
    ];

    public HalfMoonCompassKick() : base(energyCost, type, rarity, targetType, shouldShowInCardLibrary) { }

    protected override bool ShouldGlowGoldInternal => NinjaSlayerActions.ChadoInHandCount(Owner) > 0;

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        List<CardModel> chadoCards = (await CardSelectCmd.FromHand(
            choiceContext,
            Owner,
            new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 0, int.MaxValue),
            card => card is ChadoCard,
            this)).ToList();

        foreach (CardModel chado in chadoCards)
        {
            await CardCmd.Exhaust(choiceContext, chado);
        }

        decimal damage = DynamicVars.Damage.BaseValue
            + chadoCards.Count * DynamicVars["ChadoDamage"].BaseValue;
        await DamageCmd.Attack(damage)
            .FromCard(this, cardPlay)
            .WithDefectStrikeHitFx()
            .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
            .TargetingAllOpponents(CombatState ?? throw new InvalidOperationException("Half Moon Compass Kick requires combat."))
            .ExecuteWithFinisher(choiceContext, this, cardPlay, damageOverride: damage);
    }

    protected override void OnUpgrade()
    {
        DynamicVars["ChadoDamage"].UpgradeValueBy(4);
    }
}
