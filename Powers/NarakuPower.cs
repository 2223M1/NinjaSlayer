using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using NinjaSlayer.Cards;
using NinjaSlayer.Code.Nodes;
using STS2RitsuLib.Interop.AutoRegistration;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.Powers;

public sealed class NarakuPower : NinjaSlayerPowerTemplate
{
    private static readonly FieldInfo PowerNodes =
        AccessTools.Field(typeof(NPowerContainer), "_powerNodes")
        ?? throw new MissingFieldException(typeof(NPowerContainer).FullName, "_powerNodes");
    private static readonly MethodInfo UpdatePositions =
        AccessTools.Method(typeof(NPowerContainer), "UpdatePositions")
        ?? throw new MissingMethodException(typeof(NPowerContainer).FullName, "UpdatePositions");

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new HpLossVar(4)
    ];

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        NarakuVisualOverlay.Sync(Owner);
        NinjaSlayerCombatAudioSet.Play(NinjaSlayerAudio.PangbaiScaryEvent);
        NinjaSlayerCombatVfx.PlayBurnStatusFeedback([Owner]);
        return Task.CompletedTask;
    }

    public override Task AfterRemoved(Creature oldOwner)
    {
        RemoveStaleNode(oldOwner);
        NarakuVisualOverlay.Sync(oldOwner);
        NinjaSlayerCombatVfx.PlayBurnStatusFeedback([oldOwner]);
        return Task.CompletedTask;
    }

    private void RemoveStaleNode(Creature oldOwner)
    {
        if (oldOwner.Powers.Contains(this))
        {
            return;
        }

        NPowerContainer? container = NCombatRoom.Instance
            ?.GetCreatureNode(oldOwner)
            ?.GetNodeOrNull<NCreatureStateDisplay>("%HealthBar")
            ?.GetNodeOrNull<NPowerContainer>("%PowerContainer");
        NPower? node = container
            ?.GetChildren()
            .OfType<NPower>()
            .FirstOrDefault(candidate => ReferenceEquals(candidate.Model, this));
        if (node == null || node.IsQueuedForDeletion())
        {
            return;
        }

        if (PowerNodes.GetValue(container) is not List<NPower> nodes)
        {
            throw new InvalidOperationException(
                "NPowerContainer._powerNodes has an unexpected runtime type.");
        }
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

    public override async Task BeforeCardPlayed(CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner == Owner.Player && cardPlay.Card.Type == CardType.Attack)
        {
            await Content.NinjaSlayerActions.AddGeneratedCard<BurningCard>(Owner.Player, PileType.Discard);
        }
    }

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (side == Owner.Side
            && participants.Contains(Owner)
            && Owner.Player is { } player
            && NinjaSlayerActions.ChadoInHandCount(player) > 0)
        {
            await NinjaSlayerActions.ExitNaraku(Owner);
        }
    }

    public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
    {
        if (dealer != Owner || !props.IsPoweredAttack())
        {
            return;
        }

        IReadOnlyList<Creature> enemies = CombatState.HittableEnemies;
        if (enemies.Count == 0)
        {
            return;
        }

        NinjaSlayerCombatVfx.PlayBurnStatusFeedback(enemies);

        await CreatureCmd.Damage(
            choiceContext,
            enemies,
            DynamicVars.HpLoss.BaseValue,
            ValueProp.Unblockable | ValueProp.Unpowered,
            Owner,
            cardSource
#if !NINJASLAYER_LEGACY_DAMAGE_API
            , null
#endif
        );
    }
}
