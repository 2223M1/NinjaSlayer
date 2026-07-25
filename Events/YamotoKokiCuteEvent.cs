using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Content;
using NinjaSlayer.Relics;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Events;

[RegisterSharedEvent]
public sealed class YamotoKokiCuteEvent : ModEventTemplate
{
    public override EventAssetProfile AssetProfile => new(
        InitialPortraitPath: "res://NinjaSlayer/images/events/yamoto_koki_cute_event.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new GoldVar(50)
    ];

    public override bool IsAllowed(IRunState runState)
    {
        if (!NinjaSlayerContentAccess.HasNinjaSlayer(runState))
        {
            return false;
        }

        bool passedFixedTreasure = runState.MapPointHistory
            .ElementAtOrDefault(runState.CurrentActIndex)?
            .Any(entry => entry.MapPointType == MapPointType.Treasure) == true;
        return runState.CurrentActIndex switch
        {
            1 => passedFixedTreasure,
            2 => !passedFixedTreasure,
            _ => false
        };
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions() =>
    [
        new EventOption(this, HugYamotoKoki, InitialOptionKey("HUG_YAMOTO_KOKI"), HoverTipFactory.FromRelic<YamotoKokiCuteRelic>()),
        new EventOption(this, RobYamotoKoki, InitialOptionKey("ROB_YAMOTO_KOKI"))
    ];

    public override Task AfterEventStarted()
    {
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.YamotoKokiEvent);
        return Task.CompletedTask;
    }

    private async Task HugYamotoKoki()
    {
        await RelicCmd.Obtain<YamotoKokiCuteRelic>(Owner!);
        SetEventFinished(PageDescription("HUGGED"));
    }

    private async Task RobYamotoKoki()
    {
        await PlayerCmd.GainGold(DynamicVars.Gold.BaseValue, Owner!);
        SetEventFinished(PageDescription("ROBBED"));
    }
}
