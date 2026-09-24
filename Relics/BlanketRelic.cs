using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.HoverTips;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

public sealed class BlanketRelic : NinjaSlayerRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Common;

    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromPower<NarakuLifePower>()];

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new NarakuLifeVar(2)
    ];

    public override async Task AfterPlayerTurnStart(
        MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext choiceContext,
        MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        if (player != Owner) return;
        Flash();
        await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<NarakuLifePower>(choiceContext,
            Owner.Creature, DynamicVars.NarakuLife().BaseValue, Owner.Creature, null);
    }
}
