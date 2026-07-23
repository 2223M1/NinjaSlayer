using HarmonyLib;
using MegaCrit.Sts2.Core.AutoSlay;
using MegaCrit.Sts2.Core.AutoSlay.Handlers.Rooms;
using MegaCrit.Sts2.Core.AutoSlay.Handlers.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Random;
using NinjaSlayer.Content;

namespace NinjaSlayer.SmokeDriver;

[HarmonyPatch(typeof(NCharacterSelectButton), nameof(NCharacterSelectButton.Select))]
internal static class NinjaSlayerSmokeCharacterSelectionPatch
{
    private static bool _redirecting;

    public static bool Prefix(NCharacterSelectButton __instance)
    {
        SmokeController? controller = SmokeController.Current;
        if (_redirecting
            || controller?.ShouldForceCharacter != true
            || __instance.Character is NinjaSlayerCharacter)
        {
            return true;
        }

        NCharacterSelectButton? ninjaSlayer = __instance.GetParent()
            .GetChildren()
            .OfType<NCharacterSelectButton>()
            .FirstOrDefault(button => button.Character is NinjaSlayerCharacter);
        if (ninjaSlayer is null)
        {
            throw new InvalidOperationException("NinjaSlayer character button was not present.");
        }

        try
        {
            _redirecting = true;
            ninjaSlayer.UnlockIfPossible();
            ninjaSlayer.Select();
            controller.ReportCharacterSelected(ninjaSlayer.Character.Id.ToString());
            return false;
        }
        finally
        {
            _redirecting = false;
        }
    }
}

[HarmonyPatch(typeof(MapScreenHandler), nameof(MapScreenHandler.HandleAsync))]
internal static class NinjaSlayerSmokeFirstMapPatch
{
    public static bool Prefix(ref Task __result)
    {
        SmokeController? controller = SmokeController.Current;
        return controller is null || !controller.TryHoldFirstMap(ref __result);
    }
}

[HarmonyPatch(typeof(CombatRoomHandler), nameof(CombatRoomHandler.HandleAsync))]
internal static class NinjaSlayerSmokeCombatPatch
{
    public static bool Prefix(Rng random, CancellationToken ct, ref Task __result)
    {
        SmokeController? controller = SmokeController.Current;
        if (controller is null || !controller.TryClaimFirstCombat())
        {
            return true;
        }

        __result = controller.ExecuteFirstCombatAsync(random, ct);
        return false;
    }
}

[HarmonyPatch(typeof(AutoSlayer), "QuitGame")]
internal static class NinjaSlayerSmokeAutoSlayExitPatch
{
    public static void Prefix(ref int exitCode) =>
        SmokeController.Current?.BeforeFullAutoSlayExit(ref exitCode);
}
