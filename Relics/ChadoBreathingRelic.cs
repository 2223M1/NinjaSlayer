using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Relics;

[RegisterCharacterStarterRelic(typeof(NinjaSlayerCharacter), 1)]
[RegisterTouchOfOrobasRefinement(typeof(DeepChadoBreathingRelic))]
public class ChadoBreathingRelic : NinjaSlayerRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Starter;

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromCard<ChadoCard>()
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

        ChadoCard chado = combatState.CreateCard<ChadoCard>(Owner);
        await CardPileCmd.AddGeneratedCardToCombat(chado, PileType.Hand, Owner);
        Flash();
    }
}
