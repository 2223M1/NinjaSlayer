using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

public sealed class BlanketRelic : NinjaSlayerRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Common;

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new NarakuLifeVar(3)
    ];

    public override async Task AfterPowerAmountChanged(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext choiceContext, PowerModel power, decimal amount, MegaCrit.Sts2.Core.Entities.Creatures.Creature? applier, CardModel? cardSource)
    {
        if (power is NarakuPower && power.Owner == Owner.Creature && amount > 0)
        {
            Flash();
            await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<NarakuLifePower>(choiceContext, Owner.Creature, DynamicVars.NarakuLife().BaseValue, Owner.Creature, null);
        }
    }
}
