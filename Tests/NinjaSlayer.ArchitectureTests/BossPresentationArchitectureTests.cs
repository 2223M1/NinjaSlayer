namespace NinjaSlayer.ArchitectureTests;

public sealed partial class RepositoryArchitectureTests
{
    [Fact]
    public void BossFragmentsUseOneSemanticGpuAtlasAndFrozenBattleScale()
    {
        string capture = SourceText("Code/ExternalAnimations/BossVisualCapture.cs");
        string partitioner = SourceText("Code/ExternalAnimations/BossFragmentPartitioner.cs");
        string presentation = TypeSourceText("BossDismembermentPresentation");
        string math = SourceText("Code/Combat/BossDismembermentMath.cs");
        string shader = File.ReadAllText(Path.Combine(
            Root,
            "NinjaSlayer",
            "shaders",
            "vfx",
            "boss_dismemberment_clip.gdshader"));

        Assert.Contains("PreferredScreenSupersampling = 2f", capture, StringComparison.Ordinal);
        Assert.Contains("MaximumAtlasPixels = 4096", capture, StringComparison.Ordinal);
        Assert.Contains("PartsPerFrame = 2", capture, StringComparison.Ordinal);
        Assert.Contains("SubViewport.UpdateMode.Once", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("GetImage(", capture, StringComparison.Ordinal);
        Assert.Contains("TryPackAtlas", capture, StringComparison.Ordinal);
        Assert.Contains("ApplySlotIsolation", capture, StringComparison.Ordinal);
        Assert.Contains("slot.Call(\"set_color\"", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("DuplicateVisualOnly(_template)", capture, StringComparison.Ordinal);
        Assert.Contains("TakePreparedFragments", capture, StringComparison.Ordinal);
        Assert.Contains("spawn_setup_ms=", presentation, StringComparison.Ordinal);
        Assert.Contains("GroupBy(slot => slot.BoneId)", partitioner, StringComparison.Ordinal);
        Assert.Contains("MeasureIsolatedBounds", partitioner, StringComparison.Ordinal);
        Assert.Contains("slot.Call(\"set_attachment\"", partitioner, StringComparison.Ordinal);
        Assert.Contains("OversizedAreaRatio = 0.22f", partitioner, StringComparison.Ordinal);
        Assert.Contains("OversizedSpanRatio = 0.45f", partitioner, StringComparison.Ordinal);
        Assert.Contains("Rect2 semanticSourceBounds = atlasParts", partitioner, StringComparison.Ordinal);
        Assert.Contains("ToFragmentRect(semanticSourceBounds)", partitioner, StringComparison.Ordinal);
        int readBoneName = partitioner.IndexOf(
            "private static string ReadBoneName",
            StringComparison.Ordinal);
        int directBoneName = partitioner.IndexOf(
            "ReadString(bone, \"get_bone_name\", \"get_name\")",
            readBoneName,
            StringComparison.Ordinal);
        int boneDataFallback = partitioner.IndexOf(
            "CallObject(bone, \"get_data\")",
            readBoneName,
            StringComparison.Ordinal);
        Assert.True(
            readBoneName >= 0
            && directBoneName > readBoneName
            && boneDataFallback > directBoneName);
        Assert.Contains("public const int MaximumPieces = 16", math, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildFountainRagdollLinks", presentation, StringComparison.Ordinal);
        Assert.Contains("CombatVfxContainer", presentation, StringComparison.Ordinal);
        Assert.Contains("BodyToSceneContainer", presentation, StringComparison.Ordinal);
        Assert.Contains("ValidateBaselineFragmentGeometry", presentation, StringComparison.Ordinal);
        Assert.Contains("partition.SourceBounds", presentation, StringComparison.Ordinal);
        Assert.Contains("semantic_source_size=", presentation, StringComparison.Ordinal);
        Assert.Contains(
            "Vector2 compressionOrigin = fragment.CompressionOrigin",
            presentation,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "_burstOrigin.X + MathF.Cos(phase) * CompressionSlideRadius",
            presentation,
            StringComparison.Ordinal);
        Assert.Contains("keeping the original death pose visible", presentation, StringComparison.Ordinal);
        Assert.Contains("uniform vec2 control_offsets[16]", shader, StringComparison.Ordinal);
        Assert.Contains("part_bounds_min", shader, StringComparison.Ordinal);
        Assert.Contains("atlas_content_min", shader, StringComparison.Ordinal);
        Assert.DoesNotContain("texture_bounds_min", shader, StringComparison.Ordinal);
        Assert.DoesNotContain("seed_16", shader, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(Root, "Code/Combat/BossCaptureSamplingMath.cs")));
        Assert.False(File.Exists(Path.Combine(Root, "Code/Combat/BossSemanticPartPolicy.cs")));
        Assert.False(File.Exists(Path.Combine(Root, "Code/Combat/BossSemanticPartMergePolicy.cs")));
        Assert.False(File.Exists(Path.Combine(Root, "Code/Combat/BossSpineTopologyPolicy.cs")));
    }

    [Fact]
    public void BossBurstAtomicallyOwnsCombatEndMusicAndDeathFade()
    {
        string groups = SourceText("Code/Patches/NinjaSlayerPatchGroups.cs");
        string entry = SourceText("Scripts/Entry.cs");
        string patches = SourceText("Code/Patches/BossBurstPresentationPatches.cs");
        string compatibility = SourceText("Code/Compatibility/GameCompatibility.BossBurst.cs");
        string registry = SourceText(
            "Code/ExternalAnimations/BossBurstParticipationRegistry.cs");
        string fadeRegistry = SourceText(
            "Code/ExternalAnimations/BossBurstDeathFadeRegistry.cs");
        string coordinator = SourceText(
            "Code/ExternalAnimations/BossBurstPresentationCoordinator.cs");
        string musicSession = SourceText(
            "Code/ExternalAnimations/BossBurstMusicSession.cs");
        string deathPatch = SourceText("Code/Patches/BossDeathPresentationPatch.cs");

        Assert.Contains("class BossBurstPresentationPatchGroup", groups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<BossDeathPresentationPatch>()", groups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<BossBurstCombatEndMusicPatch>()", groups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<BossBurstSingleDeathFadePatch>()", groups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<BossBurstGroupedDeathFadePatch>()", groups, StringComparison.Ordinal);
        Assert.Contains("RegisterPatch<BossBurstDeathFadePlaybackPatch>()", groups, StringComparison.Ordinal);
        Assert.Contains("InstallCapability<BossBurstPresentationPatchGroup>", entry, StringComparison.Ordinal);
        Assert.Contains("GameCompatibility.BossBurst.GetProbes()", entry, StringComparison.Ordinal);
        Assert.Contains("nameof(NRunMusicController.UpdateTrack)", patches, StringComparison.Ordinal);
        Assert.Contains(
            "BossBurstParticipationRegistry.ShouldSuppressCombatEndMusicAfterFailure()",
            patches,
            StringComparison.Ordinal);
        Assert.Contains("typeof(List<NCreature>)", patches, StringComparison.Ordinal);
        Assert.Contains("Prefix(NCreature creatureNode", patches, StringComparison.Ordinal);
        Assert.Contains("Prefix(ref List<NCreature> creatureNodes", patches, StringComparison.Ordinal);
        Assert.DoesNotContain("object[] __args", patches, StringComparison.Ordinal);
        Assert.Contains(
            "BossBurstPresentationPolicy.ResolveGroupedDeathFade",
            patches,
            StringComparison.Ordinal);
        Assert.Contains("creatureNodes = remaining", patches, StringComparison.Ordinal);
        Assert.Contains("BossBurstDeathFadeRegistry.MarkPlaybackSuppressed", patches, StringComparison.Ordinal);
        Assert.Contains("BossBurstDeathFadeRegistry.ConsumePlaybackSuppression", patches, StringComparison.Ordinal);
        Assert.Contains("__result = Task.CompletedTask", patches, StringComparison.Ordinal);
        Assert.Contains("ConditionalWeakTable<NMonsterDeathVfx", fadeRegistry, StringComparison.Ordinal);
        Assert.Contains("musicEvent.Call(\"stop\", 1)", compatibility, StringComparison.Ordinal);
        Assert.Contains(
            "proxy.Set(\"_musicEv\", default(Variant))",
            compatibility,
            StringComparison.Ordinal);
        Assert.DoesNotContain("__instance.StopMusic()", patches, StringComparison.Ordinal);
        Assert.Contains(
            "proxy.Call(\"update_global_parameter\", \"Progress\", 0f)",
            compatibility,
            StringComparison.Ordinal);
        Assert.Contains("NRunMusicController.ResolveMusic", compatibility, StringComparison.Ordinal);
        Assert.Contains("nameof(NMonsterDeathVfx.PlayVfx)", compatibility, StringComparison.Ordinal);
        Assert.Contains("CurrentTrack?.SetValue", compatibility, StringComparison.Ordinal);
        Assert.Contains("TryStopBossMusicImmediately", musicSession, StringComparison.Ordinal);
        int musicBegin = deathPatch.IndexOf("BossBurstMusicSession.Begin(room)", StringComparison.Ordinal);
        int deathAnimation = deathPatch.IndexOf("controller.StartDeathAnimation(shouldRemove)", StringComparison.Ordinal);
        Assert.True(musicBegin >= 0 && deathAnimation > musicBegin);
        Assert.Contains("BossBurstPresentationPolicy.ShouldRollbackMusic", deathPatch, StringComparison.Ordinal);
        Assert.Contains("ConditionalWeakTable<NCreature", registry, StringComparison.Ordinal);
        Assert.Contains("ConditionalWeakTable<NCombatRoom", registry, StringComparison.Ordinal);
        Assert.Contains("Rooms.Remove(sceneRoom)", registry, StringComparison.Ordinal);
        Assert.Contains("rawInstance.Call(\"start\")", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("audioEvent.TryPlay()", coordinator, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(
            Root,
            "Code/Patches/NinjaSlayerVictoryRewardSfxPatch.cs")));
    }

    [Fact]
    public void ArchitectExecutionUsesTheSharedSoftBodyLeadAndConcurrentExit()
    {
        string cinematic = SourceText("Code/ExternalAnimations/ArchitectExecutionCinematic.cs");
        string deathSession = SourceText("Code/ExternalAnimations/ArchitectDeathPresentationSession.cs");
        string presentation = TypeSourceText("BossDismembermentPresentation");
        string patch = SourceText("Code/Patches/ArchitectExecutionPatch.cs");

        Assert.Contains("CreatureCmd.Kill(_architectNode.Entity, force: true)", cinematic, StringComparison.Ordinal);
        Assert.Contains("WaitUntilDeathStarts(killTask", cinematic, StringComparison.Ordinal);
        int capture = cinematic.IndexOf(
            "BossDismembermentPresentation.TryCapture(",
            StringComparison.Ordinal);
        int kill = cinematic.IndexOf(
            "CreatureCmd.Kill(_architectNode.Entity, force: true)",
            StringComparison.Ordinal);
        Assert.True(capture >= 0 && kill > capture);
        Assert.Contains("TrySpawnArchitectLead", cinematic, StringComparison.Ordinal);
        Assert.Contains("BossBurstPresentationCoordinator.Register", cinematic, StringComparison.Ordinal);
        Assert.Contains("await registration.Cue.WaitAsync", cinematic, StringComparison.Ordinal);
        Assert.Contains("registration.CombatRelease.WaitAsync", cinematic, StringComparison.Ordinal);
        Assert.DoesNotContain("registration.Completion.WaitAsync", cinematic, StringComparison.Ordinal);
        Assert.Contains("private const float ExitSpeedPixelsPerSecond = 840f", cinematic, StringComparison.Ordinal);
        int replacementDecision = cinematic.IndexOf(
            "bool fragmentReplacementReady = _softBodyLead != null",
            StringComparison.Ordinal);
        int logicalDeath = cinematic.IndexOf(
            "_deathSession.CompleteVisuals()",
            replacementDecision,
            StringComparison.Ordinal);
        int registration = cinematic.IndexOf(
            "BossBurstPresentationCoordinator.Register",
            replacementDecision,
            StringComparison.Ordinal);
        int exitStart = cinematic.IndexOf("StartExitScene()", StringComparison.Ordinal);
        int combatRelease = cinematic.IndexOf(
            "registration.CombatRelease.WaitAsync",
            replacementDecision,
            StringComparison.Ordinal);
        int fallbackRelease = cinematic.IndexOf(
            "_deathSession.CompleteVisuals()",
            combatRelease,
            StringComparison.Ordinal);
        Assert.True(
            replacementDecision >= 0
            && logicalDeath > replacementDecision
            && registration > logicalDeath
            && exitStart > registration
            && combatRelease > exitStart
            && fallbackRelease > combatRelease);
        Assert.Contains("if (fragmentReplacementReady)", cinematic, StringComparison.Ordinal);
        Assert.Contains("if (!fragmentReplacementReady)", cinematic, StringComparison.Ordinal);
        Assert.Contains("private CinematicSessionLifetime? _exitLifetime", cinematic, StringComparison.Ordinal);
        Assert.Contains("private async Task RunExitScene", cinematic, StringComparison.Ordinal);
        Assert.Contains("room.AddChildSafely(controller)", cinematic, StringComparison.Ordinal);
        Assert.Contains("private bool _initialized", cinematic, StringComparison.Ordinal);
        Assert.DoesNotContain("CancellationTokenSource", cinematic, StringComparison.Ordinal);
        Assert.DoesNotContain("NinjaSlayerNinjaSoulEvent", cinematic, StringComparison.Ordinal);
        Assert.Contains(
            "public const float DurationSeconds = BossBurstTimeline.LeadSeconds",
            deathSession,
            StringComparison.Ordinal);
        Assert.Contains("private readonly ulong _architectInstanceId", deathSession, StringComparison.Ordinal);
        Assert.DoesNotContain("_architect.GetInstanceId()", deathSession, StringComparison.Ordinal);
        Assert.Contains("PresentationMode.ArchitectLead", presentation, StringComparison.Ordinal);
        Assert.Contains("SolveSoftBodies(seconds, _floorY)", presentation, StringComparison.Ordinal);
        Assert.Contains("maximumClusterSize", presentation, StringComparison.Ordinal);
        Assert.Contains("CanBreak = canBreak", presentation, StringComparison.Ordinal);
        Assert.Contains("_joints.Clear();", presentation, StringComparison.Ordinal);
        Assert.Contains(
            "BuildRagdollLinks(_bodies.Count, canBreak: false)",
            presentation,
            StringComparison.Ordinal);
        Assert.DoesNotContain("BuildFountainRagdollLinks", presentation, StringComparison.Ordinal);
        int architectBurst = presentation.IndexOf(
            "internal bool TriggerArchitectBurst()",
            StringComparison.Ordinal);
        int clearLeadLinks = presentation.IndexOf(
            "_joints.Clear();",
            architectBurst,
            StringComparison.Ordinal);
        int applyFountainPlan = presentation.IndexOf(
            "ApplyFountainPlan();",
            architectBurst,
            StringComparison.Ordinal);
        Assert.True(
            architectBurst >= 0
            && clearLeadLinks > architectBurst
            && applyFountainPlan > clearLeadLinks);
        Assert.Contains("fragment.Body.PinCompressed", presentation, StringComparison.Ordinal);
        Assert.Contains("ApplyRenderFrame();", presentation, StringComparison.Ordinal);
        Assert.Contains(
            "SetCollisionEnvelope(hullScale: 1f, marginScale: 0f)",
            presentation,
            StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(
            Root,
            "Code/ExternalAnimations/ArchitectRagdollDeathAnimation.cs")));
        Assert.Contains("__instance.DeathAnimationTask = deathTask", patch, StringComparison.Ordinal);
        Assert.Contains("__result = ArchitectDeathPresentationSession.DurationSeconds", patch, StringComparison.Ordinal);
        Assert.Contains("return false", patch, StringComparison.Ordinal);
    }

    [Fact]
    public void BossBurstVideoCannotBeSkippedAndKeepsGlobalStatusUiAboveIt()
    {
        string coordinator = SourceText(
            "Code/ExternalAnimations/BossBurstPresentationCoordinator.cs");
        string patch = SourceText("Code/Patches/BossDeathPresentationPatch.cs");

        Assert.Contains("public const int VideoZIndex = 100", coordinator, StringComparison.Ordinal);
        Assert.Contains("public const int TopBarZIndex = 110", coordinator, StringComparison.Ordinal);
        Assert.Contains("SetLayer(globalUi.TopBar, TopBarZIndex)", coordinator, StringComparison.Ordinal);
        Assert.Contains("creature.Visuals.GetCurrentBody()", coordinator, StringComparison.Ordinal);
        Assert.Contains("LowerShadows(creature.Visuals)", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("SetLayer(creature.Visuals, ActorZIndex)", coordinator, StringComparison.Ordinal);
        Assert.Contains("Volume = 0f", coordinator, StringComparison.Ordinal);
        Assert.Contains(
            "BossBurstTimeline.ResolveFadeAlpha(videoPosition)",
            coordinator,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CombatReleaseSeconds", coordinator, StringComparison.Ordinal);
        int videoTimeline = coordinator.IndexOf(
            "private async Task PlayVideoTimeline",
            StringComparison.Ordinal);
        int videoStop = coordinator.IndexOf(
            "FreeVideo(batch)",
            videoTimeline,
            StringComparison.Ordinal);
        int combatRelease = coordinator.IndexOf(
            "batch.CombatReleaseSource.TrySetResult()",
            videoStop,
            StringComparison.Ordinal);
        Assert.True(videoTimeline >= 0 && videoStop > videoTimeline && combatRelease > videoStop);
        Assert.Contains("rawInstance.HasMethod(\"get_playback_state\")", coordinator, StringComparison.Ordinal);
        Assert.Contains("hasUnreleasedPresentation", coordinator, StringComparison.Ordinal);
        Assert.Contains("!batch.CombatReleaseSource.Task.IsCompleted", coordinator, StringComparison.Ordinal);
        Assert.Contains("private ulong _roomInstanceId", coordinator, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(active, this)", coordinator, StringComparison.Ordinal);
        Assert.Contains("if (ownsRegistration)", coordinator, StringComparison.Ordinal);
        Assert.Contains("FinalizeBatch(batch", coordinator, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref batch.Finalized, 1)", coordinator, StringComparison.Ordinal);
        Assert.Contains("room.GetTree().Paused || IsBlockingUiOpen()", coordinator, StringComparison.Ordinal);
        Assert.Contains("using the timed fallback", coordinator, StringComparison.Ordinal);
        Assert.Contains("root.AddChildSafely(player)", coordinator, StringComparison.Ordinal);
        Assert.Contains("!player.IsInsideTree()", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("room.ProcessMode == ProcessModeEnum.Disabled", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("Input.IsKeyPressed", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("Key.Space", coordinator, StringComparison.Ordinal);
        Assert.Contains("__instance.Entity.IsPrimaryEnemy", patch, StringComparison.Ordinal);
    }

    [Fact]
    public void BossBurstOwnsTheDeathTaskAndBlocksCombatEndInInstantMode()
    {
        string controller = SourceText(
            "Code/ExternalAnimations/BossDeathPresentationController.cs");
        string patch = SourceText("Code/Patches/BossDeathPresentationPatch.cs");
        string coordinator = SourceText(
            "Code/ExternalAnimations/BossBurstPresentationCoordinator.cs");
        string feedback = SourceText("Content/NinjaSlayerCombatFeedback.cs");

        Assert.Contains("internal float StartDeathAnimation(bool shouldRemove)", controller, StringComparison.Ordinal);
        Assert.Contains("_boss.DeathAnimationTask = deathTask", controller, StringComparison.Ordinal);
        Assert.Contains("return BossBurstTimeline.LeadSeconds", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("IDeathDelayer", controller, StringComparison.Ordinal);
        Assert.Contains("controller.StartDeathAnimation(shouldRemove)", patch, StringComparison.Ordinal);
        Assert.Contains("return false", patch, StringComparison.Ordinal);
        Assert.Contains("WaitForCombatRelease", coordinator, StringComparison.Ordinal);
        Assert.Contains(
            "await BossBurstPresentationCoordinator.WaitForCombatRelease()",
            feedback,
            StringComparison.Ordinal);
        Assert.Contains("registration.CombatRelease.WaitAsync", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("registration.Completion.WaitAsync", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void BossDeathWhiteoutRunsDuringTheFinalLeadWindowAndRestoresSpineState()
    {
        string controller = SourceText(
            "Code/ExternalAnimations/BossDeathPresentationController.cs");
        string lease = SourceText("Code/ExternalAnimations/BossDeathWhiteoutLease.cs");
        string timeline = SourceText("Code/Combat/BossBurstTimeline.cs");
        string character = SourceText("Content/NinjaSlayerCharacter.cs");
        string shader = File.ReadAllText(Path.Combine(
            Root,
            "NinjaSlayer",
            "shaders",
            "vfx",
            "boss_death_whiteout.gdshader"));

        Assert.Contains("WhiteoutStartSeconds = LeadSeconds - WhiteoutSeconds", timeline, StringComparison.Ordinal);
        Assert.Contains("ResolveWhiteoutMix", timeline, StringComparison.Ordinal);
        Assert.Contains("RunWhiteoutUntilCue", controller, StringComparison.Ordinal);
        Assert.Contains("Task whiteoutTask", controller, StringComparison.Ordinal);
        Assert.Contains("DisposeWhiteout()", controller, StringComparison.Ordinal);
        Assert.Contains("GetNormalMaterial()", lease, StringComparison.Ordinal);
        Assert.Contains("SetNormalMaterial", lease, StringComparison.Ordinal);
        Assert.Contains("get_color", lease, StringComparison.Ordinal);
        Assert.Contains("set_color", lease, StringComparison.Ordinal);
        Assert.Contains("state.Original.A", lease, StringComparison.Ordinal);
        Assert.Contains("BossDeathWhiteoutLease.AssetPaths", character, StringComparison.Ordinal);
        Assert.Contains("color.rgb = mix(color.rgb, vec3(1.0), white_mix)", shader, StringComparison.Ordinal);
        Assert.DoesNotContain("color.a =", shader, StringComparison.Ordinal);
    }

    [Fact]
    public void BossBurstUsesOnlyNinjaSoulAndTheProjectVideo()
    {
        string coordinator = SourceText(
            "Code/ExternalAnimations/BossBurstPresentationCoordinator.cs");
        string character = SourceText("Content/NinjaSlayerCharacter.cs");
        string architect = SourceText("Code/ExternalAnimations/ArchitectExecutionCinematic.cs");
        string controller = SourceText(
            "Code/ExternalAnimations/BossDeathPresentationController.cs");

        Assert.Contains("ninja_slayer_boss_burst.ogv", coordinator, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerNinjaSoulEvent", coordinator, StringComparison.Ordinal);
        Assert.Contains("BossBurstPresentationCoordinator.AssetPaths", character, StringComparison.Ordinal);
        Assert.DoesNotContain("BossDeathExplosionVfx", architect, StringComparison.Ordinal);
        Assert.DoesNotContain("BossDeathExplosionVfx", controller, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(
            Root,
            "Code/ExternalAnimations/BossDeathExplosionVfx.cs")));
    }

    [Fact]
    public void ArchitectVictoryCleanupSuppressesOnlyTheMarkedNinjaSlayerDeath()
    {
        string cinematic = SourceText("Code/ExternalAnimations/ArchitectExecutionCinematic.cs");
        string deathPatch = SourceText("Code/Patches/NinjaSlayerDeathAnimPatch.cs");
        string cleanup = SourceText("Code/ExternalAnimations/ArchitectVictoryCleanup.cs");

        int completed = cinematic.IndexOf("_completed = true;", StringComparison.Ordinal);
        int mark = cinematic.IndexOf("ArchitectVictoryCleanup.Mark(_owner)", StringComparison.Ordinal);
        int ready = cinematic.IndexOf("SetLocalPlayerReady()", mark, StringComparison.Ordinal);
        Assert.True(completed >= 0 && mark > completed && ready > mark);
        Assert.DoesNotContain("ArchitectVictoryCleanup.Clear", cinematic, StringComparison.Ordinal);
        Assert.Contains("ArchitectVictoryCleanup.TryConsume(__instance.Entity)", deathPatch, StringComparison.Ordinal);
        Assert.Contains("ConditionalWeakTable<Creature, Marker>", cleanup, StringComparison.Ordinal);
    }
}
