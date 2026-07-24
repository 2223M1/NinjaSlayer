using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Content;
using NinjaSlayer.Relics;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Events;

[RegisterSharedEvent]
public sealed class XiaoJiCuteEvent : ModEventTemplate
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new GoldVar(50)
    ];

    public override bool IsAllowed(IRunState runState) =>
        NinjaSlayerContentAccess.HasNinjaSlayer(runState);

    protected override IReadOnlyList<EventOption> GenerateInitialOptions() =>
    [
        new EventOption(this, HugXiaoJi, InitialOptionKey("HUG_XIAO_JI"), HoverTipFactory.FromRelic<XiaoJiCuteRelic>()),
        new EventOption(this, RobXiaoJi, InitialOptionKey("ROB_XIAO_JI"))
    ];

    private async Task HugXiaoJi()
    {
        await RelicCmd.Obtain<XiaoJiCuteRelic>(Owner!);
        SetEventFinished(PageDescription("HUGGED"));
    }

    private async Task RobXiaoJi()
    {
        await PlayerCmd.GainGold(DynamicVars.Gold.BaseValue, Owner!);
        SetEventFinished(PageDescription("ROBBED"));
    }
}
