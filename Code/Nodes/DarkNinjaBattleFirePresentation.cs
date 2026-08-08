using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Code.ExternalAnimations;
using NinjaSlayer.Content;
using NinjaSlayer.Monsters;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Nodes;

internal static class DarkNinjaBattleFirePresentation
{
    internal const string RootName = "DarkNinjaBattleFire";
    internal const string FireScenePath =
        "res://scenes/vfx/fires/vfx_additive_step_fire.tscn";
    private const float FireAlpha = 0.5f;

    internal static IEnumerable<string> AssetPaths => [FireScenePath];

    internal static void Ensure(NCombatRoom room, bool revealImmediately) =>
        GetOrCreate(room, revealImmediately);

    internal static Task RevealFromRightToLeft(NCombatRoom room) =>
        GetOrCreate(room, revealImmediately: false).RevealFromRightToLeft();

    private static DarkNinjaBattleFireRoot GetOrCreate(
        NCombatRoom room,
        bool revealImmediately)
    {
        Control container = room.BackCombatVfxContainer;
        DarkNinjaBattleFireRoot? existing =
            container.GetNodeOrNull<DarkNinjaBattleFireRoot>(RootName);
        if (existing != null)
        {
            return existing;
        }

        var root = new DarkNinjaBattleFireRoot
        {
            Name = RootName,
            Modulate = new Color(1f, 1f, 1f, FireAlpha)
        };
        container.AddChild(root);
        container.MoveChild(root, 0);
        root.Configure(room, container, revealImmediately);
        return root;
    }
}

internal sealed partial class DarkNinjaBattleFireRoot : Node2D
{
    private const float DefaultCombatWidth = 1920f;
    private const int MaximumLayoutFrames = 12;
    private const int RequiredStableFrames = 2;
    private const string FireSfxPath = "event:/sfx/characters/attack_fire";

    private static readonly float[] XScales = [1.12f, 1.25f, 1.05f, 1.18f];
    private static readonly float[] YScales = [0.95f, 1.08f, 0.82f, 1.01f, 0.9f];
    private static readonly float[] XJitter = [-8f, 5f, -12f, 10f, 0f, 12f, -4f];
    private static readonly float[] Rotations = [-1.25f, 0.75f, -0.4f, 1.5f, 0f, -0.9f];

    private readonly List<FireInstance> _fires = [];
    private NCombatRoom? _room;
    private Control? _container;
    private Task _initialization = Task.CompletedTask;
    private Task? _reveal;
    private bool _revealed;

    internal void Configure(
        NCombatRoom room,
        Control container,
        bool revealImmediately)
    {
        _room = room;
        _container = container;
        _initialization = Initialize(revealImmediately);
    }

    internal Task RevealFromRightToLeft() =>
        _reveal ??= RevealFromRightToLeftCore();

    private async Task Initialize(bool revealImmediately)
    {
        try
        {
            await WaitForInitialLayout();
            if (!IsActive())
            {
                return;
            }

            Position = new Vector2(0f, ResolveBaseline());
            if (revealImmediately)
            {
                PopulateFireRow(hidden: false);
                _revealed = true;
            }
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn($"Dark Ninja battle fire was unavailable: {exception.Message}");
            if (GodotObject.IsInstanceValid(this))
            {
                this.QueueFreeSafely();
            }
        }
    }

    private async Task RevealFromRightToLeftCore()
    {
        try
        {
            await _initialization;
            if (_revealed || !IsActive())
            {
                return;
            }

            _revealed = true;
            PopulateFireRow(hidden: true);
            float[] canvasX = _fires.Select(fire => fire.Node.Position.X).ToArray();
            int[] revealOrder = DarkNinjaBattleFireLayout.OrderRevealByX(canvasX);
            var animations = new List<Task>(_fires.Count);
            for (int revealIndex = 0; revealIndex < revealOrder.Length; revealIndex++)
            {
                FireInstance fire = _fires[revealOrder[revealIndex]];
                NinjaSlayerCombatAudioSet.Play(
                    FireSfxPath,
                    DarkNinjaBattleFireLayout.PerInstanceSfxVolume);
                animations.Add(RevealFire(fire));
                if (revealIndex + 1 < revealOrder.Length)
                {
                    await Cmd.Wait(DarkNinjaBattleFireLayout.RevealIntervalSeconds);
                }
            }

            await Task.WhenAll(animations);
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn($"Dark Ninja battle fire reveal was interrupted: {exception.Message}");
            CompleteFireRow();
        }
    }

    private static async Task RevealFire(FireInstance fire)
    {
        if (!GodotObject.IsInstanceValid(fire.Node))
        {
            return;
        }

        fire.Node.Visible = true;
        Tween tween = fire.Node.CreateTween();
        tween.TweenMethod(
                Callable.From<float>(progress =>
                {
                    if (!GodotObject.IsInstanceValid(fire.Node))
                    {
                        return;
                    }

                    fire.Node.Scale = new Vector2(
                        fire.AuthoredScale.X,
                        Mathf.Lerp(
                            fire.AuthoredScale.Y * DarkNinjaBattleFireLayout.InitialHeightScale,
                            fire.AuthoredScale.Y,
                            progress));
                    Color modulate = fire.Node.Modulate;
                    modulate.A = progress;
                    fire.Node.Modulate = modulate;
                }),
                0f,
                1f,
                DarkNinjaBattleFireLayout.RevealTweenSeconds)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Quad);
        await TweenPlayback.AwaitCompletion(tween, fire.Node);

