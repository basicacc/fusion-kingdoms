using FusionKingdoms.Sim;
using Godot;
using SVec = System.Numerics.Vector2;

namespace FusionKingdoms;

public partial class ArenaView : Node2D
{
    public const float Tile = 16f;

    public World W { get; private set; } = null!;
    public float Speed = 1f;
    public int Selected = -1;

    [Signal] public delegate void SelectionChangedEventHandler(int unitId);

    MatchView? _view;
    float _acc;

    public override void _Ready() => Reset(1);

    public void Reset(int seed)
    {
        W = Maps.Lab(seed);
        Selected = -1;
        _view?.QueueFree();
        _view = new MatchView { Team = 0 };
        AddChild(_view);
        _view.Init(W, null);
        Snapshot();
        EmitSignal(SignalName.SelectionChanged, -1);
    }

    public Vector2 WorldSize => new(W.Width * Tile, W.Height * Tile);

    public Unit Spawn(Mind bp, int team)
    {
        var keep = W.KeepOf(team)!;
        var off = new SVec((float)GD.RandRange(-2.5, 2.5), (float)GD.RandRange(-2.5, 2.5));
        var u = W.AddUnit(team, Species.Human, keep.Pos + off, new BrainController(bp.Brain));
        u.Mind = bp;
        Snapshot();
        return u;
    }

    public Unit SpawnAberration(Mind bp)
    {
        var at = new SVec(W.Width / 2f, W.Height / 2f);
        var u = W.AddUnit(2, Species.Troll, at, new BrainController(bp.Brain));
        u.Mind = bp;
        Snapshot();
        return u;
    }

    void Snapshot() => _view?.Snapshot();

    public override void _Process(double delta)
    {
        if (_view == null) return;
        _acc += (float)delta * Speed;
        while (_acc >= Rules.Dt)
        {
            Snapshot();
            W.Step();
            _view.OnEvents(W.Events);
            _acc -= Rules.Dt;
        }
        _view.Alpha = _acc / Rules.Dt;
        _view.Selected.Clear();
        if (Selected >= 0 && Selected < W.Units.Count) _view.Selected.Add(W.Units[Selected]);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_view == null || e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;
        var u = _view.UnitAt(_view.GetGlobalMousePosition(), 12f);
        Selected = u?.Id ?? -1;
        if (u != null) Sound.I.Play("select", -6f);
        EmitSignal(SignalName.SelectionChanged, Selected);
    }
}
