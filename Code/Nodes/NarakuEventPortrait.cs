using Godot;

namespace NinjaSlayer.Code.Nodes;

[GlobalClass]
public partial class NarakuEventPortrait : Node2D
{
    private VideoStreamPlayer? _video;

    public override void _Ready()
    {
        _video = GetNode<VideoStreamPlayer>("NarakuFilm");
        _video.Play();
    }

    public override void _ExitTree()
    {
        if (_video != null)
        {
            _video.Stop();
            _video.Stream = null;
        }
    }
}
