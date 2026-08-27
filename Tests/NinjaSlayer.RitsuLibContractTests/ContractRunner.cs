using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Net;
using System.Net.Sockets;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Afflictions;
using NinjaSlayer.Code.Commands;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Feedback;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Code.Prepared;
using NinjaSlayer.Content;
using STS2RitsuLib;
using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.Patching.Models;
using STS2RitsuLib.RunData;
using STS2RitsuLib.Scaffolding.Visuals.Definition;

namespace NinjaSlayer.RitsuLibContractTests;

public partial class ContractRunner : Node
{
    private const int ExpectedRequiredPatchTargetCount = 95;
    private const int ExpectedCriticalRequiredPatchTargetCount = 62;
    private static readonly List<ModPatcher> CapturedPatchers = [];
    private static Assembly? _productAssembly;
    private static ModPatchInfo[]? _capturedRequiredPatches;
    private static string? _patcherFailureName;
    private static readonly Dictionary<Creature, int> ProductFinisherKillCalls =
        new(ReferenceEqualityComparer.Instance);
    private static readonly List<Exception> ProductFinisherKillFailures = [];
    private static bool _productFinisherContextIsCurrent = true;
    private static int _productFinisherCleanupCalls;
    private static ProductFinisherKillFaultMode _productFinisherKillFaultMode;
    private static Exception? _productFinisherPresentationFailure;
    private static int _productFinisherPresentationTargetCalls;
    private static int _productFinisherUnregisterCalls;
    private static ProductPrepareFaultMode _productPrepareFaultMode;
    private static Func<object, CancellationToken, Task>? _productTransitionAnimationFactory;
    private static bool _productTransitionForceWatchdogTimeout;
    private static int _productTransitionWatchdogStartedAfterDispose;

    public override void _Ready()
    {
        try
        {
            VerifyOutboundNetworkIsolation();
            if (!RitsuLibFramework.IsInitialized)
            {
                RitsuLibFramework.Initialize();
            }
            VerifyPreparedOwnershipContracts();
            VerifyProductionPatchTransactions();
            VerifyOrobasSeaGlassPatchContract();
            VerifyBlackFlameDamagePatchContract();
            VerifyProductionDynamicPatchContracts();
            VerifyFinalizerOrderingAndTypedState();
            VerifyRunOriginalContract();
            VerifyOriginalFeedbackStreamOwnership();
            VerifyFinisherProtectionTransaction();
            VerifyFinisherSessionCompletionContracts();
            VerifyTransitionOwnershipContracts();
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

    private static void VerifyPreparedOwnershipContracts()
    {
        ModelDb.Inject(typeof(StrikeIronclad));
        ModelDb.Inject(typeof(PreparedAffliction));
        ModelDb.Inject(typeof(DampCultist));

        PreparedSafetyLifecycle lifecycle = PreparedSafetyLifecycle.Subscribe();
        Harmony faultHarmony = PreparedFaultInjection.Install();
        try
        {
            VerifyPreparedQueueAndDrawExit();
            VerifyPreparedQueueInvariantFailure();
            VerifyPreparedQueueDuplicateReferenceFailure();
            VerifyPreparedFailureRollback(
                PreparedFaultMode.UnconfirmedAdd,
                "unconfirmed add",
                "not confirmed");
            VerifyPreparedFailureRollback(
                PreparedFaultMode.RemoveOnce,
                "remove failure",
                "injected-remove");
            VerifyPreparedFailureRollback(
                PreparedFaultMode.DrawAddOnce,
                "add failure",
                "injected-draw-add");
            VerifyPreparedRepositionRollback();
            VerifyPreparedFailureRollback(
                PreparedFaultMode.UnconfirmedAfterMutation,
                "postcondition failure",
                "not confirmed");
            VerifyPreparedRollbackFailure();
            VerifyPreparedCallerFailurePropagation();
        }
        finally
        {
            PreparedFaultInjection.Reset();
            faultHarmony.UnpatchAll(faultHarmony.Id);
            lifecycle.Dispose();
            lifecycle.Dispose();
        }
    }

    private static void VerifyPreparedQueueAndDrawExit()
    {
        using var fixture = new PreparedCombatFixture();
        CardModel[] cards = Enumerable.Range(0, 4)
            .Select(index => fixture.CreateCard(index == 0 ? PileType.Draw : PileType.Discard))
            .ToArray();
        int firstAfflictionChanges = 0;
        cards[0].AfflictionChanged += () => firstAfflictionChanges++;

        for (int index = 0; index < cards.Length; index++)
        {
            Require(
                PrepareCmd.Apply(cards[index]).GetAwaiter().GetResult(),
                $"Prepared application {index + 1} was rejected.");
            RequirePreparedQueue(fixture, cards.Take(index + 1).ToArray());
        }

        Require(firstAfflictionChanges == 1, "Prepared application changed the first affliction more than once.");
        CardPileAddResult drawExit = CardPileCmd.Add(
                cards[0],
                PileType.Hand.GetPile(fixture.Player))
            .GetAwaiter()
            .GetResult();
        Require(drawExit.success, "Prepared Draw-exit fixture did not move the card.");
        Require(cards[0].Affliction is null, "Confirmed Draw-exit did not clear Prepared.");
        Require(firstAfflictionChanges == 2, "Confirmed Draw-exit did not clear Prepared exactly once.");

        CardPileCmd.Add(cards[0], PileType.Discard.GetPile(fixture.Player))
            .GetAwaiter()
            .GetResult();
        Require(firstAfflictionChanges == 2, "A later non-Draw pile event repeated Prepared cleanup.");
        RequirePreparedQueue(fixture, cards.Skip(1).ToArray());

        int removalEvents = 0;
        using (RitsuLibFramework.SubscribeLifecycle<CardMovedBetweenPilesEvent>(evt =>
               {
                   if (ReferenceEquals(evt.Card, cards[1]) && evt.PreviousPile == PileType.Draw)
                   {
                       removalEvents++;
                   }
               }, replayCurrentState: false))
        {
            CardPileCmd.RemoveFromCombat(cards[1], skipVisuals: true)
                .GetAwaiter()
                .GetResult();
        }
        Require(cards[1].Pile is null, "Prepared removal fixture retained a pile.");
        Require(removalEvents == 1, "Prepared removal did not publish one confirmed Draw-exit event.");
        Require(cards[1].Affliction is null, "Confirmed Draw removal did not clear Prepared.");
        RequirePreparedQueue(fixture, cards.Skip(2).ToArray());

        Require(
            !PrepareCmd.Apply(ModelDb.Card<StrikeIronclad>()).GetAwaiter().GetResult(),
            "Canonical card preparation was not rejected as a legal no-op.");
    }

    private static void VerifyPreparedQueueInvariantFailure()
    {
        using var fixture = new PreparedCombatFixture();
        CardModel prepared = fixture.CreateCard(PileType.Discard);
        Require(PrepareCmd.Apply(prepared).GetAwaiter().GetResult(), "Prepared prefix fixture was rejected.");
        CardModel unprepared = fixture.CreateCard(PileType.Draw, index: 0);
        CardModel target = fixture.CreateCard(PileType.Discard);

        InvalidOperationException failure = ExpectException<InvalidOperationException>(
            () => PrepareCmd.Apply(target),
            "queue prefix");
        Require(
            failure.Message.Contains("queue prefix", StringComparison.OrdinalIgnoreCase),
            "Broken Prepared queue did not surface its invariant failure.");
        Require(target.Affliction is null, "Queue-prefix rejection mutated the target affliction.");
        Require(ReferenceEquals(target.Pile, PileType.Discard.GetPile(fixture.Player)),
            "Queue-prefix rejection moved the target card.");
        Require(ReferenceEquals(fixture.DrawPile.Cards[0], unprepared),
            "Queue-prefix rejection rewrote the broken producer state.");
    }

    private static void VerifyPreparedQueueDuplicateReferenceFailure()
    {
        using var fixture = new PreparedCombatFixture();
        CardModel prepared = fixture.CreateCard(PileType.Discard);
        Require(PrepareCmd.Apply(prepared).GetAwaiter().GetResult(),
            "Prepared duplicate-reference fixture was rejected.");
        fixture.DiscardPile.AddInternal(prepared, index: -1, silent: true);
        CardModel target = fixture.CreateCard(PileType.Discard);

        _ = ExpectException<InvalidOperationException>(
            () => PrepareCmd.Apply(target),
            "exactly one pile reference");
        Require(target.Affliction is null,
            "Duplicate Prepared queue rejection mutated the target affliction.");
        Require(ReferenceEquals(target.Pile, fixture.DiscardPile),
            "Duplicate Prepared queue rejection moved the target card.");
    }

    private static void VerifyPreparedFailureRollback(
        PreparedFaultMode mode,
        string label,
        string failureFragment)
    {
        using var fixture = new PreparedCombatFixture();
        CardModel card = fixture.CreateCard(PileType.Discard);
        int originalIndex = fixture.DiscardPile.Cards.Count - 1;
        PreparedFaultInjection.Configure(mode, card);
        try
        {
            _ = ExpectException<InvalidOperationException>(
                () => PrepareCmd.Apply(card),
                failureFragment);
        }
        finally
        {
            PreparedFaultInjection.Reset();
        }

        RequireRestored(fixture, card, fixture.DiscardPile, originalIndex, label);
    }

    private static void VerifyPreparedRepositionRollback()
    {
        using var fixture = new PreparedCombatFixture();
        CardModel queued = fixture.CreateCard(PileType.Discard);
        Require(PrepareCmd.Apply(queued).GetAwaiter().GetResult(), "Reposition fixture queue setup failed.");
        CardModel card = fixture.CreateCard(PileType.Discard);
        int originalIndex = fixture.DiscardPile.Cards.Count - 1;

        PreparedFaultInjection.Configure(PreparedFaultMode.RepositionAddOnce, card);
        try
        {
            _ = ExpectException<InvalidOperationException>(
                () => PrepareCmd.Apply(card),
                "injected-reposition-add");
        }
        finally
        {
            PreparedFaultInjection.Reset();
        }

        RequireRestored(fixture, card, fixture.DiscardPile, originalIndex, "reposition failure");
        RequirePreparedQueue(fixture, [queued]);
    }

    private static void VerifyPreparedRollbackFailure()
    {
        using var fixture = new PreparedCombatFixture();
        CardModel card = fixture.CreateCard(PileType.Discard);
        PreparedFaultInjection.Configure(PreparedFaultMode.DrawAndRollbackAdd, card);
        AggregateException failure;
        try
        {
            failure = ExpectException<AggregateException>(
                () => PrepareCmd.Apply(card),
                "transaction and rollback");
        }
        finally
        {
            PreparedFaultInjection.Reset();
        }

        Require(failure.InnerExceptions.Count == 2,
            "Prepared rollback failure did not preserve primary and rollback errors.");
        Require(failure.ToString().Contains("injected-draw-add", StringComparison.Ordinal),
            "Prepared rollback aggregate lost the primary add failure.");
        Require(failure.ToString().Contains("injected-rollback-add", StringComparison.Ordinal),
            "Prepared rollback aggregate lost the rollback failure.");
        Require(card.Affliction is null, "Failed Prepared rollback left its partial affliction behind.");
        Require(card.Pile is null, "Rollback-failure fixture unexpectedly reported a restored pile.");
    }

    private static void VerifyPreparedCallerFailurePropagation()
    {
        Assembly product = LoadProductAssembly();
        RequireMatchingProductMetadata(product, "NinjaSlayerHostChannel");
        RequireMatchingProductMetadata(product, "NinjaSlayerGameApiVersion");

        Type prepareType = product.GetType("NinjaSlayer.Code.Commands.PrepareCmd", throwOnError: true)!;
        MethodInfo prepare = AccessTools.Method(prepareType, "Apply", [typeof(CardModel)])
            ?? throw new MissingMethodException(prepareType.FullName, "Apply");
        var harmony = new Harmony($"NinjaSlayer.ContractTests.ProductPrepared.{Guid.NewGuid():N}");
        harmony.Patch(
            prepare,
            prefix: new HarmonyMethod(typeof(ContractRunner), nameof(PrefixProductPrepare)));
        try
        {
            VerifyPreparedPowerFailure(product);
            VerifyGeneratedShurikenPrepareFailure(product);
        }
        finally
        {
            _productPrepareFaultMode = ProductPrepareFaultMode.None;
            harmony.UnpatchAll(harmony.Id);
        }
    }

    private static void VerifyPreparedPowerFailure(Assembly product)
    {
        using var fixture = new PreparedCombatFixture();
        Type powerType = product.GetType(
            "NinjaSlayer.Powers.NextDiscardPreparedPower",
            throwOnError: true)!;
        ModelDb.Inject(powerType);
        MethodInfo powerLookup = typeof(ModelDb)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == nameof(ModelDb.Power)
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 0);
        var canonical = (PowerModel)(powerLookup.MakeGenericMethod(powerType).Invoke(null, null)
            ?? throw new InvalidOperationException("Unable to find the Prepared power contract model."));
        PowerModel power = canonical.ToMutable();
        power.ApplyInternal(fixture.Player.Creature, 2m);
        CardModel card = fixture.CreateCard(PileType.Discard);
        MethodInfo afterPileChange = AccessTools.Method(
                powerType,
                nameof(PowerModel.AfterCardChangedPiles),
                [typeof(CardModel), typeof(PileType), typeof(AbstractModel)])
            ?? throw new MissingMethodException(powerType.FullName, nameof(PowerModel.AfterCardChangedPiles));

        _productPrepareFaultMode = ProductPrepareFaultMode.Throw;
        try
        {
            _ = ExpectException<InvalidOperationException>(
                () => ((Task)afterPileChange.Invoke(
                    power,
                    [card, PileType.Hand, null])!).GetAwaiter().GetResult(),
                "injected-product-prepare");
            Require(power.Amount == 2,
                "Prepared power decremented after its Prepare transaction faulted.");
        }
        finally
        {
            _productPrepareFaultMode = ProductPrepareFaultMode.None;
            power.RemoveInternal();
        }
    }

    private static void VerifyGeneratedShurikenPrepareFailure(Assembly product)
    {
        using var fixture = new PreparedCombatFixture();
        Type shurikenType = product.GetType("NinjaSlayer.Cards.ShurikenCard", throwOnError: true)!;
        ModelDb.Inject(shurikenType);
        Type actionsType = product.GetType("NinjaSlayer.Content.NinjaSlayerActions", throwOnError: true)!;
        MethodInfo addGeneratedShuriken = AccessTools.Method(
                actionsType,
                "AddGeneratedShuriken",
                [
                    typeof(PlayerChoiceContext),
                    typeof(Player),
                    typeof(int),
                    typeof(PileType),
                    typeof(bool),
                    typeof(CardPilePosition),
                    typeof(bool)
                ])
            ?? throw new MissingMethodException(actionsType.FullName, "AddGeneratedShuriken");

        _productPrepareFaultMode = ProductPrepareFaultMode.Reject;
        try
        {
            _ = ExpectException<InvalidOperationException>(
                () => ((Task)addGeneratedShuriken.Invoke(
                    null,
                    [
                        new ThrowingPlayerChoiceContext(),
                        fixture.Player,
                        1,
                        PileType.Draw,
                        false,
                        CardPilePosition.Bottom,
                        true
                    ])!).GetAwaiter().GetResult(),
                "Generated Shuriken was not prepared as requested");
        }
        finally
        {
            _productPrepareFaultMode = ProductPrepareFaultMode.None;
        }

        CardModel generated = fixture.DrawPile.Cards.Single(card => card.GetType() == shurikenType);
        fixture.TrackCard(generated);
        Require(generated.Affliction is null,
            "Rejected generated Shuriken preparation left a partial affliction.");
        Require(CountReferences(fixture.Player, generated) == 1,
            "Rejected generated Shuriken preparation duplicated the generated card.");
    }

    private static bool PrefixProductPrepare(ref Task<bool> __result)
    {
        switch (_productPrepareFaultMode)
        {
            case ProductPrepareFaultMode.Reject:
                __result = Task.FromResult(false);
                return false;
            case ProductPrepareFaultMode.Throw:
                __result = Task.FromException<bool>(
                    new InvalidOperationException("injected-product-prepare"));
                return false;
            default:
                return true;
        }
    }

    private static void RequireMatchingProductMetadata(Assembly product, string key)
    {
        string expected = ReadAssemblyMetadata(typeof(ContractRunner).Assembly, key);
        string actual = ReadAssemblyMetadata(product, key);
        Require(actual == expected,
            $"Product assembly metadata {key} was '{actual}', expected '{expected}'.");
    }

    private static string ReadAssemblyMetadata(Assembly assembly, string key) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == key)
            .Value
        ?? throw new InvalidOperationException($"Assembly metadata {key} has no value.");

