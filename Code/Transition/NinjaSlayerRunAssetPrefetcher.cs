using System.Diagnostics;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Transition;

internal static class NinjaSlayerRunAssetPrefetcher
{
    private static readonly object InFlightSyncRoot = new();
    private static readonly HashSet<string> InFlightPaths = new(StringComparer.Ordinal);
    private static readonly TransitionAssetPrefetchLeaseState LeaseState = new();
    private static readonly HashSet<string> LoggedFailures = new(StringComparer.Ordinal);
    private static int retainedUnloadPaths;

    public static void PrefetchMainMenuCandidates()
    {
        CharacterModel[] characters =
        [
            ModelDb.Character<NinjaSlayerCharacter>(),
            ModelDb.Character<NinjaSlayerDebugCharacter>()
        ];
        ActModel[] firstActs = ModelDb.ActsByIndex.Count > 0
            ? ModelDb.ActsByIndex[0].ToArray()
            : [];

        TryPrefetch(
            "main-menu-candidates",
            () => BuildPaths(characters, isMultiplayer: true, firstActs, runState: null, room: null));
        NinjaSlayerTransitionScenePrewarmer.TryStart();
    }

    public static void PrefetchSelection(CharacterModel character, bool isMultiplayer)
    {
        if (character is not INinjaSlayerCharacter)
        {
            return;
        }

        ActModel[] firstActs = ModelDb.ActsByIndex.Count > 0
            ? ModelDb.ActsByIndex[0].ToArray()
            : [];
        TryPrefetch(
            $"selection-{character.Id.Entry}",
            () => BuildPaths([character], isMultiplayer, firstActs, runState: null, room: null));
        NinjaSlayerTransitionScenePrewarmer.TryStart();
    }

    public static void PrefetchEmbark(
        IReadOnlyList<CharacterModel> characters,
        bool isMultiplayer,
        IReadOnlyList<ActModel> acts)
    {
        if (!characters.Any(character => character is INinjaSlayerCharacter))
        {
            CancelUnclaimed();
            return;
        }

        TryPrefetch(
            "embark",
            () => BuildPaths(
                characters,
                isMultiplayer,
                acts.Take(1),
                runState: null,
                room: null));
        NinjaSlayerTransitionScenePrewarmer.TryStart();
    }

    public static void PrefetchSavedMetadata(SerializableRun save)
    {
        CharacterModel[] characters = save.Players
            .Select(player => player.CharacterId is { } id
                ? ModelDb.GetByIdOrNull<CharacterModel>(id)
                : null)
            .OfType<CharacterModel>()
            .ToArray();
        if (!characters.Any(character => character is INinjaSlayerCharacter))
        {
            CancelUnclaimed();
            return;
        }

        ActModel[] acts = ResolveSavedAct(save) is { } act ? [act] : [];
        TryPrefetch(
            "saved-run-metadata",
            () => BuildPaths(
                characters,
                save.Players.Count > 1,
                acts,
                NullRunState.Instance,
                save.PreFinishedRoom));
        NinjaSlayerTransitionScenePrewarmer.TryStart();
    }

    public static void PrefetchSavedRun(IRunState runState, SerializableRoom? room)
    {
        CharacterModel[] characters = runState.Players
            .Select(player => player.Character)
            .ToArray();
        if (!characters.Any(character => character is INinjaSlayerCharacter))
        {
            CancelUnclaimed();
            return;
        }

        TryPrefetch(
            "saved-run-resolved",
            () => BuildPaths(
                characters,
                characters.Length > 1,
                [runState.Act],
                runState,
                room));
        NinjaSlayerTransitionScenePrewarmer.TryStart();
    }

    public static IEnumerable<string> FilterAssetsToUnload(IEnumerable<string> paths)
    {
        string[] result = LeaseState.FilterUnprotected(paths, out int protectedCount);
        if (protectedCount > 0)
        {
            Interlocked.Add(ref retainedUnloadPaths, protectedCount);
        }
        return result;
    }

