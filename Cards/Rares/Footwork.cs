using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

public sealed class NinjaSlayerFootwork : NinjaSlayerCardTemplate
{
    private static readonly NinjaSlayerCardSpec CardSpec = new(nameof(NinjaSlayerFootwork), 3, CardType.Power, CardRarity.Rare, TargetType.Self, true, "Footwork");



    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DynamicVar("DrawThreshold", 12),
        new DynamicVar("Evasion", 1)
    ];

    public NinjaSlayerFootwork() : base(CardSpec) { }

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
