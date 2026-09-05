using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class NinjaSlayerRunSavePatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_run_save_models";
    public static string Description => "Reject missing NinjaSlayer save models before the host substitutes deprecated content.";
    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(Player), nameof(Player.FromSerializable), [typeof(SerializablePlayer)])
    ];

    public static void Prefix(SerializablePlayer save)
    {
        // A placeholder would be persisted by the next autosave. Fail before loading inventory.
        foreach (ModelId? id in save.Deck.Select(card => card.Id)
                     .Concat(save.Relics.Select(relic => relic.Id)).Append(save.CharacterId))
        {
            if (id is not null && id.Entry.StartsWith("NINJA_SLAYER_", StringComparison.Ordinal))
            {
                _ = ModelDb.GetById<AbstractModel>(id);
            }
        }
    }
}
