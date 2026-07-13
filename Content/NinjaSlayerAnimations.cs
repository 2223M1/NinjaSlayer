using Godot;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.Scaffolding.Visuals.StateMachine;

namespace NinjaSlayer.Content;

public static class NinjaSlayerAnimations
{
    public static ModAnimStateMachine BuildCombatAnimationStateMachine(Node visualsRoot, CharacterModel character)
    {
        if (character is not INinjaSlayerCharacter)
        {
            throw new InvalidOperationException("NinjaSlayer animation state machine requires a NinjaSlayer character model.");
        }

        var builder = ModAnimStateMachineBuilder.Create()
            .AddState("idle", loop: true).AsInitial().Done()
            .AddState("attack").WithNext("idle").Done()
            .AddState("x_attack").WithNext("idle").Done()
            .AddState("cast").WithNext("idle").Done()
            .AddState("hit").WithNext("idle").Done()
            .AddState("blocked_hit").WithNext("idle").Done()
            .AddState("dead").Done()
            .AddState("relaxed", loop: true).Done();

        builder.AddAnyState("Idle", "idle");
        builder.AddAnyState("Attack", NinjaSlayerCharacter.AttackCueName);
        builder.AddAnyState("XAttack", "x_attack");
        builder.AddAnyState("XAttackCue", "x_attack");
        builder.AddAnyState("Cast", "cast");
        builder.AddAnyState("Hit", NinjaSlayerCharacter.HitCueName);
        builder.AddAnyState("BlockedHit", NinjaSlayerCharacter.BlockedHitCueName);
        builder.AddAnyState("Dead", "dead");
        builder.AddAnyState("Relaxed", "relaxed");

        return builder.BuildForVisualsRoot(visualsRoot, character, NinjaSlayerCharacter.CombatVisualCues);
    }
}
