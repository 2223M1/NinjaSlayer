using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class OneBodyOneSoulPower : NinjaSlayerPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        NarakuVisualOverlay.Sync(Owner);
        return Task.CompletedTask;
    }

    public override Task AfterRemoved(Creature oldOwner)
    {
        NarakuVisualOverlay.Sync(oldOwner);
        return Task.CompletedTask;
    }
}
