using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Content;
using NinjaSlayer.Relics;
using STS2RitsuLib;
using STS2RitsuLib.Audio;
using STS2RitsuLib.Interop;
using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Scripts;

[ModInitializer(nameof(Init))]
public class Entry
{
    public const string ModId = "NinjaSlayer";
    public static readonly Logger Logger = RitsuLibFramework.CreateLogger(ModId);
    private static readonly Type[] GodotSceneScriptTypes =
    [
        typeof(NinjaSlayerSpinPivot),
        typeof(NinjaSlayerSpinMotionBlur),
        typeof(NinjaSlayerShadowController),
        typeof(NarakuVisualOverlay),
        typeof(NinjaSlayerTransitionOverlay),
        typeof(NNinjaSlayerGroundFireVfx)
    ];

    public static void Init()
    {
        GC.KeepAlive(GodotSceneScriptTypes);
        Log.Info("Mod initialized!");

        var assembly = Assembly.GetExecutingAssembly();
        RitsuLibFramework.EnsureGodotScriptsRegistered(assembly, Logger);
        ModTypeDiscoveryHub.RegisterModAssembly(ModId, assembly);

        using (RitsuLibFramework.BeginModDataRegistration(ModId))
        {
            NinjaSlayerRunData.Register(ModId);
        }

        NinjaSlayerBalanceTelemetry.Register();

        RitsuLibFramework.CreateContentPack(ModId)
            .Character<NinjaSlayerCharacter>(character => character
                .AddStartingCard<StrikeNinjaSlayer>(4, 0)
                .AddStartingCard<DefendNinjaSlayer>(4, 1)
                .AddStartingCard<Meditation>(1, 2)
                .AddStartingCard<KarateStraight>(1, 3))
            .Character<NinjaSlayerDebugCharacter>(character => character
                .AddStartingCard<StrikeNinjaSlayer>(4, 0)
                .AddStartingCard<DefendNinjaSlayer>(4, 1)
                .AddStartingCard<Meditation>(1, 2)
                .AddStartingCard<KarateStraight>(1, 3)
                .AddStartingRelic<ChadoBreathingRelic>(1, 0))
            .Apply();

        RitsuLibFramework.RegisterArchaicToothTranscendenceMapping<KarateStraight, CollapseFist>();

        var patcher = RitsuLibFramework.CreatePatcher(ModId, "core-patches");
        patcher.RegisterPatch<NinjaSlayerAnimationPatch>();
        patcher.RegisterPatch<NinjaSlayerDebuffShakePatch>();
        patcher.RegisterPatch<NinjaSlayerSurroundedFacingPatch>();
        patcher.RegisterPatch<NinjaSlayerAttackFacingPatch>();
        patcher.RegisterPatch<NinjaSlayerDeathAnimPatch>();
        patcher.RegisterPatch<NinjaSlayerReviveAnimPatch>();
        patcher.RegisterPatch<NinjaSlayerFinisherLethalDamagePatch>();
        patcher.RegisterPatch<NinjaSlayerFinisherDamageNumberPatch>();
        patcher.RegisterPatch<NinjaSlayerFinisherCardVisualPatch>();
        patcher.RegisterPatch<NinjaSlayerFinisherAttackCommandPatch>();
        patcher.RegisterPatch<NinjaSlayerFinisherPrimaryDamagePatch>();
        patcher.RegisterPatch<NinjaSlayerFinisherAfterCardPlayedPatch>();
        patcher.RegisterPatch<NinjaSlayerFinisherCardPlayCleanupPatch>();
        patcher.RegisterPatch<TornadoFistFinisherCadencePatch>();
        patcher.RegisterPatch<ReporterPassEventOptionPatch>();
        patcher.RegisterPatch<AncientEntranceEventOptionPatch>();
        patcher.RegisterPatch<AncientEntranceCreatureVisibilityPatch>();
        patcher.RegisterPatch<NancyLeeCandidatePatch>();
        patcher.RegisterPatch<NancyLeeLoadedRunPatch>();
        patcher.RegisterPatch<KarateCardPreviewTargetPatch>();
        patcher.RegisterPatch<KarateCardPreviewClearPatch>();
        patcher.RegisterPatch<KarateHealthBarTextPreviewPatch>();
        patcher.RegisterPatch<NinjaSlayerTransitionSfxPatch>();
        patcher.RegisterPatch<BossGreetingMusicPatch>();
        patcher.RegisterPatch<CombatCinematicLayoutPatch>();
        patcher.RegisterPatch<CardTransformShineSfxPatch>();
        patcher.RegisterPatch<NinjaSlayerSwipePowerStealPatch>();
        patcher.RegisterPatch<ScreenShakeSuppressionPatch>();
        patcher.RegisterPatch<NinjaSlayerTransitionPatch>();
        patcher.RegisterPatch<NinjaSlayerTransitionPreloadPatch>();
        patcher.RegisterPatch<NinjaSlayerTransitionAssetFinalizePatch>();
        patcher.RegisterPatch<NinjaSlayerTransitionGcDeferralPatch>();
        patcher.RegisterPatch<NinjaSlayerRoomFadeInGatePatch>();
        patcher.RegisterPatch<NinjaSlayerFadeInGatePatch>();
        patcher.RegisterPatch<NinjaSlayerEmbarkLoadDelayPatch>();
        patcher.RegisterPatch<NinjaSlayerSaveLoadDelayPatch>();
        patcher.RegisterPatch<NinjaSlayerCardTitleTypographyPatch>();
        patcher.RegisterPatch<NinjaSlayerInspectRelicTypographyPatch>();
        patcher.RegisterPatch<PreparedDrawPatch>();
        patcher.RegisterPatch<PreparedPileExitPatch>();
        patcher.RegisterPatch<PreparedDrawPileDisplayOrderPatch>();
        patcher.RegisterPatch<NinjaSlayerFeedbackOpenerPatch>();
        patcher.RegisterPatch<NinjaSlayerFeedbackOpenPatch>();
        patcher.RegisterPatch<NinjaSlayerFeedbackConfirmPatch>();
        patcher.RegisterPatch<NinjaSlayerFeedbackSendPatch>();
        patcher.RegisterPatch<NinjaSlayerFeedbackClosePatch>();
        if (!patcher.PatchAll())
        {
            LogPatchFailure(patcher);
            throw new InvalidOperationException("Critical NinjaSlayer patches failed to apply.");
        }

        RegisterFmodBanksIfPresent();
    }

