using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Powers;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

// Harmony patch is kept instead of NarakuLifeHealthBarOverlay ([RegisterNodeAttachment])
// because Godot export currently emits assembly type-load warnings for node attachments.
// Revisit overlay migration when RitsuLib/export pipeline supports it cleanly.
public sealed class NarakuLifeHealthBarPatch : IPatchMethod
{
    private const string BarName = "NarakuLifeBar";
    private static readonly FieldInfo? CreatureField = AccessTools.Field(typeof(NHealthBar), "_creature");

    public static string PatchId => "ninjaslayer_naraku_life_health_bar";

    public static string Description => "Render Naraku temporary life as a white health bar extension.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NHealthBar), nameof(NHealthBar.RefreshValues))];

    public static void Postfix(NHealthBar __instance)
    {
        Creature? creature = CreatureField?.GetValue(__instance) as Creature;
        Control? foreground = __instance.GetNodeOrNull<Control>("%HpForegroundContainer");
        if (creature == null || foreground == null)
        {
            return;
        }

        ColorRect bar = GetOrCreateBar(__instance.HpBarContainer);
        int amount = creature.GetPowerAmount<NarakuLifePower>();
        if (amount <= 0 || creature.CurrentHp <= 0 || creature.MaxHp <= 0)
        {
            bar.Visible = false;
            return;
        }

        float maxWidth = foreground.Size.X;
        float currentWidth = Math.Max((float)creature.CurrentHp / creature.MaxHp * maxWidth, 12f);
        float narakuWidth = Math.Max((float)amount / creature.MaxHp * maxWidth, 3f);
        bar.Visible = true;
        bar.Position = foreground.Position + new Vector2(currentWidth, -4f);
        bar.Size = new Vector2(narakuWidth, foreground.Size.Y + 8f);
    }

    private static ColorRect GetOrCreateBar(Control container)
    {
        ColorRect? bar = container.GetNodeOrNull<ColorRect>(BarName);
        if (bar != null)
        {
            return bar;
        }

        bar = new ColorRect
        {
            Name = BarName,
            Color = Colors.White,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        container.AddChild(bar);
        container.MoveChild(bar, Math.Min(3, container.GetChildCount() - 1));
        return bar;
    }
}
