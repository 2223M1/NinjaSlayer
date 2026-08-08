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
    private void SetSignatureImpactState(
        FinisherImpactPresentation presentation,
        IReadOnlyList<NCreature> targets,
        float intensity,
        float flash)
    {
        if (_usesNinjaSlayerSignatureImpact)
        {
            presentation.SetImpactState(targets, intensity, flash);
        }
    }

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
        StartCameraTransition(FinalHitZoomMultiplier, FinalHitZoomSeconds);
        StartBackdropDarkening();
    }

    private async Task PrepareReverseImpactLead()
    {
        if (Scenario != FinisherScenarioKind.EnemyExecutesNinjaSlayer)
        {
            return;
        }

        StartFinalZoom();
        await Task.WhenAll(_cameraTransitionTask, _backdropTransitionTask);
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
            Node2D body = creatureNode.Visuals.GetCurrentBody();
            if (!snapshots.ContainsKey(body))
            {
                DeathSquashVisualState? squashState = _deathSquashStates.GetValueOrDefault(body);
                snapshots.Add(body, new ImpactVisualSnapshot(
                    body,
                    squashState?.OriginalPosition ?? body.Position,
                    squashState?.OriginalScale ?? body.Scale,
                    body.Rotation,
                    body.SelfModulate,
                    creatureNode.Visuals.Bounds,
                    ResolveImpactDirection(_actorNode, creatureNode)));
            }
        }
    }

    private CanvasItem GetCameraFocus() =>
        Scenario == FinisherScenarioKind.NinjaSlayerAttack
        && NinjaSlayerVisualRig.GetCinematicFocus(_actorNode.Visuals) is { } cinematicFocus
            ? cinematicFocus
            : _actorNode.Visuals.Bounds;

    private Vector2 GetFramedCameraPosition(float scale, float horizontalScreenOffset = 0f)
    {
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
        if (Scenario == FinisherScenarioKind.YamotoKokiIaiSlash
            && GodotObject.IsInstanceValid(_actorNode))
        {
            focusPoint += _impactPosition - _actorNode.Position;
        }

        return focusPoint;
    }

    private void ApplyDeathSquashes(IEnumerable<ImpactVisualSnapshot> snapshots)
    {
        Vector2 multiplier = GetDeathSquashMultiplier();
        foreach (ImpactVisualSnapshot snapshot in snapshots.Where(snapshot => GodotObject.IsInstanceValid(snapshot.Body)))
        {
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
                body.Scale = state.OriginalScale;
                body.Position = state.OriginalPosition;
            }
        }

        _deathSquashStates.Clear();
    }

    private static DeathSquashVisualState CaptureDeathSquashState(
        ImpactVisualSnapshot snapshot,
        Vector2 multiplier)
    {
        Node2D body = snapshot.Body;
        var fallback = new DeathSquashVisualState(
            body,
            snapshot.Position,
            snapshot.Scale,
            Parent: null,
            AnchorInBody: default,
            AnchorInParent: default,
            WasTopLevel: body.TopLevel,
            HasAnchorCompensation: false);
        FinisherSquashAnchorKind anchorKind = FinisherSquashAnchorPolicy.Resolve(
            multiplier.X,
            multiplier.Y);
        if (anchorKind == FinisherSquashAnchorKind.Center
            || !GodotObject.IsInstanceValid(snapshot.Bounds))
        {
            return fallback;
        }

        try
        {
            Rect2 bounds = snapshot.Bounds.GetGlobalRect();
            if (!bounds.Position.IsFinite()
                || !bounds.Size.IsFinite()
                || bounds.Size.X <= 0f
                || bounds.Size.Y <= 0f)
            {
                return fallback;
            }

            Vector2 center = bounds.GetCenter();
            Vector2 anchorGlobal = anchorKind switch
            {
                FinisherSquashAnchorKind.BottomCenter => new Vector2(center.X, bounds.Position.Y + bounds.Size.Y),
                _ => center
            };
            bool wasTopLevel = body.TopLevel;
            CanvasItem? parent = wasTopLevel ? null : body.GetParent() as CanvasItem;
            if (!wasTopLevel && (parent == null || !GodotObject.IsInstanceValid(parent)))
            {
                return fallback;
            }

            Vector2 anchorInBody = body.GetGlobalTransform().AffineInverse() * anchorGlobal;
            Vector2 anchorInParent = wasTopLevel
                ? anchorGlobal
                : parent!.GetGlobalTransform().AffineInverse() * anchorGlobal;
            if (!anchorInBody.IsFinite() || !anchorInParent.IsFinite())
            {
                return fallback;
            }

            return new DeathSquashVisualState(
                body,
                snapshot.Position,
                snapshot.Scale,
                parent,
                anchorInBody,
                anchorInParent,
                wasTopLevel,
                HasAnchorCompensation: true);
        }
        catch
        {
            return fallback;
        }
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

        body.Position = state.OriginalPosition;
        body.Scale = state.OriginalScale * multiplier;
        body.Rotation = rotation;
        if (!state.HasAnchorCompensation
            || body.TopLevel != state.WasTopLevel
            || (!state.WasTopLevel
                && (state.Parent == null
                    || !GodotObject.IsInstanceValid(state.Parent)
                    || !ReferenceEquals(body.GetParent(), state.Parent))))
        {
            return;
        }

        Transform2D transform = body.Transform;
        FinisherAnchorPoint position = FinisherSquashAnchorPolicy.ResolveCompensatedPosition(
            new FinisherAnchorPoint(state.AnchorInParent.X, state.AnchorInParent.Y),
            new FinisherAnchorPoint(state.AnchorInBody.X, state.AnchorInBody.Y),
            new FinisherAnchorPoint(transform.X.X, transform.X.Y),
            new FinisherAnchorPoint(transform.Y.X, transform.Y.Y));
        Vector2 compensated = new(position.X, position.Y);
        if (compensated.IsFinite())
        {
            body.Position = compensated;
        }
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
        if (includeSettle)
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

    private Vector2 GetDeathSquashMultiplier() => Scenario switch
    {
        FinisherScenarioKind.EnemyExecutesNinjaSlayer => Vector2.One,
        _ => _usesJumpDeathSquash ? JumpDeathSquash : DefaultDeathSquash
    };

    private static void ApplyEnemyFlash(
        IEnumerable<ImpactVisualSnapshot> snapshots,
        float amount)
    {
        foreach (ImpactVisualSnapshot snapshot in snapshots.Where(snapshot => GodotObject.IsInstanceValid(snapshot.Body)))
        {
            snapshot.Body.SelfModulate = snapshot.SelfModulate.Lerp(
                new Color(1.8f, 1.8f, 1.8f, snapshot.SelfModulate.A),
                amount);
        }
    }

    private void ApplyEnhancedVictimFeedback(
        IEnumerable<ImpactVisualSnapshot> snapshots,
        IReadOnlyList<ReverseVictimVisualSnapshot> reverseVictims,
        float amount,
        bool flash,
        float? reverseRotationAmount = null)
    {
        Vector2 squashMultiplier = GetDeathSquashMultiplier();
        foreach (ImpactVisualSnapshot snapshot in snapshots.Where(snapshot => GodotObject.IsInstanceValid(snapshot.Body)))
        {
            float rotation = Scenario == FinisherScenarioKind.EnemyExecutesNinjaSlayer
                ? snapshot.Rotation
                : snapshot.Rotation + Mathf.DegToRad(EnhancedEnemyTiltDegrees * snapshot.Direction * amount);
            if (_deathSquashStates.TryGetValue(snapshot.Body, out DeathSquashVisualState? state))
            {
                ApplyDeathSquashTransform(state, squashMultiplier, rotation);
            }
            else
            {
                snapshot.Body.Scale = snapshot.Scale * squashMultiplier;
                snapshot.Body.Rotation = rotation;
            }
            snapshot.Body.SelfModulate = flash
                ? snapshot.SelfModulate.Lerp(
                    new Color(1.8f, 1.8f, 1.8f, snapshot.SelfModulate.A),
                    amount)
                : snapshot.SelfModulate;
        }

        ApplyReverseVictimRotation(reverseVictims, reverseRotationAmount ?? amount);
    }

    private List<ReverseVictimVisualSnapshot> CaptureReverseVictimVisuals(
        IEnumerable<NCreature> targetNodes)
    {
        if (Scenario != FinisherScenarioKind.EnemyExecutesNinjaSlayer)
        {
            return [];
        }

        return targetNodes
            .Select(target => NinjaSlayerVisualRig.GetAirborneAnchor(target.Visuals))
            .Where(anchor => anchor != null && GodotObject.IsInstanceValid(anchor))
            .Cast<Node2D>()
            .Distinct()
            .Select(anchor => new ReverseVictimVisualSnapshot(
                anchor,
                anchor.RotationDegrees,
                anchor.ProcessMode))
            .ToList();
    }

    private static void FreezeReverseVictimVisuals(
        IEnumerable<ReverseVictimVisualSnapshot> snapshots)
    {
        foreach (ReverseVictimVisualSnapshot snapshot in snapshots.Where(snapshot =>
                     GodotObject.IsInstanceValid(snapshot.Anchor)))
        {
            snapshot.Anchor.ProcessMode = Node.ProcessModeEnum.Disabled;
        }
    }

    private static void ApplyReverseVictimRotation(
        IEnumerable<ReverseVictimVisualSnapshot> snapshots,
        float amount)
    {
        foreach (ReverseVictimVisualSnapshot snapshot in snapshots.Where(snapshot =>
                     GodotObject.IsInstanceValid(snapshot.Anchor)))
        {
            snapshot.Anchor.RotationDegrees = snapshot.RotationDegrees + 15f * amount;
        }
    }

    private static void RestoreReverseVictimVisuals(
        IEnumerable<ReverseVictimVisualSnapshot> snapshots)
    {
        foreach (ReverseVictimVisualSnapshot snapshot in snapshots.Where(snapshot =>
                     GodotObject.IsInstanceValid(snapshot.Anchor)))
        {
            snapshot.Anchor.RotationDegrees = snapshot.RotationDegrees;
            snapshot.Anchor.ProcessMode = snapshot.ProcessMode;
        }
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
            snapshot.Body.SelfModulate = snapshot.SelfModulate;
        }
    }

    private void RestoreImpactVisuals(IEnumerable<ImpactVisualSnapshot> snapshots)
    {
        Vector2 squashMultiplier = GetDeathSquashMultiplier();
        foreach (ImpactVisualSnapshot snapshot in snapshots.Where(snapshot => GodotObject.IsInstanceValid(snapshot.Body)))
        {
            if (_deathSquashStates.TryGetValue(snapshot.Body, out DeathSquashVisualState? state))
            {
                ApplyDeathSquashTransform(state, squashMultiplier, snapshot.Rotation);
            }
            else
            {
                snapshot.Body.Position = snapshot.Position;
                snapshot.Body.Scale = snapshot.Scale;
                snapshot.Body.Rotation = snapshot.Rotation;
            }
            snapshot.Body.SelfModulate = snapshot.SelfModulate;
        }
    }

    private async Task ReturnToBaseline()
    {
        if (!GodotObject.IsInstanceValid(_actorNode))
        {
            ApplyDeathKickRecovery(1f);
            _returnTimelineCompleted = true;
            SetBackdropIntensity(0f);
            _camera.ResetToBaseline();
            return;
        }

        Vector2 ownerFrom = _actorNode.Position;
        Vector2 cameraFrom = _camera.CurrentPosition;
        float scaleFrom = _camera.CurrentScale;
        float backdropFrom = _backdropIntensity;
        float actorReturnSeconds = Scenario == FinisherScenarioKind.YamotoKokiIaiSlash
            ? FinisherActionTrajectory.SlowTravelSeconds
            : ReturnSeconds;
        float totalReturnSeconds = Math.Max(ReturnSeconds, actorReturnSeconds);
        float elapsed = 0f;
        while (elapsed < totalReturnSeconds)
        {
            elapsed += await NextFrame();
            float cameraLinearProgress = Mathf.Clamp(elapsed / ReturnSeconds, 0f, 1f);
            float cameraProgress = CombatCinematicCameraLease.EaseOutCubic(cameraLinearProgress);
            float actorProgress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(elapsed / actorReturnSeconds, 0f, 1f));
            ApplyDeathKickRecovery(cameraLinearProgress);
            _actorNode.Position = ownerFrom.Lerp(_actorStartPosition, actorProgress);
            _camera.SetTransform(
                cameraFrom.Lerp(_camera.BaselinePosition, cameraProgress),
                Mathf.Lerp(scaleFrom, _camera.BaselineScale.X, cameraProgress));
            SetBackdropIntensity(Mathf.Lerp(backdropFrom, 0f, cameraProgress));
        }

        ApplyDeathKickRecovery(1f);
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
        ulong processFrame = Engine.GetProcessFrames();
        if (processFrame != _lastDeltaFrame)
        {
            ulong now = Time.GetTicksMsec();
            _cachedFrameDelta = _room.ProcessMode == Node.ProcessModeEnum.Disabled
                ? 0f
                : Math.Min((now - _lastFrameMsec) / 1000f, 0.05f);
            _lastFrameMsec = now;
            _lastDeltaFrame = processFrame;
        }

        return _cachedFrameDelta;
    }

    private async Task RunCameraShakePump()
    {
        ulong lastFrameMsec = Time.GetTicksMsec();
        try
        {
            while (!_disposed && GodotObject.IsInstanceValid(_room) && _room.IsInsideTree())
            {
                await _room.ToSignal(_room.GetTree(), SceneTree.SignalName.ProcessFrame);
                ulong now = Time.GetTicksMsec();
                float delta = _room.ProcessMode == Node.ProcessModeEnum.Disabled
                    ? 0f
                    : Math.Min((now - lastFrameMsec) / 1000f, 0.05f);
                lastFrameMsec = now;
                _camera.Advance(delta);
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

    private Node GetActorFreezeNode() => Scenario == FinisherScenarioKind.NinjaSlayerAttack
        ? _actorNode
        : _actorNode.Visuals.GetCurrentBody();

    private readonly record struct ProcessModeSnapshot(Node Node, Node.ProcessModeEnum Mode);
    private readonly record struct ImpactVisualSnapshot(
        Node2D Body,
        Vector2 Position,
        Vector2 Scale,
        float Rotation,
        Color SelfModulate,
        Control Bounds,
        float Direction);
    private readonly record struct ReverseVictimVisualSnapshot(
        Node2D Anchor,
        float RotationDegrees,
        Node.ProcessModeEnum ProcessMode);

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
        Vector2 OriginalPosition,
        Vector2 OriginalScale,
        CanvasItem? Parent,
        Vector2 AnchorInBody,
        Vector2 AnchorInParent,
        bool WasTopLevel,
        bool HasAnchorCompensation);

    private static float EaseOut(float value) => 1f - (1f - value) * (1f - value);
}