    public static IDisposable? ClaimForTransition()
    {
        long generation = LeaseState.Claim();
        return generation == 0 ? null : new PrefetchLease(generation);
    }

    public static void ResetForMainMenu() => CancelUnclaimed();

    private static IEnumerable<string> BuildPaths(
        IReadOnlyList<CharacterModel> characters,
        bool isMultiplayer,
        IEnumerable<ActModel> acts,
        IRunState? runState,
        SerializableRoom? room)
    {
        if (!GameCompatibility.AssetLoading.TryGetRunAssetPaths(
                characters,
                isMultiplayer,
                out IReadOnlyList<string> runPaths))
        {
            throw new MissingMethodException(
                typeof(PreloadManager).FullName,
                "GetRunAssetPaths");
        }

        var paths = new HashSet<string>(runPaths, StringComparer.Ordinal);
        paths.UnionWith(NRun.AssetPaths);
        paths.UnionWith(NEventRoom.AssetPaths);
        paths.UnionWith(NAncientNameBanner.AssetPaths);
        paths.Add(NAncientEventLayout.ancientScenePath);
        foreach (ActModel act in acts.DistinctBy(act => act.Id))
        {
            AddActPaths(paths, act, runState);
        }

        if (room is not null)
        {
            bool canResolveCombat = runState is not null
                && !ReferenceEquals(runState, NullRunState.Instance);
            AddRoomPaths(
                paths,
                room,
                runState ?? NullRunState.Instance,
                canResolveCombat);
        }

        return paths;
    }

    private static void AddActPaths(ISet<string> paths, ActModel act, IRunState? runState)
    {
        if (runState is not null && !ReferenceEquals(runState, NullRunState.Instance))
        {
            paths.UnionWith(act.AssetPaths);
        }
        else
        {
            paths.Add(act.BackgroundScenePath);
            paths.Add(act.MapBotBgPath);
            paths.Add(act.MapMidBgPath);
            paths.Add(act.MapTopBgPath);
        }

        IEnumerable<AncientEventModel> candidateAncients = act.AllAncients
            .Concat(ModelDb.AllSharedAncients)
            .DistinctBy(ancient => ancient.Id);
        foreach (AncientEventModel ancient in candidateAncients)
        {
            paths.UnionWith(ancient.MapNodeAssetPaths);
            paths.UnionWith(ancient.GetAssetPaths(runState ?? NullRunState.Instance));
        }
    }

    private static void AddRoomPaths(
        ISet<string> paths,
        SerializableRoom room,
        IRunState runState,
        bool canResolveCombat)
    {
        AddEventPaths(paths, room.ParentEventId, runState);
        AddEventPaths(paths, room.EventId, runState);

        if (!canResolveCombat
            || room.EncounterId is not { } encounterId
            || ModelDb.GetByIdOrNull<EncounterModel>(encounterId) is not { } encounter)
        {
            return;
        }

        paths.UnionWith(NCombatRoom.AssetPaths);
        paths.UnionWith(encounter.GetAssetPaths(runState));
    }

    private static void AddEventPaths(
        ISet<string> paths,
        ModelId? eventId,
        IRunState runState)
    {
        if (eventId is { } id && ModelDb.GetByIdOrNull<EventModel>(id) is { } eventModel)
        {
            paths.UnionWith(eventModel.GetAssetPaths(runState));
        }
    }

    private static ActModel? ResolveSavedAct(SerializableRun save)
    {
        if (save.CurrentActIndex < 0 || save.CurrentActIndex >= save.Acts.Count)
        {
            return null;
        }

        return save.Acts[save.CurrentActIndex].Id is { } id
            ? ModelDb.GetByIdOrNull<ActModel>(id)
            : null;
    }

