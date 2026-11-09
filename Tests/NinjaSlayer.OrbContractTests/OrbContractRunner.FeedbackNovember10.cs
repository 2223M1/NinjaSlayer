using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Backgrounds;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Events;
using NinjaSlayer.Orbs;
using STS2RitsuLib;
using STS2RitsuLib.Patching.Core;

namespace NinjaSlayer.OrbContractTests;

public partial class FeedbackCard : NCard
{
    public override void _Ready() { }
    public override void _ExitTree() { }
}

public partial class FeedbackCombatUi : NCombatUi
{
    public override void _Ready() { }
    public override void _ExitTree() { }
}

public partial class OrbContractRunner
{
    private static NRunMusicController? _feedbackMusic;
    private static readonly List<string> FeedbackMusicCalls = [];

    private async Task VerifyNovemberFeedback()
    {
        await VerifySawatariVisibleReload();
        VerifyDeferredBossMusic();
        await VerifyGuardStanceAndCrossGuard();
        await VerifyInterruptedCardFade();
        await VerifyKaiserSharedCapture();
    }

    private static async Task VerifySawatariVisibleReload()
    {
        using var fixture = new SawatariFixture();
        var game = new NGame();
        var transition = new NTransition();
        object? oldGame = NGame.Instance;
        object? oldEvents = RunManager.Instance.EventSynchronizer;
        var sync = (EventSynchronizer)RuntimeHelpers.GetUninitializedObject(typeof(EventSynchronizer));
        var events = new List<EventModel>();
        AccessTools.Field(typeof(EventSynchronizer), "_events").SetValue(sync, events);
        AccessTools.Property(typeof(NGame), "Instance").SetValue(null, game);
        AccessTools.Property(typeof(NGame), "Transition").SetValue(game, transition);
        AccessTools.Property(typeof(RunManager), "EventSynchronizer").SetValue(RunManager.Instance, sync);
        FieldInfo start = AccessTools.Field(typeof(TheMovingJungleEvent), "_combatStartAfterReveal");
        MethodInfo load = AccessTools.Method(typeof(NGame), "LoadRun");
        MethodInfo fade = AccessTools.Method(typeof(RunManager), "FadeIn");
        int starts = 0;
        TheMovingJungleEvent Pending()
        {
            var model = (TheMovingJungleEvent)ModelDb.Event<TheMovingJungleEvent>().ToMutable();
            start.SetValue(model, (Func<Task>)(() => { starts++; return Task.CompletedTask; }));
            events.Add(model);
            return model;
        }
        try
        {
            Pending();
            var loaded = new TaskCompletionSource();
            Task result = loaded.Task;
            SawatariRoomRevealPatch.Postfix(load, ref result);
            Require(starts == 0, "Quick Restart must finish LoadRun before starting the event.");
            loaded.SetResult();
            await result;
            Require(starts == 1, "A visible LoadRun without FadeIn must start Sawatari.");
            result = Task.CompletedTask;
            SawatariRoomRevealPatch.Postfix(fade, ref result);
            await result;
            Require(starts == 1, "A later FadeIn must not repeat the entrance.");

            events.Clear(); Pending();
            AccessTools.Property(typeof(NTransition), "InTransition").SetValue(transition, true);
            result = Task.CompletedTask;
            SawatariRoomRevealPatch.Postfix(load, ref result);
            await result;
            Require(starts == 1, "Main-menu Continue must wait while the room is covered.");
            var revealed = new TaskCompletionSource();
            result = revealed.Task;
            SawatariRoomRevealPatch.Postfix(fade, ref result);
            Require(starts == 1, "Entrance must wait for the entire reveal task.");
            AccessTools.Property(typeof(NTransition), "InTransition").SetValue(transition, false);
            revealed.SetResult();
            await result;
            Require(starts == 2, "Normal Continue must start after the fade.");

            events.Clear(); Pending();
            revealed = new TaskCompletionSource(); result = revealed.Task;
            SawatariRoomRevealPatch.Postfix(fade, ref result);
            events.Clear();
            revealed.SetResult(); await result;
            Require(starts == 2, "A stale reveal must not start the previous event.");
        }
        finally
        {
            AccessTools.Property(typeof(NGame), "Instance").SetValue(null, oldGame);
            AccessTools.Property(typeof(RunManager), "EventSynchronizer").SetValue(RunManager.Instance, oldEvents);
            game.Free(); transition.Free();
        }
        GD.Print("PASS Sawatari visible Quick Restart, covered Continue, exactly-once and stale-event cancellation.");
    }

