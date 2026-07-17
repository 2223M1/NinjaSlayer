using Godot;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;

namespace NinjaSlayer.Code.ExternalAnimations;

public interface ICinematicAnimationContext
{
    CancellationToken CancellationToken { get; }

    Task AwaitTween(Node owner, Tween tween);

    void PlaySfx(string eventPath);

    void PlayScreenShake(ShakeStrength strength, ShakeDuration duration, float degrees = -1f);
}
