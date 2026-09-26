using ChaosCardGenerator;

namespace NinjaSlayer.AutoAnthony;

// Anthony prices one ordinary Energy at 650. Split each existing source card's printed
// budget among its atoms, then average repeated occurrences; this does not change source cards.
internal sealed class NinjaComponentValuation(double perUnit) : IComponentValuation
{
    public int Estimate(ComponentValuationContext context) =>
        Math.Max(1, (int)Math.Round(perUnit * Math.Max(1, context.Value("amount"))));

    internal static ComponentValuationRegistration[] FromSources(IEnumerable<IroncladCardRecipe> recipes) =>
        recipes.SelectMany(recipe => recipe.Atoms.Where(a => a.RuntimeSpec!.Opcode == NinjaComponentSources.Opcode)
            .Select(atom => (Atom: atom, Unit: 650d * (Math.Max(0, recipe.Cost) + 1) / recipe.Atoms.Count
                / Math.Max(1, atom.RuntimeSpec!.Values[0].BaseValue))))
        .GroupBy(item => item.Atom.RuntimeSpec!.Variant)
        .Select(group =>
        {
            double unit = group.Average(item => item.Unit);
            bool downside = group.First().Atom.RuntimeSpec!.Flags.Contains("cost_wording");
            return new ComponentValuationRegistration(NinjaComponentSources.Opcode, group.Key,
                new NinjaComponentValuation(unit), NegativeLinearValuePerUnit: downside ? unit : 0);
        }).ToArray();
}
