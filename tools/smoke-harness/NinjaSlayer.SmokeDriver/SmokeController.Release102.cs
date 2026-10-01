using System.Reflection;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.AutoSlay;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using NinjaSlayer.Content;
using NinjaSlayer.Code.Feedback;
using STS2RitsuLib.Data;

namespace NinjaSlayer.SmokeDriver;

internal sealed partial class SmokeController
{
    private async Task RunRelease102Async()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        Type dataType = typeof(NinjaSlayerCharacter).Assembly.GetType("NinjaSlayer.Code.Feedback.FirstVictoryFeedbackData", true)!;
        var store = ModDataStore.For("NinjaSlayer");
        MethodInfo get = store.GetType().GetMethods().Single(method => method.Name == "Get"
            && method.IsGenericMethodDefinition && method.GetParameters().Length == 1).MakeGenericMethod(dataType);
        object data = get.Invoke(store, ["first_victory_feedback"])!;
        PropertyInfo shown = dataType.GetProperty("Shown")!;
        var stats = SaveManager.Instance.Progress.GetOrCreateCharacterStats(ModelDb.Character<NinjaSlayerCharacter>().Id);
        Func<bool> auto = NonInteractiveMode.AutoSlayerCheck;
        NonInteractiveMode.AutoSlayerCheck = static () => false;
        try
        {
            foreach (var scenario in new[] { "first", "repeat", "veteran", "custom", "other" })
            {
                if (scenario == "first") { stats.TotalWins = 0; shown.SetValue(data, false); }
                if (scenario == "veteran") { stats.TotalWins = 4; shown.SetValue(data, false); }
                if (scenario is "custom" or "other") { stats.TotalWins = 0; shown.SetValue(data, false); }
                await NGame.Instance!.StartNewSingleplayerRun(scenario == "other" ? ModelDb.Character<Ironclad>() : ModelDb.Character<NinjaSlayerCharacter>(),
                    true, ActModel.GetDefaultList(), [], _configuration.Seed + scenario, scenario == "custom" ? GameMode.Custom : GameMode.Standard, 0);
                await RunManager.Instance.EnterAct(0);
                await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<TheArchitect>());
                // The test uses native victory/progress/game-over, without recording a full run.
                await (Task)AccessTools.Method(typeof(RunManager), "WinRun").Invoke(RunManager.Instance, null)!;
                NGameOverScreen? ending = null;
                await WaitUntilAsync(() => (ending = FindDescendant<NGameOverScreen>(_tree.Root)) != null, "Native game-over screen was not created.");
                var feedback = NGame.Instance.GetOrCreateFeedbackScreen();
                if (scenario == "first")
                {
                    await WaitUntilAsync(() => feedback.Visible, "First standard victory did not invite feedback.");
                    Require(ending!.Visible && NinjaSlayerFeedbackSession.TryGetCurrentToken(feedback.GetInstanceId(), out _),
                        "First-win feedback must belong to the mod and display over completed native game-over.");
                    await WaitFrames(3);
                    Require((bool)shown.GetValue(data)!, "Showing the invitation must persist without sending.");
                    AccessTools.Method(typeof(NSendFeedbackScreen), "Close").Invoke(feedback, null);
                    RunManager.Instance.OnEnded(isVictory: true);
                    await WaitFrames(30);
                    Require(!feedback.Visible, "Repeated victory notification reopened dismissed feedback.");
                }
                else { await WaitFrames(240); Require(!feedback.Visible, scenario + " incorrectly prompted again."); }
                _checkpoints.Write("release102." + scenario, data: new JsonObject { ["wins"] = stats.TotalWins, ["shown"] = (bool)shown.GetValue(data)! });
                await NGame.Instance.ReturnToMainMenuAfterRun();
                await WaitFrames(30);
            }
        }
        finally { NonInteractiveMode.AutoSlayerCheck = auto; }
        _checkpoints.Write("release102.completed");
        NGame.Instance!.Quit();
    }
}
