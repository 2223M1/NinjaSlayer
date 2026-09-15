using Godot;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Monsters;

namespace NinjaSlayer.Code.Nodes;

internal sealed partial class SawatariWeaponVisuals : Node
{
    internal const string RootPath = "res://NinjaSlayer/images/monsters/sawatari/";
    internal static readonly string[] AssetPaths =
    [RootPath + "body.png", RootPath + "inner-fist.png", RootPath + "machete.png", RootPath + "arrow.png",
        RootPath + "bow.png", RootPath + "bow-string.png", RootPath + "bamboo.png"];
    private SawatariMonster _monster = null!;
    private Sprite2D _body = null!;
    private Vector2 _bodyScale;
    private Vector2 _groundContact;
    private readonly Node2D _weapons = new() { Name = "WeaponRig" };
    private readonly Node2D _ranged = new() { Name = "BowAndArrow", Position = new(-204.5f, -17.5f) };
    private Sprite2D _bamboo = null!;
    private Sprite2D _fist = null!;
    private Sprite2D? _arrow;
    private readonly Node2D[] _hands = [new(), new()];
    private readonly Sprite2D?[] _knives = new Sprite2D?[2];
    // Accepted grip-revision-02, in centered 461x537 body pixels.
    private static readonly Vector2[] Grips = [new(-198.9127f, -13.76517f), new(-64.79138f, -10.36161f)];
    private static readonly float[] KnifeScales = [1.5756349f, 1.5616022f];
    private static readonly float[] UprightDegrees = [91.34166f, 92.09703f];
    private static readonly float[] HorizontalDegrees = [4.774081f, 2.774081f];
    private Tween? _poseTween;
    private Tween? _actionTween;
    private bool? _dual;
    private int _incomingMachetes;
    private Tween? _swapTween;
    private Tween? _stringTween;
    private WeaponPose? _shownPose;
    private enum WeaponPose { Bamboo, Bow, Knives }
    internal static float BladeScale(int hand) => .52f * KnifeScales[hand];

    internal static SawatariWeaponVisuals? Get(Creature creature) =>
        creature.GetCreatureNode()?.Visuals.GetNodeOrNull<SawatariWeaponVisuals>("SawatariWeapons");

    internal static void Create(SawatariMonster monster)
    {
        if (monster.Creature.GetCreatureNode() is not { } actor) return;
        var visual = new SawatariWeaponVisuals { Name = "SawatariWeapons", _monster = monster };
        visual._body = NinjaSlayerVisualRig.GetBodySprite(actor.Visuals)!;
        visual._bodyScale = visual._body.Scale;
        visual._groundContact = actor.Visuals.GetNode<Node2D>("GroundContact").Position;
        if (monster.Creature.Side == CombatSide.Enemy)
        {
            float offset = -390f - actor.Visuals.IntentPosition.Position.Y;
            actor.Visuals.IntentPosition.Position += new Vector2(0, offset);
            // NCreature lays out this public container before OnAddedToCombat.
            actor.IntentContainer.Position += new Vector2(0, offset * actor.Visuals.Scale.X);
        }
        actor.Visuals.AddChild(visual);
        visual._body.AddChild(visual._weapons);
        for (int hand = 0; hand < 2; hand++)
        {
            visual._hands[hand].Position = Grips[hand];
            visual._hands[hand].Scale = Vector2.One * KnifeScales[hand];
            visual._hands[hand].ZIndex = (hand + 1) * 10;
            visual._weapons.AddChild(visual._hands[hand]);
        }
        visual._fist = new Sprite2D
        {
            Name = "InnerFist", Position = new Vector2(-31f, -8.5f),
            Texture = PreloadManager.Cache.GetTexture2D(RootPath + "inner-fist.png"),
            ZIndex = 30
        };
        visual._weapons.AddChild(visual._fist);
        visual._weapons.AddChild(visual._ranged);
        // Lossless aligned layers share the accepted outer-hand pivot.
        visual._ranged.AddChild(new Sprite2D
        {
            Name = "Bow", Texture = PreloadManager.Cache.GetTexture2D(RootPath + "bow.png"),
            Offset = new Vector2(78f, -28f), ZIndex = 10
        });
        visual._ranged.AddChild(new Sprite2D
        {
            Name = "String", Texture = PreloadManager.Cache.GetTexture2D(RootPath + "bow-string.png"),
            Offset = new Vector2(120.5f, -29.5f), ZIndex = 11
        });
        visual._bamboo = new Sprite2D
        {
            Name = "Bamboo", Texture = PreloadManager.Cache.GetTexture2D(RootPath + "bamboo.png"),
            Position = new Vector2(-73.5f, -5.5f), Offset = new Vector2(-165f, 13f), ZIndex = 10
        };
        visual._weapons.AddChild(visual._bamboo);
        visual.Refresh();
    }

