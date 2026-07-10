using Godot;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Vfx;

public static class NinjaSlayerVfxUtil
{
    public static T? TryGenVfxNode<T>(string scenePath) where T : Node2D
    {
        try
        {
            PackedScene? scene = ResourceLoader.Load<PackedScene>(
                scenePath,
                null,
                ResourceLoader.CacheMode.Reuse);
            if (scene == null || !GodotObject.IsInstanceValid(scene))
            {
                scene = ResourceLoader.Load<PackedScene>(
                    scenePath,
                    null,
                    ResourceLoader.CacheMode.Replace);
            }

            if (scene == null || !GodotObject.IsInstanceValid(scene))
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
}
