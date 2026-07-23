using MegaCrit.Sts2.Core.Rooms;
using NinjaSlayer.Code.Prepared;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Models;

namespace NinjaSlayer.Content;

[RegisterSingleton]
public sealed class PreparedSafetyLifecycleRules : NinjaSlayerSingletonTemplate
{
    public PreparedSafetyLifecycleRules() : base(HookedSingletonModel.HookType.Run)
    {
    }

    public override Task AfterCombatEnd(CombatRoom room)
    {
        PreparedSafetyService.RecoverAfterCombatEnd(room);
        return Task.CompletedTask;
    }
}
