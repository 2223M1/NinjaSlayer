using Godot;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.Code.Nodes;

internal sealed partial class SawatariWeaponVisuals
{
    internal static async Task<bool> FlyWeapon(Sprite2D weapon, Node2D destination, bool catchWeapon,
        float sourceAngle = 177.9726773f, float arcHeight = 32f, float flightSeconds = .25f, bool aimBeforeFlight = false)
    {
        // Keep the same sprite, texture, handle pivot and world transform on release.
        weapon.Reparent(NCombatRoom.Instance!.CombatVfxContainer, keepGlobalTransform: true);
        weapon.Material = null;
        weapon.ZIndex = 0;
        Transform2D start = weapon.GlobalTransform;
        float sign = start.Scale.Y < 0 ? -1f : 1f;
        float angle = (destination.GlobalPosition - start.Origin).Angle() - Mathf.DegToRad(sourceAngle) * sign;
        Tween? tween = null;
        bool landed = false;
        try
        {
            void Fly(float p)
            {
                if (!GodotObject.IsInstanceValid(destination) || !destination.IsInsideTree()) { tween?.Kill(); return; }
                Vector2 position = start.Origin.Lerp(destination.GlobalPosition, p);
                position.Y -= arcHeight * Mathf.Sin(p * Mathf.Pi);
                weapon.GlobalTransform = new Transform2D(aimBeforeFlight ? angle
                    : Mathf.LerpAngle(start.Rotation, angle, Mathf.Min(p * 3f, 1f)), start.Scale, start.Skew, position);
            }
            float duration = CombatActionTimingRuntime.VisualSeconds(flightSeconds);
            if (duration > 0f)
            {
                Fly(0);
                tween = weapon.CreateTween();
                tween.TweenMethod(Callable.From<float>(Fly), 0f, 1f, duration);
                var contact = new TaskCompletionSource<bool>();
                void Arrive() => contact.TrySetResult(true);
                void Cancel() { tween.Kill(); contact.TrySetResult(false); }
                tween.Finished += Arrive;
                weapon.TreeExiting += Cancel;
                destination.TreeExiting += Cancel;
                try { if (!await contact.Task) return false; }
                finally
                {
                    if (GodotObject.IsInstanceValid(tween)) tween.Finished -= Arrive;
                    if (GodotObject.IsInstanceValid(weapon)) weapon.TreeExiting -= Cancel;
                    if (GodotObject.IsInstanceValid(destination)) destination.TreeExiting -= Cancel;
                }
            }
            if (!GodotObject.IsInstanceValid(destination) || !destination.IsInsideTree()) return false;
            Fly(1);
            if (!catchWeapon) return false;
            // Contact releases the damage command. Only the receiving hand owns the turn tail.
            weapon.Reparent(destination, keepGlobalTransform: true);
            weapon.Position = Vector2.Zero;
            // The receiving grip owns reflection; keep size without a second mirror from the sender.
            weapon.Scale = weapon.Scale.Abs();
            weapon.Skew = 0f;
            weapon.ZIndex = 0;
            landed = true;
            float catchSeconds = CombatActionTimingRuntime.VisualSeconds(.12f);
            if (catchSeconds <= 0f) weapon.Rotation = 0f;
            else
            {
                Tween catching = weapon.CreateTween();
                catching.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
                float targetAngle = weapon.Rotation + Mathf.AngleDifference(weapon.Rotation, 0f);
                catching.TweenProperty(weapon, "rotation", targetAngle, catchSeconds);
            }
            return true;
        }
        finally
        {
            if (!landed && GodotObject.IsInstanceValid(weapon)) weapon.QueueFree();
        }
    }
}
