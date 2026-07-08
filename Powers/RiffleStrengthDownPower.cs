using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Cards;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Powers;

[RegisterPower]
public sealed class RiffleStrengthDownPower : TemporaryStrengthPower
{
    public override AbstractModel OriginModel => ModelDb.Card<Riffle>();

    protected override bool IsPositive => false;
}
