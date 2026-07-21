using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed record FinisherCameraFrame(IReadOnlyList<NCreature> Targets, bool UseTargetCentersOnly);

internal static class FinisherCameraFraming
{
    private const float SafeMarginPixels = 64f;

    public static FinisherCameraFrame SelectTargets(
        CombatCinematicCameraLease camera,
        CanvasItem ownerFocus,
        IEnumerable<NCreature> candidates,
        float maximumScale)
    {
        Vector2 focusPoint = camera.GetLocalCenter(ownerFocus);
        List<NCreature> ordered = candidates
            .Where(IsNodeActive)
            .Select((target, index) => new
            {
                Target = target,
                Index = index,
                Distance = Mathf.Abs(camera.GetLocalCenter(target.Visuals.Bounds).X - focusPoint.X)
            })
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.Index)
            .Select(item => item.Target)
            .ToList();
        if (ordered.Count == 0)
        {
            return new FinisherCameraFrame([], false);
        }

        List<NCreature> selected = [];
        foreach (NCreature target in ordered)
        {
            List<NCreature> trial = [.. selected, target];
            if (!CanFrame(camera, ownerFocus, trial, maximumScale, useTargetCentersOnly: false))
            {
                break;
            }

            selected.Add(target);
        }

        if (selected.Count > 0)
        {
            return new FinisherCameraFrame(selected, false);
        }

        // Extreme distances and oversized enemies still keep both subject centers visible.
        return new FinisherCameraFrame([ordered[0]], true);
    }

    public static Vector2 ResolveCenter(
        CombatCinematicCameraLease camera,
        CanvasItem ownerFocus,
        FinisherCameraFrame frame,
        float scale,
        float requestedHorizontalScreenOffset = 0f)
    {
        Rect2 subjects = GetSubjectBounds(camera, ownerFocus, frame.Targets, frame.UseTargetCentersOnly);
        Vector2 halfViewport = GetHalfViewport(camera, scale);
        Vector2 halfContent = GetHalfContent(camera, scale);
        Vector2 minimum = halfViewport;
        Vector2 maximum = camera.SceneSize - halfViewport;
        Vector2 subjectMinimum = subjects.End - halfContent;
        Vector2 subjectMaximum = subjects.Position + halfContent;
        minimum = new Vector2(Mathf.Max(minimum.X, subjectMinimum.X), Mathf.Max(minimum.Y, subjectMinimum.Y));
        maximum = new Vector2(Mathf.Min(maximum.X, subjectMaximum.X), Mathf.Min(maximum.Y, subjectMaximum.Y));

        Vector2 desired = subjects.GetCenter()
            + Vector2.Right * requestedHorizontalScreenOffset / Mathf.Max(scale, 0.0001f);
        return new Vector2(
            ClampOrMidpoint(desired.X, minimum.X, maximum.X),
            ClampOrMidpoint(desired.Y, minimum.Y, maximum.Y));
    }

    private static bool CanFrame(
        CombatCinematicCameraLease camera,
        CanvasItem ownerFocus,
        IReadOnlyList<NCreature> targets,
        float scale,
        bool useTargetCentersOnly)
    {
        Rect2 subjects = GetSubjectBounds(camera, ownerFocus, targets, useTargetCentersOnly);
        Vector2 halfViewport = GetHalfViewport(camera, scale);
        Vector2 halfContent = GetHalfContent(camera, scale);
        if (subjects.Size.X > halfContent.X * 2f || subjects.Size.Y > halfContent.Y * 2f)
        {
            return false;
        }

        Vector2 sceneMinimum = halfViewport;
        Vector2 sceneMaximum = camera.SceneSize - halfViewport;
        Vector2 subjectMinimum = subjects.End - halfContent;
        Vector2 subjectMaximum = subjects.Position + halfContent;
        return Mathf.Max(sceneMinimum.X, subjectMinimum.X) <= Mathf.Min(sceneMaximum.X, subjectMaximum.X)
            && Mathf.Max(sceneMinimum.Y, subjectMinimum.Y) <= Mathf.Min(sceneMaximum.Y, subjectMaximum.Y);
    }

    private static Rect2 GetSubjectBounds(
        CombatCinematicCameraLease camera,
        CanvasItem ownerFocus,
        IReadOnlyList<NCreature> targets,
        bool useTargetCentersOnly)
    {
        Vector2 focusPoint = camera.GetLocalCenter(ownerFocus);
        Rect2 bounds = new(focusPoint, Vector2.Zero);
        foreach (NCreature target in targets.Where(IsNodeActive))
        {
            Rect2 targetBounds = useTargetCentersOnly
                ? new Rect2(camera.GetLocalCenter(target.Visuals.Bounds), Vector2.Zero)
                : camera.GetLocalRect(target.Visuals.Bounds);
            bounds = bounds.Merge(targetBounds);
        }

        return bounds;
    }

    private static Vector2 GetHalfViewport(CombatCinematicCameraLease camera, float scale) =>
        camera.ViewportSize / (2f * Mathf.Max(scale, 0.0001f));

    private static Vector2 GetHalfContent(CombatCinematicCameraLease camera, float scale)
    {
        Vector2 halfViewport = GetHalfViewport(camera, scale);
        Vector2 margin = Vector2.One * SafeMarginPixels / Mathf.Max(scale, 0.0001f);
        return new Vector2(
            Mathf.Max(0f, halfViewport.X - margin.X),
            Mathf.Max(0f, halfViewport.Y - margin.Y));
    }

    private static float ClampOrMidpoint(float value, float minimum, float maximum) =>
        minimum <= maximum ? Mathf.Clamp(value, minimum, maximum) : (minimum + maximum) * 0.5f;

    private static bool IsNodeActive(NCreature node) =>
        GodotObject.IsInstanceValid(node) && node.IsInsideTree() && !node.IsQueuedForDeletion();
}