    internal static Sprite2D CreateMachete()
    {
        var knife = new Sprite2D
        {
            Name = "Machete",
            Texture = PreloadManager.Cache.GetTexture2D(RootPath + "machete.png"),
            Offset = new Vector2(-93.5f, 8.5f)
        };
        return knife;
    }

    internal Node2D ReturnHand()
    {
        int hand = (_monster.HeldMachetes & 1) == 0 ? 0 : 1;
        _incomingMachetes |= 1 << hand;
        Refresh(dual: false);
        _hands[hand].Show();
        return _hands[hand];
    }

    internal void Receive(Sprite2D knife)
    {
        int hand = Array.IndexOf(_hands, knife.GetParent());
        _knives[hand] = knife;
    }

    internal void Refresh(bool? dual = null)
    {
        _incomingMachetes &= ~_monster.HeldMachetes;
        if (!_monster.ActThree)
        {
            if (_monster.Creature.Side == CombatSide.Enemy
                && _monster.NextMove.Id == SawatariMonster.EnhanceMoveId) ShowBow(nocked: true);
            else ShowBamboo();
            return;
        }
        if (_monster.MacheteCount == 0 && dual == null)
        {
            ShowBamboo();
            return;
        }
        bool raised = dual ?? _monster.PlannedMacheteMove == SawatariMonster.DualMoveId;
        _fist.ZIndex = 30;
        SyncBody();
        for (int hand = 0; hand < 2; hand++)
        {
            _hands[hand].Visible = (_monster.HeldMachetes & (1 << hand)) != 0;
            if (_hands[hand].Visible && _knives[hand] == null)
            {
                _knives[hand] = CreateMachete();
                _hands[hand].AddChild(_knives[hand]);
            }
        }
        SwitchWeapon(WeaponPose.Knives);
        if (_dual == raised) return;
        _poseTween?.Kill();
        if (_dual == null || CombatActionTimingRuntime.VisualSeconds(.2f) <= 0f)
            for (int hand = 0; hand < 2; hand++)
                _hands[hand].RotationDegrees = raised ? UprightDegrees[hand] : HorizontalDegrees[hand];
        else
        {
            _poseTween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
            for (int hand = 0; hand < 2; hand++)
                _poseTween.TweenProperty(_hands[hand], "rotation_degrees",
                    raised ? UprightDegrees[hand] : HorizontalDegrees[hand], CombatActionTimingRuntime.VisualSeconds(.2f));
        }
        _dual = raised;
    }

    internal void ShowBamboo()
    {
        _poseTween?.Kill();
        _fist.ZIndex = 1;
        SyncBody();
        SwitchWeapon(WeaponPose.Bamboo);
        _dual = null;
    }

    internal void ShowBow(bool nocked)
    {
        _poseTween?.Kill();
        _fist.ZIndex = 1;
        SyncBody();
        SwitchWeapon(WeaponPose.Bow);
        if (_arrow == null && nocked)
        {
            _arrow = new Sprite2D
            {
                Name = "Arrow", Texture = PreloadManager.Cache.GetTexture2D(RootPath + "arrow.png"),
                Position = new Vector2(30.959879f, 4.226345f),
                Offset = new Vector2(.5401217f, .2736547f), ZIndex = 20
            };
            _ranged.AddChild(_arrow);
            ApplyBowDraw(0);
        }
        if (_arrow != null) _arrow.Visible = nocked;
        _dual = null;
    }

    private void SyncBody()
    {
        Vector2 foot = new(-20.5f, 259.5f);
        _body.Scale = _bodyScale;
        if (_body.FlipH) foot.X = -foot.X;
        _body.Position = _groundContact - foot * _bodyScale;
        _weapons.Scale = new Vector2(_body.FlipH ? -1 : 1, 1);
        _weapons.Visible = _monster.Creature.IsAlive;
    }

