using MegaCrit.Sts2.Core.Models;

namespace NinjaSlayer.Content;

internal static class NinjaSlayerCharacterIdentity
{
    public static ModelId Canonicalize(ModelId characterId) =>
        characterId == ModelDb.Character<NinjaSlayerRedesignCharacter>().Id
            ? ModelDb.Character<NinjaSlayerCharacter>().Id
            : characterId;
}
