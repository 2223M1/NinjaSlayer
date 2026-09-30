using MegaCrit.Sts2.Core.Entities.Cards;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Cards.Standard;

[RegisterCard(typeof(NinjaSlayerCardPool), Inherit = true)]
public abstract class NinjaSlayerCommonCard(
    string id,
    int cost,
    CardType type,
    TargetType target) : NinjaSlayerStandaloneCardTemplate(
        new NinjaSlayerCardSpec(id, cost, type, CardRarity.Common, target, true));
