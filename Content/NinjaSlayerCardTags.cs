using MegaCrit.Sts2.Core.Entities.Cards;
using NinjaSlayer.Scripts;
using STS2RitsuLib.CardTags;
using STS2RitsuLib.Content;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Content;

[RegisterOwnedCardTag(nameof(Shuriken))]
public class NinjaSlayerCardTags
{
    public static readonly CardTag Shuriken = ModContentRegistry.GetQualifiedCardTagId(NinjaSlayerIds.ModId, nameof(Shuriken)).GetModCardTag();
}
