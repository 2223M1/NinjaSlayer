using System.Reflection;
using System.Runtime.CompilerServices;
using System.Net;
using System.Net.Sockets;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Feedback;
using NinjaSlayer.Content;
using STS2RitsuLib;
using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.Patching.Models;
using STS2RitsuLib.RunData;
using STS2RitsuLib.Scaffolding.Visuals.Definition;

namespace NinjaSlayer.RitsuLibContractTests;

public partial class ContractRunner : Node
{
    public override void _Ready()
    {
        try
        {
            VerifyOriginalLethalTargetFingerprint();
            VerifyOriginalPreparedDrawTargetFingerprint();
            VerifyNancyLoadedRunCompatibility();
            VerifyFinalizerOrderingAndTypedState();
            VerifyRunOriginalContract();
            VerifyOriginalFeedbackStreamOwnership();
            VerifyRunDataSchemaCompatibility();
            VerifyFinisherProtectionTransaction();
            VerifyWorldVisualStylesAreIdempotent();
            VerifyCriticalRollback();
            GD.Print("NinjaSlayer RitsuLib contracts passed.");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PushError($"NinjaSlayer RitsuLib contract failed: {ex}");
            GetTree().Quit(1);
        }
    }

    private static void VerifyOriginalLethalTargetFingerprint()
    {
        Require(
            FinisherLethalTargetContract.TryValidate(out _, out _, out string reason),
            reason);
    }

    private static void VerifyOriginalPreparedDrawTargetFingerprint()
    {
        Require(
            PreparedDrawTargetContract.TryValidate(out _, out _, out string reason),
            reason);
    }

    private static void VerifyNancyLoadedRunCompatibility()
    {
        Require(
            NancyCompatibility.GetLoadedRunRepairProbes().All(probe => probe.IsAvailable),
            "The Nancy loaded-run room contract is unavailable.");

        var act = (Glory)RuntimeHelpers.GetUninitializedObject(typeof(Glory));
        var rooms = new RoomSet();
        FieldInfo roomsField = typeof(ActModel).GetField(
            "_rooms",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(ActModel).FullName, "_rooms");
        roomsField.SetValue(act, rooms);

        Require(
            NancyCompatibility.TryGetRooms(act, out RoomSet? resolved) && ReferenceEquals(rooms, resolved),
            "The Nancy compatibility adapter did not return the loaded act room set.");
    }

    private static void VerifyFinalizerOrderingAndTypedState()
    {
        ContractPatch.Reset();
        ModPatcher patcher = CreatePatcher("finalizer-contract");
        patcher.RegisterPatch<ContractPatch>();
        try
        {
            Require(patcher.PatchAll(), "ModPatcher rejected the finalizer contract patch.");
            Require(ContractTarget.Execute(4, fail: false) == 8, "The original target result changed.");
            Require(ContractPatch.PrefixObserved, "Prefix was not called.");
            Require(ContractPatch.PostfixObserved, "Postfix was not called.");
            Require(ContractPatch.FinalizerObserved, "Finalizer was not called.");
            Require(ContractPatch.SharedStateObserved, "Typed __state was not shared across patch stages.");

            MethodInfo target = ResolveTarget();
            Patches info = Harmony.GetPatchInfo(target)
                ?? throw new InvalidOperationException("Harmony did not report the installed contract patch.");
            Patch prefix = info.Prefixes.Single(item => item.owner == patcher.PatcherId);
            Patch finalizer = info.Finalizers.Single(item => item.owner == patcher.PatcherId);
            Require(prefix.priority == 321, "Method-level Harmony priority was not preserved.");
            Require(prefix.before.Contains("contract.before"), "HarmonyBefore was not preserved.");
            Require(prefix.after.Contains("contract.after"), "HarmonyAfter was not preserved.");
            Require(finalizer.PatchMethod.Name == nameof(ContractPatch.Finalizer), "Finalizer registration is incorrect.");

            try
            {
                ContractTarget.Execute(5, fail: true);
                throw new InvalidOperationException("The original target exception was suppressed.");
            }
            catch (InvalidOperationException ex) when (ex.Message == "contract-target-failure")
            {
            }
            Require(ContractPatch.ExceptionFinalizerObserved, "Finalizer did not observe the original exception.");
        }
        finally
        {
            patcher.UnpatchAll();
        }
    }

