using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;
using static NinjaSlayer.Code.ExternalAnimations.FinisherTimeline;

namespace NinjaSlayer.Code.ExternalAnimations;
internal sealed partial class FinisherSession : IAsyncDisposable
{
    private bool IsAlabamaDrop => CardPlay?.Card is NinjaSlayer.Cards.RedesignV1.AlabamaDropRedesignV1;
    private bool AlabamaOwnsRecovery => IsAlabamaDrop && !_continuousPlayerApproach;
    private Node2D? AlabamaContact => IsAlabamaDrop
        ? _focusNode.Body.GetNodeOrNull<Marker2D>(AlabamaDropAnimation.GroundContactName) : null;

    private async Task WaitEnhancedSeconds(float seconds, CancellationToken cancellationToken)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += await NextEnhancedFrame(cancellationToken);
        }
    }

    private void StartFinalZoom()
    {
        if (_finalZoomStarted)
        {
            return;
        }

        _finalZoomStarted = true;
        if (!_impactCamera) StartCameraTransition(FinalHitZoomMultiplier, FinalHitZoomSeconds);
        StartBackdropDarkening();
    }

    private void StartBackdropDarkening()
    {
        if (_enhancedImpactFailed
            || _presentation == null
            || _backdropDarkeningStarted)
        {
            return;
        }

        _backdropDarkeningStarted = true;
        int generation = ++_backdropTransitionGeneration;
        _backdropTransitionTask = RunBackdropTransition(generation, 1f, FinalHitZoomSeconds);
    }

    private async Task RunBackdropTransition(int generation, float targetIntensity, float duration)
    {
        try
        {
            float startIntensity = _backdropIntensity;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += await NextFrame();
                if (_disposed
                    || generation != _backdropTransitionGeneration
                    || _presentation == null)
                {
                    return;
                }

                float progress = CombatCinematicCameraLease.EaseOutCubic(elapsed / duration);
                SetBackdropIntensity(Mathf.Lerp(startIntensity, targetIntensity, progress));
            }

            if (!_disposed
                && generation == _backdropTransitionGeneration
                && _presentation != null)
            {
                SetBackdropIntensity(targetIntensity);
            }
        }
        catch (OperationCanceledException) when (_disposed || !GodotObject.IsInstanceValid(_room))
        {
        }
        catch (Exception ex)
        {
            _enhancedImpactFailed = true;
            DisposeEnhancedPresentation();
            Entry.Logger.Warn($"Finisher backdrop transition failed; fallback presentation will be used: {ex}");
        }
    }

    private void StartCameraTransition(float scaleMultiplier, float duration)
    {
        int generation = ++_cameraTransitionGeneration;
        _cameraTransitionTask = RunCameraTransition(generation, scaleMultiplier, duration);
    }

    private async Task RunCameraTransition(int generation, float scaleMultiplier, float duration)
    {
        try
        {
            Vector2 startPosition = _camera.CurrentPosition;
            float startScale = _camera.CurrentScale;
            float targetScale = _camera.BaselineScale.X * scaleMultiplier;
            Vector2 targetPosition = GetFramedCameraPosition(targetScale);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += await NextFrame();
                if (_disposed || generation != _cameraTransitionGeneration)
                {
                    return;
                }

                float progress = CombatCinematicCameraLease.EaseOutCubic(elapsed / duration);
                if (_approach != null)
                    targetPosition = GetFramedCameraPosition(targetScale);
                _camera.SetTransform(
                    startPosition.Lerp(targetPosition, progress),
                    Mathf.Lerp(startScale, targetScale, progress));
            }

            if (!_disposed && generation == _cameraTransitionGeneration)
            {
                _camera.SetTransform(targetPosition, targetScale);
            }
        }
        catch (OperationCanceledException) when (_disposed || !GodotObject.IsInstanceValid(_room))
        {
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"Finisher camera transition failed: {ex}");
        }
    }

    private void CaptureImpactVisuals(
        IEnumerable<NCreature> targetNodes,
        Dictionary<Node2D, ImpactVisualSnapshot> snapshots)
    {
        foreach (NCreature creatureNode in targetNodes.Where(GodotObject.IsInstanceValid))
        {
            Node2D body = NinjaSlayerVisualRig.GetAirborneAnchor(creatureNode.Visuals)
                ?? creatureNode.Visuals.GetCurrentBody();
            if (!snapshots.ContainsKey(body))
            {
                DeathSquashVisualState? squashState = _deathSquashStates.GetValueOrDefault(body);
                snapshots.Add(body, new ImpactVisualSnapshot(
                    body,
                    squashState?.OriginalPosition ?? body.Position,
                    squashState?.OriginalScale ?? body.Scale,
                    body.Rotation,
                    body.Modulate,
                    creatureNode.Visuals.Bounds,
                    ResolveImpactDirection(_actorNode, creatureNode),
                    IsAlabamaDrop ? Vector2.Down : _impactAxes.GetValueOrDefault(creatureNode.Entity,
                        (creatureNode.VfxSpawnPosition - _actorNode.VfxSpawnPosition).Normalized()),
                    GetDeathSquashMultiplier(creatureNode.Entity),
                    IsAlabamaDrop ? creatureNode.Body.GetNodeOrNull<Marker2D>(AlabamaDropAnimation.GroundContactName)?.GlobalPosition : null));
            }
        }
    }

    private CanvasItem GetCameraFocus() => IsRanged ? _focusNode.Visuals.Bounds :
        NinjaSlayerVisualRig.GetCinematicFocus(_actorNode.Visuals) is { } cinematicFocus
            ? cinematicFocus
            : _actorNode.Visuals.Bounds;

    private Vector2 GetFramedCameraPosition(float scale, float horizontalScreenOffset = 0f)
    {
        if (AlabamaContact is { } contact)
        {
            Vector2 point = _camera.GetLocalCenter(contact);
            Vector2 actor = _camera.GetLocalCenter(_actorNode.Visuals.VfxSpawnPosition);
            // Keep the actual crown/floor contact above the hand, with room for
            // both inverted bodies. Standing bounds no longer describe this pose.
            Vector2 impactCenter = new((point.X + actor.X) * .5f,
                point.Y - _camera.ViewportSize.Y * .16f / scale);
            impactCenter.X += horizontalScreenOffset / scale;
            return _camera.GetCameraPosition(_camera.ClampTarget(impactCenter, scale),
                scale, _camera.ViewportSize * .5f);
        }
        Vector2 center = FinisherCameraFraming.ResolveCenter(
            _camera,
            GetCameraFocusPoint(),
            _cameraFrame,
            scale,
            horizontalScreenOffset);
        return _camera.GetCameraPosition(center, scale, _camera.ViewportSize * 0.5f);
    }

    private Vector2 GetCameraFocusPoint()
    {
        Vector2 focusPoint = _camera.GetLocalCenter(GetCameraFocus());
        return focusPoint;
    }

    private void ApplyDeathSquashes(IEnumerable<ImpactVisualSnapshot> snapshots)
    {
        if (IsRanged) return;
        foreach (ImpactVisualSnapshot snapshot in snapshots.Where(snapshot => GodotObject.IsInstanceValid(snapshot.Body)))
        {
            Vector2 multiplier = snapshot.SquashMultiplier;
            if (multiplier == Vector2.One) continue;
            if (!_deathSquashStates.TryGetValue(snapshot.Body, out DeathSquashVisualState? state))
            {
                state = CaptureDeathSquashState(snapshot, multiplier);
                _deathSquashStates.Add(snapshot.Body, state);
            }

            ApplyDeathSquashTransform(state, multiplier, snapshot.Rotation);
        }
    }

    private void RestoreDeathSquashes()
    {
        foreach ((Node2D body, DeathSquashVisualState state) in _deathSquashStates)
        {
            if (GodotObject.IsInstanceValid(body))
            {
                body.Transform = state.OriginalTransform;
            }
        }

        _deathSquashStates.Clear();
    }

    private static DeathSquashVisualState CaptureDeathSquashState(
        ImpactVisualSnapshot snapshot,
        Vector2 multiplier)
    {
        Node2D body = snapshot.Body;
        Rect2 bounds = snapshot.Bounds.GetGlobalRect();
        Vector2 axis = snapshot.Axis.LengthSquared() > 0.0001f
            ? snapshot.Axis.Normalized() : Vector2.Right * snapshot.Direction;
        Vector2 half = bounds.Size * 0.5f;
        float distance = Math.Min(Math.Abs(axis.X) > 0.0001f ? half.X / Math.Abs(axis.X) : float.PositiveInfinity,
            Math.Abs(axis.Y) > 0.0001f ? half.Y / Math.Abs(axis.Y) : float.PositiveInfinity);
        Vector2 anchor = snapshot.GroundContact ?? (bounds.GetCenter() + axis * distance);
        Transform2D world = body.GlobalTransform;
        Transform2D inverse = world.AffineInverse();
        Vector2[] corners = [bounds.Position, new(bounds.End.X, bounds.Position.Y), bounds.End,
            new(bounds.Position.X, bounds.End.Y)];
        return new DeathSquashVisualState(body, body.Transform,
            body.TopLevel ? Transform2D.Identity : (body.GetParent() as CanvasItem)!.GetGlobalTransform(),
            inverse * anchor, anchor, axis, corners.Select(point => inverse * point).ToArray(),
            snapshot.GroundContact.HasValue ? float.PositiveInfinity : bounds.End.Y);
    }

    private static void ApplyDeathSquashTransform(
        DeathSquashVisualState state,
        Vector2 multiplier,
        float rotation)
    {
        Node2D body = state.Body;
        if (!GodotObject.IsInstanceValid(body))
        {
            return;
        }

        Transform2D basis = new(state.Axis.Angle(), Vector2.Zero);
        Transform2D compression = basis * new Transform2D(0f, multiplier, 0f, Vector2.Zero) * basis.AffineInverse();
        Transform2D world = compression
            * new Transform2D(rotation - state.OriginalTransform.Rotation, Vector2.Zero)
            * state.ParentToWorld * state.OriginalTransform;
        world.Origin = state.AnchorWorld - world.BasisXform(state.AnchorInBody);
        float bottom = state.Corners.Max(point => (world * point).Y);
        world.Origin -= Vector2.Down * Math.Max(0f, bottom - state.SupportY);
        body.Transform = state.ParentToWorld.AffineInverse() * world;
    }

    private void ArmDeathKicks(IEnumerable<Creature> targets)
    {
        _deathKickVisuals.Clear();
        foreach (Creature target in targets)
        {
            NCreature? creatureNode = _room.GetCreatureNode(target);
            if (creatureNode == null || !GodotObject.IsInstanceValid(creatureNode))
            {
                continue;
            }

            Node2D body = creatureNode.Visuals.GetCurrentBody();
            if (!GodotObject.IsInstanceValid(body))
            {
                continue;
            }

            _deathKickVisuals[creatureNode] = new DeathKickVisual(
                body,
                body.Position,
                ResolveImpactDirection(_actorNode, creatureNode));
        }
    }

    private void StartReturnTimeline(bool includeSettle)
    {
        if (_returnTimelineStarted)
        {
            return;
        }

        _returnTimelineStarted = true;
        _returnToBaselineTask = RunReturnTimeline(includeSettle);
    }

    private async Task RunReturnTimeline(bool includeSettle)
    {
        if (includeSettle && _measuredCameraTask == null)
        {
            await WaitSeconds(DeathKickSettleSeconds);
        }

        await ReturnToBaseline();
    }

    private async Task EnsureReturnToBaseline()
    {
        StartReturnTimeline(includeSettle: false);
        await _returnToBaselineTask;
    }

    private void ApplyDeathKickRecovery(float sharedProgress)
    {
        _returnTimelineProgress = Mathf.Clamp(sharedProgress, 0f, 1f);
        foreach (DeathKickVisual visual in _deathKickVisuals.Values.Where(visual => visual.Triggered))
        {
            if (!GodotObject.IsInstanceValid(visual.Body))
            {
                continue;
            }

            float recovery = FinisherDeathKickTimeline.GetRecoveryProgress(
                _returnTimelineProgress,
                visual.JoinedAtReturnProgress);
            visual.Body.Position = visual.Position
                + Vector2.Right * visual.Direction * EnemyKnockbackPixels * (1f - recovery);
        }
    }

    private void RestoreDeathKicks()
    {
        foreach (DeathKickVisual visual in _deathKickVisuals.Values)
        {
            RestoreDeathKick(visual);
        }

        _deathKickVisuals.Clear();
    }

    private static void RestoreDeathKick(DeathKickVisual visual)
    {
        if (GodotObject.IsInstanceValid(visual.Body))
        {
            visual.Body.Position = visual.Position;
        }
    }

    private Vector2 GetDeathSquashMultiplier(Creature victim) =>
        IsRanged || !AllowsDeathSquash(victim) ? Vector2.One : MeleeSquash(_previewProfile);

    private static void ApplyEnemyFlash(
        IEnumerable<ImpactVisualSnapshot> snapshots,
        float amount)
    {
        foreach (ImpactVisualSnapshot snapshot in snapshots.Where(snapshot => GodotObject.IsInstanceValid(snapshot.Body)))
        {
            snapshot.Body.Modulate = snapshot.Modulate.Lerp(
                new Color(1.8f, 1.8f, 1.8f, snapshot.Modulate.A),
                amount);
        }
    }

    private void ApplyEnhancedVictimFeedback(
        IEnumerable<ImpactVisualSnapshot> snapshots,
        float amount,
        bool flash)
    {
        foreach (ImpactVisualSnapshot snapshot in snapshots.Where(snapshot => GodotObject.IsInstanceValid(snapshot.Body)))
        {
            Vector2 squashMultiplier = snapshot.SquashMultiplier;
            float rotation = IsRanged || IsAlabamaDrop
                ? snapshot.Rotation
                : snapshot.Rotation + Mathf.DegToRad(EnhancedEnemyTiltDegrees * snapshot.Direction * amount);
            if (_deathSquashStates.TryGetValue(snapshot.Body, out DeathSquashVisualState? state))
            {
                ApplyDeathSquashTransform(state, squashMultiplier, rotation);
            }
            else
            {
                if (squashMultiplier != Vector2.One)
                    snapshot.Body.Scale = snapshot.Scale * squashMultiplier;
                snapshot.Body.Rotation = rotation;
            }
            snapshot.Body.Modulate = flash
                ? snapshot.Modulate.Lerp(
                    new Color(1.8f, 1.8f, 1.8f, snapshot.Modulate.A),
                    amount)
                : snapshot.Modulate;
        }
    }

    private List<ProcessModeSnapshot> CaptureImpactProcesses(IEnumerable<NCreature> targetNodes) =>
        targetNodes.Where(node => !node.SpineAnimation.IsValid).Append(_actorNode)
            .Where(GodotObject.IsInstanceValid)
            .Distinct()
            .Select(node => new ProcessModeSnapshot(node, node.ProcessMode))
            .ToList();

    private void FreezeImpactProcesses(IEnumerable<ProcessModeSnapshot> snapshots)
    {
        foreach (ProcessModeSnapshot snapshot in snapshots.Where(snapshot => GodotObject.IsInstanceValid(snapshot.Node)))
            snapshot.Node.ProcessMode = Node.ProcessModeEnum.Disabled;
        if (IsAlabamaDrop)
        {
            NinjaSlayerSpinMotionBlur.Get(Actor)?.Reset();
            NinjaSlayerHellTornadoVisual.Get(Actor)?.ClearExposure();
        }
    }

    private static void RestoreImpactProcesses(IEnumerable<ProcessModeSnapshot> snapshots)
    {
        foreach (ProcessModeSnapshot snapshot in snapshots.Where(snapshot => GodotObject.IsInstanceValid(snapshot.Node)))
            snapshot.Node.ProcessMode = snapshot.Mode;
    }

    private void PlayReverseImpactAudio()
    {
        if (_impactAudioPlayed || Scenario != FinisherScenarioKind.EnemyExecutesNinjaSlayer)
        {
            return;
        }

        _impactAudioPlayed = true;
        foreach (Creature victim in _ledger.DeferredDeaths)
        {
            NinjaSlayerCombatAudioSet.Play(NinjaSlayerCombatAudioSet.For(victim).Death);
            break;
        }
    }

    private static void RestoreEnemyFlash(IEnumerable<ImpactVisualSnapshot> snapshots)
    {
        foreach (ImpactVisualSnapshot snapshot in snapshots.Where(snapshot => GodotObject.IsInstanceValid(snapshot.Body)))
        {
            snapshot.Body.Modulate = snapshot.Modulate;
        }
    }

    private void RestoreImpactVisuals(IEnumerable<ImpactVisualSnapshot> snapshots)
    {
        foreach (ImpactVisualSnapshot snapshot in snapshots.Where(snapshot => GodotObject.IsInstanceValid(snapshot.Body)))
        {
            if (_deathSquashStates.TryGetValue(snapshot.Body, out DeathSquashVisualState? state))
            {
                ApplyDeathSquashTransform(state, snapshot.SquashMultiplier, snapshot.Rotation);
            }
            else
            {
                snapshot.Body.Position = snapshot.Position;
                snapshot.Body.Scale = snapshot.Scale;
                snapshot.Body.Rotation = snapshot.Rotation;
            }
            snapshot.Body.Modulate = snapshot.Modulate;
        }
    }

    private async Task ReturnToBaseline()
    {
        StopComboTravel();
        if (!GodotObject.IsInstanceValid(_actorNode))
        {
            RestoreActorLeapPose();
            ApplyDeathKickRecovery(1f);
            _returnTimelineCompleted = true;
            SetBackdropIntensity(0f);
            _camera.ResetToBaseline();
            return;
        }

        Vector2 ownerFrom = _actorNode.Position;
        if (!AlabamaOwnsRecovery) _actorAimPose?.BeginReturn();
        _approach?.BeginReturn();
        Vector2 cameraFrom = _camera.CurrentPosition;
        float scaleFrom = _camera.CurrentScale;
        float backdropFrom = _backdropIntensity;
        float actorReturnSeconds = AlabamaOwnsRecovery ? 0f : _continuousPlayerApproach
            ? IsAlabamaDrop ? .2f : NinjaSlayerAimPose.IsSomersaultHeavy(CardPlay?.Card)
                ? SlowAttackAnimation.SomersaultHalfSeconds : CombatActionTimingRuntime.ReturnSeconds
            : ReturnSeconds;
        float cameraReturnSeconds = ReturnSeconds;
        bool measuredCamera = _measuredCameraTask != null;
        float totalReturnSeconds = Math.Max(actorReturnSeconds, measuredCamera ? 0f : cameraReturnSeconds);
        float elapsed = 0f;
        while (elapsed < totalReturnSeconds)
        {
            elapsed += await NextFrame();
            float cameraLinearProgress = Mathf.Clamp(elapsed / cameraReturnSeconds, 0f, 1f);
            float cameraProgress = CombatCinematicCameraLease.EaseOutCubic(cameraLinearProgress);
            float actorProgress = Mathf.IsZeroApprox(actorReturnSeconds)
                ? 1f
                : Mathf.SmoothStep(0f, 1f, Mathf.Clamp(elapsed / actorReturnSeconds, 0f, 1f));
            ApplyDeathKickRecovery(measuredCamera
                ? (Mathf.IsZeroApprox(actorReturnSeconds) ? 1f : Mathf.Clamp(elapsed / actorReturnSeconds, 0f, 1f))
                : cameraLinearProgress);
            if (!IsRanged && !AlabamaOwnsRecovery) _actorNode.Position = ownerFrom.Lerp(_actorReturnPosition, actorProgress);
            if (_continuousPlayerApproach && IsAlabamaDrop)
                _actorNode.Position -= Vector2.Down * (70f * Mathf.Sin(actorProgress * Mathf.Pi));
            if (!AlabamaOwnsRecovery) _actorAimPose?.ApplyReturn(actorProgress);
            _approach?.ApplyReturn(actorProgress);
            if (!measuredCamera)
            {
                _camera.SetTransform(
                    cameraFrom.Lerp(_camera.BaselinePosition, cameraProgress),
                    Mathf.Lerp(scaleFrom, _camera.BaselineScale.X, cameraProgress));
                SetBackdropIntensity(Mathf.Lerp(backdropFrom, 0f, cameraProgress));
            }
        }

        if (_measuredCameraTask != null) await _measuredCameraTask;
        ApplyDeathKickRecovery(1f);
        if (!IsRanged && !AlabamaOwnsRecovery) _actorNode.Position = _actorReturnPosition;
        _approach?.ApplyReturn(1f);
        RestoreActorLeapPose();
        _returnTimelineCompleted = true;
        SetBackdropIntensity(0f);
    }

    private void SetBackdropIntensity(float intensity)
    {
        _backdropIntensity = Mathf.Clamp(intensity, 0f, 1f);
        _presentation?.SetBackdropIntensity(_backdropIntensity);
    }

    private void DisposeEnhancedPresentation()
    {
        _backdropTransitionGeneration++;
        _presentation?.Dispose();
        _presentation = null;
        _backdropIntensity = 0f;
    }

    private async Task<float> NextFrame()
    {
        if (!GodotObject.IsInstanceValid(_room) || !_room.IsInsideTree())
        {
            throw new OperationCanceledException("Combat room was unloaded during the finisher.");
        }

        await _room.ToSignal(_room.GetTree(), SceneTree.SignalName.ProcessFrame);
        AdvanceClock();
        return _cachedFrameDelta;
    }

    private void AdvanceClock()
    {
        ulong processFrame = Engine.GetProcessFrames();
        if (processFrame != _lastDeltaFrame)
        {
            ulong now = Time.GetTicksMsec();
            _cachedFrameDelta = !_room.CanProcess()
                ? 0f
                : Math.Min((now - _lastFrameMsec) / 1000f, 0.05f);
            _lastFrameMsec = now;
            _lastDeltaFrame = processFrame;
            _activeSeconds += _cachedFrameDelta;
        }

    }

    private async Task RunCameraShakePump()
    {
        try
        {
            while (!_disposed && GodotObject.IsInstanceValid(_room) && _room.IsInsideTree())
            {
                await _room.ToSignal(_room.GetTree(), SceneTree.SignalName.ProcessFrame);
                AdvanceClock();
                _camera.Advance(_cachedFrameDelta);
            }
        }
        catch (OperationCanceledException) when (_disposed || !GodotObject.IsInstanceValid(_room))
        {
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"Finisher camera shake pump stopped unexpectedly: {ex}");
        }
    }

    private async Task WaitSeconds(float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += await NextFrame();
        }
    }

    private static float ResolveImpactDirection(NCreature owner, NCreature target)
    {
        float direction = Mathf.Sign(target.GlobalPosition.X - owner.GlobalPosition.X);
        return Mathf.IsZeroApprox(direction) ? 1f : direction;
    }

    private static bool IsPrimaryAttackTrigger(string triggerName) =>
        triggerName is "Attack" or "SlowAttack" or "XAttack"
        || triggerName == TornadoFistSpinAnimation.TriggerName;

    private readonly record struct ProcessModeSnapshot(Node Node, Node.ProcessModeEnum Mode);
    private readonly record struct ImpactVisualSnapshot(
        Node2D Body,
        Vector2 Position,
        Vector2 Scale,
        float Rotation,
        Color Modulate,
        Control Bounds,
        float Direction,
        Vector2 Axis,
        Vector2 SquashMultiplier,
        Vector2? GroundContact);

    private sealed class DeathKickVisual(Node2D body, Vector2 position, float direction)
    {
        public Node2D Body { get; } = body;
        public Vector2 Position { get; } = position;
        public float Direction { get; } = direction;
        public bool Triggered { get; set; }
        public float JoinedAtReturnProgress { get; set; }
    }

    private sealed record DeathSquashVisualState(
        Node2D Body,
        Transform2D OriginalTransform,
        Transform2D ParentToWorld,
        Vector2 AnchorInBody,
        Vector2 AnchorWorld,
        Vector2 Axis,
        Vector2[] Corners,
        float SupportY)
    {
        internal Vector2 OriginalPosition => OriginalTransform.Origin;
        internal Vector2 OriginalScale => OriginalTransform.Scale;
    }

    private static float EaseOut(float value) => 1f - (1f - value) * (1f - value);
}
