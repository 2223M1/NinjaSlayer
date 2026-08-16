using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Content;
using STS2RitsuLib.Combat.SecondaryResources;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

[RegisterCharacterStarterRelic(typeof(NinjaSlayerRedesignCharacter), 1)]
[RegisterTouchOfOrobasRefinement(typeof(RedesignV1DeepChadoBreathingRelic))]
public class RedesignV1ChadoBreathingRelic : NinjaSlayerRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Starter;
    public override RelicAssetProfile AssetProfile => NinjaSlayerRelicAssets.For<ChadoBreathingRelic>();
    public override bool IsAllowed(IRunState runState) =>
        base.IsAllowed(runState)
        && NinjaSlayerRunData.GetRulesVersion((RunState)runState) == NinjaSlayerRulesVersion.RedesignV1;

    public override async Task BeforeHandDraw(
        Player player,
        PlayerChoiceContext choiceContext,
        ICombatState combatState)
    {
        if (player != Owner || Owner.PlayerCombatState is not { TurnNumber: 1 })
        {
            return;
        }

        ChadoBreathRedesignV1 card = combatState.CreateCard<ChadoBreathRedesignV1>(Owner);
        await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, Owner);
        Flash();
    }
}

public sealed class RedesignV1DeepChadoBreathingRelic : RedesignV1ChadoBreathingRelic, ISecondaryResourceHookListener
{
    public override RelicRarity Rarity => RelicRarity.Ancient;
    public override bool HasUponPickupEffect => true;
    public override RelicAssetProfile AssetProfile => NinjaSlayerRelicAssets.For<DeepChadoBreathingRelic>();

    public decimal ModifyMaxSecondaryResource(SecondaryResourceMaxContext context, decimal amount) =>
        context.Definition.Id == NinjaSlayerTeaEnergy.Id ? RedesignV1Rules.AncientTeaEnergy : amount;

    public override async Task AfterObtained()
    {
        Flash();
        await SecondaryResourceCmd.Set(
            Owner,
            NinjaSlayerTeaEnergy.Id,
            RedesignV1Rules.AncientTeaEnergy,
            source: this);
    }
}
