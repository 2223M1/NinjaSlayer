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
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Feedback;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Content;
using STS2RitsuLib;
using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.Patching.Models;
using STS2RitsuLib.RunData;
using STS2RitsuLib.Scaffolding.Visuals.Definition;
using STS2RitsuLib.Utils.HarmonyIl;

namespace NinjaSlayer.RitsuLibContractTests;

public partial class ContractRunner : Node
{
    public override void _Ready()
    {
        try
        {
            VerifyOutboundNetworkIsolation();
            if (!RitsuLibFramework.IsInitialized)
            {
                RitsuLibFramework.Initialize();
            }
            VerifyPreparedLifecyclePublishers();
            VerifyOrobasSeaGlassPatchContract();
            VerifyBlackFlameDamagePatchContract();
            VerifyProductionDynamicPatchContracts();
            VerifyFinalizerOrderingAndTypedState();
            VerifyRunOriginalContract();
            VerifyOriginalFeedbackStreamOwnership();
            VerifyFinisherProtectionTransaction();
            VerifyWorldVisualStylesAreIdempotent();
            VerifyCriticalRollback();
            WriteSuccessMarker();
            GD.Print("NinjaSlayer RitsuLib contracts passed.");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PushError($"NinjaSlayer RitsuLib contract failed: {ex}");
            GetTree().Quit(1);
        }
    }

