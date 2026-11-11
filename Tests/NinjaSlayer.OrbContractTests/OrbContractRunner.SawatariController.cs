using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Events;
using STS2RitsuLib;
using STS2RitsuLib.Patching.Core;

namespace NinjaSlayer.OrbContractTests;

// Omit textures/audio, retaining native option creation order, entrance timing,
// focus signals, GUI input, OnRelease and the event-room click boundary.
public partial class ControllerEventOption : NEventOptionButton
{
    public override void _Ready() => ConnectSignals();
    protected override void OnFocus() { }
    protected override void OnUnfocus() { }
    protected override void OnPress() { }
}

public partial class ControllerEventLayout : NCombatEventLayout
{
    public override void _EnterTree() { }
    public override void _Ready() => _optionsContainer = GetNode<VBoxContainer>("OptionsContainer");
    public override void _ExitTree() { }
}

public partial class ControllerMapScreen : NMapScreen
{
    // This fixture never initializes network map voting.
    public override void _Notification(int what) { }
}

public partial class OrbContractRunner
{
    private static readonly List<int> ControllerChoices = [];

    private static bool CreateControllerOption(EventModel eventModel, EventOption option, int index,
        ref NEventOptionButton __result)
    {
        __result = new ControllerEventOption
        {
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(400, 80)
        };
        AccessTools.Property(typeof(NEventOptionButton), nameof(NEventOptionButton.Event)).SetValue(__result, eventModel);
        AccessTools.Property(typeof(NEventOptionButton), nameof(NEventOptionButton.Option)).SetValue(__result, option);
        AccessTools.Property(typeof(NEventOptionButton), "Index").SetValue(__result, index);
        return false;
    }

    private static bool SkipControllerVoteVisuals() => false;
    private static bool CaptureControllerChoice(int index) { ControllerChoices.Add(index); return false; }

