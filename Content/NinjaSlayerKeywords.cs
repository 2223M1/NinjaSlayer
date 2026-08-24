using MegaCrit.Sts2.Core.Entities.Cards;
using NinjaSlayer.Scripts;
using STS2RitsuLib.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Keywords;

namespace NinjaSlayer.Content;

[RegisterOwnedCardKeyword(nameof(Scry), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(Judgment), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.AfterCardDescription)]
public class NinjaSlayerKeywords
{
    public static readonly CardKeyword Scry = ModContentRegistry.GetQualifiedKeywordId(NinjaSlayerIds.ModId, nameof(Scry)).GetModCardKeyword();
    public static readonly CardKeyword Judgment = ModContentRegistry.GetQualifiedKeywordId(NinjaSlayerIds.ModId, nameof(Judgment)).GetModCardKeyword();
}
