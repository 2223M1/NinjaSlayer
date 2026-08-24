using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

[RegisterCharacterStarterRelic(typeof(NinjaSlayerRedesignCharacter), 1)]
[RegisterTouchOfOrobasRefinement(typeof(RedesignV1DeepChadoBreathingRelic))]
public class RedesignV1ChadoBreathingRelic : NinjaSlayerRelicTemplate
{
    protected virtual int ChadoCount => 0;

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

        for (int i = 0; i < ChadoCount; i++)
        {
            ChadoEnergyRedesignV1 card = combatState.CreateCard<ChadoEnergyRedesignV1>(Owner);
            await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, Owner);
        }

        await ChadoBreathCmd.Apply(Owner, 2, this);
        Flash();
    }
}

public sealed class RedesignV1DeepChadoBreathingRelic : RedesignV1ChadoBreathingRelic
{
    protected override int ChadoCount => 2;

    public override RelicRarity Rarity => RelicRarity.Ancient;
    public override RelicAssetProfile AssetProfile => NinjaSlayerRelicAssets.For<DeepChadoBreathingRelic>();
}