    private static void VerifyCriticalRollback()
    {
        ModPatcher patcher = CreatePatcher("rollback-contract");
        patcher.RegisterPatch<ContractPatch>();
        patcher.RegisterPatch<MissingCriticalPatch>();
        Require(!patcher.PatchAll(), "A missing critical target did not fail the capability.");
        Patches? info = Harmony.GetPatchInfo(ResolveTarget());
        Require(!(info?.Owners.Contains(patcher.PatcherId) ?? false), "Critical failure left an earlier patch installed.");
    }

    private static void VerifyRunOriginalContract()
    {
        RunOriginalPatch.Reset();
        ModPatcher patcher = CreatePatcher("run-original-contract");
        patcher.RegisterPatch<RunOriginalPatch>();
        try
        {
            Require(patcher.PatchAll(), "ModPatcher rejected the __runOriginal contract patch.");
            Require(RunOriginalTarget.Execute(skipOriginal: false) == 17, "The run-original target result changed.");
            Require(RunOriginalPatch.ObservedOriginalRun, "Postfix did not observe __runOriginal=true.");
            Require(RunOriginalTarget.Execute(skipOriginal: true) == 0, "Skipped original did not retain its default result.");
            Require(RunOriginalPatch.ObservedOriginalSkip, "Postfix did not observe __runOriginal=false.");
            Require(RunOriginalPatch.SharedStateObserved, "Typed state was not shared when the original was skipped.");
        }
        finally
        {
            patcher.UnpatchAll();
        }
    }

    private static void VerifyRunDataSchemaCompatibility()
    {
        var defaultOptions = new RunSavedDataOptions
        {
            WritePolicy = RunSavedDataWritePolicy.WhenNonDefault
        };
        var explicitOptions = new RunSavedDataOptions
        {
            SchemaVersion = 1,
            WritePolicy = RunSavedDataWritePolicy.WhenNonDefault
        };
        Require(defaultOptions.SchemaVersion == 1, "RitsuLib's default RunData schema is no longer version 1.");
        Require(
            defaultOptions.SchemaVersion == explicitOptions.SchemaVersion,
            "Explicit schema version 1 no longer matches the former default registration.");

        NinjaSlayerRunState single = ImportRunDataFixture(
            "single-player-pre-greeting-v1.json",
            defaultOptions,
            1001);
        Require(single.PendingAncientEntranceAnimation, "The pre-greeting single-player flag was not restored.");
        Require(single.CompletedBossGreetingRoomKeys.Count == 0, "The added room-key field lost its version 1 default.");

        NinjaSlayerRunState multiplayer = ImportRunDataFixture(
            "multiplayer-boss-greeting-v1.json",
            explicitOptions,
            1001);
        Require(
            multiplayer.CompletedBossGreetingRoomKeys.Count == 6,
            "The multiplayer room-key payload did not survive RitsuLib's version 1 import.");
    }

