using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;

namespace NinjaSlayer.Code.Compatibility;

internal static partial class GameCompatibility
{
    internal static class Transition
    {
        private static readonly PropertyInfo? InTransition =
            AccessTools.Property(typeof(NTransition), nameof(NTransition.InTransition));
        private static readonly FieldInfo? Tween = AccessTools.Field(typeof(NTransition), "_tween");

        public static IReadOnlyList<CapabilityProbe> GetProbes() =>
        [
            RequiredMember("NTransition.in-transition", InTransition, "NTransition.InTransition"),
            RequiredMember("NTransition.tween", Tween, "NTransition._tween")
        ];

        public static void SetInTransition(NTransition transition, bool value) =>
            InTransition?.SetValue(transition, value);

        public static void KillTween(NTransition transition)
        {
            if (Tween?.GetValue(transition) is Tween tween)
            {
                tween.Kill();
                Tween.SetValue(transition, null);
            }
        }
    }
}
