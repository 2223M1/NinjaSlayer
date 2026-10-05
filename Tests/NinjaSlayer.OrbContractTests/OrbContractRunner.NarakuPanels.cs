using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using NinjaSlayer.Code.Patches;
using NinjaSlayer.Powers;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private static NHealthBar PanelHealthBar(Creature creature, Control parent)
    {
        var bar = new NHealthBar();
        parent.AddChild(bar);
        var hp = new Control { Name = "HpBarContainer", Size = new Vector2(175, 20), Position = new Vector2(15, 10) };
        bar.AddChild(hp);
        hp.Owner = bar;
        hp.UniqueNameInOwner = true;
        var foreground = new Control { Name = "HpForegroundContainer", Size = new Vector2(165, 20) };
        hp.AddChild(foreground);
        foreground.Owner = bar;
        foreground.UniqueNameInOwner = true;
        foreach (string name in new[] { "HpForeground", "HpMiddleground", "PoisonForeground", "DoomForeground" })
        {
            var node = new NinePatchRect { Name = name, Size = new Vector2(165, 20), PatchMarginLeft = 4 };
            foreground.AddChild(node);
            node.Owner = bar;
            node.UniqueNameInOwner = true;
            AccessTools.Field(typeof(NHealthBar), "_" + char.ToLowerInvariant(name[0]) + name[1..]).SetValue(bar, node);
        }
        var block = new Control { Name = "BlockContainer", Size = new Vector2(30, 20), Position = new Vector2(-10, 10) };
        bar.AddChild(block);
        block.Owner = bar;
        block.UniqueNameInOwner = true;
        AccessTools.Property(typeof(NHealthBar), nameof(NHealthBar.HpBarContainer)).SetValue(bar, hp);
        AccessTools.Field(typeof(NHealthBar), "_hpForegroundContainer").SetValue(bar, foreground);
        AccessTools.Field(typeof(NHealthBar), "_blockContainer").SetValue(bar, block);
        AccessTools.Field(typeof(NHealthBar), "_creature").SetValue(bar, creature);
        AccessTools.Field(typeof(NHealthBar), "_expectedMaxFgWidth").SetValue(bar, 165f);
        return bar;
    }

    private static async Task VerifyNarakuPanelBaseline()
    {
        using var combat = new OrbCombat();
        await PowerCmd.Apply<NarakuLifePower>(Choice, combat.Player.Creature, 5, combat.Player.Creature, null);
        var parent = new Control();
        try
        {
            var bar = PanelHealthBar(combat.Player.Creature, parent);
            bool reproduced = false;
            try { NarakuLifeHealthBarLayoutPatch.Postfix(bar); }
            catch (InvalidOperationException error) { reproduced = error.Message == "The active Naraku health bar is not attached to a creature node."; }
            Require(reproduced, "Published Naraku layout must reproduce the exception on a valid non-creature presentation.");
            GD.Print("PASS published Naraku baseline: a shared non-creature health bar with active Naraku throws the reported exception.");
        }
        finally { parent.Free(); }
    }

    private static async Task VerifyNarakuPanels()
    {
        using var combat = new OrbCombat();
        var parent = new Control();
        try
        {
            var creature = combat.Player.Creature;
            creature.SetCurrentHpInternal(creature.MaxHp - 20);
            var bar = PanelHealthBar(creature, parent);
            var block = bar.GetNode<Control>("%BlockContainer");
            Vector2 hpPosition = bar.HpBarContainer.Position, blockPosition = block.Position;
            await PowerCmd.Apply<NarakuLifePower>(Choice, creature, 5, creature, null);
            NarakuLifeHealthBarLayoutPatch.Postfix(bar);
            var strip = bar.GetNode<Control>("%PoisonForeground").GetParent().GetNode<NinePatchRect>("NinjaSlayerNarakuLifeStrip");
            Require(strip.Visible && bar.HpBarContainer.Position == hpPosition && block.Position == blockPosition,
                "Non-creature panels must show embedded Naraku without using creature hitbox anchoring.");
            await PowerCmd.Remove(creature.GetPower<NarakuLifePower>()!);
            NarakuLifeHealthBarLayoutPatch.Postfix(bar);
            Require(!strip.Visible, "Removing Naraku must hide the shared panel strip.");
            var display = new NCreatureStateDisplay();
            parent.AddChild(display);
            var malformedCombat = PanelHealthBar(creature, display);
            await PowerCmd.Apply<NarakuLifePower>(Choice, creature, 5, creature, null);
            bool rejected = false;
            try { NarakuLifeHealthBarLayoutPatch.Postfix(malformedCombat); }
            catch (InvalidOperationException error) { rejected = error.Message.Contains("not attached to a creature node", StringComparison.Ordinal); }
            Require(rejected, "An actually malformed combat state display must still expose its invariant violation.");
            GD.Print("PASS Naraku presentation ownership: shared panel strip, unchanged panel anchors, removal and strict combat hierarchy.");
        }
        finally { parent.Free(); }
    }
}