    private static void VerifyOriginalFeedbackStreamOwnership()
    {
        string? previousUrl = System.Environment.GetEnvironmentVariable("STS2_FEEDBACK_URL");
        int port = ReserveLoopbackPort();
        string endpoint = $"http://127.0.0.1:{port}/feedback/";
        System.Environment.SetEnvironmentVariable("STS2_FEEDBACK_URL", endpoint);
        try
        {
            using var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            Task responder = Task.Run(async () =>
            {
                HttpListenerContext context = await listener.GetContextAsync();
                await context.Request.InputStream.CopyToAsync(Stream.Null);
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentLength64 = 0;
                context.Response.Close();
            });

            var originalScreenshot = new SentinelStream([0x89, 0x50, 0x4e, 0x47]);
            var originalLogs = new SentinelStream([0x50, 0x4b, 0x03, 0x04]);
            MethodInfo originalSend = typeof(NSendFeedbackScreen).GetMethod(
                "SendFeedback",
                BindingFlags.Static | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(FeedbackData), typeof(Stream), typeof(Stream)],
                modifiers: null)
                ?? throw new MissingMethodException(typeof(NSendFeedbackScreen).FullName, "SendFeedback");
            var data = new FeedbackData
            {
                description = "NinjaSlayer ownership contract",
                category = "bug",
                gameVersion = "contract",
                uniqueId = "contract",
                commit = "contract",
                platformBranch = "contract",
                sessionId = "contract",
                lang = "eng",
            };
            Task<bool> sendTask = Task.Run(async () =>
            {
                var originalTask = (Task<bool>)(originalSend.Invoke(
                    null,
                    [data, originalScreenshot, originalLogs])
                    ?? throw new InvalidOperationException("Original feedback call returned null."));
                return await originalTask.ConfigureAwait(false);
            });

            Require(sendTask.GetAwaiter().GetResult(), "The local original feedback fixture did not succeed.");
            responder.GetAwaiter().GetResult();
            Require(
                originalScreenshot.IsClosed && originalLogs.IsClosed,
                "The original feedback method no longer owns both upload streams.");

            var replacementScreenshot = new SentinelStream([]);
            var replacementLogs = new SentinelStream([]);
            Require(
                FeedbackStreamOwnership.SendAndCloseAsync(
                    () => Task.FromResult(true),
                    replacementScreenshot,
                    replacementLogs).GetAwaiter().GetResult(),
                "The replacement feedback ownership wrapper did not return its send result.");
            Require(
                replacementScreenshot.IsClosed && replacementLogs.IsClosed,
                "The replacement feedback ownership wrapper does not match the original stream contract.");
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("STS2_FEEDBACK_URL", previousUrl);
        }
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static NinjaSlayerRunState ImportRunDataFixture(
        string fixtureName,
        RunSavedDataOptions options,
        ulong expectedNetId)
    {
        string modId = $"NinjaSlayer.ContractTests.RunData.{Guid.NewGuid():N}";
        PlayerRunSavedData<NinjaSlayerRunState> handle;
        using (RitsuLibFramework.BeginModDataRegistration(modId))
        {
            handle = RitsuLibFramework.GetRunSavedDataStore(modId).RegisterPerPlayer(
                key: "ninja_slayer_run_state",
                defaultFactory: () => new NinjaSlayerRunState(),
                options: options);
        }

        string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "RunData", fixtureName);
        string payload = System.IO.File.ReadAllText(fixturePath)
            .Replace("\"NinjaSlayer\"", $"\"{modId}\"", StringComparison.Ordinal);
        var runState = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
        Type registryType = typeof(RitsuLibFramework).Assembly.GetType(
            "STS2RitsuLib.RunData.RunSavedDataRegistry",
            throwOnError: true)!;
        MethodInfo import = registryType.GetMethod(
            "ImportPayloadIntoRun",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(registryType.FullName, "ImportPayloadIntoRun");
        import.Invoke(null, [runState, payload]);

        Require(
            handle.TryGet(runState, expectedNetId, out NinjaSlayerRunState state),
            $"RitsuLib did not import {fixtureName} for player {expectedNetId}.");
        return state;
    }

