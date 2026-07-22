using MegaCrit.Sts2.Core.Rooms;
using NinjaSlayer.Code.Lifecycle;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Models;

namespace NinjaSlayer.Content;

[RegisterSingleton]
public sealed class CardResolutionLifecycleRules : NinjaSlayerSingletonTemplate
{
    public CardResolutionLifecycleRules() : base(HookedSingletonModel.HookType.Run)
    {
    }

    public override Task BeforeCombatStart()
    {
        CardPlayResolutionScope.ResetAtLifecycleBoundary("combat start");
        return Task.CompletedTask;
    }

    public override Task AfterCombatEnd(CombatRoom room)
    {
        CardPlayResolutionScope.ResetAtLifecycleBoundary("combat end");
        return Task.CompletedTask;
    }
}