    private async Task VerifySawatariController()
    {
        using var fixture = new SawatariFixture();
        var patcher = RitsuLibFramework.CreatePatcher("NinjaSlayer.OrbContracts", "SawatariController");
        patcher.RegisterPatch<SawatariEventOptionReadyPatch>();
        Require(patcher.PatchAll(), "Sawatari controller patch must install on this exact host.");
        var harmony = new Harmony("NinjaSlayer.OrbContracts.ControllerVisuals");
        harmony.Patch(AccessTools.Method(typeof(NEventOptionButton), nameof(NEventOptionButton.Create)),
            prefix: new HarmonyMethod(GetType(), nameof(CreateControllerOption)));
        harmony.Patch(AccessTools.Method(typeof(NEventOptionButton), nameof(NEventOptionButton.RefreshVotes)),
            prefix: new HarmonyMethod(GetType(), nameof(SkipControllerVoteVisuals)));
        harmony.Patch(AccessTools.Method(typeof(NEventRoom), nameof(NEventRoom.OptionButtonClicked)),
            prefix: new HarmonyMethod(GetType(), nameof(CaptureControllerChoice)));

        var detached = new List<Node>();
        T Keep<T>(T node) where T : Node { detached.Add(node); return node; }
        void Property(object node, string name, object? value) =>
            AccessTools.Property(node.GetType(), name).SetValue(node, value);
        NSceneContainer Scene(Control child)
        {
            var scene = Keep(new NSceneContainer());
            Property(scene, nameof(NSceneContainer.CurrentScene), child);
            return scene;
        }

        var game = Keep(new NGame());
        var run = Keep(new NRun());
        var globalUi = Keep(new NGlobalUi());
        var input = Keep(new NInputManager());
        var controller = Keep(new NControllerManager());
        void SetController(bool enabled)
        {
#if NINJASLAYER_CHANNEL_STABLE
            Property(controller, nameof(NControllerManager.IsUsingController), enabled);
#else
            Property(controller, nameof(NControllerManager.InputType),
                enabled ? InputType.Controller : InputType.MouseAndKeyboard);
#endif
        }
        var eventRoom = Keep(new NEventRoom());
        var combat = Keep(new NCombatRoom());
        var combatUi = Keep(new NCombatUi());
        var hand = Keep(new NPlayerHand());
        var layout = new ControllerEventLayout();
        var handFocus = new Control { FocusMode = Control.FocusModeEnum.All };
        var options = new VBoxContainer { Name = "OptionsContainer", Size = new Vector2(400, 200) };
        layout.AddChild(options); options.Owner = layout; options.UniqueNameInOwner = true;
        foreach (string name in new[] { "EventDescription", "SharedEventLabel" })
        {
            var node = new Control { Name = name, MouseFilter = Control.MouseFilterEnum.Ignore };
            layout.AddChild(node); node.Owner = layout; node.UniqueNameInOwner = true;
        }
        Property(game, nameof(NGame.RootSceneContainer), Scene(run));
        Property(game, nameof(NGame.InputManager), input);
        Property(input, nameof(NInputManager.ControllerManager), controller);
        Property(run, nameof(NRun.GlobalUi), globalUi);
        var map = Keep(new ControllerMapScreen());
        Property(globalUi, nameof(NGlobalUi.MapScreen), map);
        Property(globalUi, nameof(NGlobalUi.CapstoneContainer), Keep(new NCapstoneContainer()));
        Property(globalUi, nameof(NGlobalUi.Overlays), Keep(new NOverlayStack()));
        AccessTools.Field(typeof(NRun), "_roomContainer").SetValue(run, Scene(eventRoom));
        AccessTools.Field(typeof(NEventRoom), "_eventContainer").SetValue(eventRoom, Scene(layout));
        AccessTools.Property(typeof(NCombatEventLayout), nameof(NCombatEventLayout.EmbeddedCombatRoom)).SetValue(layout, combat);
        Property(combat, nameof(NCombatRoom.Ui), combatUi);
        Property(combatUi, nameof(NCombatUi.Hand), hand);
        Property(hand, nameof(NPlayerHand.CardHolderContainer), handFocus);

        object? oldGame = NGame.Instance;
        FastModeType oldSpeed = SaveManager.Instance.PrefsSave.FastMode;
        Action onContextChanged = AccessTools.Method(typeof(NControllerManager), "OnScreenContextChanged")
            .CreateDelegate<Action>(controller);
        var uiType = typeof(TheMovingJungleEvent).Assembly.GetType("NinjaSlayer.Events.SawatariEventUi", true)!;
        void Show() => AccessTools.Method(uiType, "Show").Invoke(null, [false]);
        EventModel model = ModelDb.Event<TheMovingJungleEvent>().ToMutable();
        void Page(int count, bool legacy = false)
        {
            layout.ClearOptions();
            AccessTools.Property(typeof(EventModel), nameof(EventModel.Owner)).SetValue(model, fixture.Combat.Player);
            AccessTools.Field(typeof(NEventLayout), "_event").SetValue(layout, model);
            layout.AddOptions(Enumerable.Range(0, count).Select(i => new EventOption(model,
                () => Task.CompletedTask, new LocString("events", "CONTROLLER_TEST.title"),
                new LocString("events", "CONTROLLER_TEST.description"), $"CONTROLLER_TEST_{i}", [])));
            if (legacy) options.Visible = true;
            else Show();
        }
        async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        async Task Ready()
        {
            long deadline = System.Environment.TickCount64 + 5000;
            while (layout.OptionButtons.Any(button => button.MouseFilter != Control.MouseFilterEnum.Stop))
            {
                Require(System.Environment.TickCount64 < deadline, "Native event entrance did not finish.");
                await Frame();
            }
            await Frame(); await Frame();
        }
        void InputAction(StringName action, bool pressed) => GetViewport().PushInput(
            new InputEventAction { Action = action, Pressed = pressed });
        StringName[] addedActions = new[] { MegaInput.select, new StringName("debug_hide_event_ui") }
            .Where(action => !InputMap.HasAction(action)).ToArray();
        foreach (StringName action in addedActions) InputMap.AddAction(action);
        try
        {
            AccessTools.Property(typeof(NGame), nameof(NGame.Instance)).SetValue(null, game);
            SetController(true);
            AddChild(handFocus); AddChild(layout);
            // Give the native controller callback a viewport without starting Steam input.
            AddChild(controller); controller.SetProcess(false); controller.SetProcessInput(false);
            ActiveScreenContext.Instance.Updated += onContextChanged;

            SaveManager.Instance.PrefsSave.FastMode = FastModeType.Fast;
            layout.HideEventVisuals(); Page(2, legacy: true); await Ready();
            Require(ActiveScreenContext.Instance.GetCurrentScreen() == combat && layout.HasCombatStarted,
                "Baseline must reproduce visible options still belonging to the combat screen.");
            Require(!layout.OptionButtons.Any(button => button.HasFocus()),
                "Baseline unexpectedly focused an event option.");
            GD.Print("PASS reproduced 1.2.2: visible Sawatari choices retain the combat screen and lose controller focus.");

            foreach (FastModeType speed in Enum.GetValues<FastModeType>())
            foreach (int count in new[] { 2, 1 })
            {
                layout.HideEventVisuals();
                SaveManager.Instance.PrefsSave.FastMode = speed;
                Page(count); await Ready();
                var buttons = layout.OptionButtons.ToArray();
                Require(ActiveScreenContext.Instance.GetCurrentScreen() == eventRoom
                    && layout.DefaultFocusedControl == buttons[0] && buttons[0].HasFocus(),
                    $"{speed}/{count}: event context and first option must receive controller focus.");
                ControllerChoices.Clear();
                if (count == 2)
                {
                    InputAction("ui_down", true); InputAction("ui_down", false); await Frame();
                    Require(buttons[1].HasFocus(), "Native down navigation must select the duel option.");
                }
                InputAction(MegaInput.select, true); InputAction(MegaInput.select, false);
                Require(ControllerChoices.SequenceEqual(new[] { count - 1 }),
                    "Native controller confirm must reach the selected event option exactly once.");
                InputAction(MegaInput.select, false);
                Require(ControllerChoices.Count == 1, "A repeated release must not choose twice.");
                if (count == 2)
                {
                    InputAction("ui_up", true); InputAction("ui_up", false); await Frame();
                    Require(buttons[0].HasFocus(), "Native up navigation must return to the loot option.");
                    InputAction(MegaInput.select, true); InputAction(MegaInput.select, false);
                    Require(ControllerChoices.SequenceEqual(new[] { 1, 0 }), "Both intermission choices must be confirmable.");
                }

                SetController(false);
                GetViewport().GuiReleaseFocus();
                layout.OptionButtons.First().EnableButton(); await Frame();
                Require(GetViewport().GuiGetFocusOwner() == null, "Mouse mode must not grab controller focus.");
                SetController(true);
                ActiveScreenContext.Instance.FocusOnDefaultControl();
                Require(buttons[0].HasFocus(), "Returning to controller input must focus the event rather than combat.");
                layout.HideEventVisuals();
                Require(ActiveScreenContext.Instance.GetCurrentScreen() == combat && handFocus.HasFocus(),
                    "Hiding choices must restore native combat focus.");
                GD.Print($"PASS Sawatari controller {speed}, {count} options: entrance, navigation, confirm, mouse/controller switching, combat return.");
            }

            SaveManager.Instance.PrefsSave.FastMode = FastModeType.Fast;
            Page(2); layout.HideEventVisuals(); await Ready();
            Require(handFocus.HasFocus() && ActiveScreenContext.Instance.GetCurrentScreen() == combat,
                "An old entrance finishing after choices hide must not steal combat focus.");
            AccessTools.Property(typeof(NMapScreen), nameof(NMapScreen.IsOpen)).SetValue(map, true);
            Page(2); await Ready();
            Require(ActiveScreenContext.Instance.GetCurrentScreen() == map && handFocus.HasFocus(),
                "An entrance completing under the map must not steal its screen context or focus.");
            AccessTools.Property(typeof(NMapScreen), nameof(NMapScreen.IsOpen)).SetValue(map, false);
            ActiveScreenContext.Instance.Update(); await Frame(); await Frame();
            Require(layout.OptionButtons.First().HasFocus(), "Closing the map must return to event choices.");
            layout.HideEventVisuals();
            model = ModelDb.Event<FakeMerchant>().ToMutable();
            Page(1, legacy: true); await Ready();
            Require(layout.HasCombatStarted && ActiveScreenContext.Instance.GetCurrentScreen() == combat,
                "Other events must retain their original combat screen behavior.");
            GD.Print("PASS hidden-page entrance cancellation, map priority/return and unrelated event isolation.");
        }
        finally
        {
            ActiveScreenContext.Instance.Updated -= onContextChanged;
            layout.Free(); handFocus.Free();
            RemoveChild(controller);
            AccessTools.Property(typeof(NGame), nameof(NGame.Instance)).SetValue(null, oldGame);
            SaveManager.Instance.PrefsSave.FastMode = oldSpeed;
            foreach (StringName action in addedActions) InputMap.EraseAction(action);
            harmony.UnpatchAll(harmony.Id);
            foreach (Node node in detached) node.Free();
        }
    }
}
