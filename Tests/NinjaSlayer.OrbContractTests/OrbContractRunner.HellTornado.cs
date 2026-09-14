using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private void VerifyHellTornado(NCreature actor, Vector2 centeredPivot)
    {
        Node2D aim = actor.Visuals.GetNode<Node2D>("%AimPose");
        Node2D hell = (Node2D)AccessTools.Property(aim.GetType(), "HellTornado").GetValue(aim)!;
        Sprite2D source = actor.Visuals.GetNode<Sprite2D>("%Visuals");
        Sprite2D head = hell.GetNode<Sprite2D>("FixedHead");
        Sprite2D body = hell.GetNode<Sprite2D>("OrbitingBody");
        uint visibility = source.VisibilityLayer;
        bool sourceVisible = source.Visible;
        Vector2 root = actor.Position;
        void Call(string method, params object[] args) => AccessTools.Method(hell.GetType(), method).Invoke(hell, args);
        try
        {
            Call("Accelerate", 0f);
            Transform2D fixedHead = head.GlobalTransform;
            Vector2 core = actor.Visuals.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin;
            Vector2 pivot = head.GlobalTransform * centeredPivot;
            Require(source.Visible == sourceVisible && source.VisibilityLayer == 0,
                "Hell Tornado must suppress the logical body without changing its facing visibility state.");
            bool orbitMoved = false;
            for (int frame = 0; frame < 20; frame++)
            {
                hell._Process(1d / 60d);
                Require(head.GlobalTransform.IsEqualApprox(fixedHead), "Hell Tornado rotated the head with its body.");
                Require((body.GlobalTransform * centeredPivot).DistanceTo(pivot) < .5f,
                    "Hell Tornado orbited around a point other than its actual head pivot.");
                Require(actor.Visuals.VfxSpawnPosition.GetGlobalTransformWithCanvas().Origin.DistanceTo(core) < .5f,
                    "The hit core followed the orbiting torso instead of the fixed head.");
                orbitMoved |= Math.Abs(body.GlobalRotation - head.GlobalRotation) > .1f;
            }
            Require(orbitMoved && actor.Position.IsEqualApprox(root), "Hell Tornado did not orbit independently of the UI root.");
            Require(body.Material is ShaderMaterial material && material.Shader.ResourcePath.EndsWith("/horizontal_motion_blur.gdshader", StringComparison.Ordinal),
                "Hell Tornado torso bypassed the cylindrical spin blur output.");
            Require(head.Material == null, "Hell Tornado blurred the fixed head.");
            Type coordinator = hell.GetType().Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.NinjaSlayerRapidAnimationCoordinator", true)!;
            AccessTools.Method(coordinator, "CancelOrdinaryActions").Invoke(null, [actor.Entity]);
            Require((bool)AccessTools.Property(hell.GetType(), "Active").GetValue(hell)!,
                "Alabama's ordinary-motion cleanup removed the persistent head/body split.");
            Call("Decelerate", .3f);
            for (int frame = 0; frame < 19; frame++) hell._Process(1d / 60d);
            Require(body.Transform.IsEqualApprox(head.Transform), "Hell Tornado did not settle on the authored orientation.");
        }
        finally { Call("Reset"); }
        Require(source.VisibilityLayer == visibility && !hell.Visible,
            "Hell Tornado cleanup left the source hidden or its replacement visible.");
        GD.Print("PASS separated Hell Tornado: delivered layers, fixed head/core, orbit pivot, stable UI, exact stop and visibility cleanup.");
    }
}
