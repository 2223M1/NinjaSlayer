using global::AutoAnthony;
using ChaosCardGenerator;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace NinjaSlayer.AutoAnthony;

internal sealed class NinjaNativeCardAdapter : IExternalNativeCardAdapter
{
    public string ProfileId => NinjaComponentSources.ProfileId;
    public bool CanHandle(CardModel source) => NinjaComponentSources.Sources.Any(s => s.Card == source.GetType());

    public bool TryCreateDefinition(CardModel source, out GeneratedCard definition)
    {
        var row = NinjaComponentSources.Sources.SingleOrDefault(s => s.Card == source.GetType());
        if (row is null) { definition = null!; return false; }
        CardModel canonical = ModelDb.GetById<CardModel>(source.Id);
        var recipe = NinjaComponentSources.Recipe(row, canonical);
        CardModel upgraded = canonical.ToMutable();
        upgraded.UpgradeInternal();
        upgraded.FinalizeUpgradeInternal();
        var upgradeRecipe = NinjaComponentSources.Recipe(row, upgraded);
        var operations = recipe.Atoms.Select(atom => new GeneratorOperation(atom.Template, atom.Scope,
            atom.ChineseText, new Dictionary<string, int>(), RequiresSingleTarget: atom.RequiresSingleTarget,
            RuntimeSpec: atom.RuntimeSpec, LocalizedText: atom.LocalizedText)).ToArray();
        var effects = new List<CardUpgradeEffect>();
        for (int index = 0; index < operations.Length; index++)
        {
            var original = recipe.Atoms[index].RuntimeSpec!;
            var changed = upgradeRecipe.Atoms[index].RuntimeSpec!;
            foreach (var slot in original.Values)
            {
                int delta = changed.Values.Single(v => v.Id == slot.Id).BaseValue - slot.BaseValue;
                if (delta != 0) effects.Add(new(CardUpgradeKind.IncreaseNumber, index, delta, slot.Id));
            }
        }
        definition = new(recipe.Cost, recipe.Type, recipe.Target, recipe.OriginalRarity,
            string.Join('\n', recipe.Atoms.Select(a => a.ChineseText)), recipe.Tags, operations,
            new(row.Chinese, row.English, [row.Card.Name]),
            new(upgradeRecipe.Cost, effects, string.Join('\n', upgradeRecipe.Atoms.Select(a => a.ChineseText)),
                upgradeRecipe.Tags.Except(recipe.Tags).ToArray(),
                string.Join('\n', upgradeRecipe.Atoms.Select(a => a.LocalizedText!.RenderEnglish(a.RuntimeSpec!)!)),
                recipe.Tags.Except(upgradeRecipe.Tags).ToArray()),
            string.Join('\n', recipe.Atoms.Select(a => a.LocalizedText!.RenderEnglish(a.RuntimeSpec!)!)));
        return true;
    }

    public void CopyInstanceState(CardModel source, ChaosCardModel destination)
    {
        var row = NinjaComponentSources.Sources.Single(s => s.Card == source.GetType());
        var reference = ModelDb.GetById<CardModel>(source.Id).ToMutable();
        for (int level = 0; level < source.CurrentUpgradeLevel; level++)
        {
            reference.UpgradeInternal();
            reference.FinalizeUpgradeInternal();
        }
        var expected = row.Components(reference);
        var actual = row.Components(source);
        var operations = destination.Generated.Operations.Select((operation, index) =>
        {
            var spec = operation.RuntimeSpec! with
            {
                Values = operation.RuntimeSpec!.Values.Select(slot => slot with
                {
                    BaseValue = slot.BaseValue + actual[index].RuntimeSpec!.Values.Single(v => v.Id == slot.Id).BaseValue
                        - expected[index].RuntimeSpec!.Values.Single(v => v.Id == slot.Id).BaseValue
                }).ToArray()
            };
            return operation with { RuntimeSpec = spec, ChineseText = operation.LocalizedText!.RenderChinese(spec) };
        }).ToArray();
        int costDelta = source.EnergyCost.GetWithModifiers(CostModifiers.Local) - reference.EnergyCost.GetWithModifiers(CostModifiers.Local);
        var generated = destination.Generated;
        destination.ApplyFreeformDefinition(generated with
        {
            Cost = generated.Cost < 0 ? generated.Cost : generated.Cost + costDelta,
            Upgrade = generated.Upgrade! with { UpgradedCost = generated.Upgrade!.UpgradedCost < 0
                ? generated.Upgrade.UpgradedCost : generated.Upgrade.UpgradedCost + costDelta },
            Operations = operations
        });
    }
}
