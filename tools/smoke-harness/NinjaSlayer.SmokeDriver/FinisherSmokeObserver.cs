using System.Collections;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.SmokeDriver;

internal static class FinisherSmokeObserver
{
    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticMembers =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Assembly ModAssembly = typeof(DarkNinjaMonster).Assembly;
    private static readonly Type SessionType = GetModType(
        "NinjaSlayer.Code.ExternalAnimations.FinisherSession");
    private static readonly Type RegistryType = GetModType(
        "NinjaSlayer.Code.ExternalAnimations.FinisherSessionRegistry");
    private static readonly Type PresentationType = GetModType(
        "NinjaSlayer.Code.ExternalAnimations.FinisherImpactPresentation");
    private static readonly Type CameraLeaseType = GetModType(
        "NinjaSlayer.Code.ExternalAnimations.CombatCinematicCameraLease");
    private static readonly Type HoverTipLeaseType = GetModType(
        "NinjaSlayer.Code.Nodes.NinjaSlayerHoverTipSuppression");
    private static readonly PropertyInfo SessionId = GetProperty(SessionType, "SessionId");
    private static readonly PropertyInfo Scenario = GetProperty(SessionType, "Scenario");
    private static readonly PropertyInfo ResolvedHits = GetProperty(SessionType, "ResolvedHits");
    private static readonly FieldInfo Ledger = GetField(SessionType, "_ledger");
    private static readonly FieldInfo Completion = GetField(SessionType, "_completion");
    private static readonly FieldInfo SessionDisposed = GetField(SessionType, "_disposed");
    private static readonly FieldInfo HoverTipSuppression = GetField(SessionType, "_hoverTipSuppression");
    private static readonly FieldInfo ActorLayerLease = GetField(SessionType, "_actorLayerLease");
    private static readonly FieldInfo Presentation = GetField(SessionType, "_presentation");
    private static readonly PropertyInfo LedgerVictims = GetProperty(Ledger.FieldType, "Victims");
    private static readonly MethodInfo GetActiveSession = GetMethod(RegistryType, "GetActiveSession");
    private static readonly FieldInfo PendingSession = GetField(RegistryType, "_pendingAfterCardPlayed");
    private static readonly PropertyInfo CameraLeaseActive = GetProperty(
        CameraLeaseType,
        "IsControllingCamera");
    private static readonly FieldInfo ActiveHoverTipLeases = GetField(
        HoverTipLeaseType,
        "_activeLeases");
    private static readonly List<ObservedSession> Sessions = [];
    private static bool _injectPresentationFailure;

    internal static MethodInfo BeginMethod => GetMethod(SessionType, "Begin");

    internal static MethodInfo StartCompletionMethod => GetMethod(
        SessionType,
        "StartCompletion");

    internal static MethodInfo PresentationCreateMethod => PresentationType
        .GetMethods(StaticMembers)
        .Single(method => method.Name == "Create" && method.GetParameters().Length == 3);

    internal static bool PresentationFailureWasInjected { get; private set; }

    internal static void Reset(bool injectPresentationFailure = false)
    {
        Sessions.Clear();
        _injectPresentationFailure = injectPresentationFailure;
        PresentationFailureWasInjected = false;
    }

    internal static void ObserveBegin(object session)
    {
        if (Sessions.Any(observed => ReferenceEquals(observed.Session, session)))
        {
            return;
        }

        Sessions.Add(CreateObservation(session));
    }

    internal static void ObserveStartCompletion(object session, bool commitDeaths)
    {
        ObservedSession observed = GetOrCreate(session);
        observed.CommitDeaths = commitDeaths;
        if (observed.CompletionObservationStarted)
        {
            return;
        }

        observed.CompletionObservationStarted = true;
        object source = Completion.GetValue(session)
            ?? throw new InvalidOperationException("A FinisherSession had no completion source.");
        Task task = source.GetType().GetProperty("Task")?.GetValue(source) as Task
            ?? throw new InvalidOperationException("A FinisherSession completion source had no Task.");
        _ = ObserveCompletion(observed, task);
    }

    internal static object? ObserveKillStarted(Creature target)
    {
        ObservedSession? observed = Sessions.LastOrDefault(candidate =>
            !candidate.CompletionObserved
            && candidate.Victims.Any(victim => ReferenceEquals(victim, target)));
        if (observed == null)
        {
            return null;
        }

        Increment(observed.KillAttempts, target);
        return observed;
    }

    internal static Task ObserveKillCompletion(Task original, Creature target, object? state) =>
        state is ObservedSession observed
            ? CompleteObservedKill(original, target, observed)
            : original;

    internal static FinisherSessionSnapshot[] Snapshots() => Sessions
        .Select(observed => new FinisherSessionSnapshot(
            observed.SessionId,
            observed.Scenario,
            observed.ResolvedHits,
            observed.Victims,
            observed.CommitDeaths,
            observed.CompletionObserved,
            observed.CompletionFailure,
            IsSessionReleased(observed.Session),
            new Dictionary<Creature, int>(
                observed.KillAttempts,
                ReferenceEqualityComparer.Instance),
            new Dictionary<Creature, int>(
                observed.SuccessfulKills,
                ReferenceEqualityComparer.Instance)))
        .ToArray();

    internal static bool HasRegisteredSession() =>
        GetActiveSession.Invoke(null, null) != null
        || PendingSession.GetValue(null) != null;

    internal static bool HasActiveCameraLease() =>
        CameraLeaseActive.GetValue(null) is true;

    internal static int ActiveHoverTipLeaseCount() =>
        ActiveHoverTipLeases.GetValue(null) is int count
            ? count
            : throw new InvalidOperationException("The finisher hover-tip lease count was unavailable.");

