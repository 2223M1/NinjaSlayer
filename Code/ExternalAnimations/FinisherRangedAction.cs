using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Cards;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.Code.ExternalAnimations;

// An executing ranged action owns its visual references; it never owns targeting or damage.
internal sealed class FinisherRangedAction : IDisposable
{
    private static readonly AsyncLocal<FinisherRangedAction?> Current = new();
    private readonly FinisherRangedAction? _previous;
    private readonly List<Node> _retained = [];
    private readonly List<Task> _arrivals = [];
    private bool _released;

    private FinisherRangedAction(Creature actor, CardModel? source)
    {
        Actor = actor;
        Source = source;
        _previous = Current.Value;
        Baseline = NCombatRoom.Instance is { } room
            ? FinisherImpactVfxFreezeLease.CaptureBaseline(room) : new HashSet<ulong>();
        Current.Value = this;
    }

    internal Creature Actor { get; }
    internal CardModel? Source { get; }
    internal IReadOnlySet<ulong> Baseline { get; }
    internal List<Node> Visuals { get; } = [];
    internal FinisherSession? Session { get; private set; }
    internal static FinisherRangedAction? Active => Current.Value;
    internal static FinisherRangedAction Begin(Creature actor, CardModel? source = null) => new(actor, source);
    internal static FinisherRangedAction? For(Creature? actor) =>
        Current.Value is { } action && action.Actor == actor ? action : null;

    internal static bool IsRangedCard(CardModel card) =>
        card is Shiv or SawatariMachete || card.Tags.Contains(NinjaSlayerCardTags.Shuriken);

    internal static bool IsRangedMove(MonsterModel monster) => (monster, monster.NextMove.Id) switch
    {
        (DarkNinjaMonster, DarkNinjaMonster.DeathSlashMoveId) => true,
        (YukanoMonster, _) => true,
        (CrossbowRubyRaider, "FIRE_MOVE") => true,
        (TurretOperator, "UNLOAD_MOVE" or "UNLOAD_MOVE_2") => true,
        (FakeMerchantMonster, "SPEW_COINS_MOVE" or "THROW_RELIC_MOVE") => true,
        (Toadpole, "SPIKE_SPIT_MOVE") => true,
        (SludgeSpinner, "OIL_SPRAY_MOVE") => true,
        (MechaKnight, "FLAMETHROWER_MOVE") => true,
        (MagiKnight, "MAGIC_BOMB") => true,
        (KinPriest, "BEAM_MOVE") => true,
        (Aeonglass, "EYE_LASERS_MOVE") => true,
        (TheLost, "EYE_LASERS") => true,
        (TorchHeadAmalgam, "BEAM_MOVE") => true,
        _ => false
    };

    internal void Attach(FinisherSession session)
    {
        if (Session != null && Session != session)
            throw new InvalidOperationException("A ranged action cannot belong to two finishers.");
        Session = session;
    }

    internal void Track(Node visual, Task? arrival = null)
    {
        if (!Visuals.Contains(visual)) Visuals.Add(visual);
        if (arrival != null) _arrivals.Add(arrival);
    }

    internal void TrackArrival(Task arrival) => _arrivals.Add(arrival);

    internal async Task WaitForImpact()
    {
        // A delayed release may add its projectile while this action is waiting.
        for (int index = 0; index < _arrivals.Count; index++) await _arrivals[index];
    }

    internal bool Retain(Node visual)
    {
        if (_released) return false;
        Track(visual);
        if (!_retained.Contains(visual)) _retained.Add(visual);
        return true;
    }

    internal void ReleaseVisuals()
    {
        _released = true;
        foreach (Node visual in _retained)
            if (GodotObject.IsInstanceValid(visual) && !visual.IsQueuedForDeletion()) visual.QueueFree();
        _retained.Clear();
        Visuals.Clear();
    }

    internal void RestoreCaller()
    {
        if (ReferenceEquals(Current.Value, this)) Current.Value = _previous;
    }

    public void Dispose()
    {
        RestoreCaller();
        if (Session == null) ReleaseVisuals();
    }
}
