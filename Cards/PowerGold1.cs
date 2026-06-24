using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

[RegisterCard(typeof(NinjaSlayerCardPool))]
public sealed class PowerGold1 : ModCardTemplate
{
    private const int energyCost = 3;
    private const CardType type = CardType.Power;
    private const CardRarity rarity = CardRarity.Rare;
    private const TargetType targetType = TargetType.Self;
    private const bool shouldShowInCardLibrary = true;

    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://NinjaSlayer/images/cards/{GetType().Name}.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DynamicVar("DrawThreshold", 12),
        new DynamicVar("Evasion", 1)
    ];

    public PowerGold1() : base(energyCost, type, rarity, targetType, shouldShowInCardLibrary) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        DrawForEvasionPower? power = await PowerCmd.Apply<DrawForEvasionPower>(choiceContext, Owner.Creature, DynamicVars["Evasion"].BaseValue, Owner.Creature, this);
        if (power != null)
        {
            power.DrawThreshold = DynamicVars["DrawThreshold"].IntValue;
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars["DrawThreshold"].UpgradeValueBy(-2);
    }
}
