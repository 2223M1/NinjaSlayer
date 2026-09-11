using MegaCrit.Sts2.Core.Entities.Cards;
using NinjaSlayer.Scripts;
using STS2RitsuLib.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Keywords;

namespace NinjaSlayer.Content;

[RegisterOwnedCardKeyword(nameof(Scry), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
public class NinjaSlayerKeywords
{
    public static readonly CardKeyword Scry = ModContentRegistry.GetQualifiedKeywordId(NinjaSlayerIds.ModId, nameof(Scry)).GetModCardKeyword();
}
