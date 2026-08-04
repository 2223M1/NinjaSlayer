using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.Combat;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed record FinisherCameraFrame(IReadOnlyList<NCreature> Targets, bool UseTargetCentersOnly);

internal static class FinisherCameraFraming
{
    private const float SafeMarginPixels = 64f;

    public static FinisherCameraFrame SelectTargets(
        CombatCinematicCameraLease camera,
        CanvasItem ownerFocus,
        IEnumerable<NCreature> candidates,
        float maximumScale) =>
        SelectTargets(camera, camera.GetLocalCenter(ownerFocus), candidates, maximumScale);

    public static FinisherCameraFrame SelectTargets(
        CombatCinematicCameraLease camera,
        Vector2 ownerFocusPoint,
        IEnumerable<NCreature> candidates,
        float maximumScale)
    {
        List<NCreature> ordered = candidates
            .Where(IsNodeActive)
            .Select((target, index) => new
            {
                Target = target,
                Index = index,
                Distance = Mathf.Abs(camera.GetLocalCenter(target.Visuals.Bounds).X - ownerFocusPoint.X)
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
            if (!CanFrame(camera, ownerFocusPoint, trial, maximumScale, useTargetCentersOnly: false))
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
        float requestedHorizontalScreenOffset = 0f) =>
        ResolveCenter(
            camera,
            camera.GetLocalCenter(ownerFocus),
            frame,
            scale,
            requestedHorizontalScreenOffset);

    public static Vector2 ResolveCenter(
        CombatCinematicCameraLease camera,
        Vector2 ownerFocusPoint,
        FinisherCameraFrame frame,
        float scale,
        float requestedHorizontalScreenOffset = 0f)
    {
        Rect2 subjects = GetSubjectBounds(
            camera,
            ownerFocusPoint,
            frame.Targets,
            frame.UseTargetCentersOnly);
        Vector2 desired = subjects.GetCenter()
            + Vector2.Right * requestedHorizontalScreenOffset / Mathf.Max(scale, 0.0001f);
        return camera.ClampTarget(desired, scale);
    }

    private static bool CanFrame(
        CombatCinematicCameraLease camera,
        Vector2 ownerFocusPoint,
        IReadOnlyList<NCreature> targets,
        float scale,
        bool useTargetCentersOnly)
    {
        Rect2 subjects = GetSubjectBounds(camera, ownerFocusPoint, targets, useTargetCentersOnly);
        Vector2 halfViewport = GetHalfViewport(camera, scale);
        Vector2 halfContent = GetHalfContent(camera, scale);
        if (subjects.Size.X > halfContent.X * 2f || subjects.Size.Y > halfContent.Y * 2f)
        {
            return false;
        }

        Rect2 sceneBounds = camera.BaselineVisibleSceneBounds;
        Vector2 sceneMinimum = sceneBounds.Position + halfViewport;
        Vector2 sceneMaximum = sceneBounds.End - halfViewport;
        Vector2 subjectMinimum = subjects.End - halfContent;
        Vector2 subjectMaximum = subjects.Position + halfContent;
        return Mathf.Max(sceneMinimum.X, subjectMinimum.X) <= Mathf.Min(sceneMaximum.X, subjectMaximum.X)
            && Mathf.Max(sceneMinimum.Y, subjectMinimum.Y) <= Mathf.Min(sceneMaximum.Y, subjectMaximum.Y);
    }

    private static Rect2 GetSubjectBounds(
        CombatCinematicCameraLease camera,
        Vector2 ownerFocusPoint,
        IReadOnlyList<NCreature> targets,
        bool useTargetCentersOnly)
    {
        Rect2 bounds = new(ownerFocusPoint, Vector2.Zero);
        foreach (NCreature target in targets.Where(IsNodeActive))
        {
            Rect2 targetBounds = useTargetCentersOnly
                ? new Rect2(camera.GetLocalCenter(target.Visuals.Bounds), Vector2.Zero)
                : camera.GetLocalRect(target.Visuals.Bounds);
            bounds = bounds.Merge(targetBounds);
        }

        return bounds;
    }

    private static Vector2 GetHalfViewport(CombatCinematicCameraLease camera, float scale)
    {
        Rect2 bounds = camera.BaselineVisibleSceneBounds;
        return new Vector2(
            CinematicCameraContainment.ResolveVisibleHalfExtent(
                bounds.Position.X,
                bounds.End.X,
                camera.BaselineScale.X,
                scale),
            CinematicCameraContainment.ResolveVisibleHalfExtent(
                bounds.Position.Y,
                bounds.End.Y,
                camera.BaselineScale.Y,
                scale));
    }

    private static Vector2 GetHalfContent(CombatCinematicCameraLease camera, float scale)
    {
        Vector2 halfViewport = GetHalfViewport(camera, scale);
        Vector2 viewportSize = camera.ViewportSize;
        Vector2 margin = new(
            viewportSize.X > 0f ? halfViewport.X * 2f * SafeMarginPixels / viewportSize.X : 0f,
            viewportSize.Y > 0f ? halfViewport.Y * 2f * SafeMarginPixels / viewportSize.Y : 0f);
        return new Vector2(
            Mathf.Max(0f, halfViewport.X - margin.X),
            Mathf.Max(0f, halfViewport.Y - margin.Y));
    }

    private static bool IsNodeActive(NCreature node) =>
        GodotObject.IsInstanceValid(node) && node.IsInsideTree() && !node.IsQueuedForDeletion();
}
