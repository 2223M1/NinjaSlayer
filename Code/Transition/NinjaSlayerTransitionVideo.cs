using Godot;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Transition;

internal enum TransitionVideoLoadPollResult
{
    Waiting,
    Loaded,
    Failed
}

internal static class NinjaSlayerTransitionVideo
{
    private static VideoStream? cachedStream;
    private static bool preloadRequested;

    public static void BeginPreload()
    {
        if (cachedStream != null || preloadRequested)
        {
            return;
        }

        preloadRequested = true;
        Error error = ResourceLoader.LoadThreadedRequest(NinjaSlayerAssetProfile.TransitionVideoPath);
        if (error != Error.Ok)
        {
            preloadRequested = false;
        }
    }

    internal static TransitionVideoLoadPollResult PollPreloadedStream(
        out VideoStream? stream,
        out string? diagnostic)
    {
        if (cachedStream != null && GodotObject.IsInstanceValid(cachedStream))
        {
            stream = cachedStream;
            diagnostic = null;
            return TransitionVideoLoadPollResult.Loaded;
        }

        if (!preloadRequested)
        {
            BeginPreload();
        }

        if (!preloadRequested)
        {
            stream = null;
            diagnostic = "Godot rejected the threaded load request";
            return TransitionVideoLoadPollResult.Failed;
        }

        string path = NinjaSlayerAssetProfile.TransitionVideoPath;
        ResourceLoader.ThreadLoadStatus status = ResourceLoader.LoadThreadedGetStatus(path);
        if (status == ResourceLoader.ThreadLoadStatus.InProgress)
        {
            stream = null;
            diagnostic = null;
            return TransitionVideoLoadPollResult.Waiting;
        }

        if (status != ResourceLoader.ThreadLoadStatus.Loaded)
        {
            stream = null;
            diagnostic = $"threaded load ended with {status}";
            return TransitionVideoLoadPollResult.Failed;
        }

        Resource resource = ResourceLoader.LoadThreadedGet(path);
        if (resource is not VideoStream loadedStream)
        {
            stream = null;
            diagnostic = $"resource type was {resource.GetType().Name} instead of VideoStream";
            return TransitionVideoLoadPollResult.Failed;
        }

        cachedStream = loadedStream;
        stream = loadedStream;
        diagnostic = null;
        return TransitionVideoLoadPollResult.Loaded;
    }

    internal static void AllowPreloadRetry()
    {
        preloadRequested = false;
    }

    public static VideoStream GetStream()
    {
        if (cachedStream != null && GodotObject.IsInstanceValid(cachedStream))
        {
            return cachedStream;
        }

        string path = NinjaSlayerAssetProfile.TransitionVideoPath;
        ResourceLoader.ThreadLoadStatus status = ResourceLoader.LoadThreadedGetStatus(path);
        Resource? resource = status is ResourceLoader.ThreadLoadStatus.InProgress or ResourceLoader.ThreadLoadStatus.Loaded
            ? ResourceLoader.LoadThreadedGet(path)
            : ResourceLoader.Load(path, cacheMode: ResourceLoader.CacheMode.Reuse);
        if (resource is not VideoStream stream)
        {
            Entry.Logger.Warn($"Missing NinjaSlayer transition video resource: {path}");
            throw new InvalidOperationException($"Missing NinjaSlayer transition video: {path}");
        }

        cachedStream = stream;
        return stream;
    }
}
