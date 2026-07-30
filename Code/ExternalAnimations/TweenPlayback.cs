using Godot;

namespace NinjaSlayer.Code.ExternalAnimations;

internal static class TweenPlayback
{
    public static async Task<bool> AwaitCompletion(
        Tween tween,
        Node owner,
        CancellationToken cancellationToken = default)
    {
        if (!GodotObject.IsInstanceValid(tween)
            || !GodotObject.IsInstanceValid(owner)
            || !owner.IsInsideTree())
        {
            return false;
        }

        bool finished = false;
        tween.Finished += OnFinished;
        try
        {
            while (!finished)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!GodotObject.IsInstanceValid(owner)
                    || !owner.IsInsideTree()
                    || !GodotObject.IsInstanceValid(tween)
                    || !tween.IsValid()
                    || !tween.IsRunning())
                {
                    return false;
                }

                SceneTree tree = owner.GetTree();
                if (!GodotObject.IsInstanceValid(tree))
                {
                    return false;
                }

                await owner.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }

            return GodotObject.IsInstanceValid(owner) && owner.IsInsideTree();
        }
        finally
        {
            if (GodotObject.IsInstanceValid(tween))
            {
                tween.Finished -= OnFinished;
            }
        }

        void OnFinished() => finished = true;
    }
}
