using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;
using System.Runtime.CompilerServices;

namespace NinjaSlayer.Code.ExternalAnimations;

public enum NinjaSlayerDeathKind
{
    EnemyKill,
    Other
}

internal sealed record NinjaSlayerDeathContext(
    NinjaSlayerDeathKind Kind,
    DamageReceivedEntry? FatalEntry,
    Creature? Dealer,
    IReadOnlySet<ulong> VfxBaselineChildIds);

public static class DeathAnimation
{
    public const float EnemyKillDurationSeconds = 0.8f;
    public const float OtherDeathDurationSeconds = 0.45f;
    private const float EnemyFinisherImpactSeconds = 0.3f;

    private const float HitRotationDegrees = -15f;
    private const float DoomHitRotationDegrees = 15f;
    private const float FallRotationDegrees = -90f;
    private static readonly Vector2 DeathFallPivotTextureOffset = new(
        NinjaSlayerVisualRig.SpinPivotDeltaX,
        260f);
    private const float FlightTargetHeightRatio = 0.28f;
    private const float FlightArcHeightRatio = 0.3f;
    private const float FlightExitPadding = 32f;
    private const float ImpactLeadSeconds = 0.04f;
    private const float ImpactRecoverySeconds = 0.1f;
    private const float CameraZoomMultiplier = 2f;
    private const float CameraPunchMultiplier = 1.06f;
    private const float BackdropFadeInSeconds = 0.1f;
    private const float CameraReturnSeconds = 0.2f;
    private const float ImpactVfxTargetMargin = 160f;

    private static readonly ConditionalWeakTable<Creature, DeathVisualState> VisualStates = new();
    private static readonly ConditionalWeakTable<Creature, ConsumedFatalDamage> ConsumedFatalDamageEntries = new();
    private static readonly Dictionary<Creature, IncomingDamageCapture> IncomingDamageCaptures = [];

    internal static NinjaSlayerDeathContext CreateContext(Creature creature)
    {
        DamageReceivedEntry? fatalEntry = CombatManager.Instance?.History.Entries
            .OfType<DamageReceivedEntry>()
            .LastOrDefault(entry => entry.Receiver == creature && entry.Result.WasTargetKilled);

        var consumed = ConsumedFatalDamageEntries.GetOrCreateValue(creature);
        if (fatalEntry == null || ReferenceEquals(consumed.Entry, fatalEntry))
        {
            return new NinjaSlayerDeathContext(
                NinjaSlayerDeathKind.Other,
                null,
                null,
                new HashSet<ulong>());
        }

        consumed.Entry = fatalEntry;
        Creature? dealer = fatalEntry.Dealer;
        bool isEnemyKill = dealer != null
            && dealer != creature
            && dealer.Side != creature.Side
            && NCombatRoom.Instance?.GetCreatureNode(dealer) != null;
        IReadOnlySet<ulong> baseline = isEnemyKill
            && IncomingDamageCaptures.TryGetValue(creature, out IncomingDamageCapture? capture)
            && capture.Dealer == dealer
                ? capture.VfxBaselineChildIds
                : new HashSet<ulong>();
        return new NinjaSlayerDeathContext(
            isEnemyKill ? NinjaSlayerDeathKind.EnemyKill : NinjaSlayerDeathKind.Other,
            fatalEntry,
            isEnemyKill ? dealer : null,
            baseline);
    }

    public static float GetDuration(NinjaSlayerDeathKind kind) => kind switch
    {
        NinjaSlayerDeathKind.EnemyKill => EnemyFinisherImpactSeconds + EnemyKillDurationSeconds,
        _ => OtherDeathDurationSeconds
    };