    private static void VerifyOutboundNetworkIsolation()
    {
        if (!string.Equals(
                System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_REQUIRE_NETWORK_ISOLATION"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        string addressText = System.Environment.GetEnvironmentVariable(
                "NINJASLAYER_CONTRACT_NETWORK_PROBE_ADDRESS")
            ?? throw new InvalidOperationException("The protected Contract did not provide a network probe address.");
        string portText = System.Environment.GetEnvironmentVariable(
                "NINJASLAYER_CONTRACT_NETWORK_PROBE_PORT")
            ?? throw new InvalidOperationException("The protected Contract did not provide a network probe port.");
        if (!IPAddress.TryParse(addressText, out IPAddress? address) || IPAddress.IsLoopback(address))
        {
            throw new InvalidOperationException(
                "The protected Contract network probe address must be a non-loopback IP address.");
        }
        Require(
            int.TryParse(portText, out int port) && port is >= 1 and <= 65535,
            "The protected Contract network probe port is invalid.");

        using var client = new TcpClient(address.AddressFamily);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            client.ConnectAsync(address, port, timeout.Token).AsTask().GetAwaiter().GetResult();
        }
        catch (SocketException)
        {
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        throw new InvalidOperationException("The Godot Contract process retained non-loopback network access.");
    }

    private static void WriteSuccessMarker()
    {
        string? markerPath = System.Environment.GetEnvironmentVariable(
            "NINJASLAYER_CONTRACT_SUCCESS_MARKER");
        if (string.IsNullOrWhiteSpace(markerPath))
        {
            return;
        }

        string fullPath = Path.GetFullPath(markerPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        System.IO.File.WriteAllText(fullPath, "passed\n");
    }

    private static void VerifyPreparedLifecyclePublishers()
    {
        Require(RitsuLibFramework.IsInitialized, "RitsuLib did not complete framework initialization.");
        Type[] eventTypes =
        [
            typeof(CardMovedBetweenPilesEvent),
            typeof(RunLoadedEvent),
            typeof(CombatStartingEvent)
        ];

        IDisposable[] subscriptions =
        [
            RitsuLibFramework.SubscribeLifecycle<CardMovedBetweenPilesEvent>(static _ => { }, false),
            RitsuLibFramework.SubscribeLifecycle<RunLoadedEvent>(static _ => { }, false),
            RitsuLibFramework.SubscribeLifecycle<CombatStartingEvent>(static _ => { }, false)
        ];
        try
        {
            Require(subscriptions.Length == eventTypes.Length, "Prepared lifecycle subscriptions were not created.");
        }
        finally
        {
            foreach (IDisposable subscription in subscriptions.Reverse())
            {
                subscription.Dispose();
                subscription.Dispose();
            }
        }

        Assembly ritsuAssembly = typeof(RitsuLibFramework).Assembly;
        foreach (Type eventType in eventTypes)
        {
            Type publisherPatch = ResolveLifecyclePublisherPatch(ritsuAssembly, eventType);
            MethodInfo getTargets = publisherPatch.GetMethod(
                "GetTargets",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException(publisherPatch.FullName, nameof(IPatchMethod.GetTargets));
            var targets = (ModPatchTarget[]?)(getTargets.Invoke(null, null))
                ?? throw new InvalidOperationException($"{publisherPatch.FullName}.GetTargets() returned null.");
            Require(targets.Length > 0, $"{eventType.Name} publisher does not declare any patch targets.");

            foreach (ModPatchTarget target in targets)
            {
                Require(
                    target.HarmonyMethodType == MethodType.Normal,
                    $"{eventType.Name} publisher uses an unsupported Harmony target type.");
                MethodInfo? original = target.ParameterTypes is null
                    ? AccessTools.Method(target.TargetType, target.MethodName)
                    : AccessTools.Method(target.TargetType, target.MethodName, target.ParameterTypes);
                Require(original is not null, $"Unable to resolve {eventType.Name} publisher target {target}.");
                Patches patchInfo = Harmony.GetPatchInfo(original!)
                    ?? throw new InvalidOperationException(
                        $"Harmony did not report the {eventType.Name} publisher target {target}.");
                Patch[] matching = patchInfo.Prefixes
                    .Concat(patchInfo.Postfixes)
                    .Concat(patchInfo.Transpilers)
                    .Concat(patchInfo.Finalizers)
                    .Where(patch =>
                        patch.PatchMethod.DeclaringType == publisherPatch
                        && patch.PatchMethod.Module.Assembly == ritsuAssembly)
                    .ToArray();
                Require(
                    matching.Length == 1,
                    $"{eventType.Name} publisher target {target} has {matching.Length} matching RitsuLib bindings.");
            }
        }
    }

    private static Type ResolveLifecyclePublisherPatch(Assembly ritsuAssembly, Type eventType)
    {
        Type[] matches = ritsuAssembly.GetTypes()
            .Where(type =>
                !type.IsAbstract
                && typeof(IPatchMethod).IsAssignableFrom(type)
                && TypeTreeCreatesEvent(type, eventType))
            .ToArray();
        Require(
            matches.Length == 1,
            $"Expected one RitsuLib IPatchMethod publisher for {eventType.Name}, found {matches.Length}.");
        return matches[0];
    }

    private static bool TypeTreeCreatesEvent(Type type, Type eventType)
    {
        foreach (MethodBase method in type.GetMethods(
                     BindingFlags.Public | BindingFlags.NonPublic |
                     BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (method.GetMethodBody() is null)
            {
                continue;
            }

            HarmonyIlMethodBody body = method.GetOriginalIl(resolveAsync: false);
            if (body.Instructions.Any(instruction =>
                    instruction.opcode == System.Reflection.Emit.OpCodes.Newobj
                    && instruction.operand is ConstructorInfo constructor
                    && constructor.DeclaringType == eventType))
            {
                return true;
            }
        }

        return type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Any(nested => TypeTreeCreatesEvent(nested, eventType));
    }

    private static void VerifyOrobasSeaGlassPatchContract()
    {
        MethodInfo target = AccessTools.Method(
            typeof(Orobas),
            "GenerateInitialOptions",
            Type.EmptyTypes)
            ?? throw new MissingMethodException(
                typeof(Orobas).FullName,
                "GenerateInitialOptions");
        Require(
            target.ReturnType == typeof(IReadOnlyList<MegaCrit.Sts2.Core.Events.EventOption>),
            "Orobas.GenerateInitialOptions() no longer returns IReadOnlyList<EventOption>.");

        ModPatcher patcher = CreatePatcher("orobas-sea-glass-contract");
        patcher.RegisterPatch<OrobasSeaGlassCharacterPatch>();
        try
        {
            Require(patcher.PatchAll(), "ModPatcher rejected the production Orobas Sea Glass patch.");
            Patches info = Harmony.GetPatchInfo(target)
                ?? throw new InvalidOperationException("Harmony did not report the Orobas Sea Glass patch.");
            Patch postfix = info.Postfixes.Single(item => item.owner == patcher.PatcherId);
            Require(
                postfix.PatchMethod.DeclaringType == typeof(OrobasSeaGlassCharacterPatch)
                && postfix.PatchMethod.Name == nameof(OrobasSeaGlassCharacterPatch.Postfix),
                "Harmony bound an unexpected Orobas Sea Glass Postfix.");
        }
        finally
        {
            patcher.UnpatchAll();
            patcher.UnpatchAll();
        }

        Patches? remaining = Harmony.GetPatchInfo(target);
        Require(
            !(remaining?.Owners.Contains(patcher.PatcherId) ?? false),
            "Orobas Sea Glass patch ownership remained after idempotent unload.");
    }

    private static void VerifyBlackFlameDamagePatchContract()
    {
        ModPatchTarget descriptor = BlackFlameDamagePatch.GetTargets().Single();
        MethodInfo target = AccessTools.Method(
            descriptor.TargetType,
            descriptor.MethodName,
            descriptor.ParameterTypes)
            ?? throw new MissingMethodException(
                descriptor.TargetType.FullName,
                descriptor.MethodName);
        Require(
            target.ReturnType == typeof(Task<IEnumerable<DamageResult>>),
            "The final CreatureCmd.Damage entry no longer returns Task<IEnumerable<DamageResult>>.");

        ModPatcher patcher = CreatePatcher("black-flame-final-damage-contract");
        patcher.RegisterPatch<BlackFlameDamagePatch>();
        try
        {
            Require(patcher.PatchAll(), "ModPatcher rejected the production Black Flame damage patch.");
            Patches info = Harmony.GetPatchInfo(target)
                ?? throw new InvalidOperationException("Harmony did not report the Black Flame damage patch.");
            Patch postfix = info.Postfixes.Single(item => item.owner == patcher.PatcherId);
            Require(
                postfix.PatchMethod.DeclaringType == typeof(BlackFlameDamagePatch)
                && postfix.PatchMethod.Name == nameof(BlackFlameDamagePatch.Postfix),
                "Harmony bound an unexpected Black Flame damage Postfix.");
        }
        finally
        {
            patcher.UnpatchAll();
            patcher.UnpatchAll();
        }

        Patches? remaining = Harmony.GetPatchInfo(target);
        Require(
            !(remaining?.Owners.Contains(patcher.PatcherId) ?? false),
            "Black Flame damage patch ownership remained after idempotent unload.");
    }

    private static void VerifyProductionDynamicPatchContracts()
    {
        VerifyProductionDynamicPatchSet(
            "combat-presentation-pacing-contract",
            CombatPresentationPacingPatch.CreateDynamicPatches(),
            expectedCount: 3,
            typeof(CombatPresentationPacingPatch));
        VerifyProductionDynamicPatchSet(
            "rapid-card-resolution-contract",
            RapidCardResolutionStateMachinePatch.CreateDynamicPatches(),
            expectedCount: 2,
            typeof(RapidCardResolutionStateMachinePatch));
    }

    private static void VerifyProductionDynamicPatchSet(
        string capability,
        DynamicPatchInfo[] dynamicPatches,
        int expectedCount,
        Type expectedPatchType)
    {
        Require(
            dynamicPatches.Length == expectedCount,
            $"{capability} resolved {dynamicPatches.Length} targets instead of {expectedCount}.");
        Require(
            dynamicPatches.Select(patch => patch.Id).Distinct(StringComparer.Ordinal).Count() == expectedCount,
            $"{capability} returned duplicate dynamic patch IDs.");
        Require(
            dynamicPatches.All(patch => patch.IsCritical && patch.OriginalMethod.Name == "MoveNext"),
            $"{capability} did not resolve critical async MoveNext targets.");

        ModPatcher patcher = CreatePatcher(capability);
        try
        {
            Require(
                patcher.ApplyDynamicPatches(dynamicPatches, rollbackOnCriticalFailure: true),
                $"RitsuLib rejected the production {capability} dynamic patches.");
            Require(
                patcher.RegisteredDynamicPatchCount == expectedCount
                && patcher.AppliedPatchCount == expectedCount,
                $"{capability} did not apply every registered dynamic patch.");

            foreach (DynamicPatchInfo dynamicPatch in dynamicPatches)
            {
                Patches info = Harmony.GetPatchInfo(dynamicPatch.OriginalMethod)
                    ?? throw new InvalidOperationException(
                        $"Harmony did not report {capability} target {dynamicPatch.Id}.");
                Patch transpiler = info.Transpilers.Single(item => item.owner == patcher.PatcherId);
                Require(
                    transpiler.PatchMethod.DeclaringType == expectedPatchType
                    && (transpiler.PatchMethod.Name == "Transpiler"
                        || expectedPatchType == typeof(RapidCardResolutionStateMachinePatch)
                        && transpiler.PatchMethod.Name.StartsWith("Transpile", StringComparison.Ordinal)),
                    $"Harmony bound an unexpected transpiler for {dynamicPatch.Id}.");
            }
        }
        finally
        {
            patcher.UnpatchAll();
            patcher.UnpatchAll();
        }

        foreach (DynamicPatchInfo dynamicPatch in dynamicPatches)
        {
            Patches? remaining = Harmony.GetPatchInfo(dynamicPatch.OriginalMethod);
            Require(
                !(remaining?.Owners.Contains(patcher.PatcherId) ?? false),
                $"{dynamicPatch.Id} retained Harmony ownership after idempotent unload.");
        }
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

    private static void VerifyFinisherProtectionTransaction()
    {
        var combatState = new CombatState();
        bool contextIsCurrent = true;

        Creature failedTarget = CreateCreature(combatState, currentHp: 1, maxHp: 10);
        var failedLedger = new FinisherDamageLedger([failedTarget], combatState, () => contextIsCurrent);
        decimal failedAmount = 10m;
        Require(
            failedLedger.TryProtect(failedTarget, committing: false, ref failedAmount, out FinisherProtectionToken? failedToken),
            "Lethal protection did not create a token.");
        Require(failedTarget.CurrentHp == 2 && failedAmount == 1m, "The temporary 1 -> 2 HP bump was not applied.");
        DamageResult skippedResult = failedTarget.LoseHpInternal(0m, ValueProp.Move);
        Require(
            !failedLedger.Confirm(failedToken!, skippedResult, originalRan: false),
            "A skipped original damage call confirmed its protection token.");
        failedLedger.FinalizeProtection(failedToken!);
        Require(failedTarget.CurrentHp == 1, "Finalizer did not roll back an intact temporary HP bump.");
        Require(failedLedger.DeferredDeaths.Count == 0, "An unconfirmed damage call registered a deferred death.");

        Creature confirmedTarget = CreateCreature(combatState, currentHp: 1, maxHp: 10);
        var confirmedLedger = new FinisherDamageLedger([confirmedTarget], combatState, () => contextIsCurrent);
        decimal confirmedAmount = 10m;
        Require(
            confirmedLedger.TryProtect(
                confirmedTarget,
                committing: false,
                ref confirmedAmount,
                out FinisherProtectionToken? confirmedToken),
            "Confirmed lethal protection did not create a token.");
        DamageResult result = confirmedTarget.LoseHpInternal(confirmedAmount, ValueProp.Move);
        Require(
            confirmedLedger.Confirm(confirmedToken!, result, originalRan: true),
            "The real DamageResult did not confirm its protection token.");
        confirmedLedger.PresentProtectedDamage(confirmedToken!, result);
        confirmedLedger.FinalizeProtection(confirmedToken!);
        Require(confirmedTarget.CurrentHp == 1, "Confirmed protection changed the protected target HP.");
        Require(confirmedLedger.DeferredDeaths.SetEquals([confirmedTarget]), "Confirmed lethal damage was not deferred exactly once.");

        Creature postfixFailureTarget = CreateCreature(combatState, currentHp: 1, maxHp: 10);
        var postfixFailureLedger = new FinisherDamageLedger(
            [postfixFailureTarget],
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
        Require(
            postfixFailureLedger.Confirm(postfixFailureToken!, postfixFailureResult, originalRan: true),
            "Postfix-failure damage did not confirm its protection token.");
        try
        {
            postfixFailureLedger.PresentProtectedDamage(postfixFailureToken!, postfixFailureResult);
            throw new InvalidOperationException("Injected Postfix failure did not propagate to the patch boundary.");
        }
        catch (InvalidOperationException ex) when (ex.Message == "injected-postfix-failure")
        {
        }
        postfixFailureLedger.FinalizeProtection(postfixFailureToken!);
        Require(postfixFailureTarget.CurrentHp == 1, "Postfix failure rolled a confirmed target back to its bumped HP.");
        Require(
            postfixFailureLedger.DeferredDeaths.SetEquals([postfixFailureTarget]),
            "Postfix failure lost the confirmed deferred death.");

        Creature partiallyMutatedTarget = CreateCreature(combatState, currentHp: 1, maxHp: 10);
        var partiallyMutatedLedger = new FinisherDamageLedger(
            [partiallyMutatedTarget],
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
        var staleLedger = new FinisherDamageLedger([staleTarget], combatState, () => contextIsCurrent);
        decimal staleAmount = 10m;
        Require(
            staleLedger.TryProtect(staleTarget, committing: false, ref staleAmount, out FinisherProtectionToken? staleToken),
            "Stale-combat protection did not create a token.");
        contextIsCurrent = false;
        staleLedger.FinalizeProtection(staleToken!);
        Require(staleTarget.CurrentHp == 2, "Finalizer wrote HP into a stale combat.");

        contextIsCurrent = true;
        int duplicatePresentations = 0;
        Creature duplicateTarget = CreateCreature(combatState, currentHp: 1, maxHp: 10);
        var duplicateLedger = new FinisherDamageLedger(
            [duplicateTarget],
            combatState,
            () => contextIsCurrent,
            (_, _) => duplicatePresentations++);
        decimal duplicateAmount = 10m;
        Require(
            duplicateLedger.TryProtect(
                duplicateTarget,
                committing: false,
                ref duplicateAmount,
                out FinisherProtectionToken? duplicateToken),
            "Duplicate-call protection did not create a token.");
        DamageResult duplicateResult = duplicateTarget.LoseHpInternal(duplicateAmount, ValueProp.Move);
        Require(
            duplicateLedger.Confirm(duplicateToken!, duplicateResult, originalRan: true),
            "The first confirmation did not consume its active protection token.");
        duplicateLedger.PresentProtectedDamage(duplicateToken!, duplicateResult);
        Require(
            !duplicateLedger.Confirm(duplicateToken!, duplicateResult, originalRan: true),
            "A consumed protection token confirmed twice.");
        duplicateLedger.FinalizeProtection(duplicateToken!);
        duplicateLedger.FinalizeProtection(duplicateToken!);
        Require(duplicatePresentations == 1, "A repeated protection callback presented damage more than once.");
        Require(
            duplicateLedger.DeferredDeaths.SetEquals([duplicateTarget]),
            "Repeated confirmation changed the deferred-death set.");

        int repeatedHitPresentations = 0;
        Creature repeatedHitTarget = CreateCreature(combatState, currentHp: 1, maxHp: 10);
        var repeatedHitLedger = new FinisherDamageLedger(
            [repeatedHitTarget],
            combatState,
            () => contextIsCurrent,
            (_, _) => repeatedHitPresentations++);
        for (int hit = 0; hit < 2; hit++)
        {
            decimal repeatedAmount = 10m;
            Require(
                repeatedHitLedger.TryProtect(
                    repeatedHitTarget,
                    committing: false,
                    ref repeatedAmount,
                    out FinisherProtectionToken? repeatedToken),
                $"Repeated lethal hit {hit + 1} did not create a protection token.");
            DamageResult repeatedResult = repeatedHitTarget.LoseHpInternal(repeatedAmount, ValueProp.Move);
            Require(
                repeatedHitLedger.Confirm(repeatedToken!, repeatedResult, originalRan: true),
                $"Repeated lethal hit {hit + 1} did not confirm its protection token.");
            repeatedHitLedger.PresentProtectedDamage(repeatedToken!, repeatedResult);
            repeatedHitLedger.FinalizeProtection(repeatedToken!);
        }

        Require(repeatedHitTarget.CurrentHp == 1, "Repeated protected lethal hits changed the target's protected HP.");
        Require(repeatedHitPresentations == 2, "Repeated lethal hits did not preserve both damage presentations.");
        Require(
            repeatedHitLedger.DeferredDeaths.SetEquals([repeatedHitTarget]),
            "Repeated lethal hits registered the same deferred death more than once.");
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
