using Godot;
using MegaCrit.Sts2.Core.Assets;

namespace NinjaSlayer.Code.Vfx;

public static class NinjaSlayerVfxUtil
{
    public static T GenVfxNode<T>(string scenePath) where T : Node2D
    {
        PackedScene scene = PreloadManager.Cache.GetScene(scenePath);
        if (!GodotObject.IsInstanceValid(scene))
        {
            throw new InvalidOperationException($"The VFX scene '{scenePath}' is unavailable.");
        }

        return scene.Instantiate<T>(PackedScene.GenEditState.Disabled);
    }

    public static T GenModVfxNode<T>(string scenePath) where T : Node2D
    {
        PackedScene scene = LoadModVfxScene(scenePath);
        return scene.Instantiate<T>(PackedScene.GenEditState.Disabled);
    }

    public static void PreloadModVfxScene(string scenePath) => _ = LoadModVfxScene(scenePath);

    private static PackedScene LoadModVfxScene(string scenePath)
    {
        PackedScene? scene = ResourceLoader.Load<PackedScene>(
            scenePath,
            cacheMode: ResourceLoader.CacheMode.Reuse);
        if (!GodotObject.IsInstanceValid(scene))
        {
            throw new InvalidOperationException($"The mod VFX scene '{scenePath}' is unavailable.");
        }

        return scene;
    }
}
