using System.Collections;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.AutoSlay;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private readonly TaskCompletionSource _modCompatibilityCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private async Task RunModCompatibilityPhaseAsync()
    {
        NGame.Instance!.DebugSeedOverride = _configuration.Seed;
        SaveManager.Instance.SetFtuesEnabled(false);
        new AutoSlayer().Start(_configuration.Seed, _configuration.AutoSlayLogPath);
        await WaitTaskAsync(_modCompatibilityCompleted.Task, "mod compatibility scenarios did not complete", TimeSpan.FromMinutes(4));
        _checkpoints.Write("compatibility.completed");
        _tree.Quit(0);
    }

    private async Task ExecuteModCompatibilityAsync(CancellationToken cancellationToken)
    {
        try
        {
            await WaitUntilAsync(() => CombatManager.Instance.IsInProgress, "combat did not start", cancellationToken);
            var run = RunManager.Instance.DebugOnlyGetState()!;
            Player player = LocalContext.GetMe(run)!;
            await WaitUntilAsync(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play, "hand draw did not finish", cancellationToken);
            var combat = CombatManager.Instance.DebugOnlyGetState()!;
            var playerNode = NCombatRoom.Instance!.GetCreatureNode(player.Creature)!;

            Type cursor = AccessTools.TypeByName("CookieCursor.Core");
            var cache = (IDictionary)AccessTools.Field(cursor, "RotatedCookieCache").GetValue(null)!;
            string character = player.Character.GetType().Name.ToLowerInvariant();
            Require(cache.Contains(character), "NinjaSlayer was missing from CookieCursor's real image cache.");
            string icon = Path.Combine((string)AccessTools.Field(cursor, "CustomCursorsDir").GetValue(null)!, character, "icon.png");
            byte[] customIcon = File.ReadAllBytes(icon);
            AccessTools.Method(cursor, "CacheCharCookies").Invoke(null, null);
            Require(customIcon.SequenceEqual(File.ReadAllBytes(icon)), "Refreshing cursors overwrote an existing custom image.");
            _checkpoints.Write("compatibility.cursor", data: new JsonObject { ["character"] = character, ["iconSha256"] = Convert.ToHexString(SHA256.HashData(customIcon)) });

            int maxHp = player.Creature.MaxHp;
            Creature enemy = combat.HittableEnemies.First();
            Task motion = AlabamaDropAnimation.Play(player.Creature, enemy, () => Task.CompletedTask);
            await WaitFrames(4);
            player.Creature.SetMaxHpInternal(maxHp + 80);
            await motion;
            await Task.Delay(1500, cancellationToken);
            float expected = (float)AccessTools.Method(AccessTools.TypeByName("MaxHpSizeMod.MaxHpSizeModCode.SizeUpdater"), "CalculateScale").Invoke(null, [player.Creature])!;
            Vector2 expectedVisualScale = Vector2.One * expected * playerNode.Visuals.DefaultScale;
            Require(playerNode.Visuals.Scale.IsEqualApprox(expectedVisualScale),
                $"Alabama cleanup overwrote the external visual scale: actual {playerNode.Visuals.Scale}, expected {expectedVisualScale}.");
            _checkpoints.Write("compatibility.external-scale", data: new JsonObject { ["expected"] = expectedVisualScale.X, ["actualX"] = playerNode.Visuals.Scale.X });
            player.Creature.SetMaxHpInternal(maxHp);

            Type minty = AccessTools.TypeByName("MintySpire2.MintySpire2Code.combat.SummedIncomingDamageRender");
            var incoming = AccessTools.Method(minty, "CalculateIncomingDamage");
            int before = (int)incoming.Invoke(null, [player.Creature])!;
            Creature pet = await PlayerCmd.AddPet<YamotoKokiMonster>(player);
            int after = (int)incoming.Invoke(null, [player.Creature])!;
            Require(before == after && pet.Side == CombatSide.Player && run.Players.Count == 1,
                "A companion changed Minty's predicted enemy damage or the real player count.");
            _checkpoints.Write("compatibility.minty-pet", data: new JsonObject { ["before"] = before, ["after"] = after });

            Type removal = AccessTools.TypeByName("RemovalCostViewer.RemovalCostState");
            int uses = player.ExtraFields.CardShopRemovalsUsed;
            for (int n = 0; n < 3; n++)
            {
                player.ExtraFields.CardShopRemovalsUsed = n;
                var entry = new MerchantCardRemovalEntry(player);
                string text = (string)AccessTools.Method(removal, "GetDisplayText").Invoke(null, null)!;
                Require(text == entry.Cost.ToString(), "RemovalCostViewer diverged from the native merchant price.");
            }
            player.ExtraFields.CardShopRemovalsUsed = uses;
            _checkpoints.Write("compatibility.removal-price");

            // Exercise the released movie and arrow in the rendered game, without capturing it.
            Type popupType = typeof(YukanoMonster).Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.YukanoArrowPopup", true)!;
            object popupData = AccessTools.Field(popupType, "_runData").GetValue(null)!;
            bool PopupShown(RunState state)
            {
                object value = AccessTools.Method(popupData.GetType(), "Get").Invoke(popupData, [state])!;
                return (bool)AccessTools.Property(value.GetType(), "Shown").GetValue(value)!;
            }
            Require(!PopupShown(run), "Popup fixture must start with a fresh run.");
            await RelicCmd.Obtain<NinjaSlayer.Relics.YukanoCompanionRelic>(player);
            Creature archer = await PlayerCmd.AddPet<YukanoMonster>(player);
            var arrow = (MoveState)archer.Monster!.MoveStateMachine!.States[YukanoMonster.ArrowMoveId];
            enemy.SetMaxHpInternal(500);
            await CreatureCmd.SetCurrentHp(enemy, 500);
            var oldSpeed = SaveManager.Instance.PrefsSave.FastMode;
            var oldAutoSlayer = NonInteractiveMode.AutoSlayerCheck;
            SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
            NonInteractiveMode.AutoSlayerCheck = static () => false;
            try
            {
                Task attack = arrow.PerformMove([enemy]);
                Node? popup = null;
                await WaitUntilAsync(() => (popup = NCombatRoom.Instance!.GetNodeOrNull<Node>("YukanoArrowPopup")) != null,
                    "Yukano's movie did not start", cancellationToken);
                await WaitUntilAsync(() => (double)AccessTools.Property(popupType, "ReleasePosition").GetValue(popup)! >= 0,
                    "Yukano's movie did not release the real arrow", cancellationToken);
                double releasePosition = (double)AccessTools.Property(popupType, "ReleasePosition").GetValue(popup)!;
                Require(releasePosition >= 1.001 && enemy.CurrentHp == 500,
                    "Arrow damage preceded the movie's release frame or projectile flight.");
                await attack;
                Require(enemy.CurrentHp < 500 && PopupShown(run), "Yukano arrow did not deal damage and consume its run flag.");
                await WaitUntilAsync(() => NCombatRoom.Instance!.GetNodeOrNull<Node>("YukanoArrowPopup") == null,
                    "Yukano movie did not clean up", cancellationToken);
                _checkpoints.Write("compatibility.yukano-popup", data: new JsonObject
                {
                    ["releasePosition"] = releasePosition, ["damage"] = 500 - enemy.CurrentHp,
                    ["shown"] = true
                });
            }
            finally
            {
                SaveManager.Instance.PrefsSave.FastMode = oldSpeed;
                NonInteractiveMode.AutoSlayerCheck = oldAutoSlayer;
            }

            Type seeds = typeof(NinjaSlayer.Content.NinjaSlayerRunData).Assembly.GetType("NinjaSlayer.Content.SingleplayerSeedRules", true)!;
            object seedData = AccessTools.Property(seeds, "Data").GetValue(null)!;
            object seedState = AccessTools.Method(seedData.GetType(), "Get").Invoke(seedData, [run])!;
            AccessTools.Property(seedState.GetType(), "PotionDropCalls").SetValue(seedState, 17);
            AccessTools.Method(seedData.GetType(), "Set").Invoke(seedData, [run, seedState]);
            await SaveManager.Instance.SaveRun(null);
            string seed = run.Rng.StringSeed;
            Type quick = AccessTools.TypeByName("QuickSlAndRerollStart.QuickRestartService");
            await (Task)AccessTools.Method(quick, "QuickLoadAsync").Invoke(null, [RunManager.Instance])!;
            run = RunManager.Instance.DebugOnlyGetState()!;
            seedState = AccessTools.Method(seedData.GetType(), "Get").Invoke(seedData, [run])!;
            Require(run.Rng.StringSeed == seed && (int)AccessTools.Property(seedState.GetType(), "PotionDropCalls").GetValue(seedState)! == 17,
                "Better Menu quick load changed the seed or CFC counter.");
            Require(PopupShown(run), "Better Menu quick load lost the consumed Yukano popup flag.");
            foreach (bool sameSeed in new[] { true, false })
            {
                Task restart = (Task)AccessTools.Method(quick, "RestartCurrentRunAsync").Invoke(null, [RunManager.Instance, sameSeed])!;
                while (!restart.IsCompleted)
                {
                    if (NOverlayStack.Instance?.Peek() is Control screen
                        && screen.GetType().FullName == "HextechRunes.HextechRuneSelectionScreen")
                        await SmokeHextechSelection.ChooseAsync(screen, cancellationToken);
                    else
                        await WaitFrames(1);
                }
                await restart;
                run = RunManager.Instance.DebugOnlyGetState()!;
                seedState = AccessTools.Method(seedData.GetType(), "Get").Invoke(seedData, [run])!;
                Require((run.Rng.StringSeed == seed) == sameSeed
                    && (int)AccessTools.Property(seedState.GetType(), "PotionDropCalls").GetValue(seedState)! == 0,
                    "Better Menu restart retained an old CFC counter or used the wrong seed.");
                Require(!PopupShown(run), "Restarting a run retained the previous Yukano popup flag.");
            }
            player = LocalContext.GetMe(run)!;
            _checkpoints.Write("compatibility.better-menu");

            // Debug room entry is only fixture setup. Dialogue, throw, Continue and victory
            // below use the installed mods and native event commands without suppressing completion.
            await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<TheArchitect>());
            var room = NCombatRoom.Instance!;
            var model = ((EventRoom)run.CurrentRoom!).LocalMutableEvent;
            await WaitUntilAsync(() => model.CurrentOptions.Count > 0, "Architect options were not shown", cancellationToken);
            Require(room.GetNodeOrNull("NinjaSlayerArchitectExecution") == null, "Architect executed before Continue.");
            await model.CurrentOptions.Single().Chosen();
            var potion = ModelDb.Potion<FirePotion>().ToMutable();
            await PotionCmd.TryToProcure(potion, player);
            var holder = Descendants(_tree.Root).OfType<NPotionHolder>().Single(h => h.Potion?.Model == potion);
            Type throwing = AccessTools.TypeByName("ThrowPotionsAtArchitect.ArchitectPotionThrowService");
            Require((bool)AccessTools.Method(throwing, "ShouldOfferThrow").Invoke(null, [potion])!, "Architect greeting did not allow potions.");
            NCreature architect = room.CreatureNodes.Single(n => n.Entity.Monster is Architect);
            Func<bool> autoSlayerCheck = NonInteractiveMode.AutoSlayerCheck;
            NonInteractiveMode.AutoSlayerCheck = static () => false;
            try
            {
            Task flight = (Task)AccessTools.Method(throwing, "ThrowAndConsume").Invoke(null, [holder, potion, architect, holder.GlobalPosition])!;
            Require(!flight.IsCompleted, "Potion fixture had no in-flight interval.");
            int winsBefore = SaveManager.Instance.Progress.Wins;
            Task proceeding = model.CurrentOptions.Single().Chosen();
            await flight;
            await proceeding;
            Require(player.GetPotionSlotIndex(potion) < 0, "The thrown potion remained in inventory.");
            Require(SaveManager.Instance.Progress.Wins == winsBefore + 1, "Architect victory was missing or duplicated.");
            }
            finally { NonInteractiveMode.AutoSlayerCheck = autoSlayerCheck; }
            _checkpoints.Write("compatibility.architect");
            _modCompatibilityCompleted.TrySetResult();
        }
        catch (Exception error)
        {
            _modCompatibilityCompleted.TrySetException(error);
            throw;
        }
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            yield return child;
            foreach (Node nested in Descendants(child)) yield return nested;
        }
    }
}
