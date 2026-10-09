using Godot;

namespace FusionKingdoms;

// Developer hook for non-match scenes: FK_SHOT=<png path> saves a screenshot after FK_SHOT_FRAMES frames and quits.
public partial class DevShot : Node
{
    int _frames;
    string? _path;
    int _at;

    public override void _Ready()
    {
        _path = OS.GetEnvironment("FK_SHOT");
        _at = int.TryParse(OS.GetEnvironment("FK_SHOT_FRAMES"), out var f) ? f : 60;
        if (string.IsNullOrEmpty(_path) || !string.IsNullOrEmpty(OS.GetEnvironment("FK_LAN"))) SetProcess(false);
    }

    public override void _Process(double delta)
    {
        if (GetTree().CurrentScene is MatchScene) { SetProcess(false); return; }
        if (++_frames < _at) return;
        GetViewport().GetTexture().GetImage().SavePng(_path);
        GetTree().Quit();
    }
}
