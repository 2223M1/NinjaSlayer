using Godot;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models.Acts;
using NinjaSlayer.Relics;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Ancients;

[RegisterActAncient(typeof(Glory))]
public sealed class NancyLee : ModAncientEventTemplate
{
    public override Color ButtonColor => new(0.05f, 0.02f, 0.08f, 0.65f);
    public override Color DialogueColor => new("5B1B3C");

    public override EventAssetProfile AssetProfile => new(
        BackgroundScenePath: "res://NinjaSlayer/scenes/ancients/nancy_lee.tscn"
    );

    private IReadOnlyList<EventOption> Pool1 => [
        CreateModRelicOption<IrcTerminalRelic>()
    ];

    private IReadOnlyList<EventOption> Pool2 => [
        CreateModRelicOption<ReporterPassRelic>()
    ];

    private IReadOnlyList<EventOption> Pool3 => [
        CreateModRelicOption<ElectricBoobyTrapRelic>(),
        CreateModRelicOption<NancyZazenDrinkRelic>()
    ];

    public override IEnumerable<EventOption> AllPossibleOptions => [.. Pool1, .. Pool2, .. Pool3];

    protected override IReadOnlyList<EventOption> GenerateInitialOptions() => [
        Rng.NextItem(Pool1)!,
        Rng.NextItem(Pool2)!,
        Rng.NextItem(Pool3)!
    ];
}
