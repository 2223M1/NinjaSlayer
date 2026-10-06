using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using NinjaSlayer.Content;
using NinjaSlayer.Scripts;
using STS2RitsuLib;
using STS2RitsuLib.Data;
using STS2RitsuLib.Utils.Persistence;

namespace NinjaSlayer.Code.Feedback;

internal sealed class FirstVictoryFeedbackData
{
    public bool Shown { get; set; }
}

internal static class FirstVictoryFeedback
{
    private const string DataKey = "first_victory_feedback";
    private static RunState? _pendingRun;
    private static int _pendingProfile;

    internal static void RegisterData() => ModDataStore.For(NinjaSlayerIds.ModId)
        .Register<FirstVictoryFeedbackData>(DataKey, "first-victory-feedback.json", SaveScope.Profile,
            syncToCloud: false, defaultFactory: static () => new FirstVictoryFeedbackData(), autoCreateIfMissing: true);

    internal static void RegisterLifecycle()
    {
        RitsuLibFramework.SubscribeLifecycle<RunStartedEvent>(_ => _pendingRun = null);
        RitsuLibFramework.SubscribeLifecycle<RunLoadedEvent>(_ => _pendingRun = null);
        RitsuLibFramework.SubscribeLifecycle<MainMenuReadyEvent>(_ => _pendingRun = null);
        RitsuLibFramework.SubscribeLifecycle<RunEndedEvent>(OnRunEnded);
    }

    private static void OnRunEnded(RunEndedEvent evt)
    {
        if (!evt.IsVictory || evt.IsAbandoned || evt.Run.GameMode != GameMode.Standard
            || LocalContext.GetMe(evt.Run)?.CharacterId != ModelDb.Character<NinjaSlayerCharacter>().Id
            || !RunManager.Instance.ShouldSave) return;
        // OnEnded has already saved native progress. Only 0 -> 1 standard wins qualifies;
        // a veteran's next victory is >= 2 and never schedules this invitation.
        if (SaveManager.Instance.Progress.GetStatsForCharacter(ModelDb.Character<NinjaSlayerCharacter>().Id)?.TotalWins != 1
            || ModDataStore.For(NinjaSlayerIds.ModId).Get<FirstVictoryFeedbackData>(DataKey).Shown) return;
        _pendingRun = RunManager.Instance.DebugOnlyGetState()
            ?? throw new InvalidOperationException("Victory was published without an active run.");
        _pendingProfile = SaveManager.Instance.CurrentProfileId;
    }

    internal static async Task AfterGameOverAnimation(Task animation, NGameOverScreen screen)
    {
        await animation;
        if (_pendingRun == null) return;
        RunState run = _pendingRun;
        _pendingRun = null;
        if (!GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree() || !screen.Visible
            || !ReferenceEquals(run, RunManager.Instance.DebugOnlyGetState())
            || _pendingProfile != SaveManager.Instance.CurrentProfileId) return;
        bool opened = await NinjaSlayerFeedbackScreen.OpenAsync();
        if (NGame.Instance is null || !ReferenceEquals(run, RunManager.Instance.DebugOnlyGetState())
            || _pendingProfile != SaveManager.Instance.CurrentProfileId) return;
        if (opened && NinjaSlayerFeedbackScreen.Instance is { Visible: true })
        {
            var store = ModDataStore.For(NinjaSlayerIds.ModId);
            store.Get<FirstVictoryFeedbackData>(DataKey).Shown = true;
            store.Save(DataKey);
        }
    }
}
