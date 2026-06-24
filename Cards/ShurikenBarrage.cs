using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

[RegisterCard(typeof(NinjaSlayerCardPool))]
public sealed class ShurikenBarrage : ModCardTemplate
{
    private const int energyCost = 2;
    private const CardType type = CardType.Skill;
    private const CardRarity rarity = CardRarity.Rare;
    private const TargetType targetType = TargetType.AllEnemies;
    private const bool shouldShowInCardLibrary = true;

    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://NinjaSlayer/images/cards/{GetType().Name}.png"
    );

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DynamicVar("Shuriken", 2)
    ];

    public ShurikenBarrage() : base(energyCost, type, rarity, targetType, shouldShowInCardLibrary) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await NinjaSlayerActions.AddGeneratedShuriken(choiceContext, Owner, DynamicVars["Shuriken"].IntValue, PileType.Hand, IsUpgraded);
        foreach (CardModel shuriken in Owner.PlayerCombatState?.AllCards.Where(c => c.Tags.Contains(NinjaSlayerCardTags.Shuriken)).ToList() ?? [])
        {
            await DamageCmd.Attack(shuriken.DynamicVars.Damage.BaseValue)
                .FromCard(this)
                .WithAttackerAnim("Attack", Owner.Character.AttackAnimDelay)
                .TargetingAllOpponents(CombatState ?? throw new InvalidOperationException("Shuriken Barrage requires combat."))
                .Execute(choiceContext);
        }
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}
