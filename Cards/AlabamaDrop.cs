using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

[RegisterCard(typeof(NinjaSlayerCardPool))]
public sealed class AlabamaDrop : ModCardTemplate
{
    private const int energyCost = 2;
    private const CardType type = CardType.Skill;
    private const CardRarity rarity = CardRarity.Rare;
    private const TargetType targetType = TargetType.AnyEnemy;
    private const bool shouldShowInCardLibrary = true;

    // ponytail: reuse karate art until this card gets dedicated card art.
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: "res://NinjaSlayer/images/cards/KarateStraight.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DynamicVar("SelfKarate", 6),
        new DynamicVar("EnemyKarate", 18)
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromPower<KaratePower>()
    ];

    public AlabamaDrop() : base(energyCost, type, rarity, targetType, shouldShowInCardLibrary) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        bool appliedKarate = false;
        async Task ApplyKarate()
        {
            if (appliedKarate)
            {
                return;
            }

            appliedKarate = true;
            await PowerCmd.Apply<KaratePower>(
                choiceContext,
                Owner.Creature,
                DynamicVars["SelfKarate"].BaseValue,
                Owner.Creature,
                this);
            await PowerCmd.Apply<KaratePower>(
                choiceContext,
                cardPlay.Target,
                DynamicVars["EnemyKarate"].BaseValue,
                Owner.Creature,
                this);
        }

        await AlabamaDropAnimation.Play(Owner.Creature, cardPlay.Target, ApplyKarate);
        if (!appliedKarate)
        {
            await ApplyKarate();
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars["EnemyKarate"].UpgradeValueBy(5);
    }
}
