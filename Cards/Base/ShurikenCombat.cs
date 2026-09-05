using Godot;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Code.Nodes;
using NinjaSlayer.Content;
using NinjaSlayer.Orbs;
using NinjaSlayer.Powers;

namespace NinjaSlayer.Cards;

internal static class ShurikenCombat
{
    internal const string ProjectileTexturePath =
        "res://NinjaSlayer/images/projectiles/ninja_slayer_shuriken.png";

    internal const float VisibleDiameter = 60f;
    internal const float SourceVisibleDiameter = 343f;
    private const float ProjectileScale = VisibleDiameter / SourceVisibleDiameter;
    private const float FlightSeconds = 0.15f;
    private const float HeadAngularVelocity = 360f / FlightSeconds;
    private const uint ProjectileParticleSeed = 0x4E534852;

    private static readonly Color TrailTint = new(1f, 179f / 255f, 0f, 1f);
    private static readonly StringName ParticleColorProperty = new("color");
    private static readonly NodePath ThrowContainerPath = new("throw_container");
    private static readonly NodePath ThrowParticlePath =
        new("throw_container/vfx_dagger_spray_dagger");

    internal static async Task PlayStockThrowAnimation(
        Creature owner,
        IReadOnlyList<Creature> targets,
        ShurikenOrb originOrb,
        Action beforeThrow)
    {
        await HopAnimation.Play(owner);
        beforeThrow();
        NDebugAudioManager.Instance?.Play(TmpSfx.daggerThrow);
        foreach (Creature target in targets)
        {
            target.GetVfxContainer()?.AddChildSafely(CreateThrowVfx(owner, target, originOrb));
        }

        await Cmd.CustomScaledWait(FlightSeconds, FlightSeconds);
    }

    internal static async Task<IReadOnlyList<DamageResult>> TriggerStockWave(
        PlayerChoiceContext choiceContext,
        Creature owner,
        IReadOnlyList<Creature> targets,
        CardModel? source,
        ShurikenOrb originOrb,
        Action beforeThrow)
    {
        if (targets.Count == 0)
        {
            return [];
        }

        await PlayStockThrowAnimation(owner, targets, originOrb, beforeThrow);
        IReadOnlyList<DamageResult> results = (await CreatureCmd.Damage(
            choiceContext,
            targets,
            originOrb.EvokeVal,
            MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered
                | MegaCrit.Sts2.Core.ValueProps.ValueProp.Move,
            owner,
            source
#if !NINJASLAYER_LEGACY_DAMAGE_API
            , null
#endif
        )).ToList();
        if (owner.GetPower<ShurikenGuardRedesignPower>() is { } guard)
        {
            await guard.AfterShurikenDamage(choiceContext, results);
        }

        return results;
    }

    internal static int GetStockBaseDamage(Creature owner) =>
        RedesignV1Rules.ShurikenDamage(
            owner.GetPower<ShurikenDamagePower>()?.Amount ?? 0);

    internal static bool HasSoarSpread(CardModel card) =>
        card.IsMutable && card.Owner != null && card.Owner.Creature.HasPower<HellTornadoPower>();

    internal static AttackCommand BuildAttackCommand(
        CardModel card,
        CardPlay cardPlay,
        DynamicVar damage,
        ICombatState? combatState)
    {
        var command = DamageCmd.Attack(damage.BaseValue)
#if NINJASLAYER_LEGACY_CARD_PLAY_LINKS
            .FromCard(card)
#else
            .FromCard(card, cardPlay)
#endif
            .WithNoAttackerAnim()
            .AfterAttackerAnim(() => HopAnimation.Play(card.Owner!.Creature))
            .WithHitFx(null, null, TmpSfx.daggerThrow);

        if (HasSoarSpread(card))
        {
            Creature? vfxTarget = cardPlay.Target!;
            if (vfxTarget == null && combatState?.HittableEnemies is { Count: > 0 } enemies)
            {
                vfxTarget = enemies[^1];
            }

            return command
                .TargetingAllOpponents(combatState ?? throw new InvalidOperationException("Shuriken attacks require combat."))
                .WithHitVfxNode(_ => vfxTarget == null ? null : CreateThrowVfx(card.Owner!.Creature, vfxTarget));
        }
        return command
            .Targeting(cardPlay.Target!)
            .WithHitVfxNode(t => CreateThrowVfx(card.Owner!.Creature, t));
    }

