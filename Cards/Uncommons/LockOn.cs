using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

public sealed class LockOn : NinjaSlayerCardTemplate
{
    private static readonly NinjaSlayerCardSpec CardSpec = new(nameof(LockOn), 1, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy, true);


    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DynamicVar("DamagePercent", 25),
        new DynamicVar("DefensePercent", 25)
    ];

    public LockOn() : base(CardSpec) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);
        DamageFocusPower? power = await PowerCmd.Apply<DamageFocusPower>(choiceContext, cardPlay.Target, 1, Owner.Creature, this);
        if (power != null)
        {
            power.DamageMultiplier = 1m + DynamicVars["DamagePercent"].BaseValue / 100m;
            power.DefenseMultiplier = 1m - DynamicVars["DefensePercent"].BaseValue / 100m;
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars["DamagePercent"].UpgradeValueBy(25);
        DynamicVars["DefensePercent"].UpgradeValueBy(25);
    }
}
