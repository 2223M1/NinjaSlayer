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

    public static void BeginPreload()
    {
        if (cachedStream != null && GodotObject.IsInstanceValid(cachedStream))
        {
            return;
        }

        cachedStream = ResourceLoader.Load<VideoStream>(
            NinjaSlayerAssetProfile.TransitionVideoPath,
            cacheMode: ResourceLoader.CacheMode.Reuse);
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

        BeginPreload();
        if (cachedStream is not { } loadedStream || !GodotObject.IsInstanceValid(loadedStream))
        {
            stream = null;
            diagnostic = "the transition VideoStream resource was unavailable";
            return TransitionVideoLoadPollResult.Failed;
        }

        stream = loadedStream;
        diagnostic = null;
        return TransitionVideoLoadPollResult.Loaded;
    }

    public static VideoStream GetStream()
    {
        if (cachedStream != null && GodotObject.IsInstanceValid(cachedStream))
        {
            return cachedStream;
        }

        BeginPreload();
        if (cachedStream is not { } stream || !GodotObject.IsInstanceValid(stream))
        {
            Entry.Logger.Warn(
                $"Missing NinjaSlayer transition video resource: {NinjaSlayerAssetProfile.TransitionVideoPath}");
            throw new InvalidOperationException(
                $"Missing NinjaSlayer transition video: {NinjaSlayerAssetProfile.TransitionVideoPath}");
        }

        return stream;
    }
}
