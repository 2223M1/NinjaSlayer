using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Characters;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using STS2RitsuLib;

namespace NinjaSlayer.ProductPreparedContractTests;

public partial class ProductPreparedContractRunner : Node
{
    private static Assembly _product = null!;
    private static Type _preparedAfflictionType = null!;
    private static Type _shurikenType = null!;
    private static Type _readyBladeType = null!;
    private static MethodInfo _prepare = null!;
    private static MethodInfo _productAfflict = null!;

    public override void _Ready()
    {
        try
        {
            if (!RitsuLibFramework.IsInitialized)
            {
                RitsuLibFramework.Initialize();
            }

            LoadProductAssembly();
            InjectModels();
            using IDisposable lifecycle = SubscribeProductPreparedLifecycle();
            Harmony faultHarmony = PreparedFaultInjection.Install();
            try
            {
                VerifyQueueAndDrawExit();
                VerifyBrokenQueuePrefix();
                VerifyDuplicatePileReference();
                VerifyFailureRollback(PreparedFaultMode.UnconfirmedAdd, "not confirmed");
                VerifyFailureRollback(PreparedFaultMode.RemoveOnce, "injected-remove");
                VerifyFailureRollback(PreparedFaultMode.DrawAddOnce, "injected-draw-add");
                VerifyRepositionRollback();
                VerifyFailureRollback(PreparedFaultMode.UnconfirmedAfterMutation, "not confirmed");
                VerifyForeignAfflictionRollback();
                VerifyPrimaryAndRollbackFailure();
                VerifyNextDiscardPreparedPower();
                VerifyGeneratedShuriken();
                VerifyReadyBlade();
            }
            finally
            {
                PreparedFaultInjection.Reset();
                faultHarmony.UnpatchAll(faultHarmony.Id);
            }

            WriteSuccessMarker();
            GD.Print("NinjaSlayer product Prepared transaction contracts passed.");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"NinjaSlayer product Prepared contract failed: {exception}");
            GetTree().Quit(1);
        }
    }

    private static void LoadProductAssembly()
    {
        string productPath = System.Environment.GetEnvironmentVariable(
                "NINJASLAYER_CONTRACT_PRODUCT_ASSEMBLY")
            ?? throw new InvalidOperationException(
                "Product Prepared contracts require NINJASLAYER_CONTRACT_PRODUCT_ASSEMBLY.");
        if (!Path.IsPathFullyQualified(productPath))
        {
            throw new InvalidOperationException(
                "NINJASLAYER_CONTRACT_PRODUCT_ASSEMBLY must be an absolute path.");
        }

        string fullPath = Path.GetFullPath(productPath);
        if (!System.IO.File.Exists(fullPath))
        {
            throw new FileNotFoundException("The candidate product assembly does not exist.", fullPath);
        }

        string expectedRevision = System.Environment.GetEnvironmentVariable(
                "NINJASLAYER_CONTRACT_EXPECTED_SOURCE_REVISION")
            ?? throw new InvalidOperationException(
                "Product Prepared contracts require NINJASLAYER_CONTRACT_EXPECTED_SOURCE_REVISION.");
        if (expectedRevision.Length != 40 || expectedRevision.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException(
                "NINJASLAYER_CONTRACT_EXPECTED_SOURCE_REVISION must be a full 40-character SHA.");
        }

        AssemblyLoadContext context = AssemblyLoadContext.GetLoadContext(
                typeof(ProductPreparedContractRunner).Assembly)
            ?? throw new InvalidOperationException("The product Prepared runner has no load context.");
        _product = context.LoadFromAssemblyPath(fullPath);
        Require(
            ReadAssemblyMetadata(_product, "NinjaSlayerSourceRevision")
                .Equals(expectedRevision, StringComparison.OrdinalIgnoreCase),
            "The product Prepared assembly source revision does not match the requested candidate SHA.");
        RequireMatchingMetadata("NinjaSlayerHostChannel");
        RequireMatchingMetadata("NinjaSlayerGameApiVersion");
        RequireMatchingMetadata("NinjaSlayerRitsuLibPackageId");
        RequireMatchingMetadata("NinjaSlayerRitsuLibVersion");

        _preparedAfflictionType = ProductType("NinjaSlayer.Afflictions.PreparedAffliction");
        _shurikenType = ProductType("NinjaSlayer.Cards.ShurikenCard");
        _readyBladeType = ProductType("NinjaSlayer.Cards.ReadyBlade");
        Type prepareType = ProductType("NinjaSlayer.Code.Commands.PrepareCmd");
        _prepare = AccessTools.Method(prepareType, "Apply", [typeof(CardModel)])
            ?? throw new MissingMethodException(prepareType.FullName, "Apply");
        _productAfflict = typeof(CardCmd)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == nameof(CardCmd.Afflict)
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 2)
            .MakeGenericMethod(_preparedAfflictionType);
    }

    private static void InjectModels()
    {
        ModelDb.Inject(typeof(StrikeIronclad));
        ModelDb.Inject(typeof(Ironclad));
        ModelDb.Inject(typeof(DampCultist));
        ModelDb.Inject(_preparedAfflictionType);
        ModelDb.Inject(_shurikenType);
        ModelDb.Inject(_readyBladeType);
        ModelDb.Inject(ProductType("NinjaSlayer.Powers.NextDiscardPreparedPower"));
    }

    private static IDisposable SubscribeProductPreparedLifecycle()
    {
        Type lifecycleType = ProductType("NinjaSlayer.Code.Prepared.PreparedSafetyLifecycle");
        MethodInfo subscribe = AccessTools.Method(lifecycleType, "Subscribe", Type.EmptyTypes)
            ?? throw new MissingMethodException(lifecycleType.FullName, "Subscribe");
        return (IDisposable)(subscribe.Invoke(null, null)
            ?? throw new InvalidOperationException("Product Prepared lifecycle returned null."));
    }

    private static void VerifyQueueAndDrawExit()
    {
        using var fixture = new PreparedCombatFixture();
        CardModel[] cards = Enumerable.Range(0, 4)
            .Select(index => fixture.CreateHostCard(index == 0 ? PileType.Draw : PileType.Discard))
            .ToArray();
        var afflictions = new List<object>();
        for (int index = 0; index < cards.Length; index++)
        {
            Require(InvokePrepare(cards[index]), $"Product Prepare rejected card {index + 1}.");
            object affliction = cards[index].Affliction
                ?? throw new InvalidOperationException("Product Prepare left no affliction.");
            Require(ReferenceEquals(cards[index].Affliction, affliction)
                && affliction.GetType() == _preparedAfflictionType,
                "Product Prepare did not retain its exact product affliction instance.");
            afflictions.Add(affliction);
            RequirePreparedQueue(fixture, cards.Take(index + 1).ToArray(), afflictions);
        }

        CardPileAddResult drawExit = CardPileCmd.Add(cards[0], PileType.Hand.GetPile(fixture.Player))
            .GetAwaiter().GetResult();
        Require(drawExit.success, "Product Prepared Draw-exit did not move the card.");
        Require(cards[0].Affliction is null,
            "Product Prepared lifecycle did not clear the exact affliction on Draw exit.");
        RequirePreparedQueue(fixture, cards.Skip(1).ToArray(), afflictions.Skip(1).ToArray());
    }

    private static void VerifyBrokenQueuePrefix()
    {
        using var fixture = new PreparedCombatFixture();
        CardModel prepared = fixture.CreateHostCard(PileType.Discard);
        Require(InvokePrepare(prepared), "Broken-prefix fixture could not prepare its queue card.");
        CardModel unprepared = fixture.CreateHostCard(PileType.Draw, index: 0);
        CardModel target = fixture.CreateHostCard(PileType.Discard);
        int originalIndex = fixture.DiscardPile.Cards.Count - 1;

        _ = ExpectException<InvalidOperationException>(
            () => InvokePrepare(target),
            "queue prefix");
        RequireRestored(fixture, target, fixture.DiscardPile, originalIndex, "broken queue prefix");
        Require(ReferenceEquals(fixture.DrawPile.Cards[0], unprepared),
            "Product Prepare rewrote the broken queue producer state.");
    }

    private static void VerifyDuplicatePileReference()
    {
        using var fixture = new PreparedCombatFixture();
        CardModel prepared = fixture.CreateHostCard(PileType.Discard);
        Require(InvokePrepare(prepared), "Duplicate-reference fixture could not prepare its queue card.");
        fixture.DiscardPile.AddInternal(prepared, index: -1, silent: true);
        CardModel target = fixture.CreateHostCard(PileType.Discard);
        int originalIndex = fixture.DiscardPile.Cards.Count - 1;

        _ = ExpectException<InvalidOperationException>(
            () => InvokePrepare(target),
            "unique pile ownership");
        RequireRestored(fixture, target, fixture.DiscardPile, originalIndex, "duplicate pile reference");
        Require(CountReferences(fixture.Player, prepared) == 2,
            "Product Prepare rewrote the duplicate-reference producer state.");
    }

    private static void VerifyFailureRollback(PreparedFaultMode mode, string failureFragment)
    {
        using var fixture = new PreparedCombatFixture();
        CardModel card = fixture.CreateHostCard(PileType.Discard);
        int originalIndex = fixture.DiscardPile.Cards.Count - 1;
        PreparedFaultInjection.Configure(mode, card);
        try
        {
            _ = ExpectException<InvalidOperationException>(
                () => InvokePrepare(card),
                failureFragment);
        }
        finally
        {
            PreparedFaultInjection.Reset();
        }

        RequireRestored(fixture, card, fixture.DiscardPile, originalIndex, mode.ToString());
    }

    private static void VerifyRepositionRollback()
    {
        using var fixture = new PreparedCombatFixture();
        CardModel queued = fixture.CreateHostCard(PileType.Discard);
        Require(InvokePrepare(queued), "Reposition fixture could not prepare its queue card.");
        object queuedAffliction = queued.Affliction!;
        CardModel card = fixture.CreateHostCard(PileType.Discard);
        int originalIndex = fixture.DiscardPile.Cards.Count - 1;
        PreparedFaultInjection.Configure(PreparedFaultMode.RepositionAddOnce, card);
        try
        {
            _ = ExpectException<InvalidOperationException>(
                () => InvokePrepare(card),
                "injected-reposition-add");
        }
        finally
        {
            PreparedFaultInjection.Reset();
        }

        RequireRestored(fixture, card, fixture.DiscardPile, originalIndex, "reposition failure");
        RequirePreparedQueue(fixture, [queued], [queuedAffliction]);
    }

    private static void VerifyForeignAfflictionRollback()
    {
        using var fixture = new PreparedCombatFixture();
        CardModel card = fixture.CreateHostCard(PileType.Discard);
        int originalIndex = fixture.DiscardPile.Cards.Count - 1;
        PreparedFaultInjection.Configure(PreparedFaultMode.ReplacePreparedAfterMutation, card);
        AggregateException failure;
        object replacement;
        try
        {
            failure = ExpectException<AggregateException>(
                () => InvokePrepare(card),
                "transaction and rollback");
            replacement = PreparedFaultInjection.ReplacementAffliction
                ?? throw new InvalidOperationException("Replacement fault produced no foreign affliction.");
        }
        finally
        {
            PreparedFaultInjection.Reset();
        }

        Require(failure.ToString().Contains("lost ownership", StringComparison.OrdinalIgnoreCase),
            "Product Prepare did not expose loss of its exact affliction instance.");
        Require(failure.ToString().Contains("refused to clear", StringComparison.OrdinalIgnoreCase),
            "Product Prepare rollback did not expose foreign-affliction ownership.");
        Require(ReferenceEquals(card.Affliction, replacement),
            "Product Prepare rollback cleared or replaced the foreign affliction.");
        Require(ReferenceEquals(card.Pile, fixture.DiscardPile)
            && ReferenceEquals(fixture.DiscardPile.Cards[originalIndex], card),
            "Product Prepare foreign-affliction rollback lost the original pile position.");
        Require(CountReferences(fixture.Player, card) == 1,
            "Product Prepare foreign-affliction rollback lost unique pile ownership.");
    }

    private static void VerifyPrimaryAndRollbackFailure()
    {
        using var fixture = new PreparedCombatFixture();
        CardModel card = fixture.CreateHostCard(PileType.Discard);
        PreparedFaultInjection.Configure(PreparedFaultMode.DrawAndRollbackAdd, card);
        AggregateException failure;
        try
        {
            failure = ExpectException<AggregateException>(
                () => InvokePrepare(card),
                "transaction and rollback");
        }
        finally
        {
            PreparedFaultInjection.Reset();
        }

        Require(failure.InnerExceptions.Count == 2,
            "Product Prepare did not preserve primary and rollback failures.");
        Require(failure.InnerExceptions[0].ToString().Contains("injected-draw-add", StringComparison.Ordinal),
            "Product Prepare aggregate lost the primary failure.");
        Require(failure.InnerExceptions[1].ToString().Contains("injected-rollback-add", StringComparison.Ordinal),
            "Product Prepare aggregate lost the rollback failure.");
        Require(card.Affliction is null,
            "Product Prepare double-failure cleanup retained its affliction.");
        Require(card.Pile is null,
            "Product Prepare double-failure fixture falsely reported pile restoration.");
    }

    private static void VerifyNextDiscardPreparedPower()
    {
        Type powerType = ProductType("NinjaSlayer.Powers.NextDiscardPreparedPower");
        MethodInfo afterPileChange = AccessTools.Method(
                powerType,
                nameof(PowerModel.AfterCardChangedPiles),
                [typeof(CardModel), typeof(PileType), typeof(AbstractModel)])
            ?? throw new MissingMethodException(powerType.FullName, nameof(PowerModel.AfterCardChangedPiles));

        using (var fixture = new PreparedCombatFixture())
        {
            PowerModel power = CreateProductPower(powerType, fixture.Player, 2m);
            try
            {
                CardModel card = fixture.CreateHostCard(PileType.Discard);
                InvokeTask(afterPileChange, power, [card, PileType.Hand, null]);
                Require(power.Amount == 1,
                    "NextDiscardPreparedPower did not decrement after product Prepare succeeded.");
                Require(IsProductPrepared(card) && ReferenceEquals(card.Pile, fixture.DrawPile),
                    "NextDiscardPreparedPower success did not expose the Prepared queue result.");
            }
            finally
            {
                power.RemoveInternal();
            }
        }

        using (var fixture = new PreparedCombatFixture())
        {
            PowerModel power = CreateProductPower(powerType, fixture.Player, 2m);
            CardModel card = fixture.CreateHostCard(PileType.Discard);
            int originalIndex = fixture.DiscardPile.Cards.Count - 1;
            PreparedFaultInjection.Configure(PreparedFaultMode.UnconfirmedAdd, card);
            try
            {
                _ = ExpectException<InvalidOperationException>(
                    () => InvokeTask(afterPileChange, power, [card, PileType.Hand, null]),
                    "not confirmed");
                Require(power.Amount == 2,
                    "NextDiscardPreparedPower decremented before product Prepare completed.");
                RequireRestored(fixture, card, fixture.DiscardPile, originalIndex, "power failure");
            }
            finally
            {
                PreparedFaultInjection.Reset();
                power.RemoveInternal();
            }
        }
    }

    private static void VerifyGeneratedShuriken()
    {
        using (var fixture = new PreparedCombatFixture())
        {
            InvokeAddGeneratedShuriken(fixture.Player, count: 2);
            CardModel[] generated = fixture.DrawPile.Cards
                .Where(card => card.GetType() == _shurikenType)
                .ToArray();
            fixture.TrackCards(generated);
            Require(generated.Length == 2,
                "AddGeneratedShuriken success generated an unexpected card count.");
            RequirePreparedQueue(fixture, generated, generated.Select(card => card.Affliction!).ToArray());
        }

        using (var fixture = new PreparedCombatFixture())
        {
            PreparedFaultInjection.ConfigureForType(PreparedFaultMode.UnconfirmedAdd, _shurikenType);
            try
            {
                _ = ExpectException<InvalidOperationException>(
                    () => InvokeAddGeneratedShuriken(fixture.Player, count: 2),
                    "not confirmed");
            }
            finally
            {
                PreparedFaultInjection.Reset();
            }

            CardModel[] generated = fixture.DrawPile.Cards
                .Where(card => card.GetType() == _shurikenType)
                .ToArray();
            fixture.TrackCards(generated);
            Require(generated.Length == 2,
                "AddGeneratedShuriken failure changed the requested generation count.");
            Require(generated.All(card => card.Affliction is null
                    && CountReferences(fixture.Player, card) == 1),
                "AddGeneratedShuriken failure retained partial Prepare state or duplicate references.");
        }
    }

    private static void VerifyReadyBlade()
    {
        MethodInfo onPlay = AccessTools.Method(
                _readyBladeType,
                "OnPlay",
                [typeof(PlayerChoiceContext), typeof(CardPlay)])
            ?? throw new MissingMethodException(_readyBladeType.FullName, "OnPlay");

        using (var fixture = new PreparedCombatFixture())
        {
            CardModel readyBlade = fixture.CreateProductCard(_readyBladeType, PileType.Play);
            fixture.CreateHostCard(PileType.Draw);
            InvokeTask(onPlay, readyBlade, [new ThrowingPlayerChoiceContext(), null]);
            CardModel[] generated = fixture.Player.Piles
                .SelectMany(pile => pile.Cards)
                .Where(card => card.GetType() == _shurikenType)
                .Distinct<CardModel>(ReferenceEqualityComparer.Instance)
                .ToArray();
            fixture.TrackCards(generated);
            Require(generated.Length == 3,
                "ReadyBlade success did not generate its full Shuriken set.");
            Require(PileType.Hand.GetPile(fixture.Player).Cards.Count == 1,
                "ReadyBlade did not execute its draw after every product Prepare succeeded.");
            Require(generated.Count(IsProductPrepared) == 2
                && generated.Count(card => card.Pile?.Type == PileType.Hand && card.Affliction is null) == 1,
                "ReadyBlade success did not expose the expected Prepared queue and Draw-exit behavior.");
        }

        using (var fixture = new PreparedCombatFixture())
        {
            CardModel readyBlade = fixture.CreateProductCard(_readyBladeType, PileType.Play);
            CardModel drawSentinel = fixture.CreateHostCard(PileType.Draw);
            PreparedFaultInjection.ConfigureForType(PreparedFaultMode.UnconfirmedAdd, _shurikenType);
            try
            {
                _ = ExpectException<InvalidOperationException>(
                    () => InvokeTask(onPlay, readyBlade, [new ThrowingPlayerChoiceContext(), null]),
                    "not confirmed");
            }
            finally
            {
                PreparedFaultInjection.Reset();
            }

            CardModel[] generated = fixture.DrawPile.Cards
                .Where(card => card.GetType() == _shurikenType)
                .ToArray();
            fixture.TrackCards(generated);
            Require(generated.Length == 3 && generated.All(card => card.Affliction is null),
                "ReadyBlade failure retained partial product Prepare state.");
            Require(PileType.Hand.GetPile(fixture.Player).Cards.Count == 0
                && ReferenceEquals(drawSentinel.Pile, fixture.DrawPile),
                "ReadyBlade executed its draw before all product Prepare transactions succeeded.");
        }
    }

    private static bool InvokePrepare(CardModel card)
    {
        object? result = Invoke(_prepare, null, [card]);
        return ((Task<bool>)(result
            ?? throw new InvalidOperationException("Product Prepare returned null.")))
            .GetAwaiter().GetResult();
    }

    private static object AfflictProduct(CardModel card)
    {
        var task = (Task)(Invoke(_productAfflict, null, [card, 1m])
            ?? throw new InvalidOperationException("Product Prepared affliction command returned null."));
        task.GetAwaiter().GetResult();
        return task.GetType().GetProperty(nameof(Task<object>.Result))?.GetValue(task)
            ?? throw new InvalidOperationException("Product Prepared affliction command produced no instance.");
    }

    private static void InvokeAddGeneratedShuriken(Player player, int count)
    {
        Type actionsType = ProductType("NinjaSlayer.Content.NinjaSlayerActions");
        MethodInfo method = AccessTools.Method(
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
        InvokeTask(
            method,
            null,
            [
                new ThrowingPlayerChoiceContext(),
                player,
                count,
                PileType.Draw,
                false,
                CardPilePosition.Bottom,
                true
            ]);
    }

    private static void InvokeTask(MethodInfo method, object? instance, object?[] parameters)
    {
        var task = (Task)(Invoke(method, instance, parameters)
            ?? throw new InvalidOperationException($"{method.DeclaringType?.FullName}.{method.Name} returned null."));
        task.GetAwaiter().GetResult();
    }

    private static object? Invoke(MethodInfo method, object? instance, object?[] parameters)
    {
        try
        {
            return method.Invoke(instance, parameters);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static PowerModel CreateProductPower(Type powerType, Player owner, decimal amount)
    {
        PowerModel canonical = ProductModel<PowerModel>(powerType, nameof(ModelDb.Power));
        PowerModel power = canonical.ToMutable();
        power.ApplyInternal(owner.Creature, amount);
        return power;
    }

    private static TModel ProductModel<TModel>(Type modelType, string lookupName)
        where TModel : AbstractModel
    {
        MethodInfo lookup = typeof(ModelDb)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == lookupName
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 0);
        return (TModel)(lookup.MakeGenericMethod(modelType).Invoke(null, null)
            ?? throw new InvalidOperationException($"ModelDb.{lookupName} returned null for {modelType.FullName}."));
    }

    private static void RequirePreparedQueue(
        PreparedCombatFixture fixture,
        IReadOnlyList<CardModel> expectedCards,
        IReadOnlyList<object> expectedAfflictions)
    {
        Require(expectedCards.Count == expectedAfflictions.Count,
            "Prepared queue assertion has mismatched cards and afflictions.");
        for (int index = 0; index < expectedCards.Count; index++)
        {
            CardModel card = expectedCards[index];
            Require(ReferenceEquals(fixture.DrawPile.Cards[index], card),
                "Product Prepared queue order changed.");
            Require(ReferenceEquals(card.Affliction, expectedAfflictions[index])
                && card.Affliction?.GetType() == _preparedAfflictionType,
                "Product Prepared queue lost its exact affliction instance.");
            Require(ReferenceEquals(card.Owner, fixture.Player)
                && ReferenceEquals(card.CombatState, fixture.CombatState)
                && fixture.Player.PlayerCombatState?.AllCards.Contains(card) == true
                && ReferenceEquals(card.Pile, fixture.DrawPile)
                && CountReferences(fixture.Player, card) == 1,
                "Product Prepared queue lost active-combat or unique pile ownership.");
        }

        Require(fixture.DrawPile.Cards.Skip(expectedCards.Count).All(card => !IsProductPrepared(card)),
            "Product Prepared card exists outside the queue prefix.");
        Require(fixture.Player.Piles
            .Where(pile => !ReferenceEquals(pile, fixture.DrawPile))
            .SelectMany(pile => pile.Cards)
            .All(card => !IsProductPrepared(card)),
            "Product Prepared card exists outside the draw pile.");
    }

    private static void RequireRestored(
        PreparedCombatFixture fixture,
        CardModel card,
        CardPile originalPile,
        int originalIndex,
        string label)
    {
        Require(card.Affliction is null, $"{label} retained a partial affliction.");
        Require(ReferenceEquals(card.Owner, fixture.Player)
            && ReferenceEquals(card.CombatState, fixture.CombatState)
            && fixture.Player.PlayerCombatState?.AllCards.Contains(card) == true,
            $"{label} changed combat ownership.");
        Require(ReferenceEquals(card.Pile, originalPile)
            && ReferenceEquals(originalPile.Cards[originalIndex], card)
            && CountReferences(fixture.Player, card) == 1,
            $"{label} did not restore pile position and unique ownership.");
    }

    private static int CountReferences(Player owner, CardModel card) =>
        owner.Piles.Sum(pile => pile.Cards.Count(candidate => ReferenceEquals(candidate, card)));

    private static bool IsProductPrepared(CardModel card) =>
        card.Affliction?.GetType() == _preparedAfflictionType;

    private static TException ExpectException<TException>(Action action, string fragment)
        where TException : Exception
    {
        Exception? observed = null;
        try
        {
            action();
        }
        catch (Exception exception)
        {
            observed = exception;
        }

        Require(observed is TException,
            $"Expected {typeof(TException).Name}, observed {observed?.GetType().Name ?? "no exception"}.");
        Require(observed!.ToString().Contains(fragment, StringComparison.OrdinalIgnoreCase),
            $"{typeof(TException).Name} did not contain '{fragment}'.");
        return (TException)observed;
    }

    private static Type ProductType(string fullName) =>
        _product.GetType(fullName, throwOnError: true)!;

    private static string ReadAssemblyMetadata(Assembly assembly, string key) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == key)
            .Value
        ?? throw new InvalidOperationException($"Assembly metadata {key} has no value.");

    private static void RequireMatchingMetadata(string key)
    {
        string expected = ReadAssemblyMetadata(typeof(ProductPreparedContractRunner).Assembly, key);
        string actual = ReadAssemblyMetadata(_product, key);
        Require(actual == expected,
            $"Product assembly metadata {key} was '{actual}', expected '{expected}'.");
    }

    private static void WriteSuccessMarker()
    {
        string marker = System.Environment.GetEnvironmentVariable(
                "NINJASLAYER_CONTRACT_SUCCESS_MARKER")
            ?? throw new InvalidOperationException(
                "Product Prepared contracts require NINJASLAYER_CONTRACT_SUCCESS_MARKER.");
        string fullPath = Path.GetFullPath(marker);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        System.IO.File.WriteAllText(fullPath, "passed\n");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private enum PreparedFaultMode
    {
        None,
        UnconfirmedAdd,
        RemoveOnce,
        DrawAddOnce,
        RepositionAddOnce,
        DrawAndRollbackAdd,
        UnconfirmedAfterMutation,
        ReplacePreparedAfterMutation
    }

    private static class PreparedFaultInjection
    {
        private static PreparedFaultMode _mode;
        private static CardModel? _target;
        private static Type? _targetType;
        private static int _drawAddCount;

        public static object? ReplacementAffliction { get; private set; }

        public static Harmony Install()
        {
            var harmony = new Harmony($"NinjaSlayer.ProductPreparedContracts.{Guid.NewGuid():N}");
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
            MethodInfo triggerAnim = AccessTools.Method(
                    typeof(CreatureCmd),
                    nameof(CreatureCmd.TriggerAnim),
                    [typeof(Creature), typeof(string), typeof(float)])
                ?? throw new MissingMethodException(typeof(CreatureCmd).FullName, nameof(CreatureCmd.TriggerAnim));

            harmony.Patch(add,
                prefix: new HarmonyMethod(typeof(PreparedFaultInjection), nameof(PrefixAdd)),
                postfix: new HarmonyMethod(typeof(PreparedFaultInjection), nameof(PostfixAdd)));
            harmony.Patch(addInternal,
                prefix: new HarmonyMethod(typeof(PreparedFaultInjection), nameof(PrefixAddInternal)));
            harmony.Patch(removeInternal,
                prefix: new HarmonyMethod(typeof(PreparedFaultInjection), nameof(PrefixRemoveInternal)));
            harmony.Patch(triggerAnim,
                prefix: new HarmonyMethod(typeof(PreparedFaultInjection), nameof(PrefixTriggerAnim)));
            return harmony;
        }

        public static void Configure(PreparedFaultMode mode, CardModel target)
        {
            Reset();
            _mode = mode;
            _target = target;
        }

        public static void ConfigureForType(PreparedFaultMode mode, Type targetType)
        {
            Reset();
            _mode = mode;
            _targetType = targetType;
        }

        public static void Reset()
        {
            _mode = PreparedFaultMode.None;
            _target = null;
            _targetType = null;
            _drawAddCount = 0;
            ReplacementAffliction = null;
        }

        private static bool PrefixAdd(CardModel card, ref Task<CardPileAddResult> __result)
        {
            if (_mode != PreparedFaultMode.UnconfirmedAdd
                || !Matches(card)
                || !IsProductPrepared(card))
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
            if (!Matches(card))
            {
                return;
            }

            if (_mode == PreparedFaultMode.UnconfirmedAfterMutation)
            {
                __result = ReturnUnconfirmedAfterMutation(__result);
            }
            else if (_mode == PreparedFaultMode.ReplacePreparedAfterMutation)
            {
                __result = ReplacePreparedAfterMutation(card, __result);
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

        private static async Task<CardPileAddResult> ReplacePreparedAfterMutation(
            CardModel card,
            Task<CardPileAddResult> resultTask)
        {
            CardPileAddResult result = await resultTask;
            CardCmd.ClearAffliction(card);
            ReplacementAffliction = AfflictProduct(card);
            _mode = PreparedFaultMode.None;
            return result;
        }

        private static void PrefixRemoveInternal(CardModel card)
        {
            if (_mode != PreparedFaultMode.RemoveOnce || !Matches(card))
            {
                return;
            }

            _mode = PreparedFaultMode.None;
            throw new InvalidOperationException("injected-remove");
        }

        private static void PrefixAddInternal(CardPile __instance, CardModel card)
        {
            if (!Matches(card))
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

        private static bool PrefixTriggerAnim(ref Task __result)
        {
            __result = Task.CompletedTask;
            return false;
        }

        private static bool Matches(CardModel card)
        {
            if (_target is not null)
            {
                return ReferenceEquals(card, _target);
            }

            if (_targetType?.IsInstanceOfType(card) != true)
            {
                return false;
            }

            _target = card;
            return true;
        }
    }

    private sealed class PreparedCombatFixture : IDisposable
    {
        private readonly HashSet<CardModel> _cards = new(ReferenceEqualityComparer.Instance);
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

        public CardModel CreateHostCard(PileType pileType, int index = -1) =>
            AddCard(ModelDb.Card<StrikeIronclad>().ToMutable(), pileType, index);

        public CardModel CreateProductCard(Type cardType, PileType pileType, int index = -1) =>
            AddCard(ProductModel<CardModel>(cardType, nameof(ModelDb.Card)).ToMutable(), pileType, index);

        public void TrackCards(IEnumerable<CardModel> cards)
        {
            foreach (CardModel card in cards)
            {
                _cards.Add(card);
            }
        }

        public void Dispose()
        {
            PreparedFaultInjection.Reset();
            TrackCards(Player.Piles.SelectMany(pile => pile.Cards));
            foreach (CardModel card in _cards)
            {
                if (card.Affliction is not null)
                {
                    CardCmd.ClearAffliction(card);
                }

                foreach (CardPile pile in Player.Piles)
                {
                    while (pile.Cards.Any(candidate => ReferenceEquals(candidate, card)))
                    {
                        pile.RemoveInternal(card, silent: true);
                    }
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

        private CardModel AddCard(CardModel card, PileType pileType, int index)
        {
            CombatState.AddCard(card, Player);
            pileType.GetPile(Player).AddInternal(card, index, silent: true);
            _cards.Add(card);
            return card;
        }

        private static Player CreatePlayer(CombatState combatState)
        {
            var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            SetField(player, "<Deck>k__BackingField", new CardPile(PileType.Deck));
            SetField(player, "_runState", NullRunState.Instance);
            SetPropertyOrBackingField(player, nameof(Player.Character), ModelDb.Character<Ironclad>());
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
                    ?? throw new InvalidOperationException("Unable to create CombatTurnState fixture.");
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

        private static void SetPropertyOrBackingField(object instance, string name, object value)
        {
            PropertyInfo property = instance.GetType().GetProperty(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMemberException(instance.GetType().FullName, name);
            MethodInfo? setter = property.GetSetMethod(nonPublic: true);
            if (setter is not null)
            {
                setter.Invoke(instance, [value]);
                return;
            }

            SetField(instance, $"<{name}>k__BackingField", value);
        }

        private static void SetField(object instance, string name, object value)
        {
            FieldInfo field = AccessTools.Field(instance.GetType(), name)
                ?? throw new MissingFieldException(instance.GetType().FullName, name);
            field.SetValue(instance, value);
        }
    }
}