        if (GodotObject.IsInstanceValid(fire.Node))
        {
            fire.Node.Scale = fire.AuthoredScale;
            Color modulate = fire.Node.Modulate;
            modulate.A = 1f;
            fire.Node.Modulate = modulate;
        }
    }

    private async Task WaitForInitialLayout()
    {
        if (_room == null)
        {
            return;
        }

        SceneTree tree = _room.GetTree();
        LayoutSnapshot previous = default;
        int stableFrames = 0;
        for (int frame = 0; frame < MaximumLayoutFrames && stableFrames < RequiredStableFrames; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (!GodotObject.IsInstanceValid(this))
            {
                return;
            }

            LayoutSnapshot current = CaptureLayout(_room);
            stableFrames = current == previous ? stableFrames + 1 : 0;
            previous = current;
        }
    }

    private void PopulateFireRow(bool hidden)
    {
        if (_container == null || _fires.Count > 0)
        {
            return;
        }

        PackedScene scene = PreloadManager.Cache.GetScene(
            DarkNinjaBattleFirePresentation.FireScenePath);
        if (!GodotObject.IsInstanceValid(scene))
        {
            throw new InvalidOperationException(
                $"Unable to load {DarkNinjaBattleFirePresentation.FireScenePath}.");
        }

        float width = _container.Size.X;
        if (!float.IsFinite(width) || width <= 0f)
        {
            width = DefaultCombatWidth;
        }

        int count = DarkNinjaBattleFireLayout.GetInstanceCount(width);
        for (int index = 0; index < count; index++)
        {
            Node2D fire = scene.Instantiate<Node2D>(PackedScene.GenEditState.Disabled);
            float direction = index % 2 == 0 ? 1f : -1f;
            fire.Position = new Vector2(
                -DarkNinjaBattleFireLayout.HorizontalOverhang
                    + index * DarkNinjaBattleFireLayout.InstanceSpacing
                    + XJitter[index % XJitter.Length],
                0f);
            Vector2 authoredScale = new(
                XScales[index % XScales.Length] * direction,
                YScales[index % YScales.Length]);
            fire.Scale = hidden
                ? new Vector2(
                    authoredScale.X,
                    authoredScale.Y * DarkNinjaBattleFireLayout.InitialHeightScale)
                : authoredScale;
            fire.Rotation = Mathf.DegToRad(Rotations[index % Rotations.Length]);
            fire.Visible = !hidden;
            if (hidden)
            {
                fire.Modulate = new Color(1f, 1f, 1f, 0f);
            }

            AddChild(fire);
            _fires.Add(new FireInstance(fire, authoredScale));
        }
    }

    private float ResolveBaseline()
    {
        if (_room == null || _container == null)
        {
            return float.NaN;
        }

        var hitboxBottoms = new List<float>();
        var shadowTops = new List<float>();
        Transform2D toContainer = _container.GetGlobalTransform().AffineInverse();
        foreach (NCreature creature in _room.CreatureNodes)
        {
            if (!GodotObject.IsInstanceValid(creature) || !creature.IsNodeReady())
            {
                continue;
            }

            if (creature.Entity.Monster is not YamotoKokiOrigamiMissile)
            {
                hitboxBottoms.Add((toContainer * creature.GetBottomOfHitbox()).Y);
            }

            AddShadowTops(creature.Visuals, toContainer, shadowTops);
        }

        float baseline = DarkNinjaBattleFireLayout.ResolveBaseline(hitboxBottoms, shadowTops);
        if (!float.IsFinite(baseline))
        {
            throw new InvalidOperationException("No valid creature baseline was available.");
        }

        return baseline;
    }

    private void CompleteFireRow()
    {
        foreach (FireInstance fire in _fires)
        {
            if (!GodotObject.IsInstanceValid(fire.Node))
            {
                continue;
            }

            fire.Node.Visible = true;
            fire.Node.Scale = fire.AuthoredScale;
            Color modulate = fire.Node.Modulate;
            modulate.A = 1f;
            fire.Node.Modulate = modulate;
        }
    }

    private bool IsActive() =>
        _room != null
        && _container != null
        && GodotObject.IsInstanceValid(_room)
        && GodotObject.IsInstanceValid(_container)
        && GodotObject.IsInstanceValid(this)
        && IsInsideTree();

    private static void AddShadowTops(
        Node root,
        Transform2D toContainer,
        ICollection<float> shadowTops)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is Sprite2D sprite
                && sprite.Name.ToString().Contains("shadow", StringComparison.OrdinalIgnoreCase)
                && sprite.Texture != null
                && sprite.IsVisibleInTree()
                && sprite.Modulate.A > 0.01f)
            {
                Rect2 rect = sprite.GetRect();
                Transform2D transform = toContainer * sprite.GetGlobalTransform();
                float top = new[]
                    {
                        rect.Position,
                        new Vector2(rect.End.X, rect.Position.Y),
                        rect.End,
                        new Vector2(rect.Position.X, rect.End.Y)
                    }
                    .Min(point => (transform * point).Y);
                shadowTops.Add(top);
            }

            AddShadowTops(child, toContainer, shadowTops);
        }
    }

    private static LayoutSnapshot CaptureLayout(NCombatRoom room)
    {
        ulong membership = 1469598103934665603UL;
        int positionHash = 17;
        int count = 0;
        foreach (NCreature creature in room.CreatureNodes)
        {
            membership = Mix(membership, creature.GetInstanceId());
            positionHash = HashCode.Combine(positionHash, creature.Position.X, creature.Position.Y);
            count++;
        }

        return new LayoutSnapshot(count, membership, positionHash);
    }

    private static ulong Mix(ulong hash, ulong value) =>
        (hash ^ value) * 1099511628211UL;

    private readonly record struct FireInstance(Node2D Node, Vector2 AuthoredScale);

    private readonly record struct LayoutSnapshot(int Count, ulong Membership, int PositionHash);
}