    private static NShivThrowVfx? CreateThrowVfx(
        Creature owner,
        Creature target,
        ShurikenOrb? originOrb = null)
    {
        NCombatRoom? room = NCombatRoom.Instance;
        NCreature? ownerNode = room?.GetCreatureNode(owner);
        NCreature? targetNode = room?.GetCreatureNode(target);
        if (ownerNode == null || targetNode == null)
        {
            return null;
        }

        Vector2 origin = TryGetOrbThrowOrigin(owner, originOrb, out Vector2 orbOrigin)
            ? orbOrigin
            : ownerNode.VfxSpawnPosition;
        Vector2 targetPosition = targetNode.VfxSpawnPosition;
        NShivThrowVfx? vfx = NShivThrowVfx.Create(origin, targetPosition, TrailTint);
        if (vfx == null)
        {
            return null;
        }

        Node2D throwContainer = vfx.GetNode<Node2D>(ThrowContainerPath);
        GpuParticles2D trail = vfx.GetNode<GpuParticles2D>(ThrowParticlePath);
        throwContainer.Position = new Vector2(
            -origin.DistanceTo(targetPosition) - trail.Position.X,
            throwContainer.Position.Y);

        Texture2D shurikenTexture = PreloadManager.Cache.GetTexture2D(ProjectileTexturePath);
        trail.UseFixedSeed = true;
        trail.Seed = ProjectileParticleSeed;

        GpuParticles2D head = (GpuParticles2D)trail.Duplicate();
        head.Name = "ShurikenHead";
        head.Texture = shurikenTexture;
        head.Material = null;
        head.TrailEnabled = false;
        head.ZIndex = trail.ZIndex + 1;
        head.ProcessMaterial = CreateShurikenHeadMaterial(head.ProcessMaterial);
        head.Emitting = true;
        throwContainer.AddChildSafely(head);
        return vfx;
    }

    private static bool TryGetOrbThrowOrigin(
        Creature owner,
        ShurikenOrb? orb,
        out Vector2 origin)
    {
        Node? container = NCombatRoom.Instance?
            .GetCreatureNode(owner)?
            .OrbManager?
            .GetNodeOrNull<Node>("%Orbs");
        if (orb is not null && container is not null)
        {
            foreach (Node child in container.GetChildren())
            {
                if (child is NOrb { Model: ShurikenOrb model } node
                    && ReferenceEquals(model, orb))
                {
                    FindShurikenVisual(node)?.SyncNow();
                    origin = node.GlobalPosition;
                    return true;
                }
            }
        }

        origin = default;
        return false;
    }

    private static ShurikenOrbVisual? FindShurikenVisual(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is ShurikenOrbVisual visual)
            {
                return visual;
            }

            ShurikenOrbVisual? descendant = FindShurikenVisual(child);
            if (descendant != null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static ParticleProcessMaterial CreateShurikenHeadMaterial(Material? source)
    {
        ParticleProcessMaterial material = source is ParticleProcessMaterial particleMaterial
            ? (ParticleProcessMaterial)particleMaterial.Duplicate()
            : throw new InvalidOperationException("The vanilla Shiv projectile is missing its particle material.");
        material.ScaleMin = ProjectileScale;
        material.ScaleMax = ProjectileScale;
        material.AngularVelocityMin = HeadAngularVelocity;
        material.AngularVelocityMax = HeadAngularVelocity;
        material.Set(ParticleColorProperty, Colors.White);
        return material;
    }
}
