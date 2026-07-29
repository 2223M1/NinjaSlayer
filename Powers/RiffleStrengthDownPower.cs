using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Cards;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using STS2RitsuLib.Scaffolding.Content.Patches;

namespace NinjaSlayer.Powers;

[RegisterPower]
public sealed class RiffleStrengthDownPower : TemporaryStrengthPower, IModPowerAssetOverrides
{
    public PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.For(GetType());
    public string? CustomIconPath => AssetProfile.IconPath;
    public string? CustomBigIconPath => AssetProfile.BigIconPath;

    public override AbstractModel OriginModel => ModelDb.Card<Riffle>();

    protected override bool IsPositive => false;
}