    internal static async Task<Sprite2D?> PlayThrow(SawatariMonster monster, Creature target, int hand)
    {
        if (Get(monster.Creature) is not { } visual || target.GetCreatureNode() is not { } victim) return null;
        visual.Refresh(dual: false);
        visual._hands[hand].Show();
        Sprite2D knife = visual._knives[hand]!;
        Node2D? catchHand = target.Player is { } player ? PlayerMacheteVisuals.CatchHand(player) : null;
        var actor = monster.Creature.GetCreatureNode()!;
        Node2D anchor = NinjaSlayerVisualRig.GetAirborneAnchor(actor.Visuals)!;
        Node2D center = actor.Visuals.VfxSpawnPosition;
        var (baseline, core) = StaggerAnimation.CaptureAttackPose(monster.Creature, anchor, center);
        float facing = visual._body.FlipH ? 1f : -1f;
        float duration = CombatActionTimingRuntime.VisualSeconds(.167f);
        const float release = .166f / .334f;
        Task<bool> flight = Task.FromResult(false);
        void Apply(float p)
        {
            var motion = ShurikenThrowMotion.Sample(p);
            var transform = new Transform2D(Mathf.DegToRad(motion.Degrees) * facing, Vector2.Zero);
            transform.Origin = core + new Vector2(motion.Distance * facing, 0f) - transform.BasisXform(core);
            StaggerAnimation.ApplyAttackPose(monster.Creature, anchor, center, transform * baseline, transform * core);
        }
        void Release()
        {
            if (!monster.Creature.IsAlive || !target.IsAlive
                || !ReferenceEquals(monster.Creature.CombatState, target.CombatState) || !target.IsHittable
                || !GodotObject.IsInstanceValid(victim) || !victim.IsInsideTree()) return;
            visual._knives[hand] = null;
            NDebugAudioManager.Instance?.Play(TmpSfx.daggerThrow);
            flight = FlyWeapon(knife, catchHand ?? victim.Visuals.VfxSpawnPosition, catchWeapon: catchHand != null);
            visual._hands[hand].Hide();
        }
        try
        {
            if (duration <= 0f) Release();
            else
            {
                visual._actionTween?.Kill();
                visual._actionTween = visual.CreateTween();
                visual._actionTween.TweenMethod(Callable.From<float>(Apply), 0f, release, duration * release);
                visual._actionTween.TweenCallback(Callable.From(Release));
                visual._actionTween.TweenMethod(Callable.From<float>(Apply), release, 1f, duration * (1f - release));
                await TweenPlayback.AwaitCompletion(visual._actionTween, visual);
            }
            return await flight ? knife : null;
        }
        finally
        {
            if (GodotObject.IsInstanceValid(anchor) && GodotObject.IsInstanceValid(center))
                StaggerAnimation.ApplyAttackPose(monster.Creature, anchor, center, baseline, core);
        }
    }

    internal static async Task PlayArrow(Creature source, Creature target)
    {
        if (Get(source) is not { } visual || target.GetCreatureNode() is not { } victim) return;
        visual.ShowBow(nocked: true);
        try
        {
            Task<bool> flight = Task.FromResult(false);
            await visual.DrawBow(victim.Visuals.VfxSpawnPosition, () =>
            {
                if (!source.IsAlive || !target.IsAlive
                    || !ReferenceEquals(source.CombatState, target.CombatState) || !target.IsHittable
                    || !GodotObject.IsInstanceValid(victim) || !victim.IsInsideTree()) return;
                Sprite2D arrow = visual._arrow!;
                visual._arrow = null;
                visual.ReleaseString();
                SfxCmd.Play("event:/sfx/enemy/enemy_attacks/crossbow_ruby_raider/crossbow_ruby_raider_attack");
                flight = FlyWeapon(arrow, victim.Visuals.VfxSpawnPosition, catchWeapon: false,
                    sourceAngle: 180f, arcHeight: 0f, flightSeconds: .133f, aimBeforeFlight: true);
            });
            await flight;
        }
        finally
        {
            if (GodotObject.IsInstanceValid(visual) && visual.IsInsideTree()) visual.ShowBamboo();
        }
    }

    public override void _ExitTree()
    {
        _poseTween?.Kill();
        _actionTween?.Kill();
        _swapTween?.Kill();
        _stringTween?.Kill();
        if (GodotObject.IsInstanceValid(_weapons)) _weapons.QueueFree();
    }
}
