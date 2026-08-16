using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Monsters;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

public sealed class YamotoKokiAllyLayoutPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_yamoto_koki_ally_layout";
    public static string Description => "Lay out full-size NinjaSlayer companions as multiplayer slots.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NCombatRoom), nameof(NCombatRoom.PositionPlayersAndPets),
            [typeof(List<NCreature>), typeof(float), typeof(bool)])
    ];

    public static bool Prefix(List<NCreature> creatureNodes, float scaling, bool fullyCenterPlayers)
    {
        if (!creatureNodes.Any(node => IsFullSizeCompanion(node.Entity.Monster)))
        {
            return true;
        }

        Apply(creatureNodes, scaling, fullyCenterPlayers);
        return false;
    }

    internal static void Reflow(NCombatRoom room)
    {
        List<NCreature> allies = room.CreatureNodes
            .Where(node => node.Entity.IsPlayer || node.Entity.PetOwner != null)
            .ToList();
        if (!allies.Any(node => IsFullSizeCompanion(node.Entity.Monster)))
        {
            return;
        }

        Creature? playerCreature = allies.FirstOrDefault(node => node.Entity.IsPlayer)?.Entity;
        float scaling = playerCreature?.CombatState?.Encounter?.GetCameraScaling()
            ?? room.SceneContainer.Scale.X;
        bool fullyCenterPlayers = playerCreature?.CombatState?.Encounter?.FullyCenterPlayers ?? false;
        Apply(allies, scaling, fullyCenterPlayers);
    }

    private static void Apply(
        List<NCreature> creatureNodes,
        float scaling,
        bool fullyCenterPlayers)
    {
        List<Slot> playerSlots = creatureNodes
            .Where(node => node.Entity.IsPlayer)
            .Select(node => new Slot(node, node.Visuals.Bounds.Size.X))
            .OrderByDescending(slot => LocalContext.IsMe(slot.Anchor.Entity))
            .ToList();

        List<Slot> slots = [];
        foreach (Slot playerSlot in playerSlots)
        {
            slots.Add(playerSlot);
            foreach (NCreature companion in creatureNodes.Where(node =>
                         IsFullSizeCompanion(node.Entity.Monster)
                         && node.Entity.PetOwner == playerSlot.Anchor.Entity.Player))
            {
                // A full-size companion follows the vanilla two-player footprint.
                slots.Add(new Slot(companion, playerSlot.Width));
            }
        }

        foreach (NCreature pet in creatureNodes.Where(node =>
                     !node.Entity.IsPlayer
                     && !IsFullSizeCompanion(node.Entity.Monster)
                     && node.Entity.Monster is not YamotoKokiOrigamiMissile))
        {
            Slot? ownerSlot = playerSlots.FirstOrDefault(slot =>
                slot.Anchor.Entity.Player == pet.Entity.PetOwner);
            ownerSlot?.Pets.Add(pet);
        }

        IReadOnlyList<YamotoKokiSlotPosition> positions = YamotoKokiGridLayoutMath.Calculate(
            slots.Select(slot => slot.Width).ToList(),
            scaling,
            fullyCenterPlayers);
        int currentRow = -1;
        float rowFlowOffset = 0f;
        for (int i = 0; i < slots.Count; i++)
        {
            Slot slot = slots[i];
            YamotoKokiSlotPosition position = positions[i];
            if (position.Row != currentRow)
            {
                currentRow = position.Row;
                rowFlowOffset = 0f;
            }

            Vector2 basePosition = new(position.X - rowFlowOffset, position.Y);
            slot.Anchor.Position = basePosition;
            bool shiftedForOsty = PositionLocalPlayerOsty(slot, basePosition);
            PositionPets(slot, basePosition, shiftedForOsty);
            if (shiftedForOsty)
            {
                rowFlowOffset += 100f;
            }

            Color tint = position.Row > 0 ? new Color(0.5f, 0.5f, 0.5f) : Colors.White;
            slot.Anchor.Visuals.Modulate = tint;
            foreach (NCreature pet in slot.Pets)
            {
                pet.Visuals.Modulate = tint;
            }
        }

        foreach (Slot slot in slots)
        {
            slot.Anchor.GetParent().MoveChildSafely(slot.Anchor, 0);
            for (int i = 0; i < slot.Pets.Count; i++)
            {
                NCreature pet = slot.Pets[i];
                pet.GetParent().MoveChildSafely(pet, i + 1);
                if (slot.Anchor.Entity.IsPlayer && !LocalContext.IsMe(slot.Anchor.Entity))
                {
                    pet.Visuals.Bounds.Visible = false;
                }
            }
        }

        if (NCombatRoom.Instance is { } room)
        {
            YamotoKokiAllyFacingController.Ensure(room).SyncNow();
            YamotoKokiOrigamiMissileOrbitController.Ensure(room).LayoutNow(snapNewMissiles: true);
        }
    }

    private static bool PositionLocalPlayerOsty(Slot slot, Vector2 basePosition)
    {
        NCreature anchor = slot.Anchor;
        if (!anchor.Entity.IsPlayer
            || !LocalContext.IsMe(anchor.Entity)
            || anchor.Entity.Player!.Character is not Necrobinder)
        {
            return false;
        }

        NCreature? osty = slot.Pets.FirstOrDefault(pet => pet.Entity.Monster is Osty);
        if (osty != null)
        {
            slot.Pets.Remove(osty);
            osty.Position = new Vector2(basePosition.X + slot.Width * 0.5f, basePosition.Y)
                + NCreature.GetOstyOffsetFromPlayer(osty.Entity);
        }

        anchor.Position = basePosition + Vector2.Left * 150f;
        return true;
    }

    private static void PositionPets(Slot slot, Vector2 basePosition, bool shiftedForOsty)
    {
        List<NCreature> pets = slot.Pets;
        float petStep = pets.Count > 1
            ? slot.Width / (pets.Count - 1)
            : 0f;
        float ostyFlowOffset = shiftedForOsty ? 100f : 0f;
        for (int i = 0; i < pets.Count; i++)
        {
            NCreature pet = pets[i];
            pet.Position = new Vector2(
                basePosition.X + slot.Width * 0.5f - ostyFlowOffset
                    + 20f - i * petStep - pet.Visuals.Bounds.Size.X * 0.5f,
                basePosition.Y + 10f);
        }
    }

    private static bool IsFullSizeCompanion(MonsterModel? monster) =>
        monster is YamotoKokiMonster or SawatariMonster or YukanoMonster;

    private sealed class Slot(NCreature anchor, float width)
    {
        public NCreature Anchor { get; } = anchor;
        public float Width { get; } = width;
        public List<NCreature> Pets { get; } = [];
    }
}

public sealed class YamotoKokiDynamicAllyLayoutPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_yamoto_koki_dynamic_ally_layout";
    public static string Description => "Reflow Yamoto Koki slots after allies are added or removed.";
    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NCombatRoom), nameof(NCombatRoom.AddCreature)),
        new(typeof(NCombatRoom), nameof(NCombatRoom.RemoveCreatureNode))
    ];

    public static void Postfix(NCombatRoom __instance) => YamotoKokiAllyLayoutPatch.Reflow(__instance);
}
