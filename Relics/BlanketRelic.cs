using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Content;
using NinjaSlayer.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

[RegisterRelic(typeof(NinjaSlayerRelicPool))]
public sealed class BlanketRelic : ModRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Common;

    public override RelicAssetProfile AssetProfile => new(
        IconPath: $"res://NinjaSlayer/images/relics/{GetType().Name}.png",
        IconOutlinePath: $"res://NinjaSlayer/images/relics/{GetType().Name}_outline.png",
        BigIconPath: $"res://NinjaSlayer/images/relics/{GetType().Name}_large.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DynamicVar("NarakuLife", 3)
    ];

    public override async Task AfterPowerAmountChanged(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext choiceContext, PowerModel power, decimal amount, MegaCrit.Sts2.Core.Entities.Creatures.Creature? applier, CardModel? cardSource)
    {
        if (power is NarakuPower && power.Owner == Owner.Creature && amount > 0)
        {
            Flash();
            await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<NarakuLifePower>(choiceContext, Owner.Creature, DynamicVars["NarakuLife"].BaseValue, Owner.Creature, null);
        }
    }
}
