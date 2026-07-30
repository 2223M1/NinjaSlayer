using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.ExternalAnimations;
public sealed partial class BossDismembermentPresentation
{
    internal static BossDismembermentSnapshot? TryCapture(
        NCombatRoom room,
        NCreature creature,
        string? detachedBoneName = null)
    {
        if (!GodotObject.IsInstanceValid(room)
            || !room.IsInsideTree()
            || !GodotObject.IsInstanceValid(creature))
        {
            return null;
        }

        try
        {
            Node2D sourceBody = creature.Body;
            if (!GodotObject.IsInstanceValid(sourceBody))
            {
                return null;
            }

            Node presentationParent = room.CombatVfxContainer;
            if (!GodotObject.IsInstanceValid(presentationParent)
                || !presentationParent.IsInsideTree())
            {
                throw new InvalidOperationException(
                    "The combat VFX container is unavailable for dismemberment.");
            }

            if (!CombatCinematicCameraLease.TryResolveBaseline(
                    room,
                    out CombatSceneBaseline baseline))
            {
                throw new InvalidOperationException(
                    "The complete-battle camera baseline is unavailable.");
            }

            Transform2D bodyToSceneContainer = room.SceneContainer
                .GetGlobalTransform()
                .AffineInverse()
                * sourceBody.GlobalTransform;
            Rect2 fallbackBounds = ResolveFallbackCaptureBounds(creature, sourceBody);
            ulong seed = CreateSeed(creature);
            bool canSplitSpine = creature.HasSpineAnimation
                && !creature.Visuals.IsUsingPhobiaModeBody;
            BossVisualCapture? capture = BossVisualCapture.TryCreate(
                presentationParent,
                sourceBody,
                fallbackBounds,
                bodyToSceneContainer,
                baseline,
                canSplitSpine,
                seed,
                detachedBoneName);
            if (capture == null)
            {
                return null;
            }

            return new BossDismembermentSnapshot(
                capture,
                capture.BodyLocalBounds,
                seed,
                creature.Entity.Monster?.Id.Entry ?? creature.Name.ToString());
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn(
                $"Boss dismemberment snapshot capture failed for "
                + $"{creature.Entity.Monster?.Id.Entry}: {exception}");
            return null;
        }
    }

    internal static BossDismembermentSpawn TrySpawn(
        NCombatRoom room,
        NCreature creature,
        BossDismembermentSnapshot? snapshot,
        Vector2 bodyExplosionCenter,
        string? detachedBoneName = null,
        Vector2? detachedExplosionCenter = null,
        int zIndex = BossBurstPresentationCoordinator.FragmentZIndex)
    {
        BossDismembermentPresentation? presentation = TryCreatePresentation(
            room,
            creature,
            snapshot,
            bodyExplosionCenter,
            detachedBoneName,
            detachedExplosionCenter,
            zIndex,
            PresentationMode.CompressedBurst,
            architectFallDirection: 0f,
            out string failureReason);
        return presentation == null
            ? CompleteWithoutFragments(creature, failureReason)
            : new BossDismembermentSpawn(true, presentation.Completion);
    }

    internal static ArchitectBossSoftBodyLead? TrySpawnArchitectLead(
        NCombatRoom room,
        NCreature creature,
        BossDismembermentSnapshot? snapshot,
        float fallDirection,
        int zIndex = BossBurstPresentationCoordinator.FragmentZIndex)
    {
        Vector2 burstOrigin = snapshot?.BodyGlobalCenter ?? Vector2.Zero;
        BossDismembermentPresentation? presentation = TryCreatePresentation(
            room,
            creature,
            snapshot,
            burstOrigin,
            detachedBoneName: null,
            detachedExplosionCenter: null,
            zIndex,
            PresentationMode.ArchitectLead,
            fallDirection,
            out string failureReason);
        if (presentation != null)
        {
            return new ArchitectBossSoftBodyLead(presentation);
        }

        CompleteWithoutFragments(creature, failureReason);
        return null;
    }

