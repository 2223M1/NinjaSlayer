using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.ExternalAnimations;

internal readonly record struct FinisherActionContext(
    Vector2 OriginalPosition,
    Vector2 TravelStartPosition,
    Vector2 TravelEndPosition,
    Vector2 ImpactPosition)
{
    public static FinisherActionContext Stationary(NCreature actor) =>
        new(actor.Position, actor.Position, actor.Position, actor.Position);

    public static FinisherActionContext CloseRange(
        NCreature actor,
        NCreature target,
        float travelPixels,
        FinisherApproachMode approachMode,
        Vector2 squashMultiplier)
    {
        Vector2 originalPosition = actor.Position;
        float fallbackDirection = actor.Entity.Side == CombatSide.Player ? 1f : -1f;
        float impactX = FinisherImpactPositionResolver.ResolveImpactX(
            actor,
            target,
            squashMultiplier,
            NinjaSlayerCombatVisuals.CloseRangeApproachGap);
        FinisherApproachPath path = FinisherApproachPath.CreateToImpact(
            approachMode,
            originalPosition.X,
            impactX,
            travelPixels,
            fallbackDirection);
        return new FinisherActionContext(
            originalPosition,
            new Vector2(path.TravelStartX, originalPosition.Y),
            new Vector2(path.TravelEndX, originalPosition.Y),
            new Vector2(path.ImpactX, originalPosition.Y));
    }
}

internal interface IFinisherActionAdapter
{
    string Id { get; }
    float TravelPixels { get; }
    float TravelSeconds { get; }
    float ReturnSeconds { get; }
    bool RequiresPositioning { get; }
    bool HasContinuousTravel { get; }
    FinisherApproachMode ApproachMode { get; }
    FinisherActionContext CreateContext(
        NCreature actor,
        NCreature focus,
        Vector2 squashMultiplier);
    bool IsPeakTrigger(string triggerName);
    float GetTravelProgress(float progress);
}

internal static class FinisherActionAdapters
{
    private static readonly HashSet<string> UnknownTriggers = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, IFinisherActionAdapter> TriggerRegistry =
        new(StringComparer.Ordinal);

    public static IFinisherActionAdapter Stationary { get; } = new FinisherActionAdapter(
        "stationary",
        0f,
        0f,
        FinisherTimeline.ReturnSeconds,
        FinisherApproachMode.Stationary,
        static _ => 1f,
        PeakTriggerName: null);

    public static IFinisherActionAdapter TeleportAtPeak { get; } = new FinisherActionAdapter(
        "teleport-at-peak",
        0f,
        0f,
        FinisherTimeline.ReturnSeconds,
        FinisherApproachMode.TeleportAtPeak,
        static _ => 1f,
        PeakTriggerName: null);

    public static IFinisherActionAdapter Fast { get; } = new FinisherActionAdapter(
        "fast",
        FinisherActionTrajectory.FastTravelPixels,
        FinisherActionTrajectory.FastTravelSeconds,
        FinisherTimeline.ReturnSeconds,
        FinisherApproachMode.ContinuousToImpact,
        FinisherActionTrajectory.FastProgress,
        PeakTriggerName: null);

    public static IFinisherActionAdapter Slow { get; } = new FinisherActionAdapter(
        "slow",
        FinisherActionTrajectory.SlowTravelPixels,
        FinisherActionTrajectory.SlowTravelSeconds,
        FinisherActionTrajectory.SlowTravelSeconds,
        FinisherApproachMode.ContinuousToImpact,
        FinisherActionTrajectory.SlowProgress,
        PeakTriggerName: null);

    public static IFinisherActionAdapter Combo { get; } = new FinisherActionAdapter(
        "combo",
        FinisherActionTrajectory.SlowTravelPixels,
        FinisherActionTrajectory.SlowTravelSeconds,
        FinisherTimeline.ReturnSeconds,
        FinisherApproachMode.ContinuousToImpact,
        FinisherActionTrajectory.SlowProgress,
        PeakTriggerName: null);

    public static IFinisherActionAdapter YamotoKokiIai { get; } = new FinisherActionAdapter(
        "yamoto-koki-iai",
        FinisherActionTrajectory.SlowTravelPixels,
        FinisherActionTrajectory.SlowTravelSeconds,
        FinisherActionTrajectory.SlowTravelSeconds,
        FinisherApproachMode.PrepositionThenLunge,
        FinisherActionTrajectory.SlowProgress,
        PeakTriggerName: null);

    static FinisherActionAdapters()
    {
        Register("Attack", Fast);
        Register("SlowAttack", Slow);
        Register("XAttack", Combo);
        Register(TornadoFistSpinAnimation.TriggerName, Combo);
    }

    internal static void Register(string triggerName, IFinisherActionAdapter adapter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerName);
        ArgumentNullException.ThrowIfNull(adapter);
        lock (TriggerRegistry)
        {
            TriggerRegistry[triggerName] = adapter;
        }
    }

    public static IFinisherActionAdapter Resolve(
        GameCompatibility.AttackCommandState command,
        bool jumpActive)
    {
        if (jumpActive)
        {
            return Fast;
        }

        if (!command.ShouldPlayAnimation || string.IsNullOrWhiteSpace(command.AttackerAnimName))
        {
            return TeleportAtPeak;
        }

        lock (TriggerRegistry)
        {
            return TriggerRegistry.TryGetValue(command.AttackerAnimName, out IFinisherActionAdapter? adapter)
                ? adapter
                : ResolveUnknown(command.AttackerAnimName);
        }
    }

    private static IFinisherActionAdapter ResolveUnknown(string triggerName)
    {
        lock (UnknownTriggers)
        {
            if (UnknownTriggers.Add(triggerName))
            {
                FinisherLog.Warn(
                    $"No continuous finisher action is registered for TriggerAnim '{triggerName}'; "
                    + "the finisher will teleport at that animation's peak.");
            }
        }

        return new FinisherActionAdapter(
            $"teleport-at-peak:{triggerName}",
            0f,
            0f,
            FinisherTimeline.ReturnSeconds,
            FinisherApproachMode.TeleportAtPeak,
            static _ => 1f,
            triggerName);
    }

    private sealed record FinisherActionAdapter(
        string Id,
        float TravelPixels,
        float TravelSeconds,
        float ReturnSeconds,
        FinisherApproachMode ApproachMode,
        Func<float, float> Progress,
        string? PeakTriggerName) : IFinisherActionAdapter
    {
        public bool RequiresPositioning => ApproachMode != FinisherApproachMode.Stationary;

        public bool HasContinuousTravel =>
            ApproachMode is FinisherApproachMode.ContinuousToImpact
                or FinisherApproachMode.PrepositionThenLunge;

        public FinisherActionContext CreateContext(
            NCreature actor,
            NCreature focus,
            Vector2 squashMultiplier) =>
            RequiresPositioning
                ? FinisherActionContext.CloseRange(
                    actor,
                    focus,
                    TravelPixels,
                    ApproachMode,
                    squashMultiplier)
                : FinisherActionContext.Stationary(actor);

        public bool IsPeakTrigger(string triggerName) =>
            ApproachMode == FinisherApproachMode.TeleportAtPeak
            && PeakTriggerName != null
            && string.Equals(PeakTriggerName, triggerName, StringComparison.Ordinal);

        public float GetTravelProgress(float progress) => Progress(progress);
    }
}
