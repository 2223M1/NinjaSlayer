using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Monsters;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

/// <summary>
/// Places each <see cref="XiaoJiMonster"/> in its own multiplayer-style slot instead of the foot pet band.
/// </summary>
public sealed class XiaoJiAllyLayoutPatch : IPatchMethod
{
    public static string PatchId => "ninjaslayer_xiao_ji_ally_layout";

    public static string Description =>
        "Lay out Xiao Ji companions in independent multiplayer player slots.";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NCombatRoom), nameof(NCombatRoom.PositionPlayersAndPets),
            [typeof(List<NCreature>), typeof(float), typeof(bool)])
    ];

    public static bool Prefix(List<NCreature> creatureNodes, float scaling, bool fullyCenterPlayers)
    {
        if (!creatureNodes.Any(node => node.Entity.Monster is XiaoJiMonster))
        {
            return true;
        }

        PositionWithXiaoJiSlots(creatureNodes, scaling, fullyCenterPlayers);
        return false;
    }

    private static void PositionWithXiaoJiSlots(
        List<NCreature> creatureNodes,
        float scaling,
        bool fullyCenterPlayers)
    {
        List<Slot> slots = [];
        foreach (NCreature creatureNode in creatureNodes)
        {
            if (!creatureNode.Entity.IsPlayer)
            {
                continue;
            }

            Slot slot = new()
            {
                Anchor = creatureNode,
                Pets = []
            };
            if (LocalContext.IsMe(creatureNode.Entity))
            {
                slots.Insert(0, slot);
            }
            else
            {
                slots.Add(slot);
            }
        }

        foreach (NCreature creature in creatureNodes)
        {
            if (creature.Entity.IsPlayer)
            {
                continue;
            }

            if (creature.Entity.Monster is XiaoJiMonster)
            {
                slots.Add(new Slot { Anchor = creature, Pets = [] });
                continue;
            }

            Slot ownerSlot = slots.First(slot =>
                slot.Anchor.Entity.IsPlayer
                && slot.Anchor.Entity.Player == creature.Entity.PetOwner);
            ownerSlot.Pets.Add(creature);
        }

        float viewportWidth = 960f / scaling;
        float spacing = 70f;
        int columns = (int)Math.Ceiling(Math.Sqrt(slots.Count));
        int rows = (int)Math.Ceiling((double)slots.Count / columns);
        float firstRowWidth = creatureNodes.Take(columns).Sum(n => n.Visuals.Bounds.Size.X);
        float rowSpan = firstRowWidth + (columns - 1) * spacing;
        float staggerX = firstRowWidth * 0.33f;
        float rowXStep = rows > 1 ? staggerX / (rows - 1) : 0f;
        float rowYStep = rows > 1 ? 120f / (rows - 1) : 0f;

        float startX;
        if (fullyCenterPlayers)
        {
            startX = creatureNodes.First(c => c.Entity.IsPlayer).Visuals.Bounds.Size.X * -0.5f;
        }
        else
        {
            startX = (viewportWidth - rowSpan) * 0.5f;
            startX = Math.Max(startX, 150f);
            if (slots.Count >= columns * 2)
            {
                firstRowWidth += staggerX;
            }

            if (startX + rowSpan > viewportWidth)
            {
                spacing = (viewportWidth - 150f - firstRowWidth) / (columns - 1);
                rowSpan = firstRowWidth + (columns - 1) * spacing;
                startX = (viewportWidth - rowSpan) * 0.5f;
            }
        }

        for (int row = 0; row < columns; row++)
        {
            float targetXPos = startX + rowXStep * row;
            for (int col = 0; col < columns; col++)
            {
                int index = row * columns + col;
                if (index >= slots.Count)
                {
                    break;
                }

                Slot slot = slots[index];
                NCreature anchor = slot.Anchor;
                List<NCreature> pets = slot.Pets;
                anchor.Position = new Vector2(
                    0f - targetXPos - anchor.Visuals.Bounds.Size.X * 0.5f,
                    200f - rowYStep * row);

                if (anchor.Entity.IsPlayer
                    && LocalContext.IsMe(anchor.Entity)
                    && anchor.Entity.Player!.Character is Necrobinder)
                {
                    NCreature? osty = null;
                    for (int i = 0; i < pets.Count; i++)
                    {
                        if (pets[i].Entity.Monster is Osty)
                        {
                            osty = pets[i];
                            pets.RemoveAt(i);
                            break;
                        }
                    }

                    PositionLocalPlayerOsty(ref targetXPos, anchor.Position.Y, anchor, osty);
                }

                float petStep = pets.Count > 1
                    ? anchor.Visuals.Bounds.Size.X / (pets.Count - 1)
                    : 0f;
                for (int i = 0; i < pets.Count; i++)
                {
                    NCreature pet = pets[i];
                    pet.Position = new Vector2(
                        0f - targetXPos + 20f - i * petStep - pet.Visuals.Bounds.Size.X * 0.5f,
                        anchor.Position.Y + 10f);
                }

                if (row > 0)
                {
                    anchor.Visuals.Modulate = new Color(0.5f, 0.5f, 0.5f);
                    foreach (NCreature pet in pets)
                    {
                        pet.Visuals.Modulate = new Color(0.5f, 0.5f, 0.5f);
                    }
                }

                targetXPos += anchor.Visuals.Bounds.Size.X + spacing;
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
    }

    private static void PositionLocalPlayerOsty(
        ref float targetXPos,
        float playerYPosition,
        NCreature player,
        NCreature? osty)
    {
        Vector2 position = player.Position;
        position.X = player.Position.X - 150f;
        player.Position = position;
        if (osty != null)
        {
            osty.Position = new Vector2(0f - targetXPos, playerYPosition)
                + NCreature.GetOstyOffsetFromPlayer(osty.Entity);
        }

        targetXPos += 100f;
    }

    private sealed class Slot
    {
        public required NCreature Anchor { get; init; }
        public required List<NCreature> Pets { get; init; }
    }
}