    private static void VerifyFinisherProtectionTransaction()
    {
        var combatState = new CombatState();
        bool contextIsCurrent = true;

        Creature failedTarget = CreateCreature(combatState, currentHp: 1, maxHp: 10);
        var failedLedger = new FinisherDamageLedger([failedTarget], 1, 1, combatState, () => contextIsCurrent);
        decimal failedAmount = 10m;
        Require(
            failedLedger.TryProtect(failedTarget, committing: false, ref failedAmount, out FinisherProtectionToken? failedToken),
            "Lethal protection did not create a token.");
        Require(failedTarget.CurrentHp == 2 && failedAmount == 1m, "The temporary 1 -> 2 HP bump was not applied.");
        failedLedger.FinalizeProtection(failedToken!);
        Require(failedTarget.CurrentHp == 1, "Finalizer did not roll back an intact temporary HP bump.");
        Require(failedLedger.DeferredDeaths.Count == 0, "An unconfirmed damage call registered a deferred death.");

        Creature confirmedTarget = CreateCreature(combatState, currentHp: 1, maxHp: 10);
        var confirmedLedger = new FinisherDamageLedger([confirmedTarget], 2, 1, combatState, () => contextIsCurrent);
        decimal confirmedAmount = 10m;
        Require(
            confirmedLedger.TryProtect(
                confirmedTarget,
                committing: false,
                ref confirmedAmount,
                out FinisherProtectionToken? confirmedToken),
            "Confirmed lethal protection did not create a token.");
        DamageResult result = confirmedTarget.LoseHpInternal(confirmedAmount, ValueProp.Move);
        confirmedLedger.Confirm(confirmedToken!, result, originalRan: true);
        confirmedLedger.FinalizeProtection(confirmedToken!);
        Require(confirmedTarget.CurrentHp == 1, "Confirmed protection changed the protected target HP.");
        Require(confirmedToken!.IsConfirmed, "The real DamageResult did not confirm its protection token.");
        Require(confirmedLedger.DeferredDeaths.SetEquals([confirmedTarget]), "Confirmed lethal damage was not deferred exactly once.");

        Creature postfixFailureTarget = CreateCreature(combatState, currentHp: 1, maxHp: 10);
        var postfixFailureLedger = new FinisherDamageLedger(
            [postfixFailureTarget],
            5,
            1,
            combatState,
            () => contextIsCurrent,
            (_, _) => throw new InvalidOperationException("injected-postfix-failure"));
        decimal postfixFailureAmount = 10m;
        Require(
            postfixFailureLedger.TryProtect(
                postfixFailureTarget,
                committing: false,
                ref postfixFailureAmount,
                out FinisherProtectionToken? postfixFailureToken),
            "Postfix-failure protection did not create a token.");
        DamageResult postfixFailureResult = postfixFailureTarget.LoseHpInternal(postfixFailureAmount, ValueProp.Move);
        try
        {
            postfixFailureLedger.Confirm(postfixFailureToken!, postfixFailureResult, originalRan: true);
            throw new InvalidOperationException("Injected Postfix failure did not propagate to the patch boundary.");
        }
        catch (InvalidOperationException ex) when (ex.Message == "injected-postfix-failure")
        {
        }
        postfixFailureLedger.FinalizeProtection(postfixFailureToken!);
        Require(postfixFailureTarget.CurrentHp == 1, "Postfix failure rolled a confirmed target back to its bumped HP.");
        Require(postfixFailureToken!.IsConfirmed, "Postfix failure lost the confirmed DamageResult state.");
        Require(
            postfixFailureLedger.DeferredDeaths.SetEquals([postfixFailureTarget]),
            "Postfix failure lost the confirmed deferred death.");

        Creature partiallyMutatedTarget = CreateCreature(combatState, currentHp: 1, maxHp: 10);
        var partiallyMutatedLedger = new FinisherDamageLedger(
            [partiallyMutatedTarget],
            3,
            1,
            combatState,
            () => contextIsCurrent);
        decimal partialAmount = 10m;
        Require(
            partiallyMutatedLedger.TryProtect(
                partiallyMutatedTarget,
                committing: false,
                ref partialAmount,
                out FinisherProtectionToken? partiallyMutatedToken),
            "Partial-mutation protection did not create a token.");
        partiallyMutatedTarget.SetCurrentHpInternal(1);
        partiallyMutatedLedger.FinalizeProtection(partiallyMutatedToken!);
        Require(partiallyMutatedTarget.CurrentHp == 1, "Finalizer overwrote HP changed by the original method.");

        Creature staleTarget = CreateCreature(combatState, currentHp: 1, maxHp: 10);
        var staleLedger = new FinisherDamageLedger([staleTarget], 4, 1, combatState, () => contextIsCurrent);
        decimal staleAmount = 10m;
        Require(
            staleLedger.TryProtect(staleTarget, committing: false, ref staleAmount, out FinisherProtectionToken? staleToken),
            "Stale-combat protection did not create a token.");
        contextIsCurrent = false;
        staleLedger.FinalizeProtection(staleToken!);
        Require(staleTarget.CurrentHp == 2, "Finalizer wrote HP into a stale combat.");
    }

