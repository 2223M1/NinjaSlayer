using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using STS2RitsuLib.Scaffolding.Content.Patches;

namespace NinjaSlayer.Powers;

[RegisterPower]
public sealed class HookRopeStrengthDownPower : TemporaryStrengthPower, IModPowerAssetOverrides
{
    public PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named("RiffleStrengthDownPower");
    public string? CustomIconPath => AssetProfile.IconPath;
    public string? CustomBigIconPath => AssetProfile.BigIconPath;

    public override AbstractModel OriginModel => ModelDb.Card<HookRopeRedesignV1>();

    protected override bool IsPositive => false;
}
