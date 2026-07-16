using Godot;

namespace NinjaSlayer.Code.ExternalAnimations;

public interface ICinematicAnimationContext
{
    CancellationToken CancellationToken { get; }

    Task AwaitTween(Node owner, Tween tween);

    void PlaySfx(string eventPath);
}