    private static void VerifyWorldVisualStylesAreIdempotent()
    {
        VerifyWorldVisualStyle(
            NinjaSlayerWorldVisualProfile.Merchant.BodyStyle(),
            new Vector2(
                NinjaSlayerWorldVisualProfile.Merchant.BodyPositionX,
                NinjaSlayerWorldVisualProfile.Merchant.BodyPositionY),
            NinjaSlayerWorldVisualProfile.Merchant.BodyScale,
            "merchant");
        VerifyWorldVisualStyle(
            NinjaSlayerWorldVisualProfile.RestSite.BodyStyle(),
            new Vector2(
                NinjaSlayerWorldVisualProfile.RestSite.BodyPositionX,
                NinjaSlayerWorldVisualProfile.RestSite.BodyPositionY),
            NinjaSlayerWorldVisualProfile.RestSite.BodyScale,
            "rest site");
    }

    private static void VerifyWorldVisualStyle(
        VisualNodeStyle style,
        Vector2 expectedPosition,
        float expectedScale,
        string label)
    {
        Require(style.Position == expectedPosition, $"The {label} style lost its calibrated absolute position.");
        Require(style.Offset is null, $"The {label} style reintroduced an accumulating offset.");
        Require(
            style.Scale == new Vector2(expectedScale, expectedScale),
            $"The {label} style lost its calibrated scale.");

        Type applicatorType = typeof(VisualNodeStyle).Assembly.GetType(
            "STS2RitsuLib.Scaffolding.Visuals.Definition.VisualNodeStyleApplicator",
            throwOnError: true)!;
        MethodInfo apply = applicatorType.GetMethod(
            "ApplyTo",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(applicatorType.FullName, "ApplyTo");
        var node = new Node2D
        {
            Position = new Vector2(600f, 400f),
            Scale = new Vector2(3f, 2f)
        };
        try
        {
            for (int replay = 0; replay < 3; replay++)
            {
                apply.Invoke(null, [style, node, null]);
                Require(node.Position == expectedPosition, $"The {label} style drifted on replay {replay + 1}.");
                Require(
                    node.Scale == new Vector2(expectedScale, expectedScale),
                    $"The {label} scale drifted on replay {replay + 1}.");
            }
        }
        finally
        {
            node.Free();
        }
    }

    private static Creature CreateCreature(ICombatState combatState, int currentHp, int maxHp)
    {
        var creature = (Creature)RuntimeHelpers.GetUninitializedObject(typeof(Creature));
        AccessTools.Field(typeof(Creature), "_maxHp").SetValue(creature, maxHp);
        AccessTools.Field(typeof(Creature), "_currentHp").SetValue(creature, currentHp);
        creature.CombatState = combatState;
        return creature;
    }

    private static MethodInfo ResolveTarget() => AccessTools.Method(
        typeof(ContractTarget),
        nameof(ContractTarget.Execute),
        [typeof(int), typeof(bool)])!;

    private static ModPatcher CreatePatcher(string capability) =>
        RitsuLibFramework.CreatePatcher(
            "NinjaSlayer.ContractTests",
            $"{capability}-{Guid.NewGuid():N}",
            capability);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static class ContractTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Execute(int value, bool fail)
        {
            if (fail)
            {
                throw new InvalidOperationException("contract-target-failure");
            }
            return value * 2;
        }
    }

