using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using NinjaSlayer.Code.ExternalAnimations;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

internal sealed class FinisherHeavyBluntSequencePatch : IPatchMethod
{
    private static readonly ConditionalWeakTable<NHeavyBluntVfx, Playback> Plays = new();
    private static readonly AccessTools.FieldRef<NHeavyBluntVfx, Godot.Collections.Array<GpuParticles2D>> Anticipation =
        AccessTools.FieldRefAccess<NHeavyBluntVfx, Godot.Collections.Array<GpuParticles2D>>("_anticipationParticles");
    private static readonly AccessTools.FieldRef<NHeavyBluntVfx, Godot.Collections.Array<GpuParticles2D>> Impact =
        AccessTools.FieldRefAccess<NHeavyBluntVfx, Godot.Collections.Array<GpuParticles2D>>("_impactParticles");

    public static string PatchId => "ninjaslayer_finisher_heavy_impact_phase";
    public static string Description => "Let a finisher seek the existing heavy impact without replaying its anticipation.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() => [new(typeof(NHeavyBluntVfx), "PlaySequence", [])];

    public static bool Prefix(NHeavyBluntVfx __instance, Vector2 ____debugPosition, ref Task __result)
    {
        if (FinisherSessionRegistry.GetActiveSession() == null) return true;
        __instance.GlobalPosition = ____debugPosition;
        var playback = new Playback(__instance);
        Plays.Add(__instance, playback);
        __result = playback.Run();
        return false;
    }

    internal static bool PrepareImpact(Node root, out float age)
    {
        age = 0f;
        if (root is not NHeavyBluntVfx heavy || !Plays.TryGetValue(heavy, out Playback? playback)) return false;
        playback.StartImpact(seek: true);
        age = playback.ImpactAge;
        playback.ImpactAge = Math.Max(age, 0.1f);
        return true;
    }

    private sealed class Playback(NHeavyBluntVfx node)
    {
        private bool _impacted;
        internal float ImpactAge { get; set; }

        internal bool StartImpact(bool seek)
        {
            if (_impacted) return false;
            _impacted = true;
            if (seek)
                foreach (GpuParticles2D particle in Anticipation(node)) particle.Hide();
            foreach (GpuParticles2D particle in Impact(node)) particle.Restart();
            if (!seek) NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Short);
            return true;
        }

        internal async Task Run()
        {
            SceneTree tree = node.GetTree();
            foreach (GpuParticles2D particle in Anticipation(node)) particle.Restart();
            float time = 0f;
            while (GodotObject.IsInstanceValid(node) && node.IsInsideTree() && !_impacted && time < 0.2f)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                if (GodotObject.IsInstanceValid(node) && node.CanProcess()) time += (float)node.GetProcessDeltaTime();
            }
            if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree()) return;
            StartImpact(seek: false);
            while (GodotObject.IsInstanceValid(node) && node.IsInsideTree() && ImpactAge < 2f)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                if (GodotObject.IsInstanceValid(node) && node.CanProcess()) ImpactAge += (float)node.GetProcessDeltaTime();
            }
            if (GodotObject.IsInstanceValid(node)) node.QueueFree();
        }
    }
}
