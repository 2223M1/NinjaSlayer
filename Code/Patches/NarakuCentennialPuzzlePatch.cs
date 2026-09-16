using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NarakuCentennialPuzzlePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_naraku_centennial_puzzle";
    public static string Description => "Let Centennial Puzzle recognize actual Naraku Life loss.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(CentennialPuzzle), nameof(CentennialPuzzle.AfterDamageReceived),
            [typeof(PlayerChoiceContext), typeof(Creature), typeof(DamageResult), typeof(ValueProp),
                typeof(Creature), typeof(CardModel)])
    ];

    public static void Prefix(ref DamageResult result)
    {
        int absorbed = NarakuLifeDamagePatch.AbsorbedBy(result);
        if (result.UnblockedDamage > 0 || absorbed == 0) return;
        // Only this relic sees the absorption as HP loss. The shared result stays unchanged.
        result = new DamageResult(result.Receiver, result.Props)
        {
            UnblockedDamage = absorbed,
            BlockedDamage = result.BlockedDamage,
            OverkillDamage = result.OverkillDamage,
            WasTargetKilled = result.WasTargetKilled,
            WasBlockBroken = result.WasBlockBroken,
            WasFullyBlocked = result.WasFullyBlocked
        };
    }
}