    private static BossDismembermentPresentation? TryCreatePresentation(
        NCombatRoom room,
        NCreature creature,
        BossDismembermentSnapshot? snapshot,
        Vector2 bodyExplosionCenter,
        string? detachedBoneName,
        Vector2? detachedExplosionCenter,
        int zIndex,
        PresentationMode mode,
        float architectFallDirection,
        out string failureReason)
    {
        long spawnStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        failureReason = string.Empty;
        if (!GodotObject.IsInstanceValid(room)
            || !GodotObject.IsInstanceValid(creature)
            || !room.IsInsideTree())
        {
            failureReason = "the combat room or creature is no longer available";
            return null;
        }

        if (snapshot == null)
        {
            failureReason = "the pre-death capture is unavailable";
            return null;
        }

        BossVisualCapture? capture = snapshot.TakeCapture();
        if (capture == null)
        {
            failureReason = "the pre-death capture was already consumed";
            return null;
        }

        if (!capture.IsReady
            || capture.Texture == null
            || capture.Partition == null)
        {
            string reason = string.IsNullOrWhiteSpace(capture.FailureReason)
                ? "the GPU capture did not finish before the presentation began"
                : capture.FailureReason;
            capture.Dispose();
            failureReason = reason;
            return null;
        }

        Node presentationParent = room.CombatVfxContainer;
        if (!GodotObject.IsInstanceValid(presentationParent)
            || !presentationParent.IsInsideTree())
        {
            capture.Dispose();
            failureReason = "the capture parent is unavailable";
            return null;
        }

        var presentation = new BossDismembermentPresentation
        {
            Name = "NinjaSlayerBossDismemberment",
            ProcessMode = ProcessModeEnum.Always,
            ZAsRelative = false,
            ZIndex = zIndex,
            _capture = capture,
            _room = room,
            _bodyLocalBounds = snapshot.BodyLocalBounds,
            _seed = snapshot.Seed,
            _monsterId = snapshot.MonsterId,
            _mode = mode,
            _burstTriggered = mode == PresentationMode.CompressedBurst,
            _captureSetupTicks = capture.SetupElapsedTicks,
            _captureReadyElapsedTicks = capture.ReadyElapsedTicks,
            _captureMeasurementCpuTicks = capture.MeasurementCpuTicks,
            _captureAtlasCpuTicks = capture.AtlasCpuTicks,
            _captureFragmentPreparationCpuTicks = capture.FragmentPreparationCpuTicks,
            _captureMaximumCpuFrameTicks = capture.MaximumCpuFrameTicks
        };
        IReadOnlyList<BossCapturedFragmentRenderSurface.PreparedResource>? preparedFragments = null;
        try
        {
            presentationParent.AddChildSafely(presentation);
            if (!GodotObject.IsInstanceValid(presentation) || !presentation.IsInsideTree())
            {
                throw new InvalidOperationException(
                    "the fragment presentation could not enter the scene tree");
            }

            presentation.InitializeGeometry(
                snapshot.BodyToSceneContainer,
                snapshot.BaselineSceneToGlobal);
            BossFragmentPartition partition = capture.Partition;
            if (partition.Fragments.Count < 2)
            {
                throw new InvalidOperationException(
                    "the captured body produced fewer than two semantic fragments");
            }

            preparedFragments = capture.TakePreparedFragments();
            if (preparedFragments.Count != partition.Fragments.Count)
            {
                throw new InvalidOperationException(
                    "the prebuilt soft-fragment resources are incomplete");
            }

            Vector2I captureSize = capture.PixelSize;
            presentation._capturePixelWidth = captureSize.X;
            presentation._capturePixelHeight = captureSize.Y;
            presentation._captureTextureBytes = capture.EstimatedTextureBytes;
            presentation._semanticPartCount = partition.SemanticPartCount;
            presentation._mergedPartCount = partition.MergedPartCount;
            presentation._splitFragmentCount = partition.SplitFragmentCount;
            presentation._atlasDensity = capture.AtlasDensity;
            presentation._baselineSceneScale = snapshot.BaselineSceneScale.X;
            presentation._currentSceneScale = room.SceneContainer.Scale.X;
            presentation.ValidateBaselineFragmentGeometry(
                partition,
                snapshot.BodyBaselineScreenBounds);
            presentation._burstOrigin = presentation.ToLocalPoint(bodyExplosionCenter);
            presentation.InitializeSoftBodies(
                partition,
                preparedFragments,
                zIndex,
                mode,
                architectFallDirection,
                detachedExplosionCenter);
            if (GodotObject.IsInstanceValid(creature.Body))
            {
                creature.Body.Visible = false;
            }
            presentation.SetProcess(true);
            presentation._spawnSetupTicks =
                System.Diagnostics.Stopwatch.GetTimestamp() - spawnStarted;
            return presentation;
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn(
                $"Boss dismemberment fragment generation failed for "
                + $"{creature.Entity.Monster?.Id.Entry}: {exception}");
            failureReason = exception.Message;
            if (GodotObject.IsInstanceValid(presentation))
            {
                presentation.CompleteAndFree();
            }

            if (preparedFragments != null)
            {
                foreach (BossCapturedFragmentRenderSurface.PreparedResource prepared
                    in preparedFragments)
                {
                    prepared.Dispose();
                }
            }

            capture.Dispose();
            return null;
        }
    }

    internal static BossDismembermentSpawn CompleteWithoutFragments(
        NCreature creature,
        string reason)
    {
        Entry.Logger.Warn(
            $"Boss dismemberment completed without fragments for "
            + $"{creature.Entity.Monster?.Id.Entry}: {reason}; "
            + "keeping the original death pose visible.");
        return new BossDismembermentSpawn(false, Task.CompletedTask);
    }
}