    private static Assembly LoadProductAssembly()
    {
        if (_productAssembly is not null)
        {
            return _productAssembly;
        }

        string productPath = System.Environment.GetEnvironmentVariable(
                "NINJASLAYER_CONTRACT_PRODUCT_ASSEMBLY")
            ?? throw new InvalidOperationException(
                "Production ownership contracts require NINJASLAYER_CONTRACT_PRODUCT_ASSEMBLY.");
        AssemblyLoadContext context = AssemblyLoadContext.GetLoadContext(typeof(ContractRunner).Assembly)
            ?? throw new InvalidOperationException("The ContractRunner assembly has no load context.");
        _productAssembly = context.LoadFromAssemblyPath(Path.GetFullPath(productPath));
        return _productAssembly;
    }

    private static void VerifyProductionPatchTransactions()
    {
        Assembly product = LoadProductAssembly();
        ModPatchInfo[] requiredPatches = CaptureRequiredPatchesFromFailedInitialization(product);
        ValidateRequiredPatchManifest(product, requiredPatches);

        ModPatcher requiredPatcher = CreatePatcher("production-required-contract");
        requiredPatcher.RegisterPatches(requiredPatches);
        try
        {
            Require(requiredPatcher.PatchAll(),
                "The captured production required Patch transaction did not apply.");
            Require(requiredPatcher.AppliedPatchCount == requiredPatches.Length,
                "The production required Patch transaction did not apply every registered target.");
            RequirePatcherOwnsTargets(requiredPatcher, requiredPatches);

            VerifyOptionalFailureIsolation(
                product,
                requiredPatcher,
                requiredPatches,
                "BossBurstPresentationPatchGroup",
                "TransitionCorePatchGroup");
            VerifyOptionalFailureIsolation(
                product,
                requiredPatcher,
                requiredPatches,
                "TransitionCorePatchGroup",
                "BossBurstPresentationPatchGroup");
        }
        finally
        {
            requiredPatcher.UnpatchAll();
        }

        RequirePatcherReleasedTargets(requiredPatcher, requiredPatches);
    }

    private static ModPatchInfo[] CaptureRequiredPatchesFromFailedInitialization(Assembly product)
    {
        ResetPatchTransactionInstrumentation();
        _patcherFailureName = "Entry";
        Harmony instrumentation = InstallPatchTransactionInstrumentation();
        try
        {
            _ = ExpectException<InvalidOperationException>(
                () => InvokeProductEntryMethod(product, "Init"),
                "Required NinjaSlayer patch installation failed");
        }
        finally
        {
            instrumentation.UnpatchAll(instrumentation.Id);
            _patcherFailureName = null;
        }

        ModPatcher requiredPatcher = CapturedPatchers.Single(
            patcher => patcher.PatcherName == "Entry");
        ModPatchInfo[] requiredPatches = _capturedRequiredPatches
            ?? throw new InvalidOperationException(
                "The Entry required Patch transaction was not observed before fault injection.");
        Require(requiredPatches.Length > 0,
            "Entry registered no required production Patch targets.");
        Require(requiredPatcher.AppliedPatchCount == 0,
            "Entry retained required patches after its injected critical failure.");
        Require(CapturedPatchers.Count == 1,
            "Entry continued into optional Patch installation after required initialization failed.");
        RequirePatcherReleasedTargets(requiredPatcher, requiredPatches);
        return requiredPatches;
    }

    private static void ValidateRequiredPatchManifest(
        Assembly product,
        IReadOnlyList<ModPatchInfo> requiredPatches)
    {
        Require(requiredPatches.Count == ExpectedRequiredPatchTargetCount,
            $"The required Patch transaction registered {requiredPatches.Count} targets; " +
            $"expected {ExpectedRequiredPatchTargetCount}.");
        Require(requiredPatches.Count(patch => patch.IsCritical)
                == ExpectedCriticalRequiredPatchTargetCount,
            "The required Patch transaction changed its critical target markers.");
        Require(requiredPatches.Select(patch => patch.Id).Distinct().Count() == requiredPatches.Count,
            "The required Patch transaction contains duplicate target IDs.");
        foreach (ModPatchInfo patch in requiredPatches)
        {
            Require(ReferenceEquals(patch.PatchType.Assembly, product),
                $"Required Patch {patch.Id} did not come from the production assembly.");
            Require(PatchTargetMethodResolver.Resolve(patch) is not null,
                $"Required Patch target {FormatPatchTarget(patch)} did not resolve.");
        }

        string? manifestPath = System.Environment.GetEnvironmentVariable(
            "NINJASLAYER_CONTRACT_PATCH_MANIFEST");
        if (!string.IsNullOrWhiteSpace(manifestPath))
        {
            string fullPath = Path.GetFullPath(manifestPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            IEnumerable<string> lines =
            [
                $"channel={ReadAssemblyMetadata(product, "NinjaSlayerHostChannel")}",
                $"gameApi={ReadAssemblyMetadata(product, "NinjaSlayerGameApiVersion")}",
                $"hostMvid={typeof(CombatState).Assembly.ManifestModule.ModuleVersionId:D}",
                $"count={requiredPatches.Count}",
                .. requiredPatches.Select(FormatPatchTarget).Order(StringComparer.Ordinal)
            ];
            System.IO.File.WriteAllLines(fullPath, lines);
        }
    }

    private static string FormatPatchTarget(ModPatchInfo patch)
    {
        string parameters = patch.ParameterTypes is { Length: > 0 }
            ? string.Join(",", patch.ParameterTypes.Select(type => type.FullName))
            : "";
        return string.Join(
            "|",
            patch.Id,
            patch.IsCritical,
            patch.PatchType.FullName,
            patch.TargetType?.FullName,
            patch.MethodName,
            parameters,
            patch.HarmonyMethodType,
            patch.IgnoreIfTargetMissing);
    }

    private static void VerifyOptionalFailureIsolation(
        Assembly product,
        ModPatcher requiredPatcher,
        IReadOnlyList<ModPatchInfo> requiredPatches,
        string failingPatcherName,
        string survivingPatcherName)
    {
        ResetPatchTransactionInstrumentation();
        _patcherFailureName = failingPatcherName;
        Harmony instrumentation = InstallPatchTransactionInstrumentation();
        try
        {
            InvokeProductEntryMethod(product, "InstallOptionalPresentations");
        }
        finally
        {
            instrumentation.UnpatchAll(instrumentation.Id);
            _patcherFailureName = null;
        }

        try
        {
            ModPatcher failedPatcher = CapturedPatchers.Single(
                patcher => patcher.PatcherName == failingPatcherName);
            ModPatcher survivingPatcher = CapturedPatchers.Single(
                patcher => patcher.PatcherName == survivingPatcherName);
            Require(failedPatcher.AppliedPatchCount == 0,
                $"Optional Patch transaction {failingPatcherName} retained partial patches.");
            Require(survivingPatcher.RegisteredPatchCount > 0
                && survivingPatcher.AppliedPatchCount == survivingPatcher.RegisteredPatchCount,
                $"Failure of {failingPatcherName} prevented {survivingPatcherName} from applying.");
            RequirePatcherOwnsTargets(requiredPatcher, requiredPatches);
        }
        finally
        {
            for (int index = CapturedPatchers.Count - 1; index >= 0; index--)
            {
                CapturedPatchers[index].UnpatchAll();
            }
        }

        RequirePatcherOwnsTargets(requiredPatcher, requiredPatches);
    }

    private static Harmony InstallPatchTransactionInstrumentation()
    {
        MethodInfo createPatcher = typeof(RitsuLibFramework)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == nameof(RitsuLibFramework.CreatePatcher)
                && method.ReturnType == typeof(ModPatcher));
        MethodInfo patchAll = AccessTools.Method(
                typeof(ModPatcher),
                nameof(ModPatcher.PatchAll),
                Type.EmptyTypes)
            ?? throw new MissingMethodException(typeof(ModPatcher).FullName, nameof(ModPatcher.PatchAll));
        var harmony = new Harmony($"NinjaSlayer.ContractTests.PatchTransactions.{Guid.NewGuid():N}");
        harmony.Patch(
            createPatcher,
            postfix: new HarmonyMethod(typeof(ContractRunner), nameof(PostfixCreatePatcher)));
        harmony.Patch(
            patchAll,
            prefix: new HarmonyMethod(typeof(ContractRunner), nameof(PrefixPatchAll)));
        return harmony;
    }

