using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using NinjaSlayer.Code.Compatibility;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class OrobasSeaGlassCharacterPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_orobas_sea_glass_character_filter";

    public static string Description =>
        "Prevent Orobas from offering NinjaSlayer's card pool to other characters through Sea Glass.";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(Orobas), GameCompatibility.OrobasSeaGlass.TargetMethodName, Type.EmptyTypes)];

    public static void Postfix(Orobas __instance, IReadOnlyList<EventOption> __result)
    {
        if (!NinjaSlayerPatchCapabilities.CoreContentEnabled
            || !NinjaSlayerPatchCapabilities.OrobasSeaGlassEnabled)
        {
            return;
        }

        CharacterModel? ownerCharacter = __instance.Owner?.Character;
        if (ownerCharacter == null || ownerCharacter is INinjaSlayerCharacter)
        {
            return;
        }

        foreach (SeaGlass seaGlass in __result
                     .Select(option => option.Relic)
                     .OfType<SeaGlass>())
        {
            if (seaGlass.CharacterId is not { } targetId
                || ModelDb.GetById<CharacterModel>(targetId) is not { } targetCharacter
                || !OrobasSeaGlassCandidatePolicy.ShouldReplace(
                    ownerCharacter is INinjaSlayerCharacter,
                    targetCharacter is INinjaSlayerCharacter))
            {
                continue;
            }

            CharacterModel replacement = OrobasSeaGlassCandidatePolicy.SelectReplacement(
                ownerCharacter,
                __instance.Owner!.UnlockState.Characters,
                candidate => candidate is INinjaSlayerCharacter,
                (left, right) => left.Id == right.Id,
                candidates => __instance.Rng.NextItem(candidates) ?? ownerCharacter);
            seaGlass.CharacterId = replacement.Id;
        }
    }
}
