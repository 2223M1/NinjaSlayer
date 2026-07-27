using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Content;

namespace NinjaSlayer.Code.ExternalAnimations;

internal readonly record struct FinisherActionContext(
    Vector2 OriginalPosition,
    Vector2 PreparationPosition,
    Vector2 ImpactPosition)
{
    public static FinisherActionContext Stationary(NCreature actor) =>
        new(actor.Position, actor.Position, actor.Position);

    public static FinisherActionContext CloseRange(
        NCreature actor,
        NCreature target,
        float travelPixels)
    {
        Vector2 originalPosition = actor.Position;
        float direction = Mathf.Sign(target.Position.X - originalPosition.X);
        if (Mathf.IsZeroApprox(direction))
        {
            direction = actor.Entity.Side == CombatSide.Player ? 1f : -1f;
        }

        float targetHalfWidth = target.Visuals.Bounds.Size.X
            * Mathf.Abs(target.Visuals.Scale.X)
            * 0.5f;
        Vector2 impactPosition = new(
            target.Position.X
                - direction * (targetHalfWidth + NinjaSlayerCombatVisuals.CloseRangeApproachGap),
            originalPosition.Y);
        Vector2 preparationPosition = impactPosition
            - Vector2.Right * direction * travelPixels;
        return new FinisherActionContext(
            originalPosition,
            preparationPosition,
            impactPosition);
    }
}

internal interface IFinisherActionAdapter
{
    string Id { get; }
    float TravelPixels { get; }
    float TravelSeconds { get; }
    float ReturnSeconds { get; }
    bool MovesActor { get; }
    FinisherActionContext CreateContext(NCreature actor, NCreature focus);
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
        static _ => 1f);

    public static IFinisherActionAdapter Fast { get; } = new FinisherActionAdapter(
        "fast",
        FinisherActionTrajectory.FastTravelPixels,
        FinisherActionTrajectory.FastTravelSeconds,
        FinisherTimeline.ReturnSeconds,
        FinisherActionTrajectory.FastProgress);

    public static IFinisherActionAdapter Slow { get; } = new FinisherActionAdapter(
        "slow",
        FinisherActionTrajectory.SlowTravelPixels,
        FinisherActionTrajectory.SlowTravelSeconds,
        FinisherActionTrajectory.SlowTravelSeconds,
        FinisherActionTrajectory.SlowProgress);

    public static IFinisherActionAdapter Combo { get; } = new FinisherActionAdapter(
        "combo",
        FinisherActionTrajectory.SlowTravelPixels,
        FinisherActionTrajectory.SlowTravelSeconds,
        FinisherTimeline.ReturnSeconds,
        FinisherActionTrajectory.SlowProgress);

    public static IFinisherActionAdapter YamotoKokiIai { get; } = new FinisherActionAdapter(
        "yamoto-koki-iai",
        FinisherActionTrajectory.SlowTravelPixels,
        FinisherActionTrajectory.SlowTravelSeconds,
        FinisherActionTrajectory.SlowTravelSeconds,
        FinisherActionTrajectory.SlowProgress);

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
            return Stationary;
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
                    $"No finisher action adapter is registered for TriggerAnim '{triggerName}'; the finisher will remain stationary.");
            }
        }

        return Stationary;
    }

    private sealed record FinisherActionAdapter(
        string Id,
        float TravelPixels,
        float TravelSeconds,
        float ReturnSeconds,
        Func<float, float> Progress) : IFinisherActionAdapter
    {
        public bool MovesActor => TravelPixels > 0f && TravelSeconds > 0f;

        public FinisherActionContext CreateContext(NCreature actor, NCreature focus) =>
            MovesActor
                ? FinisherActionContext.CloseRange(actor, focus, TravelPixels)
                : FinisherActionContext.Stationary(actor);

        public float GetTravelProgress(float progress) => Progress(progress);
    }
}
