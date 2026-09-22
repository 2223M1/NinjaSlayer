using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Powers;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class StarlessNightDiscardBatchPatch : IPatchMethod
{
    private static readonly AsyncLocal<HashSet<StarlessNightRedesignPower>?> Batch = new();

    public static string PatchId => "ninjaslayer_starless_discard_batch";
    public static string Description => "Generate at most one Strong Shuriken per owner in a native discard batch.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(CardCmd), nameof(CardCmd.DiscardAndDraw),
            [typeof(PlayerChoiceContext), typeof(IEnumerable<CardModel>), typeof(int)])
    ];

    public static void Prefix(out HashSet<StarlessNightRedesignPower>? __state)
    {
        __state = Batch.Value;
        Batch.Value = [];
    }

    // The async command captures its own batch. Restore the caller immediately so
    // nested discards and independent multiplayer choices have separate allowances.
    public static Exception? Finalizer(Exception? __exception, HashSet<StarlessNightRedesignPower>? __state)
    {
        Batch.Value = __state;
        return __exception;
    }

    internal static bool CanGenerate(StarlessNightRedesignPower power) => Batch.Value?.Add(power) ?? true;
}
