using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Cards.DynamicVars;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

public sealed class SpitWater : NinjaSlayerCardTemplate
{
    private static readonly NinjaSlayerCardSpec CardSpec = new(nameof(SpitWater), 1, CardType.Skill, CardRarity.Uncommon, TargetType.Self, true, "KarateFinish");


    // ponytail: reuse back-bridge art until this card gets dedicated art.

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new PowerVar<WeakPower>(1),
        new DynamicVar("BonusWeak", 1)
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromPower<KaratePower>(),
        HoverTipFactory.FromPower<WeakPower>()
    ];

    protected override bool ShouldGlowGoldInternal =>
        CombatState?.HittableEnemies.Any(e => e.HasPower<KaratePower>()) ?? false;

    public SpitWater() : base(CardSpec) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        IReadOnlyList<Creature> enemies = CombatState?.HittableEnemies.ToList() ?? [];

        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);

        if (enemies.Count > 0)
        {
            await PowerCmd.Apply<WeakPower>(
                choiceContext,
                enemies,
                DynamicVars.Weak.BaseValue,
                Owner.Creature,
                this);
        }

        IReadOnlyList<Creature> karateEnemies = enemies
            .Where(e => e.HasPower<KaratePower>())
            .ToList();

        if (karateEnemies.Count > 0)
        {
            await PowerCmd.Apply<WeakPower>(
                choiceContext,
                karateEnemies,
                DynamicVars["BonusWeak"].IntValue,
                Owner.Creature,
                this);
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars["BonusWeak"].UpgradeValueBy(1);
    }
}