    private static void VerifyDeferredBossMusic()
    {
        using var fixture = new SawatariFixture();
        AccessTools.Property(typeof(CombatState), "Encounter").SetValue(fixture.Combat.State,
            ModelDb.Encounter<KaiserCrabBoss>().ToMutable());
        var harmony = new Harmony("NinjaSlayer.OrbContracts.FeedbackMusic");
        _feedbackMusic = new NRunMusicController();
        var other = new NRunMusicController();
        harmony.Patch(AccessTools.PropertyGetter(typeof(NRunMusicController), "Instance"),
            prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(FeedbackMusicInstance)));
        harmony.Patch(AccessTools.Method(typeof(NRunMusicController), "PlayCustomMusic"),
            prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(FeedbackPlayMusic)));
        harmony.Patch(AccessTools.Method(typeof(NRunMusicController), "UpdateMusicParameter"),
            prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(FeedbackSetMusicParameter)));
        var cancel = AccessTools.Method(typeof(BossGreetingCinematic), "CancelDeferredBossBgm");
        FeedbackMusicCalls.Clear();
        try
        {
            Require(!BossGreetingMusicPatch.Prefix(_feedbackMusic, "event:/music/act3_boss_queen"), "Boss music must defer until its greeting.");
            Require(!BossGreetingMusicParameterPatch.Prefix(_feedbackMusic, "queen_progress", 1), "Native queen entrance parameter must defer with its track.");
            Require(!BossGreetingMusicParameterPatch.Prefix(_feedbackMusic, "queen_progress", 2), "Later parameter must replace the pending value.");
            Require(BossGreetingMusicParameterPatch.Prefix(other, "queen_progress", 3), "Unrelated controllers must pass through.");
            cancel.Invoke(null, [new CombatState()]);
            Require(!BossGreetingMusicParameterPatch.Prefix(_feedbackMusic, "queen_progress", 1), "A stale greeting must not cancel the current queue.");
            Require(FeedbackMusicCalls.Count == 0, "No parameter may reach the previous act track.");
            BossGreetingCinematic.PlayDeferredBossBgm(fixture.Combat.State);
            Require(FeedbackMusicCalls.SequenceEqual(new[] { "track:event:/music/act3_boss_queen", "queen_progress:1" }),
                "The new track must start before the latest parameter is applied.");
            BossGreetingCinematic.PlayDeferredBossBgm(fixture.Combat.State);
            Require(FeedbackMusicCalls.Count == 2, "Deferred music must only release once.");
            Require(BossGreetingCinematic.TryDeferBossBgm(_feedbackMusic, "event:/music/act2_boss_kaiser_crab"), "Second pending track missing.");
            cancel.Invoke(null, [fixture.Combat.State]);
            BossGreetingCinematic.PlayDeferredBossBgm(fixture.Combat.State);
            Require(FeedbackMusicCalls.Count == 2 && BossGreetingMusicParameterPatch.Prefix(_feedbackMusic, "kaiser_crab_progress", 1),
                "Room cancellation must discard both track and parameters.");
        }
        finally
        {
            cancel.Invoke(null, [null]);
            harmony.UnpatchAll(harmony.Id);
            _feedbackMusic.Free(); _feedbackMusic = null; other.Free();
        }
        GD.Print("PASS deferred Boss BGM parameter ordering, replacement, controller ownership and cancellation.");
    }

    private static bool FeedbackMusicInstance(ref NRunMusicController? __result) { __result = _feedbackMusic; return false; }
    private static bool FeedbackPlayMusic(string customMusic) { FeedbackMusicCalls.Add("track:" + customMusic); return false; }
    private static bool FeedbackSetMusicParameter(string label, float trackIndex) { FeedbackMusicCalls.Add($"{label}:{trackIndex}"); return false; }
    private static bool FeedbackSkipPortrait(NCard __instance) => __instance is not FeedbackCard;

    private async Task VerifyInterruptedCardFade()
    {
        using var combat = new OrbCombat(ninjaSlayer: true);
        var harmony = new Harmony("NinjaSlayer.OrbContracts.FeedbackCards");
        var product = typeof(ShurikenOrb).Assembly;
        var patcher = RitsuLibFramework.CreatePatcher("NinjaSlayer.OrbContracts", "FeedbackCardPiles");
        typeof(ModPatcherExtensions).GetMethod("RegisterPatch")!.MakeGenericMethod(
            product.GetType("NinjaSlayer.Code.Patches.RapidCardPileVisualPatch", true)!).Invoke(null, [patcher]);
        Require(patcher.PatchAll(), "Card pile visual patch did not install.");
        _hurtRoom = new FreeInputContractRoom();
        var ui = new FeedbackCombatUi();
        var play = new Control(); var effects = new Control();
        var hand = new NPlayerHand(); var queue = new NCardPlayQueue();
        AccessTools.Property(typeof(NCombatRoom), "Ui").SetValue(_hurtRoom, ui);
        AccessTools.Property(typeof(NCombatRoom), "CombatVfxContainer").SetValue(_hurtRoom, effects);
        AccessTools.Property(typeof(NCombatUi), "PlayContainer").SetValue(ui, play);
        AccessTools.Property(typeof(NCombatUi), "Hand").SetValue(ui, hand);
        AccessTools.Property(typeof(NCombatUi), "PlayQueue").SetValue(ui, queue);
        ui.AddChild(play); _hurtRoom.AddChild(ui); _hurtRoom.AddChild(effects); AddChild(_hurtRoom);
        harmony.Patch(AccessTools.PropertyGetter(typeof(NCombatRoom), "Instance"),
            prefix: new HarmonyMethod(GetType(), nameof(ResolveHurtRoom)));
        // Keep the native tween and pile movement; this minimal card has no
        // portrait widgets for the animation's mid-tween repaint callback.
        harmony.Patch(AccessTools.Method(typeof(NCard), "UpdateVisuals"),
            prefix: new HarmonyMethod(GetType(), nameof(FeedbackSkipPortrait)));
        var move = AccessTools.Method(typeof(CardPileCmd), "MoveCardNodeToNewPileBeforeTween");
        try
        {
            foreach (double fadeTime in new[] { .08, .22, .3 })
            {
                var card = combat.Card();
                var node = new FeedbackCard();
                AccessTools.Field(typeof(NCard), "_model").SetValue(node, card);
                play.AddChild(node);
                await node.AnimMultiCardPlay();
                node.PlayPileTween!.Pause(); node.PlayPileTween.CustomStep(fadeTime);
                Require(node.Modulate.A < .7f, "Regression fixture must interrupt a visibly faded native repeated-play tween.");
                move.Invoke(null, [node, PileType.Hand]);
                Require(node.Modulate == Colors.White && node.PlayPileTween?.IsRunning() != true,
                    "Native return to hand must cancel the fade and restore full opacity.");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(node.Modulate == Colors.White, "Detached animation must not fade the returned card again.");
                node.Free();
            }
            var power = AddCard<Inflame>(combat);
            power.RemoveFromCurrentPile(); PileType.Play.GetPile(combat.Player).AddInternal(power);
            var flying = new FeedbackCard();
            AccessTools.Field(typeof(NCard), "_model").SetValue(flying, power);
            play.AddChild(flying);
            Type context = product.GetType("NinjaSlayer.Code.Lifecycle.RapidCardPresentationContext", true)!;
            object scope = AccessTools.Method(context, "Begin").Invoke(null, [power])!;
            try
            {
                TestMode.IsOn = false;
                Require(NCard.FindOnTable(power) == flying, "Native play-container lookup must find the original visual.");
                object?[] arguments = [power, Task.CompletedTask];
                AccessTools.Method(product.GetType("NinjaSlayer.Code.Patches.RapidPowerCardFlyPatch", true), "Postfix").Invoke(null, arguments);
                Require(flying.GetParent() == effects && NCard.FindOnTable(power) == null,
                    "Detached Power flight must own its visual outside the native play container.");
            }
            finally
            {
                TestMode.IsOn = true;
                AccessTools.Method(scope.GetType(), "RestoreCallerContext").Invoke(scope, null);
            }
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            _hurtRoom.Free(); _hurtRoom = null; hand.Free(); queue.Free();
        }
        GD.Print("PASS native repeated-play fades interrupted at three phases restore opacity; Power flights leave play-container ownership.");
    }

    private async Task VerifyKaiserSharedCapture()
    {
        using var fixture = new SawatariFixture();
        var state = fixture.Combat.State;
        AccessTools.Property(typeof(CombatState), "Encounter").SetValue(state, ModelDb.Encounter<KaiserCrabBoss>().ToMutable());
        foreach (var enemy in state.Enemies.ToArray()) state.RemoveCreature(enemy);
        Creature Claw(MonsterModel monster)
        {
            var creature = new Creature(monster.ToMutable(), CombatSide.Enemy, null);
            AccessTools.Method(typeof(CombatState), "AttachCreature").Invoke(state, [creature]);
            state.AddCreature(creature); creature.SetMaxHpInternal(100); creature.SetCurrentHpInternal(100);
            return creature;
        }
        var crusher = Claw(ModelDb.Monster<Crusher>()); var rocket = Claw(ModelDb.Monster<Rocket>());
        GDExtensionManager.LoadExtension(System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath(
            "res://../../addons/spine/spine_godot_extension.gdextension")));
        Require(ClassDB.ClassExists("SpineSprite"), "Kaiser capture requires the native Spine extension.");
        RitsuLibFramework.EnsureGodotScriptsRegistered(typeof(NCreature).Assembly, RitsuLibFramework.CreateLogger("Kaiser contracts"));
        var loader = new MegaCrit.Sts2.Core.Assets.AtlasResourceLoader();
        ResourceLoader.AddResourceFormatLoader(loader, atFront: true);
        MegaCrit.Sts2.Core.Assets.AtlasManager.LoadAllAtlases();
        var assets = new BackgroundAssets("kaiser_crab_boss", new MegaCrit.Sts2.Core.Random.Rng(1));
        foreach (string path in assets.AssetPaths) if (path.EndsWith(".tscn", StringComparison.Ordinal)) RegisterMountedUids(path, []);
        var background = NCombatBackground.Create(assets);
        _hurtRoom = new FreeInputContractRoom { Size = new(1600, 1000) };
        var scene = new Control { Size = _hurtRoom.Size };
        var effects = new Control { Size = _hurtRoom.Size };
        _hurtRoom.AddChild(scene); _hurtRoom.AddChild(effects); scene.AddChild(background);
        AccessTools.Property(typeof(NCombatRoom), "SceneContainer").SetValue(_hurtRoom, scene);
        AccessTools.Property(typeof(NCombatRoom), "CombatVfxContainer").SetValue(_hurtRoom, effects);
        AccessTools.Property(typeof(NCombatRoom), "Background").SetValue(_hurtRoom, background);
        var actor = new AimContractCreature();
        AccessTools.Property(typeof(NCreature), "Entity").SetValue(actor, crusher);
        scene.AddChild(actor); AddChild(_hurtRoom);
        var harmony = new Harmony("NinjaSlayer.OrbContracts.FeedbackKaiser");
        harmony.Patch(AccessTools.PropertyGetter(typeof(NCombatRoom), "Instance"), prefix: new HarmonyMethod(GetType(), nameof(ResolveHurtRoom)));
        var product = typeof(ShurikenOrb).Assembly;
        Type factory = typeof(BossDismembermentPresentation);
        object? snapshot = null;
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var crab = background.GetNode<NKaiserCrabBossBackground>("%KaiserCrab");
            crab.Show();
            var body = crab.GetNode<Node2D>("%Visuals");
            await VerifyAlabamaClaws(crab, fixture.Combat.Player.Creature, crusher, rocket, scene);
            Require(ReferenceEquals(AccessTools.Method(factory, "ResolveSourceBody").Invoke(null, [_hurtRoom, actor]), body),
                "Kaiser capture must select the shared background Spine, not a claw placeholder.");
            crusher.SetCurrentHpInternal(0);
            object?[] deathArgs = [actor, true, 0f];
            MethodInfo death = AccessTools.Method(product.GetType("NinjaSlayer.Code.Patches.BossDeathPresentationPatch", true), "Prefix");
            Require((bool)death.Invoke(null, deathArgs)! && crab.GetNodeOrNull<Node>("NinjaSlayerBossDeathPresentation") == null,
                "The first defeated claw must leave the living crab and native arm death intact.");
            rocket.SetCurrentHpInternal(0);
            crab.PlayBodyDeathAnim();
            snapshot = AccessTools.Method(factory, "TryCapture").Invoke(null, [_hurtRoom, actor, null]);
            Require(snapshot != null, "The final Kaiser claw must produce a shared-body snapshot.");
            var capture = (BossVisualCapture)AccessTools.Field(snapshot!.GetType(), "_capture").GetValue(snapshot)!;
            int captureFrames = 0;
            while (captureFrames++ < 300 && !capture.IsReady && capture.FailureReason.Length == 0)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(capture.IsReady, "Native Kaiser semantic capture failed: " + capture.FailureReason);
            if (System.Environment.GetEnvironmentVariable("NINJASLAYER_FEEDBACK_RENDER_DIR") is { } renderDirectory)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var atlas = capture.Texture!.GetImage();
                Require(!atlas.IsEmpty() && atlas.GetUsedRect().HasArea(), "Rendered Kaiser fragment atlas is empty.");
                System.IO.Directory.CreateDirectory(renderDirectory);
                Require(atlas.SavePng(System.IO.Path.Combine(renderDirectory, "kaiser-atlas.png")) == Error.Ok,
                    "Could not save native Kaiser atlas evidence.");
            }
            Require(capture.BodyLocalBounds.Size.X > 500 && capture.BodyLocalBounds.Size.Y > 300,
                "Capture bounds must cover the full crab rather than one claw.");
            Type whiteout = product.GetType("NinjaSlayer.Code.ExternalAnimations.BossDeathWhiteoutLease", true)!;
            object?[] whiteArgs = [_hurtRoom, actor, null, null];
            Require((bool)AccessTools.Method(whiteout, "TryAcquire").Invoke(null, whiteArgs)!, "Shared crab whiteout failed: " + whiteArgs[3]);
            ((IDisposable)whiteArgs[2]!).Dispose();
            var controller = (BossDeathPresentationController)AccessTools.Method(typeof(BossDeathPresentationController), "Attach")
                .Invoke(null, [actor, _hurtRoom, null])!;
            Require(controller.GetParent() == crab, "The shared death controller must outlive individual claw placeholders.");
            Require((bool)death.Invoke(null, deathArgs)!, "A simultaneous second claw death must not register a second burst.");
            Vector2 center = (Vector2)AccessTools.Property(snapshot.GetType(), "BodyGlobalCenter").GetValue(snapshot)!;
            object spawned = AccessTools.Method(factory, "TrySpawn").Invoke(null, [_hurtRoom, actor, snapshot, center, null, 100])!;
            Require((bool)AccessTools.Property(spawned.GetType(), "Spawned").GetValue(spawned)! && !body.Visible,
                "Ready fragments must spawn once and hide the shared body.");
            GD.Print($"PASS native Kaiser shared rig capture {capture.BodyLocalBounds} in {captureFrames} frames, whiteout, first-claw exclusion, simultaneous-kill deduplication and fragment spawn.");
        }
        finally
        {
            ((IDisposable?)snapshot)?.Dispose();
            _hurtRoom.Free(); _hurtRoom = null; harmony.UnpatchAll(harmony.Id);
            ResourceLoader.RemoveResourceFormatLoader(loader); loader.Dispose();
        }
    }
}
