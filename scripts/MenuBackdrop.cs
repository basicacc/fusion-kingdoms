using FusionKingdoms.Sim;
using Godot;

namespace FusionKingdoms;

// Behind the title screen two AI kingdoms play a real match while the camera drifts over the land.
public partial class MenuBackdrop : Node2D
{
    Match _m = null!;
    MatchView _view = null!;
    Camera2D _cam = null!;
    float _acc, _t;
    Vector2[] _stops = [];

    public override void _Ready()
    {
        var lib = MindLibrary.I;
        int seed = (int)(GD.Randi() % 100000) + 1;
        _m = MapGen.Create1v1(lib.Factory, lib.BaseMinds, lib.All, seed, "Azure Crown", "Crimson Crown");
        _m.Ai[0] = new AiRuler(_m, _m.Kingdoms[0], AiPersonality.Warden, seed + 1);
        _m.Ai[1] = new AiRuler(_m, _m.Kingdoms[1], AiPersonality.Breeder, seed + 2);
        // A head start so the kingdoms already have fields, houses and bustle.
        for (int i = 0; i < 250; i++) _m.Step();

        _view = new MatchView { Team = 0, Spectator = true };
        AddChild(_view);
        _view.Init(_m);

        var blue = MatchView.Px(_m.Kingdoms[0].Keep.Pos);
        var red = MatchView.Px(_m.Kingdoms[1].Keep.Pos);
        var lumen = _m.W.Nodes.Where(n => n.Type == ResourceType.Lumen).Select(n => MatchView.Px(n.Pos)).FirstOrDefault(blue.Lerp(red, 0.5f));
        // The menu sits on the left, so frame each subject right of centre.
        var shift = new Vector2(-150, 0);
        _stops = [blue + shift, lumen + shift, red + shift, lumen + new Vector2(0, 120) + shift];
        _cam = new Camera2D
        {
            Zoom = new Vector2(2.5f, 2.5f), Position = _stops[0],
            LimitLeft = 0, LimitTop = 0, LimitRight = _m.W.Width * 16, LimitBottom = _m.W.Height * 16,
        };
        AddChild(_cam);
        _cam.MakeCurrent();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _t += dt;
        _acc += dt;
        int steps = 0;
        while (_acc >= Rules.Dt && steps++ < 4)
        {
            _view.Snapshot();
            _m.Step();
            _view.OnEvents(_m.W.Events);
            _acc -= Rules.Dt;
        }
        _view.Alpha = _acc / Rules.Dt;

        // Glide between points of interest, lingering at each.
        const float leg = 24f;
        int i = (int)(_t / leg) % _stops.Length;
        float f = _t % leg / leg;
        float ease = f < 0.55f ? 0f : (1f - MathF.Cos((f - 0.55f) / 0.45f * MathF.PI)) / 2f;
        var from = _stops[i];
        var to = _stops[(i + 1) % _stops.Length];
        _cam.Position = from.Lerp(to, ease) + new Vector2(MathF.Sin(_t * 0.13f) * 30f, MathF.Cos(_t * 0.11f) * 18f);
    }
}
