using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Content;
using STS2RitsuLib.Combat.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

[RegisterPower]
public sealed class HiddenEdgeTemporaryFocusPower
    : ModTemporaryAppliedPowerTemplate<HiddenEdgeRedesignV1, FocusPower>
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.Named("DamageFocusPower");
}
