using Godot;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Transition;

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
        ResourceLoader.LoadThreadedRequest(NinjaSlayerCharacter.TransitionVideoPath);
    }

    public static VideoStream GetStream()
    {
        if (cachedStream != null && GodotObject.IsInstanceValid(cachedStream))
        {
            return cachedStream;
        }

        string path = NinjaSlayerCharacter.TransitionVideoPath;
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
