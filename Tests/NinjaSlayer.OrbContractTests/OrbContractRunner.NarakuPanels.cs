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
        block.AddChild(new TextureRect { Name = "BlockIcon" });
        block.AddChild(new Label { Name = "BlockLabel" });
        var outline = new NinePatchRect { Name = "BlockOutline" };
        hp.AddChild(outline);
        outline.Owner = bar;
        outline.UniqueNameInOwner = true;
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

    private async Task VerifyNarakuPanels()
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
            Vector2 hpSize = bar.HpBarContainer.Size;
            foreach (int amount in new[] { 5, 20, 120, 12345 })
            {
                if (creature.GetPower<NarakuLifePower>() is { } old) await PowerCmd.Remove(old);
                await PowerCmd.Apply<NarakuLifePower>(Choice, creature, amount, creature, null);
                NarakuLifeHealthBarLayoutPatch.Postfix(bar, creature);
                var heart = bar.HpBarContainer.GetNode<Control>("NinjaSlayerNarakuLife");
                Require(heart.Visible && heart.GetNode<Label>("BlockLabel").Text == amount.ToString()
                    && heart.GetNode<TextureRect>("BlockIcon").Texture is { } texture
                    && texture.GetSize() == new Vector2(64, 64)
                    && heart.GetNode<TextureRect>("BlockIcon").Modulate == new Color(1f, 0.192157f, 1f)
                    && heart.GetNode<Label>("BlockLabel").GetThemeColor("font_color") == Colors.White,
                    "Naraku must show the original Temp HP heart in flame purple with an independent white amount.");
                Require(bar.HpBarContainer.Position == hpPosition && bar.HpBarContainer.Size == hpSize
                    && block.Position == blockPosition && heart.AnchorLeft == 1 && heart.AnchorRight == 1,
                    "Naraku must preserve native HP width and block placement, including amounts above max HP.");
            }
            await PowerCmd.Remove(creature.GetPower<NarakuLifePower>()!);
            NarakuLifeHealthBarLayoutPatch.Postfix(bar, creature);
            Require(!bar.HpBarContainer.GetNode<Control>("NinjaSlayerNarakuLife").Visible
                && !bar.HpBarContainer.GetNode<NinePatchRect>("NinjaSlayerNarakuOutline").Visible,
                "Removing Naraku must hide both the heart and white outline.");
            GD.Print("PASS Naraku Temp HP: original icon, flame-purple tint, white full amounts, native HP/block layout and removal.");
        }
        finally { parent.Free(); }

        STS2RitsuLib.RitsuLibFramework.EnsureGodotScriptsRegistered(typeof(NHealthBar).Assembly,
            STS2RitsuLib.RitsuLibFramework.CreateLogger("Native health bar contracts"));
        const string scene = "res://scenes/combat/health_bar.tscn";
        RegisterMountedUids(scene, []);
        var native = GD.Load<PackedScene>(scene).Instantiate<NHealthBar>();
        var patcher = STS2RitsuLib.RitsuLibFramework.CreatePatcher("NinjaSlayer.NarakuPanelContract", "Product");
        STS2RitsuLib.Patching.Core.ModPatcherExtensions.RegisterPatch<NarakuLifeHealthBarLayoutPatch>(patcher);
        Require(patcher.PatchAll(), "Naraku health bar patch failed to install.");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        AddChild(native);
        try
        {
            native.SetCreature(combat.Player.Creature);
            await CreatureCmd.GainBlock(combat.Player.Creature, 5, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, null);
            await PowerCmd.Apply<NarakuLifePower>(Choice, combat.Player.Creature, 120, combat.Player.Creature, null);
            native.RefreshValues();
            var heart = native.HpBarContainer.GetNode<Control>("NinjaSlayerNarakuLife");
            Require(heart.GetNode<Label>("BlockLabel").Text == "120" && heart.Visible
                && native.GetNode<Control>("%BlockContainer").Visible,
                "Native scene must show Naraku and ordinary Block simultaneously through the installed patch.");
            native.UpdateWidthRelativeToReferenceValue(80, 175);
            native.RefreshValues();
            Require(Math.Abs(heart.Position.X + heart.Size.X / 2 - (native.HpBarContainer.Size.X + 2)) < 0.01f
                && heart.Size == new Vector2(64, 64),
                "The original 64px Temp HP heart must overlap the bar end with its center 2px beyond the right edge.");
            if (System.Environment.GetEnvironmentVariable("NINJASLAYER_CONTRACT_NARAKU_IMAGE") is { } imagePath)
            {
                native.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
                native.Position = new Vector2(300, 150);
                for (int i = 0; i < 30; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Require(GetViewport().GetTexture().GetImage().SavePng(imagePath) == Error.Ok,
                    "Could not save native Temp HP presentation.");
            }
            await PowerCmd.Remove(combat.Player.Creature.GetPower<NarakuLifePower>()!);
            native.RefreshValues();
            Require(!heart.Visible && native.GetNode<Control>("%BlockContainer").Visible,
                "Native Naraku removal must preserve the separate Block display.");
            GD.Print("PASS native health-bar scene: installed patch, full Temp HP amount, block coexistence, narrow-bar anchor and removal.");
        }
        finally
        {
            patcher.UnpatchAll();
            native.Free();
        }
    }
}