    private static void PostfixCreatePatcher(ModPatcher __result) =>
        CapturedPatchers.Add(__result);

    private static void PrefixPatchAll(ModPatcher __instance)
    {
        if (!string.Equals(__instance.PatcherName, _patcherFailureName, StringComparison.Ordinal))
        {
            return;
        }

        if (_patcherFailureName == "Entry")
        {
            _capturedRequiredPatches = __instance.RegisteredPatches.ToArray();
        }

        _patcherFailureName = null;
        __instance.RegisterPatch<MissingCriticalPatch>();
    }

    private static void ResetPatchTransactionInstrumentation()
    {
        CapturedPatchers.Clear();
        _capturedRequiredPatches = null;
        _patcherFailureName = null;
    }

    private static void InvokeProductEntryMethod(Assembly product, string methodName)
    {
        Type entryType = product.GetType("NinjaSlayer.Scripts.Entry", throwOnError: true)!;
        MethodInfo method = AccessTools.Method(entryType, methodName, Type.EmptyTypes)
            ?? throw new MissingMethodException(entryType.FullName, methodName);
        method.Invoke(null, null);
    }

    private static void RequirePatcherOwnsTargets(
        ModPatcher patcher,
        IReadOnlyList<ModPatchInfo> patches)
    {
        foreach (MethodBase target in patches
            .Select(PatchTargetMethodResolver.Resolve)
            .OfType<MethodBase>()
            .Distinct())
        {
            Patches? info = Harmony.GetPatchInfo(target);
            Require(info?.Owners.Contains(patcher.PatcherId) == true,
                $"Patcher {patcher.PatcherName} does not own {target.DeclaringType?.FullName}.{target.Name}.");
        }
    }

    private static void RequirePatcherReleasedTargets(
        ModPatcher patcher,
        IReadOnlyList<ModPatchInfo> patches)
    {
        foreach (MethodBase target in patches
            .Select(PatchTargetMethodResolver.Resolve)
            .OfType<MethodBase>()
            .Distinct())
        {
            Patches? info = Harmony.GetPatchInfo(target);
            Require(info?.Owners.Contains(patcher.PatcherId) != true,
                $"Patcher {patcher.PatcherName} retained {target.DeclaringType?.FullName}.{target.Name}.");
        }
    }

    private static TException ExpectException<TException>(Action action, string messageFragment)
        where TException : Exception
    {
        Exception? observed = null;
        try
        {
            action();
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            observed = exception.InnerException;
        }
        catch (Exception exception)
        {
            observed = exception;
        }

        Require(observed is TException,
            $"Expected {typeof(TException).Name}, observed {observed?.GetType().Name ?? "no failure"}.");
        Require(observed!.ToString().Contains(messageFragment, StringComparison.OrdinalIgnoreCase),
            $"{typeof(TException).Name} did not contain '{messageFragment}'.");
        return (TException)observed;
    }

    private enum ProductPrepareFaultMode
    {
        None,
        Reject,
        Throw
    }

    private enum ProductFinisherKillFaultMode
    {
        None,
        FirstAttempt,
        EveryAttempt
    }

    private static void RequirePreparedQueue(PreparedCombatFixture fixture, IReadOnlyList<CardModel> expected)
    {
        Require(fixture.DrawPile.Cards.Count >= expected.Count, "Prepared draw pile is shorter than its queue.");
        for (int index = 0; index < expected.Count; index++)
        {
            CardModel card = expected[index];
            Require(ReferenceEquals(fixture.DrawPile.Cards[index], card),
                $"Prepared queue order differs at index {index}.");
            Require(card.Affliction is PreparedAffliction,
                $"Prepared queue card {index} lost its affliction.");
            Require(CountReferences(fixture.Player, card) == 1,
                $"Prepared queue card {index} does not have exactly one pile reference.");
        }

        Require(fixture.DrawPile.Cards.Skip(expected.Count).All(card => card.Affliction is not PreparedAffliction),
            "Prepared card exists outside the queue prefix.");
    }

    private static void RequireRestored(
        PreparedCombatFixture fixture,
        CardModel card,
        CardPile expectedPile,
        int expectedIndex,
        string label)
    {
        Require(card.Affliction is null, $"Prepared {label} rollback left an affliction.");
        Require(ReferenceEquals(card.Pile, expectedPile), $"Prepared {label} rollback restored the wrong pile.");
        Require(ReferenceEquals(expectedPile.Cards[expectedIndex], card),
            $"Prepared {label} rollback restored the wrong index.");
        Require(CountReferences(fixture.Player, card) == 1,
            $"Prepared {label} rollback did not restore one card reference.");
    }

    private static TException ExpectException<TException>(Func<Task<bool>> action, string messageFragment)
        where TException : Exception
    {
        Exception? observed = null;
        try
        {
            _ = action().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            observed = exception;
        }

        Require(observed is TException,
            $"Expected {typeof(TException).Name}, observed {observed?.GetType().Name ?? "no failure"}.");
        Require(observed!.ToString().Contains(messageFragment, StringComparison.OrdinalIgnoreCase),
            $"{typeof(TException).Name} did not contain '{messageFragment}'.");
        return (TException)observed;
    }

    private static int CountReferences(Player player, CardModel card) =>
        player.Piles.Sum(pile => pile.Cards.Count(candidate => ReferenceEquals(candidate, card)));

    private enum PreparedFaultMode
    {
        None,
        UnconfirmedAdd,
        UnconfirmedAfterMutation,
        RemoveOnce,
        DrawAddOnce,
        RepositionAddOnce,
        DrawAndRollbackAdd
    }

    private static class PreparedFaultInjection
    {
        private static PreparedFaultMode _mode;
        private static CardModel? _target;
        private static int _drawAddCount;

        public static Harmony Install()
        {
            var harmony = new Harmony($"NinjaSlayer.ContractTests.Prepared.{Guid.NewGuid():N}");
            MethodInfo add = AccessTools.Method(
                typeof(CardPileCmd),
                nameof(CardPileCmd.Add),
                [typeof(CardModel), typeof(CardPile), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)])
                ?? throw new MissingMethodException(typeof(CardPileCmd).FullName, nameof(CardPileCmd.Add));
            MethodInfo addInternal = AccessTools.Method(
                typeof(CardPile),
                nameof(CardPile.AddInternal),
                [typeof(CardModel), typeof(int), typeof(bool)])
                ?? throw new MissingMethodException(typeof(CardPile).FullName, nameof(CardPile.AddInternal));
            MethodInfo removeInternal = AccessTools.Method(
                typeof(CardPile),
                nameof(CardPile.RemoveInternal),
                [typeof(CardModel), typeof(bool)])
                ?? throw new MissingMethodException(typeof(CardPile).FullName, nameof(CardPile.RemoveInternal));

            harmony.Patch(
                add,
                prefix: new HarmonyMethod(typeof(PreparedFaultInjection), nameof(PrefixAdd)),
                postfix: new HarmonyMethod(typeof(PreparedFaultInjection), nameof(PostfixAdd)));
            harmony.Patch(
                addInternal,
                prefix: new HarmonyMethod(typeof(PreparedFaultInjection), nameof(PrefixAddInternal)));
            harmony.Patch(
                removeInternal,
                prefix: new HarmonyMethod(typeof(PreparedFaultInjection), nameof(PrefixRemoveInternal)));
            return harmony;
        }

        public static void Configure(PreparedFaultMode mode, CardModel target)
        {
            _mode = mode;
            _target = target;
            _drawAddCount = 0;
        }

        public static void Reset()
        {
            _mode = PreparedFaultMode.None;
            _target = null;
            _drawAddCount = 0;
        }

        private static bool PrefixAdd(CardModel card, ref Task<CardPileAddResult> __result)
        {
            if (_mode != PreparedFaultMode.UnconfirmedAdd || !ReferenceEquals(card, _target))
            {
                return true;
            }

            _mode = PreparedFaultMode.None;
            __result = Task.FromResult(new CardPileAddResult
            {
                success = false,
                cardAdded = card,
                oldPile = card.Pile
            });
            return false;
        }

        private static void PostfixAdd(CardModel card, ref Task<CardPileAddResult> __result)
        {
            if (_mode == PreparedFaultMode.UnconfirmedAfterMutation && ReferenceEquals(card, _target))
            {
                __result = ReturnUnconfirmedAfterMutation(__result);
            }
        }

        private static async Task<CardPileAddResult> ReturnUnconfirmedAfterMutation(
            Task<CardPileAddResult> resultTask)
        {
            CardPileAddResult result = await resultTask;
            _mode = PreparedFaultMode.None;
            result.success = false;
            return result;
        }

        private static void PrefixRemoveInternal(CardModel card)
        {
            if (_mode != PreparedFaultMode.RemoveOnce || !ReferenceEquals(card, _target))
            {
                return;
            }

            _mode = PreparedFaultMode.None;
            throw new InvalidOperationException("injected-remove");
        }

