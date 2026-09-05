using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Relics;

[RegisterCharacterStarterRelic(typeof(NinjaSlayerCharacter), 1)]
[RegisterTouchOfOrobasRefinement(typeof(DeepChadoBreathingRelic))]
public class ChadoBreathingRelic : NinjaSlayerRelicTemplate
{
    protected virtual int ChadoCount => 0;

    public override RelicRarity Rarity => RelicRarity.Starter;

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromCard<ChadoEnergyRedesignV1>()
    ];

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
            ChadoEnergyRedesignV1 chado = combatState.CreateCard<ChadoEnergyRedesignV1>(Owner);
            await CardPileCmd.AddGeneratedCardToCombat(chado, PileType.Hand, Owner);
        }

        await ChadoBreathCmd.Apply(Owner, 2, this);
        Flash();
    }
}
