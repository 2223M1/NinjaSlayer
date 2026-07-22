using MegaCrit.Sts2.Core.Entities.Creatures;

namespace NinjaSlayer.Code.Combat;

public interface IHitPreviewProvider
{
    bool TryGetHitPreview(Creature? target, out int hitCount);
}

internal static class HitPreviewResolver
{
    public static bool TryResolve(MegaCrit.Sts2.Core.Models.CardModel card, Creature? target, out int hitCount)
    {
        if (card is IHitPreviewProvider provider && provider.TryGetHitPreview(target, out hitCount))
        {
            hitCount = Math.Max(0, hitCount);
            return true;
        }

        return VanillaHitPreviewCompatibility.TryGetHitCount(card, target, out hitCount);
    }
}
