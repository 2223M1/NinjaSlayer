using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
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
        [new(typeof(Orobas), "GenerateInitialOptions", Type.EmptyTypes)];

    public static void Postfix(Orobas __instance, IReadOnlyList<EventOption> __result)
    {
        var owner = __instance.Owner
            ?? throw new InvalidOperationException("Orobas generated Sea Glass options without an owning player.");
        CharacterModel ownerCharacter = owner.Character
            ?? throw new InvalidOperationException("Orobas generated Sea Glass options without an owning character.");
        if (ownerCharacter is INinjaSlayerCharacter)
        {
            return;
        }

        foreach (SeaGlass seaGlass in __result
                     .Select(option => option.Relic)
                     .OfType<SeaGlass>())
        {
            if (seaGlass.CharacterId is not { } targetId
                || ModelDb.GetById<CharacterModel>(targetId) is not { } targetCharacter
                || targetCharacter is not INinjaSlayerCharacter)
            {
                continue;
            }

            CharacterModel[] candidates = owner.UnlockState.Characters
                .Where(candidate => candidate is not INinjaSlayerCharacter && candidate.Id != ownerCharacter.Id)
                .ToArray();
            CharacterModel replacement = candidates.Length == 0
                ? ownerCharacter
                : __instance.Rng.NextItem(candidates)
                  ?? throw new InvalidOperationException(
                      "Orobas failed to select from a non-empty Sea Glass character candidate set.");
            seaGlass.CharacterId = replacement.Id;
        }
    }
}