    internal static object? BeginIncomingDamageCapture(
        IEnumerable<Creature>? targets,
        Creature? dealer)
    {
        NCombatRoom? room = NCombatRoom.Instance;
        if (dealer == null || room == null || targets == null)
        {
            return null;
        }

        List<Creature> ninjaSlayerTargets = targets
            .Where(target => target.Player?.Character is INinjaSlayerCharacter
                && target != dealer
                && target.Side != dealer.Side)
            .Distinct()
            .ToList();
        if (ninjaSlayerTargets.Count == 0)
        {
            return null;
        }

        var capture = new IncomingDamageCapture(
            dealer,
            room.CombatVfxContainer.GetChildren()
                .Select(child => child.GetInstanceId())
                .ToHashSet(),
            ninjaSlayerTargets);
        foreach (Creature target in ninjaSlayerTargets)
        {
            IncomingDamageCaptures[target] = capture;
        }

        return capture;
    }

    internal static async Task<IEnumerable<DamageResult>> CompleteIncomingDamageCapture(
        Task<IEnumerable<DamageResult>> damageTask,
        object? state)
    {
        if (state is not IncomingDamageCapture capture)
        {
            return await damageTask;
        }

        try
        {
            return await damageTask;
        }
        finally
        {
            foreach (Creature target in capture.Targets)
            {
                if (IncomingDamageCaptures.TryGetValue(target, out IncomingDamageCapture? active)
                    && ReferenceEquals(active, capture))
                {
                    IncomingDamageCaptures.Remove(target);
                }
            }
        }
    }

    internal static async Task Play(Creature creature, NinjaSlayerDeathContext context)
    {
        NCombatRoom? room = NCombatRoom.Instance;
        var creatureNode = room?.GetCreatureNode(creature);
        if (room == null || creatureNode == null)
        {
            return;
        }

        RestoreVisual(creature, markCurrentFatalDamageConsumed: false);
        StaggerAnimation.Reset(creature);
        SoarSpinAnimation.ResetSpinVisual(creature);
        if (SoarVisualState.IsAirborne(creature))
        {
            SoarVisualState.ResetVisualsToGround(creature);
        }

        Node2D? anchor = NinjaSlayerVisualRig.GetAirborneAnchor(creatureNode.Visuals);
        Sprite2D? body = NinjaSlayerVisualRig.GetBodySprite(creatureNode.Visuals);
        if (anchor == null || body == null)
        {
            if (context.Kind == NinjaSlayerDeathKind.EnemyKill)
            {
                NinjaSlayerCombatAudioSet.Play(NinjaSlayerCombatAudioSet.For(creature).Death);
                NGame.Instance?.ScreenShake(ShakeStrength.TooMuch, ShakeDuration.Short);
                await creatureNode.ToSignal(
                    creatureNode.GetTree().CreateTimer(EnemyFinisherImpactSeconds),
                    SceneTreeTimer.SignalName.Timeout);
            }

            creatureNode.SetAnimationTrigger("Dead");
            await creatureNode.ToSignal(
                creatureNode.GetTree().CreateTimer(
                    context.Kind == NinjaSlayerDeathKind.EnemyKill
                        ? EnemyKillDurationSeconds
                        : OtherDeathDurationSeconds),
                SceneTreeTimer.SignalName.Timeout);
            return;
        }

        var state = DeathVisualState.Capture(anchor, body);
        VisualStates.Add(creature, state);

        try
        {
            if (context.Kind == NinjaSlayerDeathKind.EnemyKill)
            {
                if (context.Dealer is { } dealer && room.GetCreatureNode(dealer) is { } dealerNode)
                {
                    await PlayEnemyKillDeath(
                        creature,
                        creatureNode,
                        dealerNode,
                        anchor,
                        body,
                        state,
                        context.VfxBaselineChildIds);
                }
                else
                {
                    await PlayEnemyKillFallback(creature, creatureNode, anchor, body, state);
                }
            }
            else
            {
                creatureNode.SetAnimationTrigger("Dead");
                await PlayOtherDeathFall(creature, creatureNode, anchor, body, state);
            }
        }
        catch (OperationCanceledException) when (state.Cancellation.IsCancellationRequested
            || !GodotObject.IsInstanceValid(room))
        {
        }
    }

