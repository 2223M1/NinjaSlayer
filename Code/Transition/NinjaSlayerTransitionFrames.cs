using Godot;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Transition;

/// <summary>
/// Loads the 60 transition frames exactly once and keeps strong references for the
/// process lifetime. Frames are loaded through <see cref="ResourceLoader"/> (not
/// <c>PreloadManager.Cache</c>) so they are never released by
/// <c>AssetCache.UnloadMissedCacheAssets()</c> when a run starts.
/// </summary>
internal static class NinjaSlayerTransitionFrames
{
    private static Texture2D[]? cachedFrames;
    private static bool preloadRequested;

    private static string FramePath(int index) =>
        string.Format(NinjaSlayerCharacter.TransitionFramePathFormat, index);

    /// <summary>
    /// Non-blocking. Kicks off background threaded loads for every frame so the first
    /// transition does not pay the decode cost on the main thread. Idempotent.
    /// </summary>
    public static void BeginPreload()
    {
        if (cachedFrames != null || preloadRequested)
        {
            return;
        }

        preloadRequested = true;
        for (var i = 0; i < NinjaSlayerCharacter.TransitionFrameCount; i++)
        {
            ResourceLoader.LoadThreadedRequest(FramePath(i));
        }
    }

    /// <summary>
    /// Ensures all frames are loaded and cached, then returns them. Safe to call on the
    /// main thread at transition start: it completes any pending background load, and
    /// otherwise falls back to a synchronous (cache-reusing) load.
    /// </summary>
    public static Texture2D[] GetFrames()
    {
        if (cachedFrames != null && IsCacheValid(cachedFrames))
        {
            return cachedFrames;
        }

        var frames = new Texture2D[NinjaSlayerCharacter.TransitionFrameCount];
        for (var i = 0; i < frames.Length; i++)
        {
            var path = FramePath(i);
            var status = ResourceLoader.LoadThreadedGetStatus(path);
            var resource = status is ResourceLoader.ThreadLoadStatus.InProgress or ResourceLoader.ThreadLoadStatus.Loaded
                ? ResourceLoader.LoadThreadedGet(path)
                : ResourceLoader.Load(path);

            if (resource is not Texture2D texture)
            {
                Entry.Logger.Warn($"Missing NinjaSlayer transition frame resource: {path}");
                throw new InvalidOperationException($"Missing NinjaSlayer transition frame: {path}");
            }

            frames[i] = texture;
        }

        cachedFrames = frames;
        return frames;
    }

    private static bool IsCacheValid(Texture2D[] frames)
    {
        foreach (var frame in frames)
        {
            if (frame == null || !GodotObject.IsInstanceValid(frame))
            {
                return false;
            }
        }

        return true;
    }
}
