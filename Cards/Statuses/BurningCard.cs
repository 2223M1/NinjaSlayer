using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

[RegisterCard(typeof(StatusCardPool))]
public sealed class BurningCard : NinjaSlayerStandaloneCardTemplate
{
    private static readonly NinjaSlayerCardSpec CardSpec = new(nameof(BurningCard), -2, CardType.Status, CardRarity.Status, TargetType.Self, false);


    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;


    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Unplayable,
        CardKeyword.Ethereal
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Ethereal)
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(4, ValueProp.Unblockable | ValueProp.Unpowered)
    ];

    public override bool HasTurnEndInHandEffect => true;

    protected override IEnumerable<string> ExtraRunAssetPaths => NNinjaSlayerGroundFireVfx.AssetPaths;

    public BurningCard() : base(CardSpec) { }

    protected override async Task OnTurnEndInHand(PlayerChoiceContext choiceContext)
    {
        ICombatState combatState = CombatState
            ?? throw new InvalidOperationException("Burning requires combat.");

        List<Creature> targets = combatState.Creatures.Where(c => c.IsAlive).ToList();
        NinjaSlayerCombatVfx.PlayBurnStatusFeedback(targets);

        await CreatureCmd.Damage(
            choiceContext,
            targets,
            DynamicVars.Damage.BaseValue,
            DynamicVars.Damage.Props,
            Owner.Creature);

        await CardCmd.Exhaust(choiceContext, this, causedByEthereal: true);
    }

    protected override void OnUpgrade() { }
}