    private static class RunOriginalTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Execute(bool skipOriginal) => 17;
    }

    private sealed class ContractPatch : IPatchMethod
    {
        private static ContractState? _prefixState;

        public static string PatchId => "contract_finalizer_state";
        public static bool IsCritical => true;
        public static ModPatchTarget[] GetTargets() =>
            [new(typeof(ContractTarget), nameof(ContractTarget.Execute), [typeof(int), typeof(bool)])];

        public static bool PrefixObserved { get; private set; }
        public static bool PostfixObserved { get; private set; }
        public static bool FinalizerObserved { get; private set; }
        public static bool SharedStateObserved { get; private set; }
        public static bool ExceptionFinalizerObserved { get; private set; }

        [HarmonyPriority(321)]
        [HarmonyBefore("contract.before")]
        [HarmonyAfter("contract.after")]
        public static void Prefix(out ContractState __state)
        {
            __state = new ContractState();
            _prefixState = __state;
            PrefixObserved = true;
        }

        public static void Postfix(ContractState __state)
        {
            PostfixObserved = true;
            __state.PostfixObserved = true;
            SharedStateObserved = ReferenceEquals(_prefixState, __state);
        }

        public static Exception? Finalizer(ContractState __state, Exception? __exception)
        {
            FinalizerObserved = true;
            SharedStateObserved |= ReferenceEquals(_prefixState, __state)
                && (__state.PostfixObserved || __exception is not null);
            ExceptionFinalizerObserved |= __exception is not null;
            return __exception;
        }

        public static void Reset()
        {
            _prefixState = null;
            PrefixObserved = false;
            PostfixObserved = false;
            FinalizerObserved = false;
            SharedStateObserved = false;
            ExceptionFinalizerObserved = false;
        }
    }

    private sealed class MissingCriticalPatch : IPatchMethod
    {
        public static string PatchId => "contract_missing_critical";
        public static bool IsCritical => true;
        public static ModPatchTarget[] GetTargets() =>
            [new(typeof(ContractTarget), "MethodThatMustNotExist")];
        public static void Prefix() { }
    }

    private sealed class RunOriginalPatch : IPatchMethod
    {
        private static RunOriginalState? _prefixState;

        public static string PatchId => "contract_run_original_state";
        public static bool IsCritical => true;
        public static ModPatchTarget[] GetTargets() =>
            [new(typeof(RunOriginalTarget), nameof(RunOriginalTarget.Execute), [typeof(bool)])];

        public static bool ObservedOriginalRun { get; private set; }
        public static bool ObservedOriginalSkip { get; private set; }
        public static bool SharedStateObserved { get; private set; }

        public static bool Prefix(bool skipOriginal, out RunOriginalState __state)
        {
            __state = new RunOriginalState();
            _prefixState = __state;
            return !skipOriginal;
        }

        public static void Postfix(bool __runOriginal, RunOriginalState __state)
        {
            ObservedOriginalRun |= __runOriginal;
            ObservedOriginalSkip |= !__runOriginal;
            SharedStateObserved |= ReferenceEquals(_prefixState, __state);
        }

        public static Exception? Finalizer(Exception? __exception, RunOriginalState __state)
        {
            SharedStateObserved |= ReferenceEquals(_prefixState, __state);
            return __exception;
        }

        public static void Reset()
        {
            _prefixState = null;
            ObservedOriginalRun = false;
            ObservedOriginalSkip = false;
            SharedStateObserved = false;
        }
    }

    private sealed class ContractState
    {
        public bool PostfixObserved { get; set; }
    }

    private sealed class RunOriginalState;

    private sealed class SentinelStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool IsClosed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsClosed = true;
            base.Dispose(disposing);
        }
    }
}
