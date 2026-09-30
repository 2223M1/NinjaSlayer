using MegaCrit.Sts2.Core.Entities.Creatures;
using NinjaSlayer.Powers;
using NinjaSlayer.Relics;

namespace NinjaSlayer.Content;

public static class NinjaSlayerFormState
{
    public static bool IsNaraku(Creature creature) =>
        creature.HasPower<NarakuFormPower>() || IsFullyReleasedNaraku(creature);

    public static bool IsFullyReleasedNaraku(Creature creature) =>
        creature.Player?.GetRelic<NarakuUnleashedRelic>() != null;

    public static NinjaSlayerFormPresentation GetPresentation(Creature creature) =>
        NinjaSlayerFormPresentationCatalog.Resolve(
            IsNaraku(creature),
            creature.Player?.GetRelic<NarakuUnleashedRelic>() != null,
            creature.HasPower<OneMindOneBodyPower>());
}