        private static void PrefixAddInternal(CardPile __instance, CardModel card)
        {
            if (!ReferenceEquals(card, _target))
            {
                return;
            }

            if (_mode == PreparedFaultMode.DrawAddOnce && __instance.Type == PileType.Draw)
            {
                _mode = PreparedFaultMode.None;
                throw new InvalidOperationException("injected-draw-add");
            }

            if (_mode == PreparedFaultMode.RepositionAddOnce && __instance.Type == PileType.Draw)
            {
                _drawAddCount++;
                if (_drawAddCount == 2)
                {
                    _mode = PreparedFaultMode.None;
                    throw new InvalidOperationException("injected-reposition-add");
                }
            }

            if (_mode == PreparedFaultMode.DrawAndRollbackAdd)
            {
                if (__instance.Type == PileType.Draw)
                {
                    throw new InvalidOperationException("injected-draw-add");
                }

                if (__instance.Type == PileType.Discard)
                {
                    throw new InvalidOperationException("injected-rollback-add");
                }
            }
        }
    }

    private sealed class PreparedCombatFixture : IDisposable
    {
        private readonly List<CardModel> _cards = [];
        private readonly bool _previousTestMode;
        private readonly Action _restoreCombatManager;

        public CombatState CombatState { get; }
        public Player Player { get; }
        public CardPile DrawPile => PileType.Draw.GetPile(Player);
        public CardPile DiscardPile => PileType.Discard.GetPile(Player);

        public PreparedCombatFixture()
        {
            _previousTestMode = TestMode.IsOn;
            TestMode.IsOn = true;
            CombatState = new CombatState();
            Player = CreatePlayer(CombatState);
            AddLivingEnemy(CombatState);
            _restoreCombatManager = EnterCombat(CombatState);
        }

        public CardModel CreateCard(PileType pileType, int index = -1)
        {
            CardModel card = ModelDb.Card<StrikeIronclad>().ToMutable();
            CombatState.AddCard(card, Player);
            pileType.GetPile(Player).AddInternal(card, index, silent: true);
            _cards.Add(card);
            return card;
        }

        public void TrackCard(CardModel card) => _cards.Add(card);

        public void Dispose()
        {
            PreparedFaultInjection.Reset();
            foreach (CardModel card in _cards)
            {
                if (card.Affliction is not null)
                {
                    CardCmd.ClearAffliction(card);
                }

                while (card.Pile is { } pile)
                {
                    pile.RemoveInternal(card, silent: true);
                }

                if (CombatState.ContainsCard(card))
                {
                    CombatState.RemoveCard(card);
                }
            }

            Player.PlayerCombatState?.AfterCombatEnd();
            _restoreCombatManager();
            TestMode.IsOn = _previousTestMode;
        }

        private static Player CreatePlayer(CombatState combatState)
        {
            var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            SetField(player, "<Deck>k__BackingField", new CardPile(PileType.Deck));
            SetField(player, "_runState", NullRunState.Instance);
            var creature = new Creature(player, currentHp: 80, maxHp: 80)
            {
                CombatState = combatState
            };
            SetField(player, "<Creature>k__BackingField", creature);
            SetField(player, "<PlayerCombatState>k__BackingField", new PlayerCombatState(player));
            return player;
        }

        private static void AddLivingEnemy(CombatState combatState)
        {
            var enemy = new Creature(
                ModelDb.Monster<DampCultist>().ToMutable(),
                CombatSide.Enemy,
                slotName: null)
            {
                CombatState = combatState
            };
            combatState.AddCreature(enemy);
        }

        private static Action EnterCombat(CombatState combatState)
        {
            CombatManager manager = CombatManager.Instance;
            FieldInfo? turnStateField = AccessTools.Field(typeof(CombatManager), "_turnState");
            if (turnStateField is not null)
            {
                object? previous = turnStateField.GetValue(manager);
                Type turnStateType = turnStateField.FieldType;
                object turnState = Activator.CreateInstance(
                        turnStateType,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        binder: null,
                        args: [combatState],
                        culture: null)
                    ?? throw new InvalidOperationException("Unable to create preview CombatTurnState fixture.");
                turnStateType.GetProperty("IsInProgress")!.SetValue(turnState, true);
                turnStateType.GetProperty("IsStarting")!.SetValue(turnState, false);
                turnStateField.SetValue(manager, turnState);
                return () => turnStateField.SetValue(manager, previous);
            }

            FieldInfo stateField = AccessTools.Field(typeof(CombatManager), "_state")
                ?? throw new MissingFieldException(typeof(CombatManager).FullName, "_state");
            FieldInfo inProgressField = AccessTools.Field(
                    typeof(CombatManager),
                    "<IsInProgress>k__BackingField")
                ?? throw new MissingFieldException(typeof(CombatManager).FullName, "IsInProgress");
            FieldInfo startingField = AccessTools.Field(
                    typeof(CombatManager),
                    "<IsStarting>k__BackingField")
                ?? throw new MissingFieldException(typeof(CombatManager).FullName, "IsStarting");
            object? previousState = stateField.GetValue(manager);
            bool previousInProgress = (bool)inProgressField.GetValue(manager)!;
            bool previousStarting = (bool)startingField.GetValue(manager)!;
            stateField.SetValue(manager, combatState);
            inProgressField.SetValue(manager, true);
            startingField.SetValue(manager, false);
            return () =>
            {
                stateField.SetValue(manager, previousState);
                inProgressField.SetValue(manager, previousInProgress);
                startingField.SetValue(manager, previousStarting);
            };
        }

        private static void SetField(object instance, string name, object value)
        {
            FieldInfo field = AccessTools.Field(instance.GetType(), name)
                ?? throw new MissingFieldException(instance.GetType().FullName, name);
            field.SetValue(instance, value);
        }
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

    private static void VerifyFinisherSessionCompletionContracts()
    {
        Assembly product = LoadProductAssembly();
        Harmony instrumentation = InstallProductFinisherInstrumentation(product);
        try
        {
            VerifySuccessfulFinisherCompletion(product);
            VerifyFinisherEmergencyCommit(product);
            VerifyFinisherFinalCommitFailure(product);
            VerifyFinisherPresentationFailure(product);
            VerifyFinisherCleanupFailure(product);
            VerifyCombinedFinisherFailures(product);
            VerifyAfterCardPlayedFinisherCompletion(product);
            VerifyOnPlayWrapperFinisherCleanup(product);
            VerifyFinisherRoomExit(product);
            VerifyStaleFinisherCompletion(product);
            VerifyReverseFinisherCompletion(product);
        }
        finally
        {
            ResetProductFinisherRegistry(product);
            instrumentation.UnpatchAll(instrumentation.Id);
            ResetProductFinisherInstrumentation();
        }
    }

    private static void VerifySuccessfulFinisherCompletion(Assembly product)
    {
        ResetProductFinisherInstrumentation();
        using var fixture = new ProductFinisherSessionFixture(product, targetCount: 2, hitsPerTarget: 3);
        Task firstCompletion = fixture.Complete(playPose: false);
        Task repeatedCompletion = fixture.Complete(playPose: true);
        Require(ReferenceEquals(firstCompletion, repeatedCompletion),
            "Repeated Finisher completion did not return the original completion Task.");
        firstCompletion.GetAwaiter().GetResult();
        Require(fixture.Targets.All(target => target.IsDead),
            "Successful multi-target Finisher completion left a confirmed target alive.");
        Require(fixture.LivingDeferredDeathCount == 0,
            "Successful Finisher completion retained a living deferred death.");
        Require(fixture.Targets.All(target => ProductFinisherKillCalls[target] == 1),
            "Multi-hit Finisher completion committed a target death more than once.");
        RequireFinisherDetachedOnce(fixture, "successful repeated completion");
    }

    private static void VerifyFinisherEmergencyCommit(Assembly product)
    {
        ResetProductFinisherInstrumentation();
        _productFinisherKillFaultMode = ProductFinisherKillFaultMode.FirstAttempt;
        using var fixture = new ProductFinisherSessionFixture(product, targetCount: 1);
        Task completion = fixture.Complete(playPose: false);
        Task repeatedCompletion = fixture.Complete(playPose: false);
        Require(ReferenceEquals(completion, repeatedCompletion),
            "Repeated Finisher completion after a commit failure returned a different Task.");
        Exception failure = ExpectTaskFailure(completion, "injected-finisher-kill-1");
        Require(ProductFinisherKillFailures.Count == 1
            && ContainsExceptionReference(failure, ProductFinisherKillFailures[0]),
            "Emergency Finisher completion lost the original death-commit failure.");
        Require(fixture.Targets.Single().IsDead && fixture.LivingDeferredDeathCount == 0,
            "Emergency Finisher commit did not kill the still-living confirmed target.");
        Require(ProductFinisherKillCalls[fixture.Targets.Single()] == 2,
            "Emergency Finisher commit did not retry exactly once after the first Kill failure.");
        RequireFinisherDetachedOnce(fixture, "emergency completion");
    }

    private static void VerifyFinisherFinalCommitFailure(Assembly product)
    {
        ResetProductFinisherInstrumentation();
        _productFinisherKillFaultMode = ProductFinisherKillFaultMode.EveryAttempt;
        using var fixture = new ProductFinisherSessionFixture(product, targetCount: 1);
        Exception failure = ExpectTaskFailure(
            fixture.Complete(playPose: false),
            "injected-finisher-kill-1");
        Require(ProductFinisherKillFailures.Count == 2,
            "Final Finisher commit failure did not exercise primary and emergency Kill attempts.");
        Require(ProductFinisherKillFailures.All(expected => ContainsExceptionReference(failure, expected)),
            "Final Finisher commit failure did not preserve both original Kill exceptions.");
        Require(fixture.Targets.Single().IsAlive && fixture.LivingDeferredDeathCount == 1,
            "Failed Finisher completion hid its still-living confirmed target.");
        Require(ProductFinisherKillCalls[fixture.Targets.Single()] == 2,
            "Failed Finisher completion made an unexpected number of Kill attempts.");
        RequireFinisherDetachedOnce(fixture, "failed death commit");
    }

    private static void VerifyFinisherPresentationFailure(Assembly product)
    {
        ResetProductFinisherInstrumentation();
        _productFinisherPresentationFailure = new InvalidOperationException(
            "injected-finisher-presentation-target");
        using var fixture = new ProductFinisherSessionFixture(product, targetCount: 1);
        fixture.Complete(playPose: true).GetAwaiter().GetResult();
        Require(_productFinisherPresentationTargetCalls == 1,
            "Finisher pose contract did not reach the production presentation target lookup.");
        Require(fixture.Targets.Single().IsDead && fixture.LivingDeferredDeathCount == 0,
            "Presentation failure prevented the confirmed Finisher death from committing.");
        Require(ProductFinisherKillCalls[fixture.Targets.Single()] == 1,
            "Presentation fallback committed the confirmed target more than once.");
        RequireFinisherDetachedOnce(fixture, "presentation fallback");
    }

    private static void VerifyFinisherCleanupFailure(Assembly product)
    {
        ResetProductFinisherInstrumentation();
        var cleanupFailure = new InvalidOperationException("injected-finisher-cleanup");
        using var fixture = new ProductFinisherSessionFixture(product, targetCount: 1);
        fixture.FailCleanup(cleanupFailure);
        Exception failure = ExpectTaskFailure(
            fixture.Complete(playPose: false),
            cleanupFailure.Message);
        Require(ContainsExceptionReference(failure, cleanupFailure),
            "Finisher completion did not preserve the original cleanup exception.");
        Require(fixture.Targets.Single().IsDead && fixture.LivingDeferredDeathCount == 0,
            "Cleanup failure rolled back an already committed Finisher death.");
        RequireFinisherDetachedOnce(fixture, "cleanup failure");
    }

    private static void VerifyCombinedFinisherFailures(Assembly product)
    {
        ResetProductFinisherInstrumentation();
        _productFinisherKillFaultMode = ProductFinisherKillFaultMode.EveryAttempt;
        var cleanupFailure = new InvalidOperationException("injected-finisher-combined-cleanup");
        using var fixture = new ProductFinisherSessionFixture(product, targetCount: 1);
        fixture.FailCleanup(cleanupFailure);
        Exception failure = ExpectTaskFailure(
            fixture.Complete(playPose: false),
            "injected-finisher-kill-1");
        Require(ProductFinisherKillFailures.Count == 2
            && ProductFinisherKillFailures.All(expected => ContainsExceptionReference(failure, expected)),
            "Combined Finisher failure lost a primary or emergency death-commit exception.");
        Require(ContainsExceptionReference(failure, cleanupFailure),
            "Combined Finisher failure lost the cleanup exception.");
        Require(fixture.Targets.Single().IsAlive && fixture.LivingDeferredDeathCount == 1,
            "Combined Finisher failure reported a death that never committed.");
        RequireFinisherDetachedOnce(fixture, "combined death and cleanup failure");
    }

    private static void VerifyAfterCardPlayedFinisherCompletion(Assembly product)
    {
        ResetProductFinisherInstrumentation();
        using var fixture = new ProductFinisherSessionFixture(product, targetCount: 1);
        fixture.TransferToAfterCardPlayed();
        fixture.CompleteAfterCardPlayed(Task.CompletedTask).GetAwaiter().GetResult();
        Require(fixture.Targets.Single().IsDead && fixture.LivingDeferredDeathCount == 0,
            "AfterCardPlayed did not commit its pending Finisher death.");
        Require(ProductFinisherKillCalls[fixture.Targets.Single()] == 1,
            "AfterCardPlayed committed its pending Finisher death more than once.");
        RequireFinisherDetachedOnce(fixture, "AfterCardPlayed completion");
    }

    private static void VerifyOnPlayWrapperFinisherCleanup(Assembly product)
    {
        ResetProductFinisherInstrumentation();
        var cardFailure = new InvalidOperationException("injected-finisher-card-play");
        using var fixture = new ProductFinisherSessionFixture(product, targetCount: 1);
        fixture.TransferToAfterCardPlayed();
        Exception failure = ExpectTaskFailure(
            fixture.CleanupAfterCardPlay(Task.FromException(cardFailure)),
            cardFailure.Message);
        Require(ContainsExceptionReference(failure, cardFailure),
            "OnPlayWrapper cleanup did not preserve the original card failure.");
        Require(fixture.Targets.Single().IsDead && fixture.LivingDeferredDeathCount == 0,
            "OnPlayWrapper early failure skipped its pending Finisher death commit.");
        Require(ProductFinisherKillCalls[fixture.Targets.Single()] == 1,
            "OnPlayWrapper early-failure cleanup committed its target more than once.");
        RequireFinisherDetachedOnce(fixture, "OnPlayWrapper early failure");
    }

    private static void VerifyFinisherRoomExit(Assembly product)
    {
        ResetProductFinisherInstrumentation();
        using var fixture = new ProductFinisherSessionFixture(product, targetCount: 1);
        fixture.InvokeRoomTreeExiting();
        fixture.CompletionTask.GetAwaiter().GetResult();
        Require(fixture.Targets.Single().IsAlive,
            "Room-exit cancellation committed a deferred death into the departing combat.");
        Require(!ProductFinisherKillCalls.ContainsKey(fixture.Targets.Single()),
            "Room-exit cancellation called CreatureCmd.Kill.");
        RequireFinisherDetachedOnce(fixture, "room-exit cancellation");
    }

    private static void VerifyStaleFinisherCompletion(Assembly product)
    {
        ResetProductFinisherInstrumentation();
        _productFinisherContextIsCurrent = false;
        using var fixture = new ProductFinisherSessionFixture(product, targetCount: 1);
        fixture.MoveTargetsToNewCombat();
        _ = ExpectTaskFailure(
            fixture.Complete(playPose: false),
            "lost combat ownership");
        Require(fixture.Targets.Single().IsAlive,
            "Stale Finisher session killed a target now owned by another combat.");
        Require(!ProductFinisherKillCalls.ContainsKey(fixture.Targets.Single()),
            "Stale Finisher session called CreatureCmd.Kill in the new combat.");
        RequireFinisherDetachedOnce(fixture, "stale combat completion");
    }

    private static void VerifyReverseFinisherCompletion(Assembly product)
    {
        ResetProductFinisherInstrumentation();
        using var fixture = new ProductFinisherSessionFixture(
            product,
            targetCount: 1,
            scenario: "EnemyExecutesNinjaSlayer");
        fixture.Complete(playPose: false).GetAwaiter().GetResult();
        Require(fixture.Targets.Single().IsDead && fixture.LivingDeferredDeathCount == 0,
            "Reverse Finisher scenario left its confirmed target alive.");
        Require(ProductFinisherKillCalls[fixture.Targets.Single()] == 1,
            "Reverse Finisher scenario committed its target more than once.");
        RequireFinisherDetachedOnce(fixture, "reverse completion");
    }

    private static void RequireFinisherDetachedOnce(
        ProductFinisherSessionFixture fixture,
        string label)
    {
        Require(_productFinisherCleanupCalls == 1,
            $"Finisher {label} did not restore resources exactly once.");
        Require(_productFinisherUnregisterCalls == 1 && !fixture.IsRegistered,
            $"Finisher {label} did not detach registry ownership exactly once.");
    }

    private static Exception ExpectTaskFailure(Task task, string messageFragment)
    {
        Exception? observed = null;
        try
        {
            task.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            observed = ex;
        }

        Require(observed != null, "Expected a faulted Task, observed successful completion.");
        Require(observed!.ToString().Contains(messageFragment, StringComparison.OrdinalIgnoreCase),
            $"Task failure did not contain '{messageFragment}'.");
        return observed;
    }

    private static bool ContainsExceptionReference(Exception observed, Exception expected)
    {
        if (ReferenceEquals(observed, expected))
        {
            return true;
        }

        if (observed is AggregateException aggregate
            && aggregate.InnerExceptions.Any(inner => ContainsExceptionReference(inner, expected)))
        {
            return true;
        }

        return observed.InnerException != null
            && ContainsExceptionReference(observed.InnerException, expected);
    }

    private static Harmony InstallProductFinisherInstrumentation(Assembly product)
    {
        Type sessionType = ProductType(product, "NinjaSlayer.Code.ExternalAnimations.FinisherSession");
        Type registryType = ProductType(product, "NinjaSlayer.Code.ExternalAnimations.FinisherSessionRegistry");
        MethodInfo isCurrentContext = AccessTools.Method(sessionType, "IsCurrentCombatContext", Type.EmptyTypes)
            ?? throw new MissingMethodException(sessionType.FullName, "IsCurrentCombatContext");
        MethodInfo restoreResources = AccessTools.Method(sessionType, "RestoreResourcesCore", [typeof(bool)])
            ?? throw new MissingMethodException(sessionType.FullName, "RestoreResourcesCore");
        MethodInfo unregister = AccessTools.Method(registryType, "UnregisterSession", [sessionType])
            ?? throw new MissingMethodException(registryType.FullName, "UnregisterSession");
        MethodInfo kill = AccessTools.Method(
                typeof(CreatureCmd),
                nameof(CreatureCmd.Kill),
                [typeof(Creature), typeof(bool)])
            ?? throw new MissingMethodException(typeof(CreatureCmd).FullName, nameof(CreatureCmd.Kill));
        MethodInfo getCreatureNode = AccessTools.Method(
                typeof(NCombatRoom),
                nameof(NCombatRoom.GetCreatureNode),
                [typeof(Creature)])
            ?? throw new MissingMethodException(
                typeof(NCombatRoom).FullName,
                nameof(NCombatRoom.GetCreatureNode));

        var harmony = new Harmony($"NinjaSlayer.ContractTests.FinisherSession.{Guid.NewGuid():N}");
        harmony.Patch(
            isCurrentContext,
            prefix: new HarmonyMethod(typeof(ContractRunner), nameof(PrefixProductFinisherCurrentContext)));
        harmony.Patch(
            restoreResources,
            prefix: new HarmonyMethod(typeof(ContractRunner), nameof(PrefixProductFinisherCleanup)));
        harmony.Patch(
            unregister,
            prefix: new HarmonyMethod(typeof(ContractRunner), nameof(PrefixProductFinisherUnregister)));
        harmony.Patch(
            kill,
            prefix: new HarmonyMethod(typeof(ContractRunner), nameof(PrefixProductFinisherKill)));
        harmony.Patch(
            getCreatureNode,
            prefix: new HarmonyMethod(
                typeof(ContractRunner),
                nameof(PrefixProductFinisherPresentationTarget)));
        return harmony;
    }

    private static bool PrefixProductFinisherCurrentContext(ref bool __result)
    {
        __result = _productFinisherContextIsCurrent;
        return false;
    }

    private static void PrefixProductFinisherCleanup() => _productFinisherCleanupCalls++;

    private static void PrefixProductFinisherUnregister() => _productFinisherUnregisterCalls++;

    private static bool PrefixProductFinisherKill(Creature creature, ref Task __result)
    {
        ProductFinisherKillCalls.TryGetValue(creature, out int calls);
        calls++;
        ProductFinisherKillCalls[creature] = calls;
        if (_productFinisherKillFaultMode == ProductFinisherKillFaultMode.None
            || (_productFinisherKillFaultMode == ProductFinisherKillFaultMode.FirstAttempt
                && calls > 1))
        {
            return true;
        }

        var failure = new InvalidOperationException($"injected-finisher-kill-{calls}");
        ProductFinisherKillFailures.Add(failure);
        __result = Task.FromException(failure);
        return false;
    }

    private static bool PrefixProductFinisherPresentationTarget(ref NCreature? __result)
    {
        _productFinisherPresentationTargetCalls++;
        if (_productFinisherPresentationFailure != null)
        {
            throw _productFinisherPresentationFailure;
        }

        __result = null;
        return false;
    }

    private static void ResetProductFinisherInstrumentation()
    {
        ProductFinisherKillCalls.Clear();
        ProductFinisherKillFailures.Clear();
        _productFinisherContextIsCurrent = true;
        _productFinisherCleanupCalls = 0;
        _productFinisherKillFaultMode = ProductFinisherKillFaultMode.None;
        _productFinisherPresentationFailure = null;
        _productFinisherPresentationTargetCalls = 0;
        _productFinisherUnregisterCalls = 0;
    }

    private static void ResetProductFinisherRegistry(Assembly product)
    {
        Type registryType = ProductType(product, "NinjaSlayer.Code.ExternalAnimations.FinisherSessionRegistry");
        SetStaticField(registryType, "_active", null);
        SetStaticField(registryType, "_pendingAfterCardPlayed", null);
    }

    private static Type ProductType(Assembly product, string typeName) =>
        product.GetType(typeName, throwOnError: true)!;

    private static void SetInstanceField(object instance, string fieldName, object? value)
    {
        FieldInfo field = AccessTools.Field(instance.GetType(), fieldName)
            ?? throw new MissingFieldException(instance.GetType().FullName, fieldName);
        field.SetValue(instance, value);
    }

    private static void SetStaticField(Type type, string fieldName, object? value)
    {
        FieldInfo field = AccessTools.Field(type, fieldName)
            ?? throw new MissingFieldException(type.FullName, fieldName);
        field.SetValue(null, value);
    }

    private sealed class ProductFinisherSessionFixture : IDisposable
    {
        private readonly Assembly _product;
        private readonly PreparedCombatFixture _combat = new();
        private readonly object _ledger;
        private readonly object _session;
        private CardModel? _card;
        private CardPlay? _cardPlay;

        public ProductFinisherSessionFixture(
            Assembly product,
            int targetCount,
            int hitsPerTarget = 1,
            string scenario = "NinjaSlayerAttack")
        {
            Require(targetCount > 0, "Finisher session fixture requires at least one target.");
            Require(hitsPerTarget > 0, "Finisher session fixture requires at least one confirmed hit.");
            _product = product;
            Targets = Enumerable.Range(0, targetCount)
                .Select(_ => CreateTarget(_combat.CombatState))
                .ToArray();
            _ledger = CreateLedger(product, Targets, _combat.CombatState);
            foreach (Creature target in Targets)
            {
                for (int hit = 0; hit < hitsPerTarget; hit++)
                {
                    ConfirmDeferredDeath(_ledger, target);
                }
            }

            Type sessionType = ProductType(product, "NinjaSlayer.Code.ExternalAnimations.FinisherSession");
            _session = RuntimeHelpers.GetUninitializedObject(sessionType);
            SetInstanceField(_session, "_combatState", _combat.CombatState);
            SetInstanceField(_session, "_room", RuntimeHelpers.GetUninitializedObject(typeof(NCombatRoom)));
            SetInstanceField(_session, "_actorNode", null);
            SetInstanceField(_session, "_focusNode", null);
            SetInstanceField(_session, "_ledger", _ledger);
            SetInstanceField(
                _session,
                "_committedDeaths",
                new HashSet<Creature>(ReferenceEqualityComparer.Instance));
            SetNewFieldValue(_session, "_deathSquashStates");
            SetNewFieldValue(_session, "_deathKickVisuals");
            SetInstanceField(_session, "_vfxBaselineChildIds", new HashSet<ulong>());
            SetInstanceField(_session, "_completion",
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            SetCompletedTasks(_session);
            SetSessionLifetimes(product, _session);
            SetSessionCamera(product, _session);
            SetInstanceField(_session, "_actionPeakReached", true);
            SetInstanceField(_session, "_begun", true);
            SetInstanceField(_session, "<SessionId>k__BackingField", 9001L);
            SetInstanceField(
                _session,
                "<Scenario>k__BackingField",
                Enum.Parse(
                    ProductType(product, "NinjaSlayer.Code.ExternalAnimations.FinisherScenarioKind"),
                    scenario));
            SetInstanceField(
                _session,
                "<CompletionCondition>k__BackingField",
                Enum.Parse(
                    ProductType(product, "NinjaSlayer.Code.ExternalAnimations.FinisherCompletionCondition"),
                    "AnyCandidateLethal"));
            SetInstanceField(_session, "<Actor>k__BackingField", _combat.Player.Creature);
            SetInstanceField(_session, "<CardPlay>k__BackingField", null);
            SetInstanceField(_session, "<RequiresAfterCardPlayed>k__BackingField", false);
            SetInstanceField(_session, "<ResolvedHits>k__BackingField", 1);

            ResetProductFinisherRegistry(product);
            Type registryType = ProductType(product, "NinjaSlayer.Code.ExternalAnimations.FinisherSessionRegistry");
            SetStaticField(registryType, "_active", _session);
        }

        public IReadOnlyList<Creature> Targets { get; }

        public Task CompletionTask
        {
            get
            {
                FieldInfo field = AccessTools.Field(_session.GetType(), "_completion")
                    ?? throw new MissingFieldException(_session.GetType().FullName, "_completion");
                return ((TaskCompletionSource)field.GetValue(_session)!).Task;
            }
        }

        public int LivingDeferredDeathCount
        {
            get
            {
                MethodInfo method = AccessTools.Method(_ledger.GetType(), "LivingDeferredDeaths", Type.EmptyTypes)
                    ?? throw new MissingMethodException(_ledger.GetType().FullName, "LivingDeferredDeaths");
                return ((IReadOnlyCollection<Creature>)method.Invoke(_ledger, null)!).Count;
            }
        }

        public bool IsRegistered
        {
            get
            {
                Type registryType = ProductType(
                    _product,
                    "NinjaSlayer.Code.ExternalAnimations.FinisherSessionRegistry");
                MethodInfo method = AccessTools.Method(registryType, "IsSessionCurrent", [_session.GetType()])
                    ?? throw new MissingMethodException(registryType.FullName, "IsSessionCurrent");
                return (bool)method.Invoke(null, [_session])!;
            }
        }

        public Task Complete(bool playPose)
        {
            MethodInfo method = AccessTools.Method(_session.GetType(), "CompleteAsync", [typeof(bool)])
                ?? throw new MissingMethodException(_session.GetType().FullName, "CompleteAsync");
            return (Task)(method.Invoke(_session, [playPose])
                ?? throw new InvalidOperationException("FinisherSession.CompleteAsync returned null."));
        }

        public void FailCleanup(Exception failure) =>
            SetInstanceField(_session, "_cameraShakePumpTask", Task.FromException(failure));

        public void MoveTargetsToNewCombat()
        {
            var nextCombat = new CombatState();
            foreach (Creature target in Targets)
            {
                target.CombatState = nextCombat;
            }
        }

        public void TransferToAfterCardPlayed()
        {
            _card = _combat.CreateCard(PileType.Discard);
            _cardPlay = (CardPlay)RuntimeHelpers.GetUninitializedObject(typeof(CardPlay));
            SetInstanceField(_cardPlay, "<Card>k__BackingField", _card);
            SetInstanceField(_session, "<CardPlay>k__BackingField", _cardPlay);
            SetInstanceField(_session, "<RequiresAfterCardPlayed>k__BackingField", true);

            Type registryType = ProductType(
                _product,
                "NinjaSlayer.Code.ExternalAnimations.FinisherSessionRegistry");
            MethodInfo transfer = AccessTools.Method(
                    registryType,
                    "TransferToAfterCardPlayed",
                    [_session.GetType()])
                ?? throw new MissingMethodException(registryType.FullName, "TransferToAfterCardPlayed");
            transfer.Invoke(null, [_session]);
        }

        public Task CompleteAfterCardPlayed(Task original)
        {
            Require(_cardPlay != null, "Finisher fixture was not transferred to AfterCardPlayed.");
            Type cleanupType = ProductType(
                _product,
                "NinjaSlayer.Code.ExternalAnimations.FinisherCleanupService");
            MethodInfo method = AccessTools.Method(
                    cleanupType,
                    "CompleteAfterCardPlayed",
                    [typeof(Task), typeof(CardPlay)])
                ?? throw new MissingMethodException(cleanupType.FullName, "CompleteAfterCardPlayed");
            return (Task)(method.Invoke(null, [original, _cardPlay])
                ?? throw new InvalidOperationException(
                    "FinisherCleanupService.CompleteAfterCardPlayed returned null."));
        }

        public Task CleanupAfterCardPlay(Task original)
        {
            Require(_card != null, "Finisher fixture was not transferred from OnPlayWrapper.");
            Type cleanupType = ProductType(
                _product,
                "NinjaSlayer.Code.ExternalAnimations.FinisherCleanupService");
            MethodInfo method = AccessTools.Method(
                    cleanupType,
                    "CleanupAfterCardPlay",
                    [typeof(Task), typeof(CardModel)])
                ?? throw new MissingMethodException(cleanupType.FullName, "CleanupAfterCardPlay");
            return (Task)(method.Invoke(null, [original, _card])
                ?? throw new InvalidOperationException(
                    "FinisherCleanupService.CleanupAfterCardPlay returned null."));
        }

        public void InvokeRoomTreeExiting()
        {
            MethodInfo method = AccessTools.Method(_session.GetType(), "OnRoomTreeExiting", Type.EmptyTypes)
                ?? throw new MissingMethodException(_session.GetType().FullName, "OnRoomTreeExiting");
            method.Invoke(_session, null);
        }

        public void Dispose()
        {
            ResetProductFinisherRegistry(_product);
            _combat.Dispose();
        }

        private static Creature CreateTarget(CombatState combatState)
        {
            var target = new Creature(
                ModelDb.Monster<DampCultist>().ToMutable(),
                CombatSide.Enemy,
                slotName: null)
            {
                CombatState = combatState
            };
            target.SetCurrentHpInternal(1);
            combatState.AddCreature(target);
            return target;
        }

        private static object CreateLedger(
            Assembly product,
            IReadOnlyList<Creature> targets,
            ICombatState combatState)
        {
            Type ledgerType = ProductType(product, "NinjaSlayer.Code.ExternalAnimations.FinisherDamageLedger");
            ConstructorInfo constructor = ledgerType
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(candidate => candidate.GetParameters().Length == 4);
            return constructor.Invoke([targets, combatState, (Func<bool>)(() => true), null]);
        }

        private static void ConfirmDeferredDeath(object ledger, Creature target)
        {
            MethodInfo tryProtect = AccessTools.Method(ledger.GetType(), "TryProtect")
                ?? throw new MissingMethodException(ledger.GetType().FullName, "TryProtect");
            object?[] protectArguments = [target, false, 10m, null];
            Require((bool)tryProtect.Invoke(ledger, protectArguments)!,
                "Product Finisher ledger did not protect a lethal target.");
            decimal protectedAmount = (decimal)protectArguments[2]!;
            object token = protectArguments[3]
                ?? throw new InvalidOperationException("Product Finisher ledger returned no protection token.");
            DamageResult result = target.LoseHpInternal(protectedAmount, ValueProp.Move);
            MethodInfo confirm = AccessTools.Method(ledger.GetType(), "Confirm")
                ?? throw new MissingMethodException(ledger.GetType().FullName, "Confirm");
            Require((bool)confirm.Invoke(ledger, [token, result, true])!,
                "Product Finisher ledger did not confirm its lethal protection token.");
        }

        private static void SetNewFieldValue(object instance, string fieldName)
        {
            FieldInfo field = AccessTools.Field(instance.GetType(), fieldName)
                ?? throw new MissingFieldException(instance.GetType().FullName, fieldName);
            SetInstanceField(instance, fieldName, Activator.CreateInstance(field.FieldType)!);
        }

        private static void SetCompletedTasks(object session)
        {
            foreach (string fieldName in new[]
            {
                "_cameraTransitionTask",
                "_backdropTransitionTask",
                "_enhancedImpactTask",
                "_cameraShakePumpTask",
                "_returnToBaselineTask",
                "_actionPeakTask"
            })
            {
                SetInstanceField(session, fieldName, Task.CompletedTask);
            }
        }

        private static void SetSessionLifetimes(Assembly product, object session)
        {
            Type lifetimeType = ProductType(
                product,
                "NinjaSlayer.Code.ExternalAnimations.CinematicSessionLifetime");
            foreach (string fieldName in new[]
            {
                "_impactCancellation",
                "_actionCancellation",
                "_watchdogCancellation"
            })
            {
                SetInstanceField(session, fieldName, Activator.CreateInstance(lifetimeType, nonPublic: true)!);
            }
        }

        private static void SetSessionCamera(Assembly product, object session)
        {
            Type cameraType = ProductType(
                product,
                "NinjaSlayer.Code.ExternalAnimations.CombatCinematicCameraLease");
            object camera = RuntimeHelpers.GetUninitializedObject(cameraType);
            SetInstanceField(camera, "_disposed", true);
            SetInstanceField(session, "_camera", camera);
        }
    }

    private static void VerifyTransitionOwnershipContracts()
    {
        Assembly product = LoadProductAssembly();
        GD.Print("Transition contract: installing instrumentation.");
        Harmony instrumentation = InstallProductTransitionInstrumentation(product);
        try
        {
            GD.Print("Transition contract: normal reveal.");
            VerifyNormalTransitionReveal(product);
            GD.Print("Transition contract: FastMode Instant.");
            VerifyInstantTransitionReveal(product);
            GD.Print("Transition contract: cancellation.");
            VerifyTransitionCancellation(product);
            GD.Print("Transition contract: supersede.");
            VerifyTransitionSupersede(product);
            GD.Print("Transition contract: animation fault.");
            VerifyTransitionAnimationFault(product);
            GD.Print("Transition contract: presentation root exit.");
            VerifyTransitionPresentationRootExit(product);
            GD.Print("Transition contract: replacement root.");
            VerifyTransitionReplacementRoot(product);
            GD.Print("Transition contract: watchdog.");
            VerifyTransitionWatchdog(product);
            GD.Print("Transition contract: finalize drain.");
            VerifyTransitionFinalizeDrain(product);
            GD.Print("Transition contract: passed.");
        }
        finally
        {
            ResetProductTransitionState(product);
            instrumentation.UnpatchAll(instrumentation.Id);
        }
    }

    private static void VerifyNormalTransitionReveal(Assembly product)
    {
        ResetProductTransitionState(product);
        _productTransitionAnimationFactory = (session, _) =>
        {
            InvokeProductTransitionMethod(session, "BeginLoadSmoothing");
            InvokeProductTransitionMethod(session, "PrepareAnimatedView");
            return Task.CompletedTask;
        };

        var transition = CreateTransitionFixture();
        using var fixture = ProductTransitionSessionFixture.Start(product, transition);
        Require(transition.InTransition && transition.Visible
            && transition.MouseFilter == Control.MouseFilterEnum.Stop,
            "Animated Transition startup did not take ownership of the transition view.");
        Require(ReadProductTransitionLoadLimit(product) == 8,
            "Animated Transition startup did not activate its feature-local load limit.");

        int deferredPresentations = 0;
        Require(fixture.TryDeferPresentation(() => deferredPresentations++),
            "Active Transition did not accept its first deferred presentation operation.");
        Require(fixture.TryDeferPresentation(() => deferredPresentations++),
            "Active Transition did not accept its second deferred presentation operation.");
        Require(deferredPresentations == 0,
            "Transition presentation ran before reveal was claimed.");
        Require(fixture.TryClaimRevealThroughGate(),
            "Normal Transition reveal was not claimed through the production gate.");
        Require(!fixture.TryClaimRevealThroughGate(),
            "Normal Transition reveal was claimed more than once.");
        fixture.ReleasePresentation();
        Require(deferredPresentations == 2,
            "Normal Transition reveal did not flush each deferred presentation exactly once.");

        Task firstCompletion = fixture.Complete("Succeeded", forceRelease: false);
        Task repeatedCompletion = fixture.Complete("Faulted", forceRelease: true);
        Require(ReferenceEquals(firstCompletion, repeatedCompletion),
            "Repeated Transition completion did not return the original completion Task.");
        object result = AwaitProductTransitionResult(firstCompletion);
        RequireProductTransitionStatus(result, "Succeeded", "normal reveal");
        RequireTransitionInputRestored(transition, "normal reveal");
        Require(ReadProductTransitionLoadLimit(product) == 128,
            "Normal Transition completion retained its animation load limit.");
        Require(!ReadProductTransitionGateActive(product),
            "Normal Transition completion remained registered in the production gate.");
    }

    private static void VerifyInstantTransitionReveal(Assembly product)
    {
        ResetProductTransitionState(product);
        _productTransitionAnimationFactory = (session, _) =>
        {
            InvokeProductTransitionMethod(session, "PrepareInstantView");
            return Task.CompletedTask;
        };

        var transition = CreateTransitionFixture();
        using var fixture = ProductTransitionSessionFixture.Start(product, transition);
        Require(transition.InTransition && !transition.Visible,
            "FastMode Instant Transition did not prepare the host's instant view.");
        Require(fixture.TryClaimRevealThroughGate(),
            "FastMode Instant Transition did not expose one reveal claim.");
        object result = AwaitProductTransitionResult(
            fixture.Complete("Succeeded", forceRelease: false));
        RequireProductTransitionStatus(result, "Succeeded", "FastMode Instant");
        RequireTransitionInputRestored(transition, "FastMode Instant");
    }

    private static void VerifyTransitionCancellation(Assembly product)
    {
        ResetProductTransitionState(product);
        _productTransitionAnimationFactory = (session, token) =>
        {
            InvokeProductTransitionMethod(session, "BeginLoadSmoothing");
            InvokeProductTransitionMethod(session, "PrepareAnimatedView");
            return WaitForCancellation(token);
        };

        using var cancellation = new CancellationTokenSource();
        var transition = CreateTransitionFixture();
        using var fixture = ProductTransitionSessionFixture.Start(
            product,
            transition,
            cancellation.Token);
        int discardedPresentations = 0;
        Require(fixture.TryDeferPresentation(() => discardedPresentations++),
            "Cancelable Transition did not accept a deferred presentation operation.");
        cancellation.Cancel();
        object result = AwaitProductTransitionResult(fixture.CompletionTask);
        RequireProductTransitionStatus(result, "Cancelled", "external cancellation");
        Require(discardedPresentations == 0,
            "Cancelled Transition executed presentation work from the discarded scene.");
        RequireTransitionForceReleased(transition, "external cancellation");
        Require(ReadProductTransitionLoadLimit(product) == 128,
            "Cancelled Transition retained its animation load limit.");
    }

    private static void VerifyTransitionSupersede(Assembly product)
    {
        ResetProductTransitionState(product);
        _productTransitionAnimationFactory = (_, token) => WaitForCancellation(token);

        var firstTransition = CreateTransitionFixture();
        using var first = ProductTransitionSessionFixture.Start(product, firstTransition);
        var secondTransition = CreateTransitionFixture();
        using var second = ProductTransitionSessionFixture.Start(product, secondTransition);
        object firstResult = AwaitProductTransitionResult(first.CompletionTask);
        RequireProductTransitionStatus(firstResult, "Superseded", "superseded session");
        RequireTransitionForceReleased(firstTransition, "superseded session");
        Require(ReadProductTransitionGateActive(product),
            "Superseding Transition did not retain the newer active session.");

        object secondResult = AwaitProductTransitionResult(
            second.Complete("Succeeded", forceRelease: true));
        RequireProductTransitionStatus(secondResult, "Succeeded", "superseding session");
        Require(!ReadProductTransitionGateActive(product),
            "Completed superseding Transition remained active in the production gate.");
    }

    private static void VerifyTransitionAnimationFault(Assembly product)
    {
        ResetProductTransitionState(product);
        var animationFailure = new InvalidOperationException("injected-transition-animation");
        _productTransitionAnimationFactory = (session, _) =>
        {
            InvokeProductTransitionMethod(session, "BeginLoadSmoothing");
            InvokeProductTransitionMethod(session, "PrepareAnimatedView");
            return Task.FromException(animationFailure);
        };

        var transition = CreateTransitionFixture();
        using var fixture = ProductTransitionSessionFixture.Start(product, transition);
        object result = AwaitProductTransitionResult(fixture.CompletionTask);
        RequireProductTransitionStatus(result, "Faulted", "animation fault");
        Require(ReadProductTransitionDiagnostic(result).Contains(
                animationFailure.Message,
                StringComparison.Ordinal),
            "Transition animation fault diagnostic lost the original exception.");
        Require(_productTransitionWatchdogStartedAfterDispose == 0,
            "Transition animation fault started its watchdog after session disposal.");
        RequireTransitionForceReleased(transition, "animation fault");
        Require(ReadProductTransitionLoadLimit(product) == 128,
            "Faulted Transition retained its animation load limit.");
    }

    private static void VerifyTransitionPresentationRootExit(Assembly product)
    {
        ResetProductTransitionState(product);
        _productTransitionAnimationFactory = (_, token) => WaitForCancellation(token);

        var transition = CreateTransitionFixture();
        using var fixture = ProductTransitionSessionFixture.Start(product, transition);
        var root = new NRun();
        var stagedChild = new Node { ProcessMode = Node.ProcessModeEnum.Always };
        root.AddChild(stagedChild);
        try
        {
            Require(fixture.TryAttachPresentationRoot(root),
                "Transition did not attach its staged NRun presentation root.");
            Require(stagedChild.ProcessMode == Node.ProcessModeEnum.Disabled,
                "Transition did not freeze the staged presentation tree.");
            fixture.InvokePresentationRootTreeExiting();
            object result = AwaitProductTransitionResult(fixture.CompletionTask);
            RequireProductTransitionStatus(result, "Cancelled", "presentation root exit");
            Require(stagedChild.ProcessMode == Node.ProcessModeEnum.Always,
                "Presentation-root exit did not restore the staged tree process mode.");
            RequireTransitionForceReleased(transition, "presentation root exit");
        }
        finally
        {
            root.Free();
        }
    }

    private static void VerifyTransitionReplacementRoot(Assembly product)
    {
        ResetProductTransitionState(product);
        _productTransitionAnimationFactory = (_, token) => WaitForCancellation(token);

        var transition = CreateTransitionFixture();
        using var fixture = ProductTransitionSessionFixture.Start(product, transition);
        var firstRoot = new NRun();
        var firstChild = new Node { ProcessMode = Node.ProcessModeEnum.WhenPaused };
        firstRoot.AddChild(firstChild);
        var replacementRoot = new NRun();
        var replacementChild = new Node { ProcessMode = Node.ProcessModeEnum.Always };
        replacementRoot.AddChild(replacementChild);
        try
        {
            Require(fixture.TryAttachPresentationRoot(firstRoot),
                "Transition did not attach its first staged presentation root.");
            Require(!fixture.TryAttachPresentationRoot(replacementRoot),
                "Transition accepted a replacement presentation root into the same session.");
            object result = AwaitProductTransitionResult(fixture.CompletionTask);
            RequireProductTransitionStatus(result, "Cancelled", "replacement root");
            Require(firstChild.ProcessMode == Node.ProcessModeEnum.WhenPaused,
                "Replacement-root cancellation did not restore the original staged tree.");
            Require(replacementChild.ProcessMode == Node.ProcessModeEnum.Always,
                "Replacement-root cancellation mutated the unowned replacement tree.");
        }
        finally
        {
            firstRoot.Free();
            replacementRoot.Free();
        }
    }

    private static void VerifyTransitionWatchdog(Assembly product)
    {
        ResetProductTransitionState(product);
        _productTransitionAnimationFactory = (_, token) => WaitForCancellation(token);
        _productTransitionForceWatchdogTimeout = true;
        var transition = CreateTransitionFixture();
        ProductTransitionSessionFixture fixture;
        try
        {
            fixture = ProductTransitionSessionFixture.Start(product, transition);
        }
        finally
        {
            _productTransitionForceWatchdogTimeout = false;
        }

        using (fixture)
        {
            object result = AwaitProductTransitionResult(fixture.CompletionTask);
            RequireProductTransitionStatus(result, "TimedOut", "watchdog");
            Require(ReadProductTransitionDiagnostic(result).Contains(
                    "30 second watchdog",
                    StringComparison.OrdinalIgnoreCase),
                "Transition watchdog result lost its timeout diagnostic.");
            RequireTransitionForceReleased(transition, "watchdog");
        }
    }

    private static void VerifyTransitionFinalizeDrain(Assembly product)
    {
        ResetProductTransitionState(product);
        Type smoothingType = ProductType(
            product,
            "NinjaSlayer.Code.Transition.NinjaSlayerTransitionLoadSmoothing");
        MethodInfo beginSession = AccessTools.Method(smoothingType, "BeginSession", [typeof(long)])
            ?? throw new MissingMethodException(smoothingType.FullName, "BeginSession");
        MethodInfo endAnimation = AccessTools.Method(smoothingType, "EndAnimation", [typeof(long)])
            ?? throw new MissingMethodException(smoothingType.FullName, "EndAnimation");
        Type finalizePatchType = ProductType(
            product,
            "NinjaSlayer.Code.Patches.NinjaSlayerTransitionAssetFinalizePatch");
        MethodInfo prefix = AccessTools.Method(
                finalizePatchType,
                "Prefix",
                [typeof(AssetLoadingSession)])
            ?? throw new MissingMethodException(finalizePatchType.FullName, "Prefix");
        MethodInfo processLoadingQueue = AccessTools.Method(
                typeof(AssetLoadingSession),
                "ProcessLoadingQueue",
                Type.EmptyTypes)
            ?? throw new MissingMethodException(
                typeof(AssetLoadingSession).FullName,
                "ProcessLoadingQueue");
        MethodInfo finalizeLoading = AccessTools.Method(
                typeof(AssetLoadingSession),
                "FinalizeLoading",
                Type.EmptyTypes)
            ?? throw new MissingMethodException(
                typeof(AssetLoadingSession).FullName,
                "FinalizeLoading");
        Require(processLoadingQueue.ReturnType == typeof(void)
            && finalizeLoading.ReturnType == typeof(void),
            "Supported host changed a Transition private loading target signature.");

        string[] paths =
        [
            "res://transition-contract-a.tres",
            "res://transition-contract-b.tres"
        ];
        foreach (string path in paths)
        {
            Require(ResourceLoader.LoadThreadedRequest(path) == Error.Ok,
                $"Transition finalize fixture could not request {path}.");
        }

        var cache = new ConcurrentDictionary<string, Resource>();
        var session = new AssetLoadingSession("transition-contract", [], cache);
        FieldInfo finalizingField = AccessTools.Field(typeof(AssetLoadingSession), "_finalizing")
            ?? throw new MissingFieldException(typeof(AssetLoadingSession).FullName, "_finalizing");
        FieldInfo loadingField = AccessTools.Field(typeof(AssetLoadingSession), "_loading")
            ?? throw new MissingFieldException(typeof(AssetLoadingSession).FullName, "_loading");
        FieldInfo totalLoadedField = AccessTools.Field(typeof(AssetLoadingSession), "_totalLoaded")
            ?? throw new MissingFieldException(typeof(AssetLoadingSession).FullName, "_totalLoaded");
        Require(loadingField.FieldType == typeof(Queue<string>)
            && finalizingField.FieldType == typeof(Queue<string>),
            "Supported host changed a Transition loading queue type.");
        var finalizing = (Queue<string>)finalizingField.GetValue(session)!;
        foreach (string path in paths)
        {
            finalizing.Enqueue(path);
        }

        const long sessionId = 4242;
        beginSession.Invoke(null, [sessionId]);
        try
        {
            Require(ReadProductTransitionLoadLimit(product) == 8,
                "Transition finalize fixture did not activate animation smoothing.");
            int calls = 0;
            while (finalizing.Count > 0)
            {
                int before = finalizing.Count;
                bool runOriginal = (bool)prefix.Invoke(null, [session])!;
                Require(!runOriginal,
                    "Transition finalize batching delegated to the host while animation smoothing was active.");
                Require(finalizing.Count < before,
                    "Transition finalize batching did not drain at least one queued resource.");
                calls++;
                Require(calls <= paths.Length,
                    "Transition finalize batching exceeded its guaranteed drain bound.");
            }

            Require(cache.Count == paths.Length && paths.All(cache.ContainsKey),
                "Transition finalize batching lost a resource before cache insertion.");
            Require((int)totalLoadedField.GetValue(session)! == paths.Length,
                "Transition finalize batching finalized a resource more than once.");
            bool emptyRunOriginal = (bool)prefix.Invoke(null, [session])!;
            Require(!emptyRunOriginal
                && (int)totalLoadedField.GetValue(session)! == paths.Length,
                "Repeated Transition finalize invocation changed an already drained cache.");
        }
        finally
        {
            endAnimation.Invoke(null, [sessionId]);
        }

        Require(ReadProductTransitionLoadLimit(product) == 128,
            "Transition finalize fixture did not restore the host load limit.");
        Require((bool)prefix.Invoke(null, [session])!,
            "Transition finalize Patch did not yield to the original host outside animation playback.");
    }

    private static Harmony InstallProductTransitionInstrumentation(Assembly product)
    {
        MethodInfo delay = AccessTools.Method(
                typeof(Task),
                nameof(Task.Delay),
                [typeof(TimeSpan), typeof(CancellationToken)])
            ?? throw new MissingMethodException(typeof(Task).FullName, nameof(Task.Delay));
        var harmony = new Harmony($"NinjaSlayer.ContractTests.Transition.{Guid.NewGuid():N}");
        harmony.Patch(
            delay,
            prefix: new HarmonyMethod(typeof(ContractRunner), nameof(PrefixProductTransitionDelay)));
        Type sessionType = ProductType(
            product,
            "NinjaSlayer.Code.Transition.NinjaSlayerTransitionSession");
        MethodInfo watchdog = AccessTools.Method(sessionType, "RunWatchdogAsync", Type.EmptyTypes)
            ?? throw new MissingMethodException(sessionType.FullName, "RunWatchdogAsync");
        harmony.Patch(
            watchdog,
            prefix: new HarmonyMethod(
                typeof(ContractRunner),
                nameof(PrefixProductTransitionWatchdog)));
        return harmony;
    }

    private static bool PrefixProductTransitionDelay(ref Task __result)
    {
        if (!_productTransitionForceWatchdogTimeout)
        {
            return true;
        }

        __result = Task.CompletedTask;
        return false;
    }

    private static void PrefixProductTransitionWatchdog(object __instance)
    {
        FieldInfo disposed = AccessTools.Field(__instance.GetType(), "_disposed")
            ?? throw new MissingFieldException(__instance.GetType().FullName, "_disposed");
        if ((int)disposed.GetValue(__instance)! != 0)
        {
            _productTransitionWatchdogStartedAfterDispose++;
        }
    }

    private static void ResetProductTransitionState(Assembly product)
    {
        _productTransitionAnimationFactory = null;
        _productTransitionForceWatchdogTimeout = false;
        _productTransitionWatchdogStartedAfterDispose = 0;
        Type gateType = ProductType(
            product,
            "NinjaSlayer.Code.Transition.NinjaSlayerTransitionGate");
        FieldInfo activeField = AccessTools.Field(gateType, "_activeSession")
            ?? throw new MissingFieldException(gateType.FullName, "_activeSession");
        if (activeField.GetValue(null) is { } active)
        {
            AwaitProductTransitionResult(
                CompleteProductTransition(product, active, "Cancelled", forceRelease: true));
        }

        SetStaticField(gateType, "_activeSession", null);
        SetStaticField(gateType, "_pending", false);
        SetStaticField(gateType, "_activeSessionPresent", 0);
        Type smoothingType = ProductType(
            product,
            "NinjaSlayer.Code.Transition.NinjaSlayerTransitionLoadSmoothing");
        SetStaticField(smoothingType, "animationSessionId", 0L);
    }

    private static object? InvokeProductTransitionMethod(object session, string methodName)
    {
        MethodInfo method = AccessTools.Method(session.GetType(), methodName, Type.EmptyTypes)
            ?? throw new MissingMethodException(session.GetType().FullName, methodName);
        return method.Invoke(session, null);
    }

    private static Delegate CreateProductTransitionAnimationDelegate(Type sessionType)
    {
        MethodInfo factory = AccessTools.Method(
                typeof(ContractRunner),
                nameof(RunProductTransitionAnimation))
            ?.MakeGenericMethod(sessionType)
            ?? throw new MissingMethodException(
                typeof(ContractRunner).FullName,
                nameof(RunProductTransitionAnimation));
        Type delegateType = typeof(Func<,,>).MakeGenericType(
            sessionType,
            typeof(CancellationToken),
            typeof(Task));
        return Delegate.CreateDelegate(delegateType, factory);
    }

    private static Task RunProductTransitionAnimation<TSession>(
        TSession session,
        CancellationToken token)
        where TSession : class
    {
        Func<object, CancellationToken, Task> factory = _productTransitionAnimationFactory
            ?? throw new InvalidOperationException(
                "Product Transition animation factory was not configured.");
        return factory(session, token)
            ?? throw new InvalidOperationException(
                "Product Transition animation factory returned null.");
    }

    private static Task WaitForCancellation(CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return Task.FromCanceled(token);
        }

        var completion = new TaskCompletionSource();
        _ = token.Register(() => completion.TrySetCanceled(token));
        return completion.Task;
    }

    private static NTransition CreateTransitionFixture()
    {
        var transition = new NTransition
        {
            Visible = true,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        transition.AddChild(new Control
        {
            Name = "GradientTransition",
            Modulate = Colors.White
        });
        transition.AddChild(new ColorRect
        {
            Name = "SimpleTransition",
            Color = Colors.Black,
            Modulate = Colors.White
        });
        return transition;
    }

    private static int ReadProductTransitionLoadLimit(Assembly product)
    {
        Type smoothingType = ProductType(
            product,
            "NinjaSlayer.Code.Transition.NinjaSlayerTransitionLoadSmoothing");
        MethodInfo method = AccessTools.Method(
                smoothingType,
                "GetConcurrentAssetLoadLimit",
                Type.EmptyTypes)
            ?? throw new MissingMethodException(
                smoothingType.FullName,
                "GetConcurrentAssetLoadLimit");
        return (int)method.Invoke(null, null)!;
    }

    private static bool ReadProductTransitionGateActive(Assembly product)
    {
        Type gateType = ProductType(
            product,
            "NinjaSlayer.Code.Transition.NinjaSlayerTransitionGate");
        FieldInfo field = AccessTools.Field(gateType, "_activeSessionPresent")
            ?? throw new MissingFieldException(gateType.FullName, "_activeSessionPresent");
        return (int)field.GetValue(null)! != 0;
    }

    private static Task CompleteProductTransition(
        Assembly product,
        object session,
        string status,
        bool forceRelease)
    {
        Type statusType = ProductType(
            product,
            "NinjaSlayer.Code.Transition.TransitionCompletionStatus");
        MethodInfo method = AccessTools.Method(
                session.GetType(),
                "CompleteAsync",
                [statusType, typeof(bool), typeof(string)])
            ?? throw new MissingMethodException(session.GetType().FullName, "CompleteAsync");
        object statusValue = Enum.Parse(statusType, status);
        return (Task)(method.Invoke(session, [statusValue, forceRelease, null])
            ?? throw new InvalidOperationException(
                "NinjaSlayerTransitionSession.CompleteAsync returned null."));
    }

    private static object AwaitProductTransitionResult(Task completion)
    {
        completion.GetAwaiter().GetResult();
        PropertyInfo resultProperty = completion.GetType().GetProperty(nameof(Task<object>.Result))
            ?? throw new MissingMemberException(completion.GetType().FullName, nameof(Task<object>.Result));
        return resultProperty.GetValue(completion)
            ?? throw new InvalidOperationException("Transition completion returned a null result.");
    }

    private static void RequireProductTransitionStatus(
        object result,
        string expected,
        string label)
    {
        PropertyInfo status = result.GetType().GetProperty("Status")
            ?? throw new MissingMemberException(result.GetType().FullName, "Status");
        Require(string.Equals(status.GetValue(result)?.ToString(), expected, StringComparison.Ordinal),
            $"Transition {label} returned {status.GetValue(result)}, expected {expected}.");
    }

    private static string ReadProductTransitionDiagnostic(object result)
    {
        PropertyInfo diagnostic = result.GetType().GetProperty("Diagnostic")
            ?? throw new MissingMemberException(result.GetType().FullName, "Diagnostic");
        return diagnostic.GetValue(result) as string ?? string.Empty;
    }

    private static void RequireTransitionInputRestored(NTransition transition, string label)
    {
        Require(!transition.InTransition
            && transition.MouseFilter == Control.MouseFilterEnum.Ignore,
            $"Transition {label} did not restore host input ownership.");
    }

    private static void RequireTransitionForceReleased(NTransition transition, string label)
    {
        RequireTransitionInputRestored(transition, label);
        ColorRect backdrop = transition.GetNode<ColorRect>("SimpleTransition");
        Control gradient = transition.GetNode<Control>("GradientTransition");
        Require(!transition.Visible
            && backdrop.Modulate.A == 0f
            && gradient.Modulate.A == 0f,
            $"Transition {label} did not clear its FadeOut/FadeIn cover.");
    }

    private sealed class ProductTransitionSessionFixture : IDisposable
    {
        private readonly Assembly _product;
        private readonly object _session;
        private readonly NTransition _transition;

        private ProductTransitionSessionFixture(
            Assembly product,
            object session,
            NTransition transition)
        {
            _product = product;
            _session = session;
            _transition = transition;
        }

        public Task CompletionTask
        {
            get
            {
                PropertyInfo property = _session.GetType().GetProperty("Completion")
                    ?? throw new MissingMemberException(_session.GetType().FullName, "Completion");
                return (Task)(property.GetValue(_session)
                    ?? throw new InvalidOperationException(
                        "NinjaSlayerTransitionSession.Completion returned null."));
            }
        }

        public static ProductTransitionSessionFixture Start(
            Assembly product,
            NTransition transition,
            CancellationToken cancellationToken = default)
        {
            Type sessionType = ProductType(
                product,
                "NinjaSlayer.Code.Transition.NinjaSlayerTransitionSession");
            Type gateType = ProductType(
                product,
                "NinjaSlayer.Code.Transition.NinjaSlayerTransitionGate");
            Delegate animation = CreateProductTransitionAnimationDelegate(sessionType);
            MethodInfo start = gateType
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(method => method.Name == "TryStartSession"
                    && method.GetParameters().Length == 4);
            object?[] arguments = [transition, animation, cancellationToken, null];
            bool started = (bool)start.Invoke(null, arguments)!;
            Require(started && arguments[3] != null,
                "Production Transition gate rejected the contract session.");
            return new ProductTransitionSessionFixture(product, arguments[3]!, transition);
        }

        public bool TryClaimRevealThroughGate()
        {
            Type gateType = ProductType(
                _product,
                "NinjaSlayer.Code.Transition.NinjaSlayerTransitionGate");
            MethodInfo method = gateType
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(candidate => candidate.Name == "TryClaimReveal"
                    && candidate.GetParameters().Length == 2);
            object?[] arguments = [_transition, null];
            bool claimed = (bool)method.Invoke(null, arguments)!;
            Require(!claimed || ReferenceEquals(arguments[1], _session),
                "Transition gate returned a reveal claim for a different session.");
            return claimed;
        }

        public bool TryAttachPresentationRoot(NRun root)
        {
            Type gateType = ProductType(
                _product,
                "NinjaSlayer.Code.Transition.NinjaSlayerTransitionGate");
            MethodInfo method = AccessTools.Method(
                    gateType,
                    "TryAttachPresentationRoot",
                    [typeof(NRun)])
                ?? throw new MissingMethodException(gateType.FullName, "TryAttachPresentationRoot");
            return (bool)method.Invoke(null, [root])!;
        }

        public bool TryDeferPresentation(Action operation)
        {
            MethodInfo method = AccessTools.Method(
                    _session.GetType(),
                    "TryDeferPresentation",
                    [typeof(Action)])
                ?? throw new MissingMethodException(
                    _session.GetType().FullName,
                    "TryDeferPresentation");
            return (bool)method.Invoke(_session, [operation])!;
        }

        public void ReleasePresentation() =>
            InvokeProductTransitionMethod(_session, "ReleasePresentation");

        public void InvokePresentationRootTreeExiting() =>
            InvokeProductTransitionMethod(_session, "OnPresentationRootTreeExiting");

        public Task Complete(string status, bool forceRelease) =>
            CompleteProductTransition(_product, _session, status, forceRelease);

        public void Dispose()
        {
            if (!CompletionTask.IsCompleted)
            {
                AwaitProductTransitionResult(
                    Complete("Cancelled", forceRelease: true));
            }

            if (GodotObject.IsInstanceValid(_transition))
            {
                _transition.Free();
            }
        }
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
