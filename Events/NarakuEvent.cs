using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Relics;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Events;

[RegisterActEvent(typeof(Glory))]
public sealed class NarakuEvent : ModEventTemplate
{
    private static readonly int[] NarakuDamage = [5, 6, 7];

    private int _numberOfCalls;

    private int NumberOfCalls
    {
        get => _numberOfCalls;
        set
        {
            AssertMutable();
            _numberOfCalls = value;
        }
    }

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Any(HasNarakuThemedDeckCard);

    protected override IReadOnlyList<EventOption> GenerateInitialOptions() =>
        Owner?.Creature.CurrentHp < 19
            ? GenerateAcceptanceOptions("INITIAL")
            : GenerateNarakuOptions("INITIAL");

    private static bool HasNarakuThemedDeckCard(Player player) =>
        PileType.Deck.GetPile(player).Cards.Any(card => card is NarakuThemedCardTemplate);

    private IReadOnlyList<EventOption> GenerateNarakuOptions(string page) =>
        [
            new EventOption(this, CallNaraku, ModOptionKey(page, "CALL_NARAKU"))
                .ThatDoesDamage(NarakuDamage[NumberOfCalls]),
            CreateSilenceOption(page)
        ];

    private IReadOnlyList<EventOption> GenerateAcceptanceOptions(string page) =>
        [
            new EventOption(this, AcceptNaraku, ModOptionKey(page, "ACCEPT_NARAKU"), HoverTipFactory.FromRelic<NarakuWithinRelic>()),
            CreateSilenceOption(page)
        ];

    private EventOption CreateSilenceOption(string page) =>
        new(this, SilenceNaraku, ModOptionKey(page, "SILENCE_NARAKU"));

    private async Task CallNaraku()
    {
        await DealNarakuDamage();
        NumberOfCalls++;

        if (NumberOfCalls >= NarakuDamage.Length)
        {
            SetEventState(PageDescription("ACCEPT"), GenerateAcceptanceOptions("ACCEPT"));
            return;
        }

        string page = $"CALL_{NumberOfCalls}";
        SetEventState(PageDescription(page), GenerateNarakuOptions(page));
    }

    private async Task AcceptNaraku()
    {
        await RelicCmd.Obtain<NarakuWithinRelic>(Owner!);
        SetEventFinished(PageDescription("ACCEPTED"));
    }

    private Task SilenceNaraku()
    {
        SetEventFinished(PageDescription("SILENCED"));
        return Task.CompletedTask;
    }

    private async Task DealNarakuDamage()
    {
        await CreatureCmd.Damage(
            new ThrowingPlayerChoiceContext(),
            Owner!.Creature,
            NarakuDamage[NumberOfCalls],
            ValueProp.Unblockable | ValueProp.Unpowered,
            null,
            null);
    }
}
