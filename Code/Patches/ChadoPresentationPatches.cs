using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using NinjaSlayer.Code.Presentation;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class ChadoEnergyCostVisualPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_chado_energy_cost_visual";

    public static string Description => "Refresh a visible Chado after its model energy cost changes.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(CardModel), nameof(CardModel.InvokeEnergyCostChanged))];

    public static void Postfix(CardModel __instance) => ChadoCardPresentation.Refresh(__instance);
}

public sealed class ChadoCardNodeLifecyclePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_chado_card_node_lifecycle";

    public static string Description =>
        "Refresh Chado visuals when a card node becomes ready or receives its model.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NCard), nameof(NCard._Ready)),
        PatchTarget.Setter<NCard>(nameof(NCard.Model))
    ];

    public static void Postfix(NCard __instance) => ChadoCardPresentation.Refresh(__instance);
}
