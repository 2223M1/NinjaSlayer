using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NinjaSlayer.ArchitectureTests;

public sealed class RepositoryArchitectureTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly IReadOnlyList<SourceDocument> Sources = LoadSources();
    private static readonly CSharpCompilation Compilation = CreateCompilation();

    [Fact]
    public void EveryPatchBelongsToExactlyOneTypedCapabilityGroup()
    {
        Dictionary<string, INamedTypeSymbol> patches = DeclaredClasses()
            .Where(item => item.Symbol.AllInterfaces.Any(type => type.Name == "IPatchMethod")
                || item.Declaration.BaseList?.Types.Any(type => type.Type.ToString().EndsWith("IPatchMethod", StringComparison.Ordinal)) == true)
            .ToDictionary(item => item.Symbol.Name, item => item.Symbol, StringComparer.Ordinal);
        Dictionary<string, int> registrations = patches.Keys.ToDictionary(name => name, _ => 0, StringComparer.Ordinal);

        foreach ((ClassDeclarationSyntax declaration, INamedTypeSymbol symbol) in DeclaredClasses()
                     .Where(item => item.Symbol.AllInterfaces.Any(type => type.Name == "IModPatches")
                         || item.Declaration.BaseList?.Types.Any(type => type.Type.ToString().EndsWith("IModPatches", StringComparison.Ordinal)) == true))
        {
            SemanticModel model = Compilation.GetSemanticModel(declaration.SyntaxTree);
            foreach (InvocationExpressionSyntax invocation in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax
                    {
                        Name: GenericNameSyntax { Identifier.Text: "RegisterPatch", TypeArgumentList.Arguments.Count: 1 } generic
                    })
                {
                    continue;
                }

                ITypeSymbol? patchType = model.GetTypeInfo(generic.TypeArgumentList.Arguments[0]).Type;
                string patchName = patchType?.Name ?? generic.TypeArgumentList.Arguments[0].ToString();
                Assert.True(patches.ContainsKey(patchName),
                    $"Capability group {symbol.Name} registers unknown patch {patchName}.");
                registrations[patchName]++;
            }
        }

        foreach ((string patch, int count) in registrations)
        {
            Assert.True(count == 1, $"Patch {patch} must belong to exactly one capability group; found {count}.");
        }
    }

    [Fact]
    public void EveryTypedCapabilityGroupIsInstalledExactlyOnce()
    {
        string[] groups = DeclaredClasses()
            .Where(item => item.Symbol.AllInterfaces.Any(type => type.Name == "IModPatches")
                || item.Declaration.BaseList?.Types.Any(type =>
                    type.Type.ToString().EndsWith("IModPatches", StringComparison.Ordinal)) == true)
            .Select(item => item.Symbol.Name)
            .ToArray();
        CompilationUnitSyntax entry = Sources.Single(source => source.RelativePath == "Scripts/Entry.cs").Root;

        foreach (string group in groups)
        {
            int installationCount = entry.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Count(invocation => invocation.Expression is GenericNameSyntax
                {
                    Identifier.Text: "InstallCapability",
                    TypeArgumentList.Arguments.Count: 1
                } generic && generic.TypeArgumentList.Arguments[0].ToString() == group);
            Assert.Equal(1, installationCount);
        }
    }

    [Fact]
    public void CapabilityIdsAreCentralizedAndUnique()
    {
        ClassDeclarationSyntax ids = Sources
            .SelectMany(source => source.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Single(declaration => declaration.Identifier.Text == "NinjaSlayerCapabilityIds");
        VariableDeclaratorSyntax[] declarations = ids.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declaration.Variables)
            .Where(variable => variable.Initializer?.Value is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.StringLiteralExpression))
            .ToArray();
        string[] values = declarations
            .Select(variable => variable.Initializer?.Value)
            .OfType<LiteralExpressionSyntax>()
            .Select(literal => literal.Token.ValueText)
            .ToArray();

        Assert.NotEmpty(values);
        Assert.Equal(values.Length, values.Distinct(StringComparer.Ordinal).Count());

        CompilationUnitSyntax entry = Sources.Single(source => source.RelativePath == "Scripts/Entry.cs").Root;
        Assert.DoesNotContain(
            entry.DescendantNodes().OfType<InvocationExpressionSyntax>(),
            invocation => invocation.Expression is GenericNameSyntax { Identifier.Text: "InstallCapability" }
                && invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax);

        string[] installedIds = entry.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.Expression is GenericNameSyntax { Identifier.Text: "InstallCapability" })
            .Select(invocation => invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression)
            .OfType<MemberAccessExpressionSyntax>()
            .Where(member => member.Expression.ToString() == "NinjaSlayerCapabilityIds")
            .Select(member => member.Name.Identifier.Text)
            .ToArray();
        Assert.Equal(
            declarations.Select(declaration => declaration.Identifier.Text).Order(StringComparer.Ordinal),
            installedIds.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void RuntimeCapabilityGatesHaveNoMutableSetters()
    {
        ClassDeclarationSyntax gates = Sources
            .SelectMany(source => source.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Single(declaration => declaration.Identifier.Text == "NinjaSlayerPatchCapabilities");

        Assert.All(
            gates.Members.OfType<PropertyDeclarationSyntax>(),
            property => Assert.True(
                property.AccessorList == null
                || property.AccessorList.Accessors.All(accessor =>
                    !accessor.IsKind(SyntaxKind.SetAccessorDeclaration)
                    && !accessor.IsKind(SyntaxKind.InitAccessorDeclaration)),
                $"Capability gate {property.Identifier.Text} must be get-only."));
    }

    [Fact]
    public void PreparedSafetyRunsAfterPileHooksAndGameplayCreationIsGated()
    {
        string preparedPatches = Sources
            .Single(source => source.RelativePath == "Code/Patches/PreparedCardPatches.cs")
            .Root
            .ToFullString();
        Assert.Contains("PreparedPileChangeSafetyPatch", preparedPatches, StringComparison.Ordinal);
        Assert.Contains(
            "CompletePileChangeAfter(__result, combatState, card, oldPile)",
            preparedPatches,
            StringComparison.Ordinal);
        Assert.DoesNotContain("PreparedPileExitPatch", preparedPatches, StringComparison.Ordinal);
        Assert.DoesNotContain("nameof(CardPile.RemoveInternal)", preparedPatches, StringComparison.Ordinal);

        MethodDeclarationSyntax canPrepare = Sources
            .Single(source => source.RelativePath == "Code/Commands/PrepareCmd.cs")
            .Root
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.Text == "CanPrepare");
        Assert.Contains("PreparedGameplayEnabled", canPrepare.ToFullString(), StringComparison.Ordinal);

        string prepareCommand = Sources
            .Single(source => source.RelativePath == "Code/Commands/PrepareCmd.cs")
            .Root
            .ToFullString();
        Assert.Contains("internal static class PrepareCmd", prepareCommand, StringComparison.Ordinal);
        Assert.Contains("Task<PreparedApplyResult>", prepareCommand, StringComparison.Ordinal);
        Assert.Contains("PreparedQueueCompatibility.TryReposition", prepareCommand, StringComparison.Ordinal);
        Assert.Contains("RepairAfterApplyFailure", prepareCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveInternal", prepareCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("AddInternal", prepareCommand, StringComparison.Ordinal);
        Assert.DoesNotContain(
            Sources.Select(source => source.Root.ToFullString()),
            source => source.Contains("PreparedQueueReorderContext", StringComparison.Ordinal));

        string queueCompatibility = Sources
            .Single(source => source.RelativePath == "Code/Compatibility/PreparedQueueCompatibility.cs")
            .Root
            .ToFullString();
        Assert.Contains("ExpectedAddIlSha256", queueCompatibility, StringComparison.Ordinal);
        Assert.Contains("ExpectedRemoveIlSha256", queueCompatibility, StringComparison.Ordinal);
        Assert.Contains("PreparedQueueTransaction.Execute", queueCompatibility, StringComparison.Ordinal);

        string actions = Sources
            .Single(source => source.RelativePath == "Content/NinjaSlayerActions.cs")
            .Root
            .ToFullString();
        string nextDiscard = Sources
            .Single(source => source.RelativePath == "Powers/NextDiscardPreparedPower.cs")
            .Root
            .ToFullString();
        Assert.Contains("_ = await PrepareCmd.Apply", actions, StringComparison.Ordinal);
        Assert.Contains("_ = await PrepareCmd.Apply", nextDiscard, StringComparison.Ordinal);

        string entry = Sources.Single(source => source.RelativePath == "Scripts/Entry.cs").Root.ToFullString();
        Assert.True(
            entry.IndexOf("InstallCapability<PreparedSafetyPatchGroup>", StringComparison.Ordinal)
            < entry.IndexOf("InstallCapability<PreparedGameplayPatchGroup>", StringComparison.Ordinal));
    }

    [Fact]
    public void PreparedSafetyUsesContextualCombatStateAccess()
    {
        string service = SourceText("Code/Prepared/PreparedSafetyService.cs");
        string accessor = SourceText("Code/Prepared/CombatStateAccessor.cs");
        string patches = SourceText("Code/Patches/PreparedCardPatches.cs");

        Assert.DoesNotContain("DebugOnlyGetState", service, StringComparison.Ordinal);
        Assert.DoesNotContain("DebugOnlyGetState", patches, StringComparison.Ordinal);
        Assert.Contains("ICombatStateAccessor", service, StringComparison.Ordinal);
        Assert.Contains("class CardCombatStateAccessor", accessor, StringComparison.Ordinal);
        Assert.Contains("PreparedCleanupStatus.Deferred", service, StringComparison.Ordinal);
        Assert.Contains("ICombatState? combatState", patches, StringComparison.Ordinal);
    }

    [Fact]
    public void NextDiscardSourceProtectionUsesASerializedOneShotCardMarker()
    {
        string power = Sources
            .Single(source => source.RelativePath == "Powers/NextDiscardPreparedPower.cs")
            .Root
            .ToFullString();
        Assert.Contains("CardCmd.Afflict<NextDiscardSourceAffliction>", power, StringComparison.Ordinal);
        Assert.Contains("CardCmd.ClearAffliction(card)", power, StringComparison.Ordinal);
        Assert.DoesNotContain("CardPlayResolutionScope", power, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceProtectionState", power, StringComparison.Ordinal);

        string marker = Sources
            .Single(source => source.RelativePath == "Afflictions/NextDiscardSourceAffliction.cs")
            .Root
            .ToFullString();
        Assert.Contains("[RegisterAffliction]", marker, StringComparison.Ordinal);
        Assert.Contains("AfterCardChangedPilesLate", marker, StringComparison.Ordinal);
        Assert.Contains("card.Pile?.Type != PileType.Play", marker, StringComparison.Ordinal);
        Assert.DoesNotContain("AfflictionAssetProfile", marker, StringComparison.Ordinal);
    }

    [Fact]
    public void ChadoRulesDoNotOwnNodesAndPresentationCoversEveryBindingOrder()
    {
        string rules = Sources
            .Single(source => source.RelativePath == "Content/ChadoCombatRules.cs")
            .Root
            .ToFullString();
        Assert.DoesNotContain("NCard", rules, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateVisuals", rules, StringComparison.Ordinal);

        string patches = Sources
            .Single(source => source.RelativePath == "Code/Patches/ChadoPresentationPatches.cs")
            .Root
            .ToFullString();
        Assert.Contains("nameof(CardModel.InvokeEnergyCostChanged)", patches, StringComparison.Ordinal);
        Assert.Contains("nameof(NCard._Ready)", patches, StringComparison.Ordinal);
        Assert.Contains("PatchTarget.Setter<NCard>(nameof(NCard.Model))", patches, StringComparison.Ordinal);
        Assert.DoesNotContain("set_Model", patches, StringComparison.Ordinal);
    }

    [Fact]
    public void NancyFilteringAndLoadedRepairHaveIndependentCompatibilityBoundaries()
    {
        string patches = Sources
            .Single(source => source.RelativePath == "Code/Patches/NancyLeeAvailabilityPatches.cs")
            .Root
            .ToFullString();
        Assert.Contains(
            "HarmonyAfter(NancyCompatibility.RitsuLibContentRegistryHarmonyId)",
            patches,
            StringComparison.Ordinal);
        Assert.DoesNotContain("AccessTools", patches, StringComparison.Ordinal);

        string groups = Sources
            .Single(source => source.RelativePath == "Code/Patches/NinjaSlayerPatchGroups.cs")
            .Root
            .ToFullString();
        Assert.Contains("class NancyCandidateFilterPatchGroup", groups, StringComparison.Ordinal);
        Assert.Contains("class NancyLoadedRunRepairPatchGroup", groups, StringComparison.Ordinal);
        Assert.DoesNotContain("class NancyCompatibilityPatchGroup", groups, StringComparison.Ordinal);

        string compatibility = Sources
            .Single(source => source.RelativePath == "Code/Compatibility/NancyCompatibility.cs")
            .Root
            .ToFullString();
        Assert.Contains("CapabilityProbe.Required", compatibility, StringComparison.Ordinal);
        Assert.DoesNotContain("CapabilityProbe.Optional", compatibility, StringComparison.Ordinal);

        string entry = Sources.Single(source => source.RelativePath == "Scripts/Entry.cs").Root.ToFullString();
        Assert.Contains("InstallCapability<NancyCandidateFilterPatchGroup>", entry, StringComparison.Ordinal);
        Assert.Contains("InstallCapability<NancyLoadedRunRepairPatchGroup>", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void DebugCardPoolCoversEveryPotionFutureRewardBucket()
    {
        ClassDeclarationSyntax coverage = Sources
            .SelectMany(source => source.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Single(declaration => declaration.Identifier.Text == "NinjaSlayerDebugRewardCoverage");
        int minimumCandidates = coverage.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declaration.Variables)
            .Single(variable => variable.Identifier.Text == "MinimumCandidatesPerBucket")
            .Initializer?.Value is LiteralExpressionSyntax minimumLiteral
            ? (int)minimumLiteral.Token.Value!
            : throw new InvalidOperationException("Potion Future minimum candidate count must be a literal integer.");
        VariableDeclaratorSyntax requiredBuckets = coverage.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declaration.Variables)
            .Single(variable => variable.Identifier.Text == "RequiredBuckets");
        (string Rarity, string Type)[] required = RequiredRewardBuckets(requiredBuckets).ToArray();

        ClassDeclarationSyntax catalog = Sources
            .SelectMany(source => source.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Single(declaration => declaration.Identifier.Text == "NinjaSlayerDebugCardCatalog");
        HashSet<string> removed = TypeNames(catalog, "RemovedCards").ToHashSet(StringComparer.Ordinal);
        Dictionary<string, string> replacements = ReplacementTypes(catalog);
        string[] selectedTypes = TypeNames(catalog, "BaselineCards")
            .Where(type => !removed.Contains(type))
            .Select(type => replacements.GetValueOrDefault(type, type))
            .Concat(TypeNames(catalog, "AdditionalCards"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, (string Rarity, string Type)> cardSpecs = CardRewardMetadata();

        foreach (string cardType in selectedTypes)
        {
            Assert.True(cardSpecs.ContainsKey(cardType), $"Debug card {cardType} has no readable CardSpec metadata.");
        }
        foreach ((string rarity, string type) in required)
        {
            int count = selectedTypes.Count(cardType => cardSpecs[cardType] == (rarity, type));
            Assert.True(
                count >= minimumCandidates,
                $"The Future of Potions requires {minimumCandidates} {rarity}/{type} cards, but the debug pool has {count}.");
        }

        MethodDeclarationSyntax createCards = catalog.Members.OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.Text == "CreateCards");
        Assert.Contains(
            createCards.DescendantNodes().OfType<InvocationExpressionSyntax>(),
            invocation => invocation.Expression.ToString()
                == "NinjaSlayerDebugRewardCoverage.EnsurePotionFutureCoverage");
    }

    [Fact]
    public void FormPresentationPoliciesAreCentralized()
    {
        string overlay = Sources
            .Single(source => source.RelativePath == "Code/Nodes/NarakuVisualOverlay.cs")
            .Root
            .ToFullString();
        Assert.Contains("NinjaSlayerFormState.GetPresentation", overlay, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerFormPresentationCatalog.ResolveBodyTexturePath", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("res://", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("enum FormVisual", overlay, StringComparison.Ordinal);

        string[] presentationConsumers =
        [
            "Code/ExternalAnimations/NinjaSlayerXAttackSequence.cs",
            "Code/ExternalAnimations/SpinComboAudio.cs",
            "Code/Nodes/NinjaSlayerSpinPivot.cs",
            "Content/NinjaSlayerCombatAudio.cs",
            "Content/NinjaSlayerCombatVisuals.cs",
            "Powers/HellTornadoPower.cs"
        ];
        foreach (string relativePath in presentationConsumers)
        {
            string source = Sources.Single(item => item.RelativePath == relativePath).Root.ToFullString();
            Assert.Contains("NinjaSlayerFormState.GetPresentation", source, StringComparison.Ordinal);
        }

        foreach (SourceDocument source in Sources.Where(source => source.RelativePath != "Content/NinjaSlayerFormState.cs"))
        {
            Assert.DoesNotContain("IsFullyReleasedNaraku(", source.Root.ToFullString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CharacterPresentationResponsibilitiesAreSeparated()
    {
        string character = Sources
            .Single(source => source.RelativePath == "Content/NinjaSlayerCharacter.cs")
            .Root
            .ToFullString();
        Assert.Contains("NinjaSlayerCharacterStats", character, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerAssetProfile.Profile", character, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerAnimations.BuildCombatAnimationStateMachine", character, StringComparison.Ordinal);
        Assert.DoesNotContain("res://", character, StringComparison.Ordinal);
        Assert.DoesNotContain("ModVisualCues", character, StringComparison.Ordinal);
        Assert.DoesNotContain("CharacterWorldProceduralVisualSetBuilder", character, StringComparison.Ordinal);
        Assert.DoesNotContain("VisualNodeStyle", character, StringComparison.Ordinal);

        string assets = Sources
            .Single(source => source.RelativePath == "Content/NinjaSlayerAssetProfile.cs")
            .Root
            .ToFullString();
        Assert.Contains("NinjaSlayerAnimationCatalog.CombatVisualCues", assets, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerWorldVisualProfile.Profile", assets, StringComparison.Ordinal);

        string animations = Sources
            .Single(source => source.RelativePath == "Content/NinjaSlayerAnimationCatalog.cs")
            .Root
            .ToFullString();
        Assert.Contains("IdleFrameDuration = 1f / 24f", animations, StringComparison.Ordinal);
        Assert.Contains(".Sequence(\"idle\", AddIdleFrames)", animations, StringComparison.Ordinal);
        Assert.Contains(".Sequence(\"archived_attack\"", animations, StringComparison.Ordinal);
        Assert.Contains(".Sequence(\"archived_hit\"", animations, StringComparison.Ordinal);
        Assert.Contains(".Sequence(\"archived_blocked_hit\"", animations, StringComparison.Ordinal);
    }

    [Fact]
    public void CharacterStatsAndAssetPathsMatchThePreSplitSnapshot()
    {
        CompilationUnitSyntax stats = Sources
            .Single(source => source.RelativePath == "Content/NinjaSlayerCharacterStats.cs")
            .Root;
        Dictionary<string, string> statValues = stats.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(variable => variable.Initializer is not null)
            .ToDictionary(
                variable => variable.Identifier.Text,
                variable => variable.Initializer!.Value.ToString(),
                StringComparer.Ordinal);
        Assert.Equal("72", statValues["StartingHp"]);
        Assert.Equal("99", statValues["StartingGold"]);
        Assert.Equal("0.15f", statValues["AttackAnimDelay"]);
        Assert.Equal("0.2f", statValues["CastAnimDelay"]);
        Assert.Equal("false", statValues["RequiresEpochAndTimeline"]);

        CompilationUnitSyntax assets = Sources
            .Single(source => source.RelativePath == "Content/NinjaSlayerAssetProfile.cs")
            .Root;
        Dictionary<string, string> paths = assets.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(variable => variable.Initializer?.Value is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.StringLiteralExpression))
            .ToDictionary(
                variable => variable.Identifier.Text,
                variable => ((LiteralExpressionSyntax)variable.Initializer!.Value).Token.ValueText,
                StringComparer.Ordinal);
        Assert.Equal("res://NinjaSlayer/scenes/creature_visuals/ninja_slayer.tscn", paths["VisualsPath"]);
        Assert.Equal("res://NinjaSlayer/scenes/ui/ninja_slayer_energy_counter.tscn", paths["EnergyCounterPath"]);
        Assert.Equal(
            "res://NinjaSlayer/materials/transitions/ninja_slayer_transition_mat.tres",
            paths["CharacterSelectTransitionMaterialPath"]);
        Assert.Equal("res://NinjaSlayer/videos/ninja_slayer_transition.ogv", paths["TransitionVideoPath"]);
    }

    [Fact]
    public void WorldPresentationStylesUseAbsoluteCoordinates()
    {
        CompilationUnitSyntax world = Sources
            .Single(source => source.RelativePath == "Content/NinjaSlayerWorldVisualProfile.cs")
            .Root;
        Assert.DoesNotContain(
            world.DescendantNodes().OfType<InvocationExpressionSyntax>(),
            invocation => invocation.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.Text: "WithOffset"
            });

        ClassDeclarationSyntax[] placements = world.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Where(declaration => declaration.Identifier.Text is "Merchant" or "RestSite")
            .ToArray();
        Assert.Equal(2, placements.Length);
        foreach (ClassDeclarationSyntax placement in placements)
        {
            MethodDeclarationSyntax style = placement.Members
                .OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.Text == "BodyStyle");
            InvocationExpressionSyntax[] calls = style.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .ToArray();
            Assert.Single(calls, invocation => invocation.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.Text: "WithPosition"
            });
            Assert.Single(calls, invocation => invocation.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.Text: "WithScale"
            });
        }
    }

    [Fact]
    public void PresentationConsumersUseDedicatedCatalogs()
    {
        string animations = Sources
            .Single(source => source.RelativePath == "Content/NinjaSlayerAnimations.cs")
            .Root
            .ToFullString();
        Assert.Contains("NinjaSlayerAnimationCatalog.AttackCueName", animations, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerAnimationCatalog.CombatVisualCues", animations, StringComparison.Ordinal);

        string transitionVideo = Sources
            .Single(source => source.RelativePath == "Code/Transition/NinjaSlayerTransitionVideo.cs")
            .Root
            .ToFullString();
        string transitionPaths = Sources
            .Single(source => source.RelativePath == "Code/Transition/NinjaSlayerTransitionPaths.cs")
            .Root
            .ToFullString();
        Assert.Contains("NinjaSlayerAssetProfile.TransitionVideoPath", transitionVideo, StringComparison.Ordinal);
        Assert.Contains(
            "NinjaSlayerAssetProfile.CharacterSelectTransitionMaterialPath",
            transitionPaths,
            StringComparison.Ordinal);

        foreach (SourceDocument source in Sources.Where(source => source.RelativePath != "Content/NinjaSlayerCharacter.cs"))
        {
            string text = source.Root.ToFullString();
            Assert.DoesNotContain("NinjaSlayerCharacter.CombatVisualCues", text, StringComparison.Ordinal);
            Assert.DoesNotContain("NinjaSlayerCharacter.TransitionVideoPath", text, StringComparison.Ordinal);
            Assert.DoesNotContain("NinjaSlayerCharacter.OriginalAnimations", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void HarmonyInstallationIsCentralizedInCompatibilityInfrastructure()
    {
        string[] allowed =
        [
            Normalize("Code/Compatibility/Patching/ExplicitHarmonyPatchAdapter.cs")
        ];

        foreach (SourceDocument source in Sources)
        {
            bool installsHarmony = source.Root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
                .Any(creation => creation.Type.ToString() is "Harmony" or "HarmonyLib.Harmony")
                || source.Root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Any(invocation => invocation.Expression.ToString().Contains(".Patch(", StringComparison.Ordinal));
            if (installsHarmony)
            {
                Assert.Contains(source.RelativePath, allowed);
            }
        }
    }

    [Fact]
    public void ModRegistrationUsesTheCanonicalModId()
    {
        string[] registrationMethods =
        [
            "CreateLogger", "CreatePatcher", "BeginModDataRegistration", "CreateContentPack",
            "RegisterApplicant", "GetRunSavedDataStore"
        ];

        foreach (SourceDocument source in Sources)
        {
            foreach (InvocationExpressionSyntax invocation in source.Root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                string methodName = invocation.Expression switch
                {
                    MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
                    IdentifierNameSyntax identifier => identifier.Identifier.Text,
                    _ => string.Empty
                };
                if (!registrationMethods.Contains(methodName, StringComparer.Ordinal))
                {
                    continue;
                }

                Assert.DoesNotContain(invocation.ArgumentList.Arguments,
                    argument => argument.Expression is LiteralExpressionSyntax literal
                        && literal.IsKind(SyntaxKind.StringLiteralExpression)
                        && literal.Token.ValueText == "NinjaSlayer");
            }
        }
    }

    [Fact]
    public void RunHistoryTelemetryUsesTheFailClosedLocalIdentityTracker()
    {
        string telemetry = Sources
            .Single(source => source.RelativePath == "Content/NinjaSlayerBalanceTelemetry.cs")
            .Root
            .ToFullString();

        Assert.Contains("captureFilter: ShouldCaptureRunHistory", telemetry, StringComparison.Ordinal);
        Assert.Contains("IdentityTracker.TryCaptureCompletedRun", telemetry, StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerPatchCapabilities.TelemetryIdentityEnabled", telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("evt.Run.Players.Any", telemetry, StringComparison.Ordinal);
    }

    [Fact]
    public void GodotNodeScriptsArePubliclyResolvableFromScriptPathAttributes()
    {
        foreach ((ClassDeclarationSyntax declaration, INamedTypeSymbol symbol) in DeclaredClasses())
        {
            bool isGodotScript = declaration.Modifiers.Any(SyntaxKind.PartialKeyword)
                && declaration.BaseList?.Types.Any(type => type.Type.ToString() is
                    "Node" or "Node2D" or "Control" or "Godot.Node" or "Godot.Node2D" or "Godot.Control") == true;
            if (isGodotScript)
            {
                Assert.Equal(Accessibility.Public, symbol.DeclaredAccessibility);
            }
        }
    }

    [Fact]
    public void PatchIdsAreUniqueAndNonEmpty()
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((ClassDeclarationSyntax declaration, INamedTypeSymbol symbol) in DeclaredClasses())
        {
            if (!symbol.AllInterfaces.Any(type => type.Name == "IPatchMethod")
                && declaration.BaseList?.Types.Any(type => type.Type.ToString().EndsWith("IPatchMethod", StringComparison.Ordinal)) != true)
            {
                continue;
            }

            PropertyDeclarationSyntax? property = declaration.Members.OfType<PropertyDeclarationSyntax>()
                .SingleOrDefault(member => member.Identifier.Text == "PatchId");
            string? id = property?.ExpressionBody?.Expression is LiteralExpressionSyntax literal
                ? literal.Token.ValueText
                : null;
            Assert.False(string.IsNullOrWhiteSpace(id), $"Patch {symbol.Name} must declare a literal PatchId.");
            Assert.True(ids.TryAdd(id!, symbol.Name),
                $"Patch id {id} is shared by {ids.GetValueOrDefault(id!)} and {symbol.Name}.");
        }
    }

    [Fact]
    public void LethalProtectionPatchMaintainsTypedFinalizerContract()
    {
        ClassDeclarationSyntax patch = Sources
            .SelectMany(source => source.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Single(declaration => declaration.Identifier.Text == "NinjaSlayerFinisherLethalDamagePatch");
        MethodDeclarationSyntax prefix = patch.Members.OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.Text == "Prefix");
        MethodDeclarationSyntax postfix = patch.Members.OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.Text == "Postfix");
        MethodDeclarationSyntax finalizer = patch.Members.OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.Text == "Finalizer");

        ParameterSyntax prefixState = prefix.ParameterList.Parameters.Single(parameter => parameter.Identifier.Text == "__state");
        ParameterSyntax postfixState = postfix.ParameterList.Parameters.Single(parameter => parameter.Identifier.Text == "__state");
        ParameterSyntax finalizerState = finalizer.ParameterList.Parameters.Single(parameter => parameter.Identifier.Text == "__state");
        Assert.Equal("FinisherProtectionToken?", prefixState.Type?.ToString());
        Assert.Equal(prefixState.Type?.ToString(), postfixState.Type?.ToString());
        Assert.Equal(prefixState.Type?.ToString(), finalizerState.Type?.ToString());
        Assert.Contains(postfix.ParameterList.Parameters,
            parameter => parameter.Identifier.Text == "__runOriginal" && parameter.Type?.ToString() == "bool");
        Assert.Equal("Exception?", finalizer.ReturnType.ToString());
        Assert.Contains(finalizer.DescendantNodes().OfType<CatchClauseSyntax>(), _ => true);
        Assert.Contains(finalizer.DescendantNodes().OfType<ReturnStatementSyntax>(),
            statement => statement.Expression?.ToString() == "__exception");
    }

    [Fact]
    public void FinisherAttackAdaptationIsSeparatedFromTheCinematicOrchestrator()
    {
        string orchestrator = Sources
            .Single(source => source.RelativePath == "Code/ExternalAnimations/NinjaSlayerFinisherCinematic.cs")
            .Root
            .ToFullString();
        string adapter = Sources
            .Single(source => source.RelativePath == "Code/ExternalAnimations/FinisherAttackCommandAdapter.cs")
            .Root
            .ToFullString();

        Assert.DoesNotContain("class FinisherAttackCommandAdapter", orchestrator, StringComparison.Ordinal);
        Assert.Contains("GameCompatibility.Finisher.TryReadAttackCommand", adapter, StringComparison.Ordinal);
        Assert.Contains("new FinisherAttackSpec", adapter, StringComparison.Ordinal);
    }

    [Fact]
    public void FinisherRuntimeResponsibilitiesRemainSeparated()
    {
        string orchestrator = SourceText("Code/ExternalAnimations/NinjaSlayerFinisherCinematic.cs");
        string session = SourceText("Code/ExternalAnimations/FinisherSession.cs");
        string registry = SourceText("Code/ExternalAnimations/FinisherSessionRegistry.cs");
        string eligibility = SourceText("Code/ExternalAnimations/FinisherEligibilityService.cs");
        string protection = SourceText("Code/ExternalAnimations/FinisherProtectionService.cs");
        string cleanup = SourceText("Code/ExternalAnimations/FinisherCleanupService.cs");
        string forecast = SourceText("Code/ExternalAnimations/FinisherForecast.cs");

        Assert.DoesNotContain("class FinisherSession", orchestrator, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionRegistrySync", orchestrator, StringComparison.Ordinal);
        Assert.DoesNotContain("static class FinisherForecast", orchestrator, StringComparison.Ordinal);
        Assert.Contains("class FinisherSession", session, StringComparison.Ordinal);
        Assert.Contains("static class FinisherSessionRegistry", registry, StringComparison.Ordinal);
        Assert.Contains("static class FinisherEligibilityService", eligibility, StringComparison.Ordinal);
        Assert.Contains("static class FinisherProtectionService", protection, StringComparison.Ordinal);
        Assert.Contains("static class FinisherCleanupService", cleanup, StringComparison.Ordinal);
        Assert.Contains("static class FinisherForecast", forecast, StringComparison.Ordinal);
    }

    [Fact]
    public void FinisherForecastUsesDeterministicStructuredSearchAndFrameCaching()
    {
        string engine = SourceText("Code/Combat/FinisherForecastEngine.cs");
        string forecast = SourceText("Code/ExternalAnimations/FinisherForecast.cs");

        Assert.DoesNotContain("StringBuilder", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("Func<TState, string>", engine, StringComparison.Ordinal);
        Assert.Contains("FinisherForecastSimulation<TState, TStateKey>", engine, StringComparison.Ordinal);
        Assert.Contains("FinisherForecastSearchKey<TStateKey>", engine, StringComparison.Ordinal);
        Assert.Contains("FrameScopedCache<FinisherForecastFrameKey, CachedForecast>", forecast, StringComparison.Ordinal);
        Assert.Contains("Engine.GetProcessFrames()", forecast, StringComparison.Ordinal);
    }

    [Fact]
    public void HighFrequencyContextsUseOwnedScopes()
    {
        string karatePatch = SourceText("Code/Patches/KarateHealthBarPreviewPatch.cs");
        string combo = SourceText("Code/ExternalAnimations/XAttackComboContext.cs");
        string audio = SourceText("Code/ExternalAnimations/XAttackAudioContext.cs");
        string shake = SourceText("Code/Combat/ScreenShakeSuppressionContext.cs");
        string cadence = SourceText("Code/Combat/TornadoFistFinisherCadenceContext.cs");

        Assert.Contains("KaratePreviewScopeRegistry.Replace", karatePatch, StringComparison.Ordinal);
        Assert.Contains("KaratePreviewScopeRegistry.Release", karatePatch, StringComparison.Ordinal);
        Assert.Contains("AsyncLocal<State?>", combo, StringComparison.Ordinal);
        Assert.Contains("AsyncScopeDepth", audio, StringComparison.Ordinal);
        Assert.Contains("AsyncScopeDepth", shake, StringComparison.Ordinal);
        Assert.Contains("AsyncScopeDepth", cadence, StringComparison.Ordinal);
    }

    [Fact]
    public void FinisherDeathKickFailureOnlyDisablesPresentation()
    {
        string groups = SourceText("Code/Patches/NinjaSlayerPatchGroups.cs");
        string patches = SourceText("Code/Patches/NinjaSlayerFinisherPatches.cs");
        ClassDeclarationSyntax core = Sources
            .SelectMany(source => source.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Single(declaration => declaration.Identifier.Text == "FinisherCorePatchGroup");
        ClassDeclarationSyntax presentation = Sources
            .SelectMany(source => source.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Single(declaration => declaration.Identifier.Text == "FinisherPresentationPatchGroup");
        string compatibility = SourceText("Code/Compatibility/GameCompatibility.cs");

        Assert.DoesNotContain("NinjaSlayerFinisherDeathStartPatch", core.ToFullString(), StringComparison.Ordinal);
        Assert.Contains("NinjaSlayerFinisherDeathStartPatch", presentation.ToFullString(), StringComparison.Ordinal);
        Assert.Contains("GetPresentationProbes", compatibility, StringComparison.Ordinal);
        Assert.Contains("NCreature.start-death-animation", compatibility, StringComparison.Ordinal);
        Assert.Contains("FinisherPresentationPatchGroup", groups, StringComparison.Ordinal);
        Assert.Contains("DeathAnimationTask", patches, StringComparison.Ordinal);
    }

    private static string SourceText(string relativePath) => Sources
        .Single(source => source.RelativePath == relativePath)
        .Root
        .ToFullString();

    private static IEnumerable<(ClassDeclarationSyntax Declaration, INamedTypeSymbol Symbol)> DeclaredClasses()
    {
        foreach (SourceDocument source in Sources)
        {
            SemanticModel model = Compilation.GetSemanticModel(source.Tree);
            foreach (ClassDeclarationSyntax declaration in source.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is INamedTypeSymbol symbol)
                {
                    yield return (declaration, symbol);
                }
            }
        }
    }

    private static IEnumerable<string> TypeNames(ClassDeclarationSyntax catalog, string fieldName)
    {
        CollectionExpressionSyntax collection = catalog.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declaration.Variables)
            .Single(variable => variable.Identifier.Text == fieldName)
            .Initializer?.Value as CollectionExpressionSyntax
            ?? throw new InvalidOperationException($"{fieldName} must use a collection expression.");
        return collection.Elements
            .OfType<ExpressionElementSyntax>()
            .Select(element => element.Expression)
            .OfType<TypeOfExpressionSyntax>()
            .Select(expression => expression.Type.ToString());
    }

    private static Dictionary<string, string> ReplacementTypes(ClassDeclarationSyntax catalog)
    {
        CollectionExpressionSyntax collection = catalog.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declaration.Variables)
            .Single(variable => variable.Identifier.Text == "Replacements")
            .Initializer?.Value as CollectionExpressionSyntax
            ?? throw new InvalidOperationException("Replacements must use a collection expression.");
        return collection.Elements
            .OfType<ExpressionElementSyntax>()
            .Select(element => element.Expression)
            .OfType<TupleExpressionSyntax>()
            .ToDictionary(
                tuple => ((TypeOfExpressionSyntax)tuple.Arguments[0].Expression).Type.ToString(),
                tuple => ((TypeOfExpressionSyntax)tuple.Arguments[1].Expression).Type.ToString(),
                StringComparer.Ordinal);
    }

    private static IEnumerable<(string Rarity, string Type)> RequiredRewardBuckets(
        VariableDeclaratorSyntax requiredBuckets)
    {
        CollectionExpressionSyntax collection = requiredBuckets.Initializer?.Value as CollectionExpressionSyntax
            ?? throw new InvalidOperationException("RequiredBuckets must use a collection expression.");
        foreach (BaseObjectCreationExpressionSyntax creation in collection.Elements
                     .OfType<ExpressionElementSyntax>()
                     .Select(element => element.Expression)
                     .OfType<BaseObjectCreationExpressionSyntax>())
        {
            SeparatedSyntaxList<ArgumentSyntax> arguments = creation.ArgumentList?.Arguments
                ?? throw new InvalidOperationException("A reward bucket must declare rarity and type arguments.");
            yield return (
                ((MemberAccessExpressionSyntax)arguments[0].Expression).Name.Identifier.Text,
                ((MemberAccessExpressionSyntax)arguments[1].Expression).Name.Identifier.Text);
        }
    }

    private static Dictionary<string, (string Rarity, string Type)> CardRewardMetadata()
    {
        var result = new Dictionary<string, (string Rarity, string Type)>(StringComparer.Ordinal);
        foreach (ClassDeclarationSyntax declaration in Sources
                     .Where(source => source.RelativePath.StartsWith("Cards/", StringComparison.Ordinal))
                     .SelectMany(source => source.Root.DescendantNodes().OfType<ClassDeclarationSyntax>()))
        {
            VariableDeclaratorSyntax? cardSpec = declaration.Members.OfType<FieldDeclarationSyntax>()
                .SelectMany(field => field.Declaration.Variables)
                .SingleOrDefault(variable => variable.Identifier.Text == "CardSpec");
            if (cardSpec?.Initializer?.Value is not BaseObjectCreationExpressionSyntax creation
                || creation.ArgumentList?.Arguments.Count < 4)
            {
                continue;
            }

            SeparatedSyntaxList<ArgumentSyntax> arguments = creation.ArgumentList!.Arguments;
            result.Add(
                declaration.Identifier.Text,
                (
                    ((MemberAccessExpressionSyntax)arguments[3].Expression).Name.Identifier.Text,
                    ((MemberAccessExpressionSyntax)arguments[2].Expression).Name.Identifier.Text
                ));
        }
        return result;
    }

    private static CSharpCompilation CreateCompilation()
    {
        MetadataReference[] references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        return CSharpCompilation.Create(
            "NinjaSlayer.ArchitectureModel",
            Sources.Select(source => source.Tree),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IReadOnlyList<SourceDocument> LoadSources()
    {
        string[] roots = ["Afflictions", "Ancients", "Cards", "Code", "Content", "Enchantments", "Events", "Powers", "Relics", "Scripts"];
        return roots
            .SelectMany(directory => Directory.EnumerateFiles(Path.Combine(Root, directory), "*.cs", SearchOption.AllDirectories))
            .Order(StringComparer.Ordinal)
            .Select(path =>
            {
                string text = File.ReadAllText(path);
                SyntaxTree tree = CSharpSyntaxTree.ParseText(
                    text,
                    new CSharpParseOptions(LanguageVersion.Preview),
                    path);
                return new SourceDocument(
                    Normalize(Path.GetRelativePath(Root, path)),
                    tree,
                    tree.GetCompilationUnitRoot());
            })
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NinjaSlayer.csproj")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private sealed record SourceDocument(
        string RelativePath,
        SyntaxTree Tree,
        CompilationUnitSyntax Root);
}
