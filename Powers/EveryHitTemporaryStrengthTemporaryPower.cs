using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Cards;
using STS2RitsuLib.Combat.Powers;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

[RegisterPower]
public sealed class EveryHitTemporaryStrengthTemporaryPower : ModTemporaryAppliedPowerTemplate<Momentum, StrengthPower>
{
    public override PowerAssetProfile AssetProfile => NinjaSlayerPowerAssets.For(GetType());
}
