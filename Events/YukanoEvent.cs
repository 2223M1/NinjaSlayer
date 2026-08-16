using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;
using NinjaSlayer.Relics;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Events;

[RegisterSharedEvent]
public sealed class YukanoEvent : ModEventTemplate
{
    private const string PortraitPath =
        "res://NinjaSlayer/images/events/yukano_event.png";

    public override EventAssetProfile AssetProfile => new(
        InitialPortraitPath: PortraitPath);

    public override bool IsAllowed(IRunState runState) =>
        runState.CurrentActIndex == 2
        && NinjaSlayerContentAccess.HasNinjaSlayer(runState);

    public override IEnumerable<string> GetAssetPaths(IRunState runState)
    {
        string defaultPortraitPath = ImageHelper.GetImagePath(
            $"events/{Id.Entry.ToLowerInvariant()}.png");
        return base.GetAssetPaths(runState)
            .Select(path => path == defaultPortraitPath ? PortraitPath : path)
            .Distinct();
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions() =>
    [
        new EventOption(this, OpenWithSilverKey, InitialOptionKey("SILVER_KEY")),
        new EventOption(
            this,
            TravelWithYukano,
            InitialOptionKey("TRAVEL_WITH_YUKANO"),
            HoverTipFactory.FromRelic<YukanoCompanionRelic>())
    ];

    private async Task OpenWithSilverKey()
    {
        var cards = (await CardSelectCmd.FromDeckForRemoval(
                Owner!,
                new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, 1)))
            .ToList();
        await CardPileCmd.RemoveFromDeck(cards);
        SetEventFinished(PageDescription("SILVER_KEY"));
    }

    private async Task TravelWithYukano()
    {
        if (Owner!.Relics.All(relic => relic is not YukanoCompanionRelic))
        {
            await RelicCmd.Obtain<YukanoCompanionRelic>(Owner);
        }

        SetEventFinished(PageDescription("YUKANO"));
    }
}
