using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static int _drawFlipCalls;
    private static readonly List<float> ThrowGameplayWaits = [];
    private static int _throwDirectWaits;

    private static object? LatestPresentation(Node2D pose, string kind) =>
        ((System.Collections.IEnumerable)AccessTools.Field(pose.GetType(), "_presentations").GetValue(pose)!)
        .Cast<object>().LastOrDefault(m => AccessTools.Field(m.GetType(), "Kind").GetValue(m)!.ToString() == kind);

    private static float MotionNumber(Node2D pose, string kind, string field)
    {
        object? motion = LatestPresentation(pose, kind);
        return motion == null ? 0f : (float)AccessTools.Field(motion.GetType(), field).GetValue(motion)!;
    }

    private static bool ThrowReleased(Node2D pose) => LatestPresentation(pose, "Throw") != null
        && MotionNumber(pose, "Throw", "Elapsed") >= MotionNumber(pose, "Throw", "Duration") * (0.166f / 0.334f);

    private static float PresentationNumber(Node2D pose, string field) => field switch
    {
        "_flipElapsed" => MotionNumber(pose, "Backflip", "Elapsed"),
        "_flipDirection" => -MotionNumber(pose, "Backflip", "Facing"),
        "_throwElapsed" => MotionNumber(pose, "Throw", "Elapsed"),
        "_throwDuration" => MotionNumber(pose, "Throw", "Duration") * (0.166f / 0.334f),
        _ => (float)AccessTools.Field(pose.GetType(), field).GetValue(pose)!
    };

    private static async Task VerifyNonblockingThrow(OrbCombat combat, Node2D pose)
    {
        await AddStock(combat.Player, 1);
        ShurikenOrb orb = combat.Player.PlayerCombatState!.OrbQueue.Orbs.OfType<ShurikenOrb>().Single();
        Type helper = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Cards.ShurikenCombat", true)!;
        var harmony = new Harmony("NinjaSlayer.OrbContracts.NonblockingThrow");
        MethodInfo stockAnimation = AccessTools.Method(helper, "PlayStockThrowAnimation");
        MethodInfo skipStockAnimation = AccessTools.Method(typeof(OrbContractRunner), nameof(SkipThrowAnimation));
        harmony.Unpatch(stockAnimation, skipStockAnimation);
        harmony.Patch(AccessTools.Method(typeof(Cmd), nameof(Cmd.CustomScaledWait)),
            prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(RecordThrowGameplayWait)));
        harmony.Patch(AccessTools.Method(typeof(Cmd), nameof(Cmd.Wait), [typeof(float), typeof(bool)]),
            prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(RecordThrowDirectWait)));
        pose.SetProcess(false);
        try
        {
            ThrowGameplayWaits.Clear();
            _throwDirectWaits = 0;
            decimal hp = combat.Enemy.CurrentHp;
            bool feedback = false;
            await (Task)AccessTools.Method(helper, "TriggerStockWave").Invoke(null,
                [Choice, combat.Player.Creature, new[] { combat.Enemy }, null, orb, (Action)(() => feedback = true)])!;
            Require(feedback && hp - combat.Enemy.CurrentHp == orb.EvokeVal,
                "Stock damage or feedback waited for the frozen body animation.");
            Require(_throwDirectWaits == 0 && ThrowGameplayWaits.Count > 0
                && Math.Abs(ThrowGameplayWaits[0] - 0.15f) < 0.00001f
                && ThrowGameplayWaits.All(t => Math.Abs(t - 0.166f) > 0.00001f),
                $"Stock throw waits: direct={_throwDirectWaits}, scaled={string.Join(',', ThrowGameplayWaits)}.");
            var card = combat.Card();
            var play = new CardPlay { Card = card,
#if !NINJASLAYER_CHANNEL_STABLE
                Player = combat.Player,
#endif
                Target = combat.Enemy, ResultPile = PileType.Discard,
                Resources = new ResourceInfo { EnergySpent = 0, EnergyValue = 0, StarsSpent = 0, StarValue = 0 },
                IsAutoPlay = false, PlayIndex = 0, PlayCount = 1 };
            ThrowGameplayWaits.Clear();
            var attack = (AttackCommand)AccessTools.Method(helper, "BuildAttackCommand")
                .Invoke(null, [card, play, card.DynamicVars.Damage])!;
            await attack.Execute(Choice);
            Require(attack.Results.Count() == 1 && _throwDirectWaits == 0
                && ThrowGameplayWaits.All(t => Math.Abs(t - 0.166f) > 0.00001f),
                "Shuriken attack blocked on its body windup.");
            Require(MotionNumber(pose, "Throw", "Elapsed") == 0f,
                "The nonblocking test advanced animation time to finish gameplay.");
            GD.Print("PASS shuriken gameplay with frozen animation: stock and attack damage complete without a body-animation wait.");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            new Harmony("NinjaSlayer.OrbContracts.Presentation").Patch(stockAnimation,
                prefix: new HarmonyMethod(skipStockAnimation));
            AccessTools.Method(pose.GetType(), "Reset").Invoke(pose, null);
            pose.SetProcess(true);
        }
    }

    private static bool RecordThrowGameplayWait(float __1, ref Task __result)
    {
        ThrowGameplayWaits.Add(__1);
        __result = Task.CompletedTask;
        return false;
    }

    private static bool RecordThrowDirectWait(ref Task __result)
    {
        _throwDirectWaits++;
        __result = Task.CompletedTask;
        return false;
    }

    private static void VerifyDrawAndThrowPose(OrbCombat combat, Node2D pose, Node2D anchor,
        Sprite2D body, Marker2D center, AimContractCreature target, int formIndex)
    {
        Type type = pose.GetType();
        void Call(string name, params object?[] args) => AccessTools.Method(type, name).Invoke(pose, args);
        bool Flipping() => (bool)AccessTools.Property(type, "IsBackflipping").GetValue(pose)!;
        float Field(string name) => PresentationNumber(pose, name);
        Vector2 corePoint = formIndex == 2 ? new(50.8f, 120f) : new(580f, 30.30303f);
        Vector2 handPoint = formIndex == 2 ? new(432f, 390f) : new(851.5152f, -75.75758f);
        var contour = (System.Numerics.Vector2[])AccessTools.Field(typeof(ShurikenOrb).Assembly
            .GetType("NinjaSlayer.Code.Combat.CombatBodyContours"),
            formIndex == 2 ? "FullyReleasedNaraku" : "NinjaSlayer").GetValue(null)!;
        FastModeType speed = SaveManager.Instance.PrefsSave.FastMode;
        Transform2D anchorBaseline = anchor.Transform;
        pose.SetProcess(false);
        try
        {
            foreach (FastModeType mode in new[] { FastModeType.Normal, FastModeType.Fast, FastModeType.Instant })
            foreach (float facing in new[] { -1f, 1f })
            foreach (float height in new[] { 0f, -100f })
            {
                Call("Reset");
                anchor.Transform = Transform2D.Identity;
                anchor.Scale = new(facing, 1f);
                anchor.Position = new(0f, height);
                SaveManager.Instance.PrefsSave.FastMode = mode;
                Call("SyncNow");
                Transform2D baseline = body.GetGlobalTransformWithCanvas();
                Vector2 coreBaseline = center.GlobalPosition;
                float ground = contour.Max(p => (baseline * new Vector2(p.X, p.Y)).Y);
                Call("BeginBackflip");
                float duration = mode == FastModeType.Normal ? 0.5f : mode == FastModeType.Fast ? 0.25f : 0f;
                if (duration == 0f)
                {
                    Require(!Flipping() && body.GetGlobalTransformWithCanvas().IsEqualApprox(baseline), "Instant draw created a flip.");
                    continue;
                }
                float previous = baseline.X.Angle();
                float turns = 0f;
                for (int frame = 0; frame < 24; frame++)
                {
                    pose._Process(duration / 24f);
                    float current = body.GetGlobalTransformWithCanvas().X.Angle();
                    float change = Mathf.Wrap(current - previous, -Mathf.Pi, Mathf.Pi);
                    turns += change;
                    Require(change * -facing >= -0.002f && Math.Abs(change) < 0.5f,
                        $"Backflip reversed or jumped: form={formIndex}, mode={mode}, frame={frame}, delta={change}.");
                    Require((body.GetGlobalTransformWithCanvas() * corePoint).DistanceTo(center.GlobalPosition) < 0.1f,
                        "Backflip detached the creature hit center.");
                    Require(Math.Abs(center.GlobalPosition.X - coreBaseline.X) < 0.1f, "Backflip drifted horizontally.");
                    Require(contour.Max(p => (body.GetGlobalTransformWithCanvas() * new Vector2(p.X, p.Y)).Y) <= ground + 0.1f,
                        "Backflip body penetrated its ground baseline.");
                    previous = current;
                }
                pose._Process(0.00001);
                Require(Math.Abs(turns + facing * Mathf.Tau) < 0.002f && !Flipping(), "Backflip did not finish one full turn on time.");
                Require(body.GetGlobalTransformWithCanvas().IsEqualApprox(baseline), "Backflip left a body offset.");
            }

            SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
            anchor.Transform = Transform2D.Identity;
            target.Position = new(700f, 0f);
            Call("Reset");
            Call("SyncNow");
            Transform2D idle = body.GetGlobalTransformWithCanvas();
            Call("BeginBackflip");
            pose._Process(0.30);
            float flipElapsed = Field("_flipElapsed");
            Call("BeginShurikenThrow", combat.Enemy);
            Require(Flipping() && Field("_flipElapsed") == flipElapsed, "Throw restarted or interrupted the backflip.");
            pose._Process(0.165);
            Require(!ThrowReleased(pose), "Throw released before 0.166 seconds.");
            pose._Process(0.00101);
            Require(ThrowReleased(pose) && Flipping(),
                "Throw did not release at 0.166 seconds during the flip.");
            object?[] handArgs = [AimActors[combat.Player.Creature], null];
            Type orbVisual = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.Nodes.ShurikenOrbVisual", true)!;
            Require((bool)AccessTools.Method(orbVisual, "TryGetHandCanvasPosition").Invoke(null, handArgs)!, "Missing throw hand anchor.");
            Require(((Vector2)handArgs[1]!).DistanceTo(body.GetGlobalTransformWithCanvas() * handPoint) < 0.1f,
                "Throw origin does not follow the flipping hand.");
            pose._Process(0.11);
            Require(!Flipping() && Field("_throwDuration") > 0f, "Finishing the flip also cleared the throw recovery.");
            target.Position = new(-700f, 0f);
            Transform2D beforeTargetChange = body.GetGlobalTransformWithCanvas();
            Call("BeginShurikenThrow", combat.Enemy);
            Require(body.GetGlobalTransformWithCanvas().IsEqualApprox(beforeTargetChange), "Changing throw targets snapped the body.");
            pose._Process(0.33401);
            Require(body.GetGlobalTransformWithCanvas().IsEqualApprox(idle), "Concurrent motions did not return to the authored pose.");

            Call("BeginBackflip");
            pose._Process(0.12);
            Transform2D beforeAttack = body.GetGlobalTransformWithCanvas();
            target.Position = new(700f, 0f);
            Call("BeginAction", combat.Enemy, false, false);
            Require(body.GetGlobalTransformWithCanvas().IsEqualApprox(beforeAttack) && Flipping(),
                "Starting an attack interrupted the current flip.");
            pose._Process(0.05);
            Call("BeginReturn");
            Call("ApplyReturn", 1f);
            Require(Flipping() && Math.Abs(Field("_flipElapsed") - 0.17f) < 0.0001f,
                "Returning from an attack cleared or restarted the ongoing flip.");
            pose._Process(0.5f - 0.17f + 0.00001f);
            Require(body.GetGlobalTransformWithCanvas().IsEqualApprox(idle), "Flip did not finish independently after attack return.");
            Call("BeginShurikenThrow", combat.Enemy);
            pose._Process(0.04);
            Transform2D beforeThrowAttack = body.GetGlobalTransformWithCanvas();
            Call("BeginAction", combat.Enemy, false, false);
            Require(body.GetGlobalTransformWithCanvas().IsEqualApprox(beforeThrowAttack)
                && Math.Abs(Field("_throwElapsed") - 0.04f) < 0.00001f,
                "Starting an attack interrupted the throw windup.");
            pose._Process(0.05);
            Call("BeginReturn");
            Call("ApplyReturn", 1f);
            Require(Field("_throwDuration") > 0f, "Returning from an attack cancelled throw recovery.");
            pose._Process(0.24401);
            Require(body.GetGlobalTransformWithCanvas().IsEqualApprox(idle), "Throw did not finish independently after attack return.");
            VerifyTripleMotion(combat, pose, anchor, body, center, target, corePoint, handPoint);
            var dragOwner = new Node();
            pose.AddChild(dragOwner);
            try
            {
                Call("BeginBackflip");
                Call("BeginShurikenThrow", combat.Enemy);
                Call("Drag", dragOwner, combat.Card(), new Vector2(700f, -300f), combat.Enemy);
                Require((bool)AccessTools.Property(type, "IsAiming").GetValue(pose)!,
                    "Draw/throw presentation blocked the next card's aiming.");
            }
            finally
            {
                Call("Reset");
                Call("SyncNow");
                dragOwner.Free();
            }
            foreach (FastModeType mode in new[] { FastModeType.Normal, FastModeType.Fast, FastModeType.Instant })
            {
                SaveManager.Instance.PrefsSave.FastMode = mode;
                Call("BeginShurikenThrow", combat.Enemy);
                float expectedGate = mode == FastModeType.Normal ? 0.166f : mode == FastModeType.Fast ? 0.083f : 0f;
                float expectedTotal = mode == FastModeType.Normal ? 0.334f : mode == FastModeType.Fast ? 0.167f : 0f;
                Require(Math.Abs(Field("_throwDuration") - expectedGate) < 0.00001f, "Throw timing did not follow game speed.");
                if (expectedGate > 0f)
                {
                    pose._Process(expectedGate - 0.001f);
                    Require(!ThrowReleased(pose), "Throw released early for the selected speed.");
                    pose._Process(0.00101f);
                    Require(ThrowReleased(pose), "Throw missed its release time for the selected speed.");
                    pose._Process(expectedTotal - expectedGate - 0.00101f);
                    Require(Field("_throwDuration") > 0f, "Throw recovery ended early.");
                }
                pose._Process(0.00101f);
                Require(body.GetGlobalTransformWithCanvas().IsEqualApprox(idle), "Throw exceeded its total visual duration.");
            }
            SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
            Call("BeginBackflip");
            pose._Process(0.1);
            Call("BeginShurikenThrow", combat.Enemy);
            Call("Reset");
            pose._Process(0.5);
            Require(!Flipping() && body.GetGlobalTransformWithCanvas().IsEqualApprox(idle),
                "Cancelled throw gate restarted a cleaned-up pose.");
            Call("BeginAction", combat.Enemy, true, false);
            Transform2D exclusivePose = body.GetGlobalTransformWithCanvas();
            Call("BeginBackflip");
            Require(!Flipping() && body.GetGlobalTransformWithCanvas().IsEqualApprox(exclusivePose),
                "Draw interrupted an exclusive finisher pose.");
            GD.Print($"PASS draw/throw actual form {formIndex}: full flip, concurrent Attack/Slow/throw, independent return, mirror/airborne and hand origin.");
        }
        finally
        {
            Call("Reset");
            anchor.Transform = anchorBaseline;
            SaveManager.Instance.PrefsSave.FastMode = speed;
            pose.SetProcess(true);
        }
    }

    private static void VerifyTripleMotion(OrbCombat combat, Node2D pose, Node2D anchor,
        Sprite2D body, Marker2D center, AimContractCreature target, Vector2 corePoint, Vector2 handPoint)
    {
        Type type = pose.GetType();
        void Call(string name, params object?[] args) => AccessTools.Method(type, name).Invoke(pose, args);
        float Field(string name) => PresentationNumber(pose, name);
        var actor = AimActors[combat.Player.Creature];
        Transform2D anchorBaseline = anchor.Transform;
        Vector2 actorBaseline = actor.Position;
        Vector2 targetBaseline = target.Position;
        Type orbVisual = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.Nodes.ShurikenOrbVisual", true)!;
        foreach (float speed in new[] { 1f, 0.5f })
        foreach (float facing in new[] { 1f, -1f })
        foreach (float peak in new[] { 0.15f, 0.20f })
        {
            Call("Reset");
            SaveManager.Instance.PrefsSave.FastMode = speed == 1f ? FastModeType.Normal : FastModeType.Fast;
            actor.Position = actorBaseline;
            anchor.Transform = Transform2D.Identity;
            anchor.Scale = new(facing, 1f);
            anchor.Position = new(0f, -100f);
            target.Position = new(facing * 700f, 0f);
            Call("SyncNow");
            Transform2D idle = body.GetGlobalTransformWithCanvas();
            float unposedAngle = idle.X.Angle();
            Call("BeginBackflip");
            pose._Process(0.04f * speed);
            Call("BeginShurikenThrow", combat.Enemy);
            pose._Process(0.02f * speed);
            Call("BeginAction", combat.Enemy, false, false);
            bool returning = false;
            for (int frame = 1; frame <= 84; frame++)
            {
                float time = frame / 120f;
                pose._Process(speed / 120f);
                if (time <= peak)
                {
                    float p = time / peak;
                    float distance = peak < 0.2f ? 90f : 120f;
                    Call("SetTravel", new Vector2(facing * distance * p, -30f * p), p);
                }
                else
                {
                    if (!returning) { Call("BeginReturn"); returning = true; }
                    float p = Math.Min(1f, (time - peak) / 0.2f);
                    Call("ApplyReturn", p);
                }
                float flipTime = 0.06f + time;
                float throwTime = 0.02f + time;
                bool flipping = (bool)AccessTools.Property(type, "IsBackflipping").GetValue(pose)!;
                Require(flipping == (flipTime < 0.5f - 0.00001f), "Attack changed the flip duration.");
                Require((Field("_throwDuration") > 0f) == (throwTime < 0.334f - 0.00001f),
                    "Attack changed the throw duration.");
                if (flipping) Require(Math.Abs(Field("_flipElapsed") - flipTime * speed) < 0.00001f,
                    "Attack paused or restarted the flip clock.");
                float flipAngle = flipping ? -facing * Mathf.Tau * (1f - Mathf.Pow(1f - Math.Min(1f, flipTime / (0.88f * 0.5f)), 1.3f)) : 0f;
                float throwAngle = 0f;
                if (throwTime < 0.166f * 0.25f)
                    throwAngle = -Mathf.DegToRad(6f) * facing * Mathf.SmoothStep(0f, 1f, throwTime / (0.166f * 0.25f));
                else if (throwTime < 0.166f)
                    throwAngle = Mathf.DegToRad(Mathf.Lerp(-6f, 8f,
                        Mathf.SmoothStep(0f, 1f, Mathf.Clamp((throwTime / 0.166f - 0.25f) / 0.45f, 0f, 1f)))) * facing;
                else if (throwTime < 0.334f)
                    throwAngle = Mathf.DegToRad(8f) * facing * (1f - Mathf.SmoothStep(0f, 1f, (throwTime - 0.166f) / 0.168f));
                float expected = unposedAngle + Field("_baseDisplayAngle") + flipAngle + throwAngle;
                Require(Math.Abs(Mathf.Wrap(body.GetGlobalTransformWithCanvas().X.Angle() - expected, -Mathf.Pi, Mathf.Pi)) < 0.002f,
                    "Attack, flip and throw rotations did not compose on the actual sprite.");
                Require((body.GetGlobalTransformWithCanvas() * corePoint).DistanceTo(center.GlobalPosition) < 0.1f,
                    "Triple motion detached the hit center.");
                object?[] handArgs = [actor, null];
                Require((bool)AccessTools.Method(orbVisual, "TryGetHandCanvasPosition").Invoke(null, handArgs)!
                    && ((Vector2)handArgs[1]!).DistanceTo(body.GetGlobalTransformWithCanvas() * handPoint) < 0.1f,
                    "Triple motion detached the shuriken release point.");
            }
            Transform2D restored = body.GetGlobalTransformWithCanvas();
            Require(restored.Origin.DistanceTo(idle.Origin) < 0.001f
                && restored.X.IsEqualApprox(idle.X) && restored.Y.IsEqualApprox(idle.Y),
                $"Triple motion baseline: speed={speed}, facing={facing}, peak={peak}, expected={idle}, actual={body.GetGlobalTransformWithCanvas()}, pose={pose.Transform}.");
        }
        Call("Reset");
        anchor.Transform = anchorBaseline;
        actor.Position = actorBaseline;
        target.Position = targetBaseline;
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
        Call("SyncNow");
    }

    private static async Task VerifyNativeDrawBatches(OrbCombat combat, Node2D pose)
    {
        SaveManager.Instance.InitSettingsDataForTest();
        MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
        Type poseType = pose.GetType();
        var harmony = new Harmony("NinjaSlayer.OrbContracts.DrawBatches");
        var product = typeof(ShurikenOrb).Assembly;
        foreach (string name in new[] { "NinjaSlayerDrawBatchPatch", "NinjaSlayerDrawBackflipPatch" })
        {
            Type patch = product.GetType("NinjaSlayer.Code.Patches." + name, true)!;
            MethodInfo target = name.Contains("Batch", StringComparison.Ordinal)
                ? AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.Draw), [typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool)])
                : AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.Add), [typeof(CardModel), typeof(CardPile), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)]);
            harmony.Patch(target, prefix: new HarmonyMethod(AccessTools.Method(patch, "Prefix")),
                postfix: AccessTools.Method(patch, "Postfix") is { } post ? new HarmonyMethod(post) : null,
                finalizer: AccessTools.Method(patch, "Finalizer") is { } final ? new HarmonyMethod(final) : null);
        }
        harmony.Patch(AccessTools.Method(poseType, "BeginBackflip"),
            prefix: new HarmonyMethod(typeof(OrbContractRunner), nameof(CountDrawFlip)));
        PlayerTurnPhase phase = combat.Player.PlayerCombatState!.Phase;
        void ResetBatch()
        {
            foreach (var pile in combat.Player.Piles) pile.Clear(silent: true);
            AccessTools.Method(poseType, "Reset").Invoke(pose, null);
            _drawFlipCalls = 0;
        }
        try
        {
            foreach ((PlayerTurnPhase drawPhase, bool handDraw, int amount, int expected) in new[]
            {
                (PlayerTurnPhase.Play, false, 3, 1), (PlayerTurnPhase.Start, false, 3, 0),
                (PlayerTurnPhase.Play, true, 3, 0), (PlayerTurnPhase.None, false, 3, 0),
                (PlayerTurnPhase.Play, false, 0, 0)
            })
            {
                ResetBatch();
                combat.Player.PlayerCombatState.Phase = drawPhase;
                for (int i = 0; i < 3; i++) AddCard<StrikeIronclad>(combat, PileType.Draw);
                var drawn = (await CardPileCmd.Draw(Choice, amount, combat.Player, handDraw)).ToArray();
                Require(drawn.Length == amount && _drawFlipCalls == expected, "Native draw batch changed its count or animation eligibility.");
            }
            ResetBatch();
            combat.Player.PlayerCombatState.Phase = PlayerTurnPhase.Play;
            for (int i = 0; i < CardPile.MaxCardsInHand; i++) combat.Card();
            AddCard<StrikeIronclad>(combat, PileType.Draw);
            Require(!(await CardPileCmd.Draw(Choice, 1, combat.Player)).Any() && _drawFlipCalls == 0, "Full-hand draw played a flip.");
            ResetBatch();
            await PowerCmd.Apply<StatusDrawPower>(Choice, combat.Player.Creature, 2, combat.Player.Creature, null);
            AddCard<Wound>(combat, PileType.Draw);
            AddCard<StrikeIronclad>(combat, PileType.Draw);
            AddCard<StrikeIronclad>(combat, PileType.Draw);
            await CardPileCmd.Draw(Choice, 1, combat.Player);
            Require(PileType.Hand.GetPile(combat.Player).Cards.Count == 3 && _drawFlipCalls == 1,
                "Nested status-triggered draws were not grouped with the outer batch.");
            await PowerCmd.Remove(combat.Player.Creature.GetPower<StatusDrawPower>()!);
            ResetBatch();
            Type batches = product.GetType("NinjaSlayer.Code.Lifecycle.NinjaSlayerDrawAnimationBatch", true)!;
            using ((IDisposable)AccessTools.Method(batches, "Enter").Invoke(null, [combat.Player, false])!)
                for (int i = 0; i < 3; i++)
                {
                    AddCard<StrikeIronclad>(combat, PileType.Draw);
                    await CardPileCmd.Draw(Choice, combat.Player);
                }
            Require(_drawFlipCalls == 1, "Explicit single-draw loop played more than one flip.");
            AddCard<StrikeIronclad>(combat, PileType.Draw);
            await CardPileCmd.Draw(Choice, combat.Player);
            Require(_drawFlipCalls == 2, "Draw batch context escaped into the next independent draw.");
            GD.Print("PASS native draw batches: counts, turn phase, initial/full/zero draws, nested effects, explicit loops and scope cleanup.");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            ResetBatch();
            combat.Player.PlayerCombatState.Phase = phase;
        }
    }

    private static void CountDrawFlip() => _drawFlipCalls++;
}
