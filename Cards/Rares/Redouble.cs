using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

public sealed class Redouble : NinjaSlayerCardTemplate
{
    private const int energyCost = 1;
    private const CardType type = CardType.Skill;
    private const CardRarity rarity = CardRarity.Rare;
    private const TargetType targetType = TargetType.Self;
    private const bool shouldShowInCardLibrary = true;

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DynamicVar("SelfKarate", 6)
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromPower<KaratePower>(),
        HoverTipFactory.FromPower<KarateDoublingPower>()
    ];

    public Redouble() : base(energyCost, type, rarity, targetType, shouldShowInCardLibrary) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<KaratePower>(
            choiceContext,
            Owner.Creature,
            DynamicVars["SelfKarate"].BaseValue,
            Owner.Creature,
            this);

        int turn = Owner.PlayerCombatState?.TurnNumber ?? -1;
        KarateDoublingPower? existing = Owner.Creature.GetPower<KarateDoublingPower>();

        if (existing != null && existing.AppliedTurnNumber == turn)
        {
            existing.ExtendToNextTurn();
        }
        else
        {
            await PowerCmd.Apply<KarateDoublingPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars["SelfKarate"].UpgradeValueBy(-2);
    }
}
