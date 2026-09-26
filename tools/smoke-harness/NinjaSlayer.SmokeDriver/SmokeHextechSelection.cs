using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.AutoSlay;
using MegaCrit.Sts2.Core.AutoSlay.Handlers;
using MegaCrit.Sts2.Core.AutoSlay.Helpers;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Random;

namespace NinjaSlayer.SmokeDriver;

// Test input only: use the addon's visible buttons and its normal confirmation guard.
[HarmonyPatch(typeof(AutoSlayer), MethodType.Constructor)]
internal static class SmokeHextechSelection
{
    public static void Postfix(Dictionary<Type, IScreenHandler> ____screenHandlers)
    {
        if (AccessTools.TypeByName("HextechRunes.HextechRuneSelectionScreen") is { } screen)
            ____screenHandlers.Add(screen, new Selection(screen));
    }

    internal static async Task ChooseAsync(Control screen, CancellationToken cancellationToken)
    {
        await Task.Delay(1100, cancellationToken);
        var buttons = UiHelper.FindAll<Button>(screen).Where(b => b.IsVisibleInTree() && !b.Disabled).ToArray();
        var confirm = buttons.FirstOrDefault(b => b.Name.ToString().Contains("Confirm", StringComparison.Ordinal));
        var choice = confirm ?? buttons.FirstOrDefault(b => b.Name.ToString().EndsWith("_Card", StringComparison.Ordinal));
        if (choice is null) throw new InvalidOperationException("Hextech selection has no enabled choice or confirmation button.");
        choice.EmitSignal(BaseButton.SignalName.Pressed);
        await Task.Delay(200, cancellationToken);
    }

    private sealed class Selection(Type screenType) : IScreenHandler
    {
        public Type ScreenType => screenType;
        public TimeSpan Timeout => TimeSpan.FromSeconds(30);
        public async Task HandleAsync(Rng random, CancellationToken ct)
        {
            if (NOverlayStack.Instance?.Peek() is not Control screen || screen.GetType() != ScreenType)
                throw new InvalidOperationException("The Hextech overlay changed before selection.");
            await ChooseAsync(screen, ct);
        }
    }
}
