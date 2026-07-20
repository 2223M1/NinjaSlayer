using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Content;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

public sealed class KarateFinish : NinjaSlayerCardTemplate
{
    private const int energyCost = 2;
    private const CardType type = CardType.Attack;
    private const CardRarity rarity = CardRarity.Rare;
    private const TargetType targetType = TargetType.AnyEnemy;
    private const bool shouldShowInCardLibrary = true;

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DynamicVar("Multiplier", 6)
    ];

    public KarateFinish() : base(energyCost, type, rarity, targetType, shouldShowInCardLibrary) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);
        KaratePower? karate = cardPlay.Target.GetPower<KaratePower>();
        int amount = karate?.Amount ?? 0;
        if (amount > 0)
        {
            await PowerCmd.Remove(karate);
        }

        decimal damage = amount * DynamicVars["Multiplier"].BaseValue;
        ValueProp props = ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move;
        var finisherSpec = new FinisherAttackSpec(
            this,
            cardPlay,
            _ => damage,
            props,
            1,
            FinisherTargeting.Single);
        await NinjaSlayerFinisherCinematic.ExecuteDirectWithFinisher(
            choiceContext,
            finisherSpec,
            async () =>
            {
                NinjaSlayerCombatVfx.PlayDefectStrikeHitFx(cardPlay.Target);
                await CreatureCmd.Damage(choiceContext, cardPlay.Target, damage, props, this, cardPlay);
            });
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}
