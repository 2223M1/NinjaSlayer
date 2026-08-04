using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class NarakuPowerUi
    {
        private static readonly FieldInfo? PowerNodes =
            AccessTools.Field(typeof(NPowerContainer), "_powerNodes");
        private static readonly MethodInfo? UpdatePositions =
            AccessTools.Method(typeof(NPowerContainer), "UpdatePositions");

        public static void RemoveStaleNode(Creature owner, PowerModel removedPower)
        {
            if (owner.Powers.Contains(removedPower))
            {
                return;
            }

            NPowerContainer? container = NCombatRoom.Instance
                ?.GetCreatureNode(owner)
                ?.GetNodeOrNull<NCreatureStateDisplay>("%HealthBar")
                ?.GetNodeOrNull<NPowerContainer>("%PowerContainer");
            NPower? node = container
                ?.GetChildren()
                .OfType<NPower>()
                .FirstOrDefault(candidate => ReferenceEquals(candidate.Model, removedPower));
            if (node == null || node.IsQueuedForDeletion())
            {
                return;
            }

            if (PowerNodes?.GetValue(container) is not List<NPower> nodes || UpdatePositions == null)
            {
                Entry.Logger.Warn("Could not remove the stale Naraku power icon: host power-container layout is unavailable.");
                return;
            }

            try
            {
                if (!nodes.Remove(node))
                {
                    return;
                }

                try
                {
                    UpdatePositions.Invoke(container, null);
                }
                finally
                {
                    node.QueueFreeSafely();
                }
            }
            catch (Exception ex)
            {
                Entry.Logger.Warn($"Failed to remove the stale Naraku power icon: {ex}");
            }
        }
    }
}