    private static void LogPatchFailure(ModPatcher patcher)
    {
        Logger.Error(
            $"PatchAll failed for {patcher.PatcherName}: applied {patcher.AppliedPatchCount}/{patcher.RegisteredPatchCount}.");

        foreach (ModPatchInfo patch in patcher.RegisteredPatches)
        {
            string paramList = patch.ParameterTypes is { Length: > 0 } types
                ? string.Join(", ", types.Select(t => t.Name))
                : "(none)";

            Logger.Error(
                $"  patch id={patch.Id}, critical={patch.IsCritical}, target={patch.TargetType?.Name}.{patch.MethodName}({paramList})");
        }
    }

    private static void RegisterFmodBanksIfPresent()
    {
        if (!Godot.FileAccess.FileExists(NinjaSlayerAudio.BankPath) || !Godot.FileAccess.FileExists(NinjaSlayerAudio.GuidMappingsPath))
        {
            Log.Warn($"FMOD bank files are missing. Expected {NinjaSlayerAudio.BankPath} and {NinjaSlayerAudio.GuidMappingsPath}. Audio events will remain unavailable until exported FMOD bank files are added.");
            return;
        }

        FmodStudioDeferredBankRegistration.RegisterBank(NinjaSlayerAudio.BankPath);
        FmodStudioDeferredBankRegistration.RegisterStudioGuidMappings(NinjaSlayerAudio.GuidMappingsPath);
        Logger.Info($"FMOD bank registered: {NinjaSlayerAudio.BankPath}");
    }
}
