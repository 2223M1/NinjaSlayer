using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Relics;

public sealed class NarakuWithinRelic : NinjaSlayerRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override RelicAssetProfile AssetProfile => NinjaSlayerRelicAssets.FromCardImage("NarakuWithin");

    public override async Task BeforeCombatStart()
    {
        Flash();
        await NinjaSlayerActions.EnsureNarakuForm(new ThrowingPlayerChoiceContext(), Owner);
    }
}
