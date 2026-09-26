using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Monsters;
using NinjaSlayer.Powers;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private sealed partial class TheaterRuntime
    {
        private async Task TheftRound(string mode)
        {
            Require(_script.Purpose == "theft", "Theft comparison requires its dedicated script.");
            bool fast = mode == "Fast";
            SaveManager.Instance.PrefsSave.FastMode = fast ? FastModeType.Fast : FastModeType.Normal;
            Creature dark = _combat.CreateCreature(ModelDb.Monster<DarkNinjaMonster>().ToMutable(), CombatSide.Enemy, null);
            Creature hopper = _combat.CreateCreature(ModelDb.Monster<ThievingHopper>().ToMutable(), CombatSide.Enemy, null);
            AccessTools.Property(typeof(DarkNinjaMonster), "HasPlayedBegin").SetValue(dark.Monster, true);
            AccessTools.Property(typeof(DarkNinjaMonster), "HasEnteredCombatStance").SetValue(dark.Monster, true);
            await CreatureCmd.Add(dark);
            await CreatureCmd.Add(hopper);
            string darkName = "dark-" + mode, hopperName = "hopper-" + mode;
            AddActor(darkName, dark); AddActor(hopperName, hopper);
            NCreature darkNode = Node(darkName), nativeNode = Node(hopperName), ninja = Node("ninja");
            Node("sawatari").Hide();
            Vector2 originalPlayer = ninja.Position;
            darkNode.Position = _enemySlot;
            nativeNode.Position = new((originalPlayer.X + _enemySlot.X) * .5f, _enemySlot.Y);
            await RemovePower<IaiPower>(dark);
            await RemovePower<EvasionPower>(dark);
            await RemovePower<EvasionPower>(_player.Creature);
            await RemoveSmokeBlock(_player.Creature);
            await CreatureCmd.Heal(_player.Creature, _player.Creature.MaxHp);
            foreach (CardModel card in CardPile.GetCards(_player, PileType.Draw, PileType.Discard).ToArray())
                await CardPileCmd.RemoveFromCombat(card, skipVisuals: true);
            CardModel[] theftCards = [ModelDb.Card<KarateStraightRedesignV1>(), ModelDb.Card<DefendNinjaSlayerRedesignV1>(),
                ModelDb.Card<DragonFlyingKickRedesignV1>(), ModelDb.Card<HellTornadoRedesignV1>()];
            foreach (CardModel template in theftCards)
            {
                CardModel deck = _player.RunState.CreateCard(template, _player);
                await CardPileCmd.Add(deck, PileType.Deck, skipVisuals: true);
                CardModel copy = _combat.CloneCard(deck);
                copy.DeckVersion = deck;
                await CardPileCmd.Add(copy, PileType.Draw, skipVisuals: true);
            }

            var frames = new JsonArray();
            var stabFrames = new JsonArray();
            var damageFrames = new List<double>();
            var events = new JsonArray();
            int previousHp = _player.Creature.CurrentHp;
            var samples = new Dictionary<ulong, List<(Vector2 Position, float Scale, float Time, bool Death)>>();
            Type motionType = ProductType("NinjaSlayer.Code.Nodes.NativeStolenCardMotion");
            T Field<T>(object value, string name) => (T)AccessTools.Field(value.GetType(), name).GetValue(value)!;
            Node[] Motions() => _room.SceneContainer.GetChildren().Where(n => n.GetType() == motionType).ToArray();
            Task Strike() => fast
                ? Animation("DarkNinjaAttackExecution", "PlayDarkStrike", (DarkNinjaMonster)dark.Monster!,
                    new Creature[] { _player.Creature },
                    (int)AccessTools.Property(typeof(DarkNinjaMonster), "DarkStrikeDamage").GetValue(null)!, -1)
                : Move(new() { Actor = darkName, Target = "ninja", Move = DarkNinjaMonster.DarkStrikeMoveId });
            int EffectiveZ(CanvasItem item)
            {
                int z = item.ZIndex;
                while (item.ZAsRelative && item.GetParent() is CanvasItem parent)
                {
                    z += parent.ZIndex;
                    item = parent;
                }
                return z;
            }
            void SampleCards()
            {
                int hp = _player.Creature.CurrentHp;
                if (hp < previousHp)
                {
                    damageFrames.Add(Seconds);
                    events.Add(new JsonObject { ["seconds"] = Seconds, ["kind"] = "damage", ["hp"] = hp });
                }
                previousHp = hp;
                Node2D? stab = _room.SceneContainer.GetNodeOrNull<Node2D>("DarkNinjaDarkStrike");
                Node2D[] effects = _room.CombatVfxContainer.GetChildren().OfType<Node2D>()
                    .Where(n => n.Name.ToString().StartsWith("DarkNinjaStabImpact", StringComparison.Ordinal)).ToArray();
                if (stab != null || effects.Length > 0)
                {
                    Node2D anchor = (Node2D?)InvokeMethod(ProductType("NinjaSlayer.Code.Nodes.NinjaSlayerVisualRig"),
                        null, "GetAirborneAnchor", ninja.Visuals) ?? ninja.Body;
                    int targetZ = EffectiveZ(anchor);
                    void Visit(Node node)
                    {
                        if (node is CanvasItem item && item.IsVisibleInTree()) targetZ = Math.Max(targetZ, EffectiveZ(item));
                        foreach (Node child in node.GetChildren()) Visit(child);
                    }
                    Visit(anchor);
                    Node2D? hand = stab?.GetNodeOrNull<Node2D>("Character/StolenCardPos");
                    if (hand != null)
                    {
                        foreach (Node2D grip in hand.GetChildren().Cast<Node2D>())
                            Require(EffectiveZ(grip) == EffectiveZ(hand) && EffectiveZ(grip) < targetZ,
                                "The held card stack must remain behind the victim at the native layer.");
                    }
                    int? effectZ = effects.Length > 0 ? effects.Min(EffectiveZ) : null;
                    stabFrames.Add(new JsonObject { ["seconds"] = Seconds,
                        ["frontBlade"] = stab?.GetNode<Sprite2D>("FrontSword").Visible ?? false,
                        ["returning"] = stab?.GetNode<Sprite2D>("FullBody").Visible ?? false,
                        ["targetZ"] = targetZ, ["effectZ"] = effectZ });
                }
                foreach (Node motion in Motions())
                {
                    var driver = Field<NCreatureVisuals>(motion, "_driver");
                    var marker = Field<Marker2D>(motion, "_marker");
                    var card = Field<Node2D>(motion, "_card");
                    if (!GodotObject.IsInstanceValid(card)) continue;
                    float time = Field<float>(motion, "_sampledTime");
                    bool death = Field<bool>(motion, "_death");
                    Vector2 point = driver.ToLocal(marker.GlobalPosition);
                    float scale = marker.GlobalTransform.X.Length();
                    NCard face = card.GetChild<NCard>(0);
                    Vector2 expectedCenter = Field<Vector2>(motion, "_displayCenter");
                    float centerError = face.GetGlobalTransform().Origin.DistanceTo(expectedCenter);
                    var follow = Field<Func<Transform2D>?>(motion, "_follow");
                    Vector2 handCenter = follow?.Invoke() * face.Position ?? Vector2.Zero;
                    ulong id = motion.GetInstanceId();
                    if (!samples.TryGetValue(id, out var track)) samples[id] = track = [];
                    track.Add((point, scale, time, death));
                    frames.Add(new JsonObject { ["seconds"] = Seconds, ["id"] = id, ["time"] = time,
                        ["death"] = death, ["visible"] = card.Visible, ["nativeX"] = point.X,
                        ["nativeY"] = point.Y, ["nativeScale"] = scale,
                        ["x"] = card.GlobalPosition.X, ["y"] = card.GlobalPosition.Y,
                        ["centerX"] = face.GlobalPosition.X, ["centerY"] = face.GlobalPosition.Y,
                        ["handX"] = handCenter.X, ["mirrored"] = Field<bool>(motion, "_mirrored"),
                        ["centerError"] = centerError,
                        ["driverVisible"] = driver.IsVisibleInTree(), ["driverAlpha"] = driver.Modulate.A });
                }
            }
            RenderingServer.FramePostDraw += SampleCards;
            try
            {
                await Move(new() { Actor = hopperName, Target = "ninja", Move = "THIEVERY_MOVE" });
                await Wait(1.2);
                Marker2D nativeGrip = nativeNode.GetSpecialNode<Marker2D>("%StolenCardPos")
                    ?? throw new InvalidOperationException("Native Hopper has no card marker.");
                NCard nativeCard = nativeGrip.GetChildren().OfType<NCard>().Single();
                // Measure the authored held pose, not a random breathing frame.
                var nativeState = nativeNode.Visuals.SpineBody!.GetAnimationState();
                nativeState.SetAnimation("steal", false);
                var nativeTrack = nativeState.GetCurrent(0)!;
                nativeTrack.SetMixDuration(0f);
                nativeTrack.SetTrackTime(nativeTrack.GetAnimationDuration());
                nativeNode.Body.Call("update_skeleton", 0f);
                Vector2 referenceSize = CardSize(nativeCard);
                nativeState.SetAnimation("idle_loop", true);
                nativeState.GetCurrent(0)!.SetMixDuration(0f);
                Require(referenceSize.X is > 75f and < 95f && referenceSize.Y is > 105f and < 130f,
                    $"Unexpected native held card size: {referenceSize}.");
                Cover("native-theft-" + mode);
                if (fast)
                {
                    ninja.Position = new(_enemySlot.X, originalPlayer.Y);
                    darkNode.Position = originalPlayer;
                    darkNode.Body.Scale = new(-Math.Abs(darkNode.Body.Scale.X), darkNode.Body.Scale.Y);
                    Call(Pose, "FaceForAction", true, true);
                }
                for (int theft = 0; theft < 2; theft++)
                {
                    CardModel[] heldBefore = dark.Powers.OfType<SwipePower>().Select(p => p.StolenCard!).ToArray();
                    double started = Seconds;
                    int damageCount = damageFrames.Count;
                    await Strike();
                    double recovered = Seconds;
                    await Wait(1.25);
                    var theftFrames = frames.Cast<JsonObject>().Where(f => !f["death"]!.GetValue<bool>()
                        && f["seconds"]!.GetValue<double>() >= started).ToArray();
                    Require(theftFrames.Length > 2 && damageFrames.Count == damageCount + 1,
                        "A successful Dark Strike must produce one hit and one card flight.");
                    double first = theftFrames[0]["seconds"]!.GetValue<double>();
                    Require(Math.Abs(first - damageFrames[damageCount]) < .04,
                        "Theft did not start on the actual damage frame.");
                    Require(first < recovered - .3, "Theft waited until Dark Ninja had returned.");
                    JsonObject[] windup = stabFrames.Cast<JsonObject>().Where(f => f["seconds"]!.GetValue<double>() >= started
                        && f["seconds"]!.GetValue<double>() < damageFrames[damageCount]).ToArray();
                    Require(windup.Length > 0 && windup[0]["frontBlade"]!.GetValue<bool>(),
                        "The blade should already penetrate on the first unblocked windup frame.");
                    float phase = fast ? .725f : .85f;
                    Require(Math.Abs(theftFrames[0]["time"]!.GetValue<float>() - phase) < .1f,
                        "Theft restarted the native pre-hit wait.");
                    JsonObject At(float t) => theftFrames.MinBy(f => Math.Abs(f["time"]!.GetValue<float>() - t))!;
                    int side = fast ? -1 : 1;
                    Require((At(1.55f)["centerX"]!.GetValue<float>() - At(1.1f)["centerX"]!.GetValue<float>()) * side > 80f,
                        "The stolen card must follow Hopper's native flight toward the thief's side.");
                    float viewportWidth = _room.GetViewportRect().Size.X;
                    foreach (JsonObject frame in theftFrames.Where(f => f["time"]!.GetValue<float>() < 50f / 30f
                        && f["nativeScale"]!.GetValue<float>() > .05f))
                        Require(frame["centerX"]!.GetValue<float>() is var x && x > 0f && x < viewportWidth,
                            "The native stolen-card flight left the visible play area.");
                    events.Add(new JsonObject { ["kind"] = "theft", ["hit"] = damageFrames[damageCount],
                        ["flight"] = first, ["recovered"] = recovered, ["returnSide"] = side });
                    Require(dark.Powers.OfType<SwipePower>().Count() == theft + 1,
                        "Dark Strike did not steal once per successful hit.");
                    Node2D hand = darkNode.FindChildren("StolenCardPos", "Node2D", true, false).Cast<Node2D>().Single();
                    CardModel newest = dark.Powers.OfType<SwipePower>().Select(p => p.StolenCard!)
                        .Except(heldBefore).Single();
                    Node2D topGrip = hand.GetChildren().Cast<Node2D>().MaxBy(n => n.GetIndex())!;
                    Require(topGrip.GetChild<NCard>(0).Model == newest,
                        "The newly stolen card must be above all previously held cards.");
                    foreach (Node2D grip in hand.GetChildren().Cast<Node2D>())
                    {
                        NCard card = grip.GetChildren().OfType<NCard>().Single();
                        Require(CardSize(card).DistanceTo(referenceSize) < .5f,
                            $"Dark Ninja/native held size differs: {CardSize(card)} / {referenceSize}.");
                        Vector2 top = card.GetGlobalTransform() * new Vector2(0, -NCard.defaultSize.Y * .5f);
                        float overlap = (top - grip.GlobalPosition).Dot(card.GetGlobalTransform().Y.Normalized());
                        Require(Math.Abs(overlap + 8f) < .5f, "Held card is not aligned at its upper edge.");
                        Require(!hand.ShowBehindParent, "Held cards must stay above the character.");
                    }
                    _driver._tree.Root.GetTexture().GetImage().SavePng(Path.Combine(_directory, $"grip-{mode}-{theft + 1}.png"));
                    Cover("dark-theft-" + mode);
                }
                if (fast)
                {
                    Task strike = Strike();
                    await _driver.WaitUntilAsync(() => Motions().Any(n => !Field<bool>(n, "_death")
                        && Field<float>(n, "_elapsed") >= 1.05f), "Third theft did not animate", _cancel);
                    Node flight = Motions().First(n => !Field<bool>(n, "_death"));
                    float time = Field<float>(flight, "_elapsed");
                    _driver._tree.Paused = true;
                    try
                    {
                        await Task.Delay(120);
                        Require(Field<float>(flight, "_elapsed") == time, "Theft advanced while paused.");
                    }
                    finally { _driver._tree.Paused = false; }
                    await CreatureCmd.Kill(dark, force: true);
                    await strike;
                    Cover("death-during-theft");
                }
                else
                {
                    int held = dark.Powers.OfType<SwipePower>().Count();
                    int flights = samples.Count;
                    int hp = _player.Creature.CurrentHp;
                    await CreatureCmd.GainBlock(_player.Creature, 100m, ValueProp.Unpowered, null);
                    double blockedStart = Seconds;
                    await Strike();
                    Require(_player.Creature.CurrentHp == hp, "The blocked strike unexpectedly lost HP.");
                    Require(dark.Powers.OfType<SwipePower>().Count() == held && samples.Count == flights,
                        "A fully blocked Dark Strike started theft.");
                    Require(!stabFrames.Cast<JsonObject>().Where(f => f["seconds"]!.GetValue<double>() >= blockedStart)
                        .Any(f => f["frontBlade"]!.GetValue<bool>()), "A fully blocked stab penetrated the victim.");
                    await RemoveSmokeBlock(_player.Creature);
                    await PowerCmd.Apply<EvasionPower>(_choice, _player.Creature, 1, _player.Creature, null);
                    await Strike();
                    Require(_player.Creature.CurrentHp == hp, "The evaded strike unexpectedly lost HP.");
                    Require(dark.Powers.OfType<SwipePower>().Count() == held && samples.Count == flights,
                        "An evaded Dark Strike started theft.");
                    await RemovePower<EvasionPower>(_player.Creature);
                    foreach (CardModel card in CardPile.GetCards(_player, PileType.Draw, PileType.Discard).ToArray())
                        await CardPileCmd.RemoveFromCombat(card, skipVisuals: true);
                    await Strike();
                    Require(_player.Creature.CurrentHp < hp, "The empty-pile strike did not hit.");
                    Require(dark.Powers.OfType<SwipePower>().Count() == held && samples.Count == flights,
                        "Dark Strike without a stealable card created a card flight.");
                    Cover("failed-theft-cleanup");
                    await CreatureCmd.Kill(dark, force: true);
                }
                await CreatureCmd.Kill(hopper, force: true);
                await Wait(2.8);
                foreach (var track in samples.Values)
                {
                    Require(track.Count >= 3, "Insufficient actual card-motion samples.");
                    Require(track.Max(p => p.Position.DistanceTo(track[0].Position)) > 25f,
                        "Native card bone stayed still during its animation.");
                    Require(track.Max(p => p.Scale) - track.Min(p => p.Scale) > .05f,
                        "Native card scale timeline did not play.");
                }
                Require(Motions().Length == 0, "Card drivers survived their animation.");
                JsonObject[] impacts = stabFrames.Cast<JsonObject>().Where(f => f["effectZ"] != null).ToArray();
                Require(impacts.Length > 0 && impacts.All(f => f["effectZ"]!.GetValue<int>() > f["targetZ"]!.GetValue<int>()),
                    "Dark Strike impact particles were behind the victim.");
                foreach (JsonObject frame in frames.Cast<JsonObject>())
                {
                    Require(frame["driverVisible"]!.GetValue<bool>() && frame["driverAlpha"]!.GetValue<float>() == 0f,
                        "The native driver must update without drawing its artwork.");
                    if (frame["visible"]!.GetValue<bool>())
                        Require(frame["centerError"]!.GetValue<float>() <= .5f,
                            "Changing the grip pivot changed the native card-center trajectory.");
                }
                Cover("native-drop-" + mode);
            }
            finally
            {
                RenderingServer.FramePostDraw -= SampleCards;
                File.WriteAllText(Path.Combine(_directory, "stolen-cards-" + mode + ".json"), frames.ToJsonString());
                File.WriteAllText(Path.Combine(_directory, "stolen-card-events-" + mode + ".json"), events.ToJsonString());
                File.WriteAllText(Path.Combine(_directory, "dark-strike-layers-" + mode + ".json"), stabFrames.ToJsonString());
                ninja.Position = originalPlayer;
                Call(Pose, "FaceForAction", false, true);
                SaveManager.Instance.PrefsSave.FastMode = FastModeType.Fast;
            }
        }

        private static Vector2 CardSize(NCard card)
        {
            Transform2D transform = card.GetGlobalTransform();
            return NCard.defaultSize * new Vector2(transform.X.Length(), transform.Y.Length());
        }
    }
}