    internal static bool ConsumePresentationFailure()
    {
        if (!_injectPresentationFailure)
        {
            return false;
        }

        _injectPresentationFailure = false;
        PresentationFailureWasInjected = true;
        return true;
    }

    private static async Task ObserveCompletion(ObservedSession observed, Task completion)
    {
        try
        {
            await completion;
        }
        catch (Exception ex)
        {
            observed.CompletionFailure = ex;
        }
        finally
        {
            observed.CompletionObserved = true;
        }
    }

    private static async Task CompleteObservedKill(
        Task original,
        Creature target,
        ObservedSession observed)
    {
        await original;
        if (target.IsDead)
        {
            Increment(observed.SuccessfulKills, target);
        }
    }

    private static ObservedSession GetOrCreate(object session)
    {
        ObservedSession? observed = Sessions.LastOrDefault(candidate =>
            ReferenceEquals(candidate.Session, session));
        if (observed != null)
        {
            return observed;
        }

        observed = CreateObservation(session);
        Sessions.Add(observed);
        return observed;
    }

    private static ObservedSession CreateObservation(object session)
    {
        object ledger = Ledger.GetValue(session)
            ?? throw new InvalidOperationException("A FinisherSession had no damage ledger.");
        IEnumerable victims = LedgerVictims.GetValue(ledger) as IEnumerable
            ?? throw new InvalidOperationException("A FinisherSession ledger had no victim set.");
        return new ObservedSession(
            session,
            GetValue<long>(SessionId, session),
            Scenario.GetValue(session)?.ToString()
                ?? throw new InvalidOperationException("A FinisherSession had no scenario."),
            GetValue<int>(ResolvedHits, session),
            victims.Cast<Creature>().ToArray());
    }

    private static bool IsSessionReleased(object session) =>
        SessionDisposed.GetValue(session) is true
        && HoverTipSuppression.GetValue(session) == null
        && ActorLayerLease.GetValue(session) == null
        && Presentation.GetValue(session) == null;

    private static void Increment(Dictionary<Creature, int> counts, Creature target) =>
        counts[target] = counts.GetValueOrDefault(target) + 1;

    private static T GetValue<T>(PropertyInfo property, object instance) =>
        property.GetValue(instance) is T value
            ? value
            : throw new InvalidOperationException(
                $"{property.DeclaringType?.FullName}.{property.Name} had an unexpected value.");

    private static Type GetModType(string name) =>
        ModAssembly.GetType(name, throwOnError: true)!;

    private static FieldInfo GetField(Type type, string name) =>
        type.GetField(name, InstanceMembers | StaticMembers)
        ?? throw new MissingFieldException(type.FullName, name);

    private static PropertyInfo GetProperty(Type type, string name) =>
        type.GetProperty(name, InstanceMembers | StaticMembers)
        ?? throw new MissingMemberException(type.FullName, name);

    private static MethodInfo GetMethod(Type type, string name) =>
        type.GetMethod(name, InstanceMembers | StaticMembers)
        ?? throw new MissingMethodException(type.FullName, name);

    private sealed class ObservedSession(
        object session,
        long sessionId,
        string scenario,
        int resolvedHits,
        Creature[] victims)
    {
        public object Session { get; } = session;
        public long SessionId { get; } = sessionId;
        public string Scenario { get; } = scenario;
        public int ResolvedHits { get; } = resolvedHits;
        public Creature[] Victims { get; } = victims;
        public Dictionary<Creature, int> KillAttempts { get; } =
            new(ReferenceEqualityComparer.Instance);
        public Dictionary<Creature, int> SuccessfulKills { get; } =
            new(ReferenceEqualityComparer.Instance);
        public bool CommitDeaths { get; set; }
        public bool CompletionObservationStarted { get; set; }
        public bool CompletionObserved { get; set; }
        public Exception? CompletionFailure { get; set; }
    }
}

internal sealed record FinisherSessionSnapshot(
    long SessionId,
    string Scenario,
    int ResolvedHits,
    IReadOnlyList<Creature> Victims,
    bool CommitDeaths,
    bool CompletionObserved,
    Exception? CompletionFailure,
    bool ResourcesReleased,
    IReadOnlyDictionary<Creature, int> KillAttempts,
    IReadOnlyDictionary<Creature, int> SuccessfulKills);

[HarmonyPatch]
internal static class FinisherSessionBeginObservationPatch
{
    public static MethodBase TargetMethod() => FinisherSmokeObserver.BeginMethod;

    public static void Postfix(object __instance) =>
        FinisherSmokeObserver.ObserveBegin(__instance);
}

[HarmonyPatch]
internal static class FinisherSessionCompletionObservationPatch
{
    public static MethodBase TargetMethod() => FinisherSmokeObserver.StartCompletionMethod;

    public static void Postfix(object __instance, bool commitDeaths) =>
        FinisherSmokeObserver.ObserveStartCompletion(__instance, commitDeaths);
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Kill), [typeof(Creature), typeof(bool)])]
internal static class FinisherCreatureKillObservationPatch
{
    public static void Prefix(Creature __0, out object? __state) =>
        __state = FinisherSmokeObserver.ObserveKillStarted(__0);

    public static void Postfix(Creature __0, object? __state, ref Task __result) =>
        __result = FinisherSmokeObserver.ObserveKillCompletion(__result, __0, __state);
}

[HarmonyPatch]
internal static class FinisherPresentationFailurePatch
{
    public static MethodBase TargetMethod() => FinisherSmokeObserver.PresentationCreateMethod;

    public static void Prefix()
    {
        if (FinisherSmokeObserver.ConsumePresentationFailure())
        {
            throw new InvalidOperationException(
                "Injected finisher presentation creation failure.");
        }
    }
}