    private static void TryPrefetch(string reason, Func<IEnumerable<string>> pathFactory)
    {
        if (!PreloadManager.Enabled)
        {
            return;
        }

        try
        {
            string[] requested = pathFactory()
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            long generation = LeaseState.BeginOrExtend(requested);
            if (generation == 0)
            {
                return;
            }

            IReadOnlySet<string> loaded = PreloadManager.Cache.GetLoadedCacheAssets();
            string[] queued;
            int alreadyInFlight;
            lock (InFlightSyncRoot)
            {
                alreadyInFlight = requested.Count(InFlightPaths.Contains);
                queued = requested
                    .Where(path => !loaded.Contains(path) && !InFlightPaths.Contains(path))
                    .ToArray();
                InFlightPaths.UnionWith(queued);
            }

            if (queued.Length == 0)
            {
                Entry.Logger.Info(
                    $"NinjaSlayer run asset prefetch ready: reason={reason}, generation={generation}, " +
                    $"requested={requested.Length}, cached={requested.Count(loaded.Contains)}, " +
                    $"in_flight={alreadyInFlight}.");
                return;
            }

            NAssetLoader loader = NAssetLoader.Instance;
            if (!loader.IsInsideTree())
            {
                lock (InFlightSyncRoot)
                {
                    InFlightPaths.ExceptWith(queued);
                }
                throw new InvalidOperationException("NAssetLoader is not in the active scene tree.");
            }

            AssetLoadingSession session = PreloadManager.Cache.CreateSession(
                $"NinjaSlayer prefetch {reason}",
                queued);
            long startedAt = Stopwatch.GetTimestamp();
            Task<bool> task = loader.LoadInTheBackground(session);
            _ = ObserveAsync(task, reason, generation, requested.Length, queued, startedAt);
            Entry.Logger.Info(
                $"NinjaSlayer run asset prefetch queued: reason={reason}, generation={generation}, " +
                $"requested={requested.Length}, cached={requested.Count(loaded.Contains)}, " +
                $"in_flight={alreadyInFlight}, queued={queued.Length}.");
        }
        catch (Exception exception)
        {
            LogFailureOnce(reason, exception);
        }
    }

    private static async Task ObserveAsync(
        Task<bool> task,
        string reason,
        long generation,
        int requestedCount,
        string[] queued,
        long startedAt)
    {
        try
        {
            bool completed = await task;
            Entry.Logger.Info(
                $"NinjaSlayer run asset prefetch completed: reason={reason}, generation={generation}, " +
                $"requested={requestedCount}, loaded={queued.Length}, success={completed}, " +
                $"elapsed={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F1}ms.");
        }
        catch (Exception exception)
        {
            LogFailureOnce(reason, exception);
        }
        finally
        {
            lock (InFlightSyncRoot)
            {
                InFlightPaths.ExceptWith(queued);
            }
        }
    }

    private static void CancelUnclaimed()
    {
        if (LeaseState.CancelUnclaimed())
        {
            Interlocked.Exchange(ref retainedUnloadPaths, 0);
        }
    }

    private static void Release(long generation)
    {
        TransitionAssetPrefetchSnapshot before = LeaseState.Snapshot();
        if (!LeaseState.TryRelease(generation))
        {
            return;
        }

        int retained = Interlocked.Exchange(ref retainedUnloadPaths, 0);
        Entry.Logger.Info(
            $"NinjaSlayer run asset prefetch released: generation={generation}, " +
            $"protected_paths={before.ProtectedPathCount}, retained_unloads={retained}.");
    }

    private static void LogFailureOnce(string reason, Exception exception)
    {
        lock (LoggedFailures)
        {
            if (!LoggedFailures.Add(reason))
            {
                return;
            }
        }

        Entry.Logger.Warn(
            $"NinjaSlayer run asset prefetch degraded for {reason}; original loading will continue: {exception}");
    }

    private sealed class PrefetchLease(long generation) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Release(generation);
            }
        }
    }
}
