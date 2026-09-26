using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using NinjaSlayer.Code.Combat;
using NinjaSlayer.Scripts;
using STS2RitsuLib;
using STS2RitsuLib.RunData;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed partial class YukanoArrowPopup : Node
{
    internal const string Folder = "res://NinjaSlayer/animations/yukano_arrow_popup/";
    private const double FrameSeconds = 1001d / 24000d;
    private const double ReleaseSeconds = 24d * FrameSeconds;
    private static RunSavedData<YukanoArrowPopupRunData> _runData = null!;
    private static readonly HashSet<YukanoArrowPopup> Active = [];
    private readonly TaskCompletionSource<LaunchResult> _launch = new();
    private RunState _run = null!;
    private Creature _source = null!, _target = null!;
    private NCombatRoom _room = null!;
    private VideoStreamPlayer _film = null!;
    private Node2D _composition = null!;
    private Sprite2D _content = null!, _frame = null!;
    private AtlasTexture[] _frames = [];
    private Vector2[] _origins = [];
    private Action<float> _pose = null!;
    private Action _release = null!;
    private bool _started, _released, _closing, _stopped, _paused;
    private double _openingTime, _closingTime, _stallTime, _lastPosition;
    private ulong _lastTick;

    internal enum LaunchResult { Released, Fallback, Cancelled }
    internal Task<LaunchResult> Launch => _launch.Task;
    // Also sampled by the isolated recording driver at FramePreDraw.
    internal double ReleasePosition { get; private set; } = -1d;
    internal int ReleaseRenderFrame { get; private set; }
    internal double PlaybackPosition => _film.StreamPosition;

    internal static void RegisterSavedData(string modId) =>
        _runData = RitsuLibFramework.GetRunSavedDataStore(modId).Register<YukanoArrowPopupRunData>(
            "yukano_arrow_popup", options: new RunSavedDataOptions { WritePolicy = RunSavedDataWritePolicy.WhenNonDefault });

    internal static YukanoArrowPopup? TryStart(Creature source, Creature target, Action<float> pose, Action release)
    {
        if (CombatActionTimingRuntime.VisualSeconds(1f) <= 0f || !source.IsAlive || !target.IsAlive
            || source.PetOwner?.RunState is not RunState run || _runData.Get(run).Shown
            || Active.Any(p => ReferenceEquals(p._run, run)) || NCombatRoom.Instance is not { } room)
            return null;

        // Resource decoding is a boundary: a broken optional movie must not lose an attack.
        var popup = new YukanoArrowPopup
        {
            Name = "YukanoArrowPopup", _source = source, _target = target, _room = room,
            _run = run, _pose = pose, _release = release, ProcessMode = ProcessModeEnum.Always
        };
        try
        {
            popup.Build();
            room.AddChild(popup);
            return popup;
        }
        catch (Exception error)
        {
            Entry.Logger.Warn($"Yukano arrow movie unavailable: {error.Message}");
            popup.Free();
            return null;
        }
    }

    private void Build()
    {
        Texture2D atlas = ResourceLoader.Load<Texture2D>(Folder + "runtime-atlas.png")
            ?? throw new InvalidDataException("Missing frame atlas");
        Texture2D mask = ResourceLoader.Load<Texture2D>(Folder + "interior-clip.png")
            ?? throw new InvalidDataException("Missing clip mask");
        Shader shader = ResourceLoader.Load<Shader>(Folder + "clip.gdshader")
            ?? throw new InvalidDataException("Missing clip shader");
        VideoStream stream = ResourceLoader.Load<VideoStream>(Folder + "yukano-original.ogv")
            ?? throw new InvalidDataException("Missing film");
        using JsonDocument data = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(Folder + "animation.json"));
        JsonElement entries = data.RootElement.GetProperty("entries");
        if (entries.GetArrayLength() != 10) throw new InvalidDataException("Expected 5 opening, 1 film and 4 closing frames");
        _frames = new AtlasTexture[10];
        _origins = new Vector2[10];
        for (int i = 0; i < entries.GetArrayLength(); i++)
        {
            JsonElement rect = entries[i].GetProperty("atlas_rect"), origin = entries[i].GetProperty("source_origin");
            _frames[i] = new AtlasTexture { Atlas = atlas, Region = new Rect2(rect[0].GetSingle(), rect[1].GetSingle(), rect[2].GetSingle(), rect[3].GetSingle()) };
            _origins[i] = new(origin[0].GetSingle(), origin[1].GetSingle());
        }
        var layer = new CanvasLayer { Name = "ScreenSpace", Layer = 10 };
        AddChild(layer);
        _composition = new Node2D();
        layer.AddChild(_composition);
        var viewport = new SubViewport
        {
            Size = new Vector2I(1920, 1080), TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always
        };
        AddChild(viewport);
        _film = new VideoStreamPlayer
        {
            Stream = stream, Expand = true, Loop = false, Position = new(-430, -240),
            Size = new(1824, 1026), MouseFilter = Control.MouseFilterEnum.Ignore
        };
        viewport.AddChild(_film);
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("interior_mask", mask);
        _content = new Sprite2D { Texture = viewport.GetTexture(), Centered = false, Material = material, Visible = false };
        _composition.AddChild(_content);
        _frame = new Sprite2D { Centered = false };
        _composition.AddChild(_frame);
        ShowFrame(0);
    }

    public override void _Ready()
    {
        Active.Add(this);
        _film.Finished += OnFilmFinished;
        _film.Play();
        _film.Paused = true;
        RenderingServer.FramePreDraw += BeforeDraw;
    }

    public override void _Process(double delta)
    {
        if (_stopped) return;
        ulong now = Time.GetTicksUsec();
        double dt = _lastTick == 0 ? 0d : (now - _lastTick) / 1_000_000d;
        _lastTick = now;
        if (!ReferenceEquals(NCombatRoom.Instance, _room) || !_source.IsAlive
            || (!_released && (!ValidTarget() || CombatManager.Instance.IsOverOrEnding)))
        {
            Cancel();
            return;
        }
        var ui = NRun.Instance?.GlobalUi;
        _paused = GetTree().Paused || Engine.TimeScale <= 0d || CombatManager.Instance.IsPaused || !_room.CanProcess()
            || ui?.Overlays.ScreenCount > 0 || ui?.CapstoneContainer.InUse == true
            || ui?.MapScreen.IsOpen == true || NModalContainer.Instance?.OpenModal != null;
        _film.Paused = _paused || _openingTime < 5d * FrameSeconds;
        _film.Volume = SaveManager.Instance.PrefsSave.MuteInBackground && !GetWindow().HasFocus()
            ? 0f : SaveManager.Instance.SettingsSave.VolumeMaster;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        _composition.Scale = Vector2.One * (.6f * Math.Min(size.X / 1920f, size.Y / 1080f));
        // Godot's process delta can include decoding before the first visible frame.
        // Only the frame wrapper uses this pause-aware clock; launch always uses the decoder.
        if (!_started || _paused) return;
        if (_closing)
        {
            _closingTime += dt;
            if (_closingTime >= 4d * FrameSeconds) Stop(LaunchResult.Fallback);
            else ShowFrame(6 + Math.Min(3, (int)(_closingTime / FrameSeconds)));
        }
        else if (_openingTime < 5d * FrameSeconds)
        {
            _openingTime += dt;
            ShowFrame(Math.Min(5, (int)(_openingTime / FrameSeconds)));
            _content.Visible = _openingTime >= 5d * FrameSeconds;
            _film.Paused = !_content.Visible;
        }
        else
        {
            double position = _film.StreamPosition;
            _stallTime = position > _lastPosition ? 0d : _stallTime + dt;
            _lastPosition = position;
            if (_stallTime > 1d || !_film.IsPlaying())
            {
                Entry.Logger.Warn("Yukano arrow movie stopped before completion; continuing the owned attack once.");
                Stop(LaunchResult.Fallback);
            }
        }
    }

    private bool ValidTarget() => _target.IsAlive
        && ReferenceEquals(_source.CombatState, _target.CombatState)
        && _source.CombatState?.ContainsCreature(_source) == true
        && _source.CombatState.ContainsCreature(_target)
        && _target.IsHittable;

    private void BeforeDraw()
    {
        if (_stopped || _paused) return;
        if (!_started)
        {
            if (!_film.IsPlaying()) { Stop(LaunchResult.Fallback); return; }
            _started = true;
            _lastTick = Time.GetTicksUsec();
            _runData.Modify(_run, data => data.Shown = true);
        }
        if (_released || _closing || !_content.Visible) return;
        double position = _film.StreamPosition;
        _pose(Math.Clamp((float)((position - (ReleaseSeconds - .1d)) / .25d), 0f, .4f));
        if (position < ReleaseSeconds) return;
        if (!ValidTarget()) { Cancel(); return; }
        _released = true;
        ReleasePosition = position;
        ReleaseRenderFrame = Engine.GetFramesDrawn();
        // Both the final launch pose and real projectile are submitted for this rendered frame.
        _release();
        _launch.TrySetResult(LaunchResult.Released);
    }

    private void OnFilmFinished()
    {
        if (_stopped) return;
        if (!_released) { Stop(LaunchResult.Fallback); return; }
        _closing = true;
        _closingTime = 0d;
        _lastTick = Time.GetTicksUsec();
        _content.Visible = false;
        ShowFrame(6);
    }

    private void ShowFrame(int index)
    {
        _frame.Texture = _frames[index];
        _frame.Position = _origins[index];
    }

    internal void Cancel() => Stop(LaunchResult.Cancelled);

    private void Stop(LaunchResult result)
    {
        if (_stopped) return;
        _stopped = true;
        _film.Stop();
        _composition.Visible = false;
        _launch.TrySetResult(result);
        QueueFree();
    }

    public override void _ExitTree()
    {
        RenderingServer.FramePreDraw -= BeforeDraw;
        _film.Finished -= OnFilmFinished;
        Active.Remove(this);
        _launch.TrySetResult(LaunchResult.Cancelled);
    }
}

internal sealed class YukanoArrowPopupRunData
{
    public bool Shown { get; set; }
}