    private static async Task PlayEnemyKillFallback(
        Creature creature,
        NCreature creatureNode,
        Node2D anchor,
        Sprite2D body,
        DeathVisualState state)
    {
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerCombatAudioSet.For(creature).Death);
        NGame.Instance?.ScreenShake(ShakeStrength.TooMuch, ShakeDuration.Short);
        anchor.RotationDegrees = state.AnchorRotationDegrees + DoomHitRotationDegrees;
        await WaitForDuration(
            NCombatRoom.Instance!,
            new CinematicFrameClock(),
            EnemyFinisherImpactSeconds,
            state.Cancellation.Token);
        anchor.RotationDegrees = state.AnchorRotationDegrees;
        creatureNode.SetAnimationTrigger("Dead");
        await PlayEnemyKillFlight(creatureNode, anchor, body, state);
    }

    public static void RestoreVisual(Creature creature, bool markCurrentFatalDamageConsumed = true)
    {
        if (markCurrentFatalDamageConsumed)
        {
            DamageReceivedEntry? fatalEntry = CombatManager.Instance?.History.Entries
                .OfType<DamageReceivedEntry>()
                .LastOrDefault(entry => entry.Receiver == creature && entry.Result.WasTargetKilled);
            if (fatalEntry != null)
            {
                ConsumedFatalDamageEntries.GetOrCreateValue(creature).Entry = fatalEntry;
            }
        }

        if (!VisualStates.TryGetValue(creature, out DeathVisualState? state))
        {
            return;
        }

        VisualStates.Remove(creature);
        state.Restore(creature);
    }

    private static async Task PlayEnemyKillDeath(
        Creature creature,
        NCreature creatureNode,
        NCreature dealerNode,
        Node2D anchor,
        Sprite2D body,
        DeathVisualState state,
        IReadOnlySet<ulong> vfxBaselineChildIds)
    {
        NCombatRoom room = NCombatRoom.Instance!;
        CombatCinematicCameraLease? camera = null;
        FinisherImpactPresentation? presentation = null;
        ProcessModeSnapshot anchorMode = new(anchor, anchor.ProcessMode);
        Node2D dealerBody = dealerNode.Visuals.GetCurrentBody();
        ProcessModeSnapshot dealerMode = new(dealerBody, dealerBody.ProcessMode);
        List<ProcessModeSnapshot> frozenVfx = [];
        var clock = new CinematicFrameClock();

        try
        {
            CanvasItem focus = NinjaSlayerVisualRig.GetCinematicFocus(creatureNode.Visuals) is { } cinematicFocus
                ? cinematicFocus
                : creatureNode.Visuals.Bounds;
            if (CombatCinematicCameraLease.TryAcquire(room, "NinjaSlayer enemy finisher", out camera))
            {
                try
                {
                    presentation = FinisherImpactPresentation.Create(room, 1);
                }
                catch (Exception ex)
                {
                    Entry.Logger.Warn($"Could not create enemy finisher presentation; using impact-only fallback: {ex}");
                    camera?.Dispose();
                    camera = null;
                }
            }

            NinjaSlayerCombatAudioSet.Play(NinjaSlayerCombatAudioSet.For(creature).Death);
            if (camera != null)
            {
                camera.PlayScreenShake(
                    ShakeStrength.TooMuch,
                    ShakeDuration.Short,
                    rejectWeakerReplacement: true);
            }
            else
            {
                NGame.Instance?.ScreenShake(ShakeStrength.TooMuch, ShakeDuration.Short);
            }

            anchor.RotationDegrees = state.AnchorRotationDegrees + DoomHitRotationDegrees;
            anchor.Scale = state.AnchorScale;
            frozenVfx = FreezeImpactVfx(room, creatureNode, vfxBaselineChildIds);
            anchor.ProcessMode = Node.ProcessModeEnum.Disabled;
            dealerBody.ProcessMode = Node.ProcessModeEnum.Disabled;

            await PlayEnemyFinisherImpact(
                room,
                creatureNode,
                dealerNode,
                focus,
                camera,
                presentation,
                clock,
                state.Cancellation.Token);
        }
        catch (OperationCanceledException) when (state.Cancellation.IsCancellationRequested
            || !GodotObject.IsInstanceValid(room))
        {
            presentation?.Dispose();
            camera?.Dispose();
            return;
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"Enemy finisher impact failed; continuing with NinjaSlayer death flight: {ex}");
            presentation?.Dispose();
            camera?.Dispose();
            presentation = null;
            camera = null;
            await WaitForDuration(
                room,
                clock,
                Math.Max(0f, EnemyFinisherImpactSeconds - clock.Elapsed),
                state.Cancellation.Token);
        }
        finally
        {
            RestoreProcessMode(anchorMode);
            RestoreProcessMode(dealerMode);
            RestoreProcessModes(frozenVfx);
            if (GodotObject.IsInstanceValid(anchor))
            {
                anchor.RotationDegrees = state.AnchorRotationDegrees;
                anchor.Scale = state.AnchorScale;
            }
        }

        if (state.Cancellation.IsCancellationRequested)
        {
            presentation?.Dispose();
            camera?.Dispose();
            return;
        }
        creatureNode.SetAnimationTrigger("Dead");
        try
        {
            await PlayEnemyKillFlight(
                creatureNode,
                anchor,
                body,
                state,
                camera,
                presentation,
                room,
                clock);
        }
        finally
        {
            presentation?.Dispose();
            camera?.Dispose();
        }
    }

    private static async Task PlayEnemyFinisherImpact(
        NCombatRoom room,
        NCreature creatureNode,
        NCreature dealerNode,
        CanvasItem focus,
        CombatCinematicCameraLease? camera,
        FinisherImpactPresentation? presentation,
        CinematicFrameClock clock,
        CancellationToken cancellationToken)
    {
        Vector2 cameraStartPosition = camera?.CurrentPosition ?? Vector2.Zero;
        float cameraStartScale = camera?.CurrentScale ?? 1f;
        float recoveryScale = camera == null
            ? 1f
            : camera.BaselineScale.X * CameraZoomMultiplier;
        float punchScale = recoveryScale * CameraPunchMultiplier;
        FinisherCameraFrame frame = camera == null
            ? new FinisherCameraFrame([], false)
            : FinisherCameraFraming.SelectTargets(camera, focus, [dealerNode], punchScale);
        Vector2 punchPosition = camera == null
            ? Vector2.Zero
            : camera.GetCameraPosition(
                FinisherCameraFraming.ResolveCenter(camera, focus, frame, punchScale),
                punchScale,
                camera.ViewportSize * 0.5f);
        Vector2 recoveryPosition = camera == null
            ? Vector2.Zero
            : camera.GetCameraPosition(
                FinisherCameraFraming.ResolveCenter(camera, focus, frame, recoveryScale),
                recoveryScale,
                camera.ViewportSize * 0.5f);

        float elapsed = 0f;
        while (elapsed < EnemyFinisherImpactSeconds)
        {
            float delta = await NextFrame(room, clock, cancellationToken);
            elapsed += delta;
            float lead = CombatCinematicCameraLease.EaseOutCubic(elapsed / ImpactLeadSeconds);
            float recovery = elapsed <= EnemyFinisherImpactSeconds - ImpactRecoverySeconds
                ? 0f
                : CombatCinematicCameraLease.EaseOutCubic(
                    (elapsed - (EnemyFinisherImpactSeconds - ImpactRecoverySeconds)) / ImpactRecoverySeconds);
            float rayIntensity = recovery > 0f ? 1f - recovery : lead;
            float flash = elapsed < ImpactLeadSeconds
                ? Mathf.Sin(Mathf.Clamp(elapsed / ImpactLeadSeconds, 0f, 1f) * Mathf.Pi)
                : 0f;
            presentation?.SetBackdropIntensity(
                CombatCinematicCameraLease.EaseOutCubic(elapsed / BackdropFadeInSeconds));
            presentation?.SetImpactState([creatureNode], rayIntensity, flash);

            if (camera != null)
            {
                Vector2 impactPosition = cameraStartPosition.Lerp(punchPosition, lead);
                float impactScale = Mathf.Lerp(cameraStartScale, punchScale, lead);
                camera.SetTransform(
                    impactPosition.Lerp(recoveryPosition, recovery),
                    Mathf.Lerp(impactScale, recoveryScale, recovery));
                camera.Advance(delta);
            }
        }

        presentation?.SetBackdropIntensity(1f);
        presentation?.SetImpactState([], 0f, 0f);
        if (camera != null)
        {
            camera.SetTransform(recoveryPosition, recoveryScale);
        }
    }

    private static async Task ReturnEnemyFinisherCamera(
        NCombatRoom room,
        CombatCinematicCameraLease? camera,
        FinisherImpactPresentation? presentation,
        CinematicFrameClock clock,
        CancellationToken cancellationToken)
    {
        if (camera == null && presentation == null)
        {
            return;
        }

        Vector2 startPosition = camera?.CurrentPosition ?? Vector2.Zero;
        float startScale = camera?.CurrentScale ?? 1f;
        float elapsed = 0f;
        while (elapsed < CameraReturnSeconds)
        {
            float delta = await NextFrame(room, clock, cancellationToken);
            elapsed += delta;
            float progress = CombatCinematicCameraLease.EaseOutCubic(elapsed / CameraReturnSeconds);
            presentation?.SetBackdropIntensity(1f - progress);
            if (camera != null)
            {
                camera.SetTransform(
                    startPosition.Lerp(camera.BaselinePosition, progress),
                    Mathf.Lerp(startScale, camera.BaselineScale.X, progress));
                camera.Advance(delta);
            }
        }

        presentation?.SetBackdropIntensity(0f);
        camera?.ResetToBaseline();
    }

    private static async Task PlayEnemyKillFlight(
        Node creatureNode,
        Node2D anchor,
        Sprite2D body,
        DeathVisualState state,
        CombatCinematicCameraLease? camera = null,
        FinisherImpactPresentation? presentation = null,
        NCombatRoom? room = null,
        CinematicFrameClock? clock = null)
    {
        Node2D? focus = NinjaSlayerVisualRig.GetCinematicFocus((creatureNode as MegaCrit.Sts2.Core.Nodes.Combat.NCreature)?.Visuals);
        CanvasItem? anchorParent = anchor.GetParent() as CanvasItem;
        if (focus == null || anchorParent == null)
        {
            Task flightDelay = WaitForTimer(creatureNode, EnemyKillDurationSeconds);
            Task cameraReturn = room != null && clock != null
                ? ReturnEnemyFinisherCamera(
                    room,
                    camera,
                    presentation,
                    clock,
                    state.Cancellation.Token)
                : Task.CompletedTask;
            await Task.WhenAll(flightDelay, cameraReturn);
            return;
        }

        anchor.RotationDegrees = HitRotationDegrees;
        Vector2 start = focus.GetGlobalTransformWithCanvas().Origin;
        Vector2 viewportSize = creatureNode.GetViewport().GetVisibleRect().Size;
        float rightExtent = GetRightVisualExtent(anchor, start, body);
        Vector2 end = new(-rightExtent - FlightExitPadding, viewportSize.Y * FlightTargetHeightRatio);
        Vector2 control = (start + end) * 0.5f + Vector2.Up * viewportSize.Y * FlightArcHeightRatio;
        Vector2 cameraStartPosition = camera?.CurrentPosition ?? Vector2.Zero;
        float cameraStartScale = camera?.CurrentScale ?? 1f;
        float previousProgress = 0f;

        Tween tween = creatureNode.CreateTween();
        state.Tween = tween;
        tween.TweenMethod(
                Callable.From<float>(progress =>
                {
                    float elapsed = progress * EnemyKillDurationSeconds;
                    float cameraProgress = CombatCinematicCameraLease.EaseOutCubic(
                        elapsed / CameraReturnSeconds);
                    presentation?.SetBackdropIntensity(1f - cameraProgress);
                    if (camera != null)
                    {
                        camera.SetTransform(
                            cameraStartPosition.Lerp(camera.BaselinePosition, cameraProgress),
                            Mathf.Lerp(cameraStartScale, camera.BaselineScale.X, cameraProgress));
                        camera.Advance((progress - previousProgress) * EnemyKillDurationSeconds);
                    }

                    Vector2 desiredFocus = QuadraticBezier(start, control, end, progress);
                    SetFocusCanvasPosition(anchor, focus, anchorParent, desiredFocus);
                    previousProgress = progress;
                }),
                0f,
                1f,
                EnemyKillDurationSeconds)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Quad);

        await AwaitTween(tween, state.Cancellation.Token);
        presentation?.SetBackdropIntensity(0f);
        camera?.ResetToBaseline();
    }

    private static async Task PlayOtherDeathFall(
        Creature creature,
        Node creatureNode,
        Node2D anchor,
        Sprite2D body,
        DeathVisualState state)
    {
        Vector2 pivotPosition = body.Transform * DeathFallPivotTextureOffset;

        var pivot = new Node2D
        {
            Name = "NinjaSlayerDeathPivot",
            Position = pivotPosition
        };
        anchor.AddChild(pivot);

        var jitter = new NinjaSlayerDeathJitter
        {
            Name = "NinjaSlayerDeathJitter"
        };
        pivot.AddChild(jitter);
        body.Reparent(jitter, keepGlobalTransform: true);
        NarakuVisualOverlay.Sync(creature);

        state.Pivot = pivot;
        state.Jitter = jitter;
        float fallDirection = -Mathf.Sign(state.AnchorScale.X == 0f ? 1f : state.AnchorScale.X);
        state.SetDeathFallShadow(0f, fallDirection);

        Tween tween = creatureNode.CreateTween();
        state.Tween = tween;
        tween.TweenMethod(
                Callable.From<float>(progress =>
                {
                    pivot.RotationDegrees = Mathf.Lerp(0f, FallRotationDegrees, progress);
                    state.SetDeathFallShadow(progress, fallDirection);
                }),
                0f,
                1f,
                OtherDeathDurationSeconds)
            .SetEase(Tween.EaseType.In)
            .SetTrans(Tween.TransitionType.Quad);

        await AwaitTween(tween, state.Cancellation.Token);
    }

    private static float GetRightVisualExtent(Node2D anchor, Vector2 focusCanvasPosition, Sprite2D fallbackBody)
    {
        float rightExtent = 0f;
        IEnumerable<Sprite2D> sprites = anchor.FindChildren("*", nameof(Sprite2D), recursive: true, owned: false)
            .OfType<Sprite2D>()
            .Where(sprite => sprite.Visible && sprite.Texture != null);

        foreach (Sprite2D sprite in sprites.DefaultIfEmpty(fallbackBody))
        {
            Rect2 rect = sprite.GetRect();
            Transform2D transform = sprite.GetGlobalTransformWithCanvas();
            Vector2[] corners =
            [
                rect.Position,
                new Vector2(rect.End.X, rect.Position.Y),
                rect.End,
                new Vector2(rect.Position.X, rect.End.Y)
            ];
            rightExtent = Math.Max(
                rightExtent,
                corners.Max(corner => (transform * corner).X - focusCanvasPosition.X));
        }

        return rightExtent;
    }

    private static void SetFocusCanvasPosition(
        Node2D anchor,
        Node2D focus,
        CanvasItem anchorParent,
        Vector2 desiredCanvasPosition)
    {
        Vector2 desiredParentPosition = anchorParent.GetGlobalTransformWithCanvas().AffineInverse()
            * desiredCanvasPosition;
        Vector2 focusOffset = anchor.Transform.BasisXform(focus.Position);
        anchor.Position = desiredParentPosition - focusOffset;
    }

    private static Vector2 QuadraticBezier(Vector2 start, Vector2 control, Vector2 end, float progress)
    {
        float inverse = 1f - progress;
        return inverse * inverse * start
            + 2f * inverse * progress * control
            + progress * progress * end;
    }

    private static List<ProcessModeSnapshot> FreezeImpactVfx(
        NCombatRoom room,
        NCreature target,
        IReadOnlySet<ulong> baselineChildIds)
    {
        if (baselineChildIds.Count == 0 || !GodotObject.IsInstanceValid(room.CombatVfxContainer))
        {
            return [];
        }

        Rect2 targetRegion = target.Hitbox.GetGlobalRect().Grow(ImpactVfxTargetMargin);
        List<ProcessModeSnapshot> snapshots = [];
        foreach (Node vfxRoot in room.CombatVfxContainer.GetChildren())
        {
            if (baselineChildIds.Contains(vfxRoot.GetInstanceId())
                || !IsNodeActive(vfxRoot)
                || !IsVfxNearTarget(vfxRoot, targetRegion))
            {
                continue;
            }

            CaptureProcessModes(vfxRoot, snapshots);
        }

        foreach (ProcessModeSnapshot snapshot in snapshots)
        {
            if (IsNodeActive(snapshot.Node))
            {
                snapshot.Node.ProcessMode = Node.ProcessModeEnum.Disabled;
            }
        }

        return snapshots;
    }

    private static bool IsVfxNearTarget(Node vfxRoot, Rect2 targetRegion)
    {
        Vector2? position = vfxRoot switch
        {
            Control control => control.GetGlobalRect().GetCenter(),
            Node2D node => node.GlobalPosition,
            _ => null
        };
        return position.HasValue && targetRegion.HasPoint(position.Value);
    }

    private static void CaptureProcessModes(Node node, ICollection<ProcessModeSnapshot> snapshots)
    {
        if (!IsNodeActive(node))
        {
            return;
        }

        snapshots.Add(new ProcessModeSnapshot(node, node.ProcessMode));
        foreach (Node child in node.GetChildren())
        {
            CaptureProcessModes(child, snapshots);
        }
    }

    private static void RestoreProcessModes(IEnumerable<ProcessModeSnapshot> snapshots)
    {
        foreach (ProcessModeSnapshot snapshot in snapshots)
        {
            RestoreProcessMode(snapshot);
        }
    }

    private static void RestoreProcessMode(ProcessModeSnapshot snapshot)
    {
        if (IsNodeActive(snapshot.Node))
        {
            snapshot.Node.ProcessMode = snapshot.Mode;
        }
    }

    private static bool IsNodeActive(Node node) =>
        GodotObject.IsInstanceValid(node) && node.IsInsideTree() && !node.IsQueuedForDeletion();

    private static async Task<float> NextFrame(
        NCombatRoom room,
        CinematicFrameClock clock,
        CancellationToken cancellationToken)
    {
        if (!GodotObject.IsInstanceValid(room) || !room.IsInsideTree())
        {
            throw new OperationCanceledException("Combat room was unloaded during NinjaSlayer death.");
        }

        await room.ToSignal(room.GetTree(), SceneTree.SignalName.ProcessFrame);
        cancellationToken.ThrowIfCancellationRequested();
        ulong processFrame = Engine.GetProcessFrames();
        if (processFrame != clock.LastDeltaFrame)
        {
            ulong now = Time.GetTicksMsec();
            clock.CachedDelta = room.ProcessMode == Node.ProcessModeEnum.Disabled
                ? 0f
                : Math.Min((now - clock.LastFrameMsec) / 1000f, 0.05f);
            clock.LastFrameMsec = now;
            clock.LastDeltaFrame = processFrame;
            clock.Elapsed += clock.CachedDelta;
        }

        return clock.CachedDelta;
    }

    private static async Task WaitForDuration(
        NCombatRoom room,
        CinematicFrameClock clock,
        float duration,
        CancellationToken cancellationToken)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += await NextFrame(room, clock, cancellationToken);
        }
    }

    private static async Task AwaitTween(Tween tween, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFinished() => completion.TrySetResult();
        tween.Finished += OnFinished;
        using CancellationTokenRegistration registration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));
        try
        {
            await completion.Task;
        }
        finally
        {
            tween.Finished -= OnFinished;
        }
    }

    private static async Task WaitForTimer(Node owner, float duration)
    {
        await owner.ToSignal(
            owner.GetTree().CreateTimer(duration),
            SceneTreeTimer.SignalName.Timeout);
    }

    private sealed record IncomingDamageCapture(
        Creature Dealer,
        IReadOnlySet<ulong> VfxBaselineChildIds,
        IReadOnlyList<Creature> Targets);

    private sealed class CinematicFrameClock
    {
        public ulong LastFrameMsec { get; set; } = Time.GetTicksMsec();
        public ulong LastDeltaFrame { get; set; } = ulong.MaxValue;
        public float CachedDelta { get; set; }
        public float Elapsed { get; set; }
    }

    private readonly record struct ProcessModeSnapshot(Node Node, Node.ProcessModeEnum Mode);

    private sealed class ConsumedFatalDamage
    {
        public DamageReceivedEntry? Entry { get; set; }
    }

    private sealed class DeathVisualState
    {
        private readonly Node _bodyParent;
        private readonly int _bodyIndex;
        private readonly Vector2 _bodyPosition;
        private readonly float _bodyRotationDegrees;
        private readonly Vector2 _bodyScale;
        private readonly Vector2 _bodyOffset;
        private readonly Vector2 _anchorPosition;
        private readonly float _anchorRotationDegrees;
        private readonly Vector2 _anchorScale;
        private readonly NinjaSlayerShadowController? _shadowController;

        private DeathVisualState(Node2D anchor, Sprite2D body)
        {
            Anchor = anchor;
            Body = body;
            _bodyParent = body.GetParent();
            _bodyIndex = body.GetIndex();
            _bodyPosition = body.Position;
            _bodyRotationDegrees = body.RotationDegrees;
            _bodyScale = body.Scale;
            _bodyOffset = body.Offset;
            _anchorPosition = anchor.Position;
            _anchorRotationDegrees = anchor.RotationDegrees;
            _anchorScale = anchor.Scale;
            _shadowController = anchor.GetParent()
                ?.GetNodeOrNull<NinjaSlayerShadowController>(NinjaSlayerVisualRig.ShadowControllerNodeName);
        }

        public Node2D Anchor { get; }
        public Sprite2D Body { get; }
        public float AnchorRotationDegrees => _anchorRotationDegrees;
        public Vector2 AnchorScale => _anchorScale;
        public CancellationTokenSource Cancellation { get; } = new();
        public Node2D? Pivot { get; set; }
        public NinjaSlayerDeathJitter? Jitter { get; set; }
        public Tween? Tween { get; set; }

        public static DeathVisualState Capture(Node2D anchor, Sprite2D body) => new(anchor, body);

        public void SetDeathFallShadow(float progress, float direction)
        {
            if (_shadowController != null && GodotObject.IsInstanceValid(_shadowController))
            {
                _shadowController.SetDeathFall(progress, direction);
            }
        }

        public void Restore(Creature creature)
        {
            Cancellation.Cancel();
            if (Tween?.IsValid() == true)
            {
                Tween.Kill();
            }

            Jitter?.StopAndReset();
            if (GodotObject.IsInstanceValid(Body) && GodotObject.IsInstanceValid(_bodyParent))
            {
                if (!ReferenceEquals(Body.GetParent(), _bodyParent))
                {
                    Body.Reparent(_bodyParent, keepGlobalTransform: false);
                }

                _bodyParent.MoveChild(Body, Math.Min(_bodyIndex, _bodyParent.GetChildCount() - 1));
                Body.Position = _bodyPosition;
                Body.RotationDegrees = _bodyRotationDegrees;
                Body.Scale = _bodyScale;
                Body.Offset = _bodyOffset;
            }

            if (GodotObject.IsInstanceValid(Anchor))
            {
                Anchor.Position = _anchorPosition;
                Anchor.RotationDegrees = _anchorRotationDegrees;
                Anchor.Scale = _anchorScale;
            }

            if (_shadowController != null && GodotObject.IsInstanceValid(_shadowController))
            {
                _shadowController.ClearDeathFall();
            }

            NarakuVisualOverlay.Sync(creature);
            if (Pivot != null && GodotObject.IsInstanceValid(Pivot))
            {
                Pivot.QueueFree();
            }
        }
    }
}
