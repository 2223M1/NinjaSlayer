using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Cards;

[RegisterCard(typeof(NinjaSlayerCardPool), Inherit = true)]
public abstract class NinjaSlayerCardTemplate : ModCardTemplate, IHitPreviewProvider
{
    protected NinjaSlayerCardTemplate(
        int energyCost,
        CardType type,
        CardRarity rarity,
        TargetType targetType,
        bool shouldShowInCardLibrary)
        : base(energyCost, type, rarity, targetType, shouldShowInCardLibrary)
    {
    }

    public override CardAssetProfile AssetProfile => NinjaSlayerCardAssets.For(this);

    public virtual bool TryGetHitPreview(Creature? target, out int hitCount)
    {
        if (Type != CardType.Attack)
        {
            hitCount = 0;
            return false;
        }

        if (DynamicVars.TryGetValue("CalculatedHits", out DynamicVar? calculatedHits))
        {
            UpdateDynamicVarPreview(CardPreviewMode.Normal, target, DynamicVars);
            hitCount = Math.Max(0, (int)calculatedHits.PreviewValue);
            return true;
        }

        if (DynamicVars.TryGetValue("Repeat", out DynamicVar? repeat))
        {
            hitCount = Math.Max(0, repeat.IntValue);
            return true;
        }

        hitCount = 1;
        return true;
    }
}
