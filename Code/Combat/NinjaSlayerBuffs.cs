using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using NinjaSlayer.Powers;
#if NINJASLAYER_CHANNEL_STABLE
using AimPower = NinjaSlayer.Powers.BladeSweepPower;
#endif

namespace NinjaSlayer.Code.Combat;

internal static class NinjaSlayerBuffs
{
    public static bool Includes(PowerModel power) =>
        power.Amount > 0 && power.TypeForCurrentAmount == PowerType.Buff
        && power is StrengthPower or DexterityPower or VigorPower or KaratePower or FocusPower
            or ArtifactPower or BufferPower or IntangiblePower or ThornsPower or PlatingPower
            or RegenPower or EvasionPower or NarakuLifePower or StretchPower or SoarPower or AimPower;
}
