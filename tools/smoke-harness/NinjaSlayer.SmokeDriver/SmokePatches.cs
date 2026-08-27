using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.AutoSlay;
using MegaCrit.Sts2.Core.AutoSlay.Handlers.Rooms;
using MegaCrit.Sts2.Core.AutoSlay.Handlers.Screens;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Odds;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Content;
using NinjaSlayer.Events;

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
        if (controller is null)
        {
            return true;
        }

        if (controller.TryHandleSawatariEventCombat(ct, ref __result))
        {
            return false;
        }

        if (!controller.TryClaimFirstCombat())
        {
            return true;
        }

        __result = controller.ExecuteClaimedCombatAsync(random, ct);
        return false;
    }
}

[HarmonyPatch(typeof(ActModel), nameof(ActModel.PullNextEvent))]
internal static class NinjaSlayerSmokeSawatariEventPatch
{
    public static void Postfix(RunState runState, ref EventModel __result) =>
        SmokeController.Current?.ForceFirstSawatariEvent(runState, ref __result);
}

[HarmonyPatch(typeof(EventRoomHandler), "HandleEventCombat")]
internal static class NinjaSlayerSmokeSawatariCombatPatch
{
    public static bool Prefix(CancellationToken ct, ref Task __result)
    {
        SmokeController? controller = SmokeController.Current;
        return controller is null || !controller.TryHandleSawatariEventCombat(ct, ref __result);
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCombatStart))]
internal static class NinjaSlayerSmokeSawatariBeforeCombatPatch
{
    public static void Prefix(ICombatState? combatState) =>
        SmokeController.Current?.ObserveSawatariBeforeCombatStart(combatState);
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatEnd))]
internal static class NinjaSlayerSmokeSawatariAfterCombatPatch
{
    public static void Prefix(ICombatState? combatState) =>
        SmokeController.Current?.ObserveSawatariAfterCombatEnd(combatState);
}

[HarmonyPatch(typeof(NCombatStartBanner), nameof(NCombatStartBanner.Create))]
internal static class NinjaSlayerSmokeCombatStartBannerPatch
{
    public static void Postfix() => SmokeController.Current?.ObserveCombatStartBanner();
}

[HarmonyPatch(typeof(UnknownMapPointOdds), nameof(UnknownMapPointOdds.Roll))]
internal static class NinjaSlayerSmokeUnknownRoomRollPatch
{
    [ThreadStatic]
    private static int _depth;

    [ThreadStatic]
    private static int _hookCalls;

    [ThreadStatic]
    private static float _initialMonsterOdds;

    [ThreadStatic]
    private static UnknownMapPointOdds? _rollOdds;

    [ThreadStatic]
    private static bool _forcedMonsterOddsObserved;

    public static void Prefix(UnknownMapPointOdds __instance)
    {
        if (_depth++ == 0)
        {
            _hookCalls = 0;
            _forcedMonsterOddsObserved = false;
            _initialMonsterOdds = __instance.MonsterOdds;
            _rollOdds = __instance;
        }
    }

    [HarmonyPriority(Priority.Last)]
    public static Exception? Finalizer(
        Exception? __exception,
        UnknownMapPointOdds __instance,
        IRunState runState)
    {
        if (--_depth == 0)
        {
            SmokeController.Current?.ObserveUnknownRoomRoll(
                runState,
                _hookCalls,
                !_forcedMonsterOddsObserved
                    || __instance.MonsterOdds == _initialMonsterOdds);
            _rollOdds = null;
        }

        return __exception;
    }

    public static void ObserveRoomTypeHook()
    {
        if (_depth == 1)
        {
            _hookCalls++;
        }
    }

    public static void ObserveRoomTypeHookCompleted()
    {
        if (_depth == 1
            && _initialMonsterOdds != 1f
            && _rollOdds!.MonsterOdds == 1f)
        {
            _forcedMonsterOddsObserved = true;
        }
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.ModifyUnknownMapPointRoomTypes))]
internal static class NinjaSlayerSmokeUnknownRoomTypeHookPatch
{
    public static void Prefix() => NinjaSlayerSmokeUnknownRoomRollPatch.ObserveRoomTypeHook();

    [HarmonyPriority(Priority.Last - 1)]
    public static void Postfix() => NinjaSlayerSmokeUnknownRoomRollPatch.ObserveRoomTypeHookCompleted();
}

[HarmonyPatch(typeof(Hook), nameof(Hook.ModifyDamage))]
internal static class NinjaSlayerSmokeDamageHookPatch
{
    public static void Prefix(
        Creature? target,
        Creature? dealer,
        ModifyDamageHookType modifyDamageHookType,
        CardPreviewMode previewMode)
    {
        if (!NinjaSlayerSmokeAttackIntentPatch.IsEvaluating
            && modifyDamageHookType == ModifyDamageHookType.All
            && previewMode == CardPreviewMode.None)
        {
            SmokeController.Current?.ObserveDarkStrikeDamageHook(target, dealer);
        }
    }
}

[HarmonyPatch(typeof(AttackIntent), nameof(AttackIntent.GetSingleDamage))]
internal static class NinjaSlayerSmokeAttackIntentPatch
{
    [ThreadStatic]
    private static int _depth;

    public static bool IsEvaluating => _depth > 0;

    public static void Prefix() => _depth++;

    public static Exception? Finalizer(Exception? __exception)
    {
        _depth--;
        return __exception;
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeAttack))]
internal static class NinjaSlayerSmokeBeforeAttackHookPatch
{
    public static void Prefix(AttackCommand command) =>
        SmokeController.Current?.ObserveDarkStrikeAttackHook(command, after: false);
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterAttack))]
internal static class NinjaSlayerSmokeAfterAttackHookPatch
{
    public static void Prefix(AttackCommand command) =>
        SmokeController.Current?.ObserveDarkStrikeAttackHook(command, after: true);
}

[HarmonyPatch(typeof(NinjaSlayerCombatAudioSet), nameof(NinjaSlayerCombatAudioSet.Play))]
internal static class NinjaSlayerSmokeDarkStrikeAudioPatch
{
    public static void Prefix(string? eventPath) =>
        SmokeController.Current?.ObserveDarkStrikeAudio(eventPath);
}

[HarmonyPatch(typeof(VfxCmd), nameof(VfxCmd.PlayVfx))]
internal static class NinjaSlayerSmokeDarkStrikeVfxPatch
{
    public static void Prefix(Vector2 position, string? path) =>
        SmokeController.Current?.ObserveDarkStrikeVfx(position, path);
}

[HarmonyPatch(typeof(AutoSlayer), "QuitGame")]
internal static class NinjaSlayerSmokeAutoSlayExitPatch
{
    public static void Prefix(ref int exitCode) =>
        SmokeController.Current?.BeforeFullAutoSlayExit(ref exitCode);
}
