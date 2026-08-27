using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Net;
using System.Net.Sockets;
using Godot;
using HarmonyLib;
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
    private static ProductPrepareFaultMode _productPrepareFaultMode;

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
