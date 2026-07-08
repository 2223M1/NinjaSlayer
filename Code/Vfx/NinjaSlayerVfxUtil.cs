using System.Collections.Concurrent;
using Godot;

namespace NinjaSlayer.Code.Vfx;

public static class NinjaSlayerVfxUtil
{
    public static readonly ConcurrentDictionary<string, PackedScene> ModSceneCache = new();

    public static T GenVfxNode<T>(string scenePath) where T : Node2D
    {
        if (ModSceneCache.TryGetValue(scenePath, out PackedScene? cached))
        {
            return cached.Instantiate<T>(PackedScene.GenEditState.Disabled);
        }

        PackedScene scene = GD.Load<PackedScene>(scenePath);
        ModSceneCache[scenePath] = scene;
        return scene.Instantiate<T>(PackedScene.GenEditState.Disabled);
    }

    public static void EnsureCached(IEnumerable<string> scenePaths)
    {
        foreach (string path in scenePaths)
        {
            if (ModSceneCache.ContainsKey(path))
            {
                continue;
            }

            ModSceneCache[path] = GD.Load<PackedScene>(path);
        }
    }
}
