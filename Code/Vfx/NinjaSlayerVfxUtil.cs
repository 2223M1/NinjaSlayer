using Godot;
using MegaCrit.Sts2.Core.Assets;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Vfx;

public static class NinjaSlayerVfxUtil
{
    private static readonly Dictionary<string, PackedScene> ModSceneCache = [];
    private static readonly object ModSceneCacheLock = new();

    public static T? TryGenVfxNode<T>(string scenePath) where T : Node2D
    {
        try
        {
            PackedScene scene = PreloadManager.Cache.GetScene(scenePath);
            if (!GodotObject.IsInstanceValid(scene))
            {
                Entry.Logger.Warn($"Unable to load VFX scene: {scenePath}");
                return null;
            }

            return scene.Instantiate<T>(PackedScene.GenEditState.Disabled);
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"Failed to instantiate VFX scene '{scenePath}': {ex}");
            return null;
        }
    }

    public static T? TryGenModVfxNode<T>(string scenePath) where T : Node2D
    {
        try
        {
            PackedScene? scene = GetOrLoadModScene(scenePath);
            if (!GodotObject.IsInstanceValid(scene))
            {
                Entry.Logger.Warn($"Unable to load mod VFX scene: {scenePath}");
                return null;
            }

            return scene.Instantiate<T>(PackedScene.GenEditState.Disabled);
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"Failed to instantiate mod VFX scene '{scenePath}': {ex}");
            return null;
        }
    }

    public static void PreloadModVfxScene(string scenePath)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(GetOrLoadModScene(scenePath)))
            {
                Entry.Logger.Warn($"Unable to preload mod VFX scene: {scenePath}");
            }
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"Failed to preload mod VFX scene '{scenePath}': {ex}");
        }
    }

    private static PackedScene? GetOrLoadModScene(string scenePath)
    {
        lock (ModSceneCacheLock)
        {
            if (ModSceneCache.TryGetValue(scenePath, out PackedScene? scene))
            {
                return scene;
            }

            scene = ResourceLoader.Load<PackedScene>(scenePath, cacheMode: ResourceLoader.CacheMode.Reuse);
            if (GodotObject.IsInstanceValid(scene))
            {
                ModSceneCache[scenePath] = scene;
            }

            return scene;
        }
    }
}
