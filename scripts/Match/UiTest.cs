using FusionKingdoms.Sim;
using Godot;
using SVec = System.Numerics.Vector2;

namespace FusionKingdoms;

// Developer hook FK_UITEST=1: plays the build flow with real mouse events (Build button -> a building -> click the
// map) for every Age-1 building, and prints whether each site actually got placed. Then quits.
public partial class UiTest : Node
{
    readonly MatchScene _s;
    int _step;
    float _wait = 1.5f;
    int _index;
    readonly List<BuildingType> _types;
    readonly List<string> _log = new();

    public UiTest(MatchScene s)
    {
        _s = s;
        _types = Defs.Buildings.Where(d => d.Age == 1).Select(d => d.Type).ToList();
        ProcessMode = ProcessModeEnum.Always;
    }

    static Button? FindButton(Node root, Func<string, bool> pred)
    {
        foreach (var c in root.GetChildren())
        {
            if (c is Button b && b.IsVisibleInTree() && pred(b.Text)) return b;
            if (FindButton(c, pred) is { } found) return found;
        }
        return null;
    }

    void Click(Vector2 at, MouseButton button = MouseButton.Left)
    {
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = button, Pressed = true });
        Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = button, Pressed = false });
    }

    public override void _Process(double delta)
    {
        _wait -= (float)delta;
        if (_wait > 0f) return;
        _wait = 0.5f;
        _s.Cam.PositionSmoothingEnabled = false;
        var M = _s.M;
        var me = _s.Me;
        // Plenty of materials so cost isn't what we're testing.
        me.Stored[(int)ResourceType.Timber] = 999; me.Stored[(int)ResourceType.Stone] = 999; me.Stored[(int)ResourceType.Lumen] = 999;
        if (_index >= _types.Count)
        {
            foreach (var l in _log) GD.Print("[uitest] " + l);
            GetTree().Quit();
            return;
        }
        var type = _types[_index];
        string name = Defs.Of(type).Name;
        switch (_step)
        {
            case 0:
            {
                var b = FindButton(_s.Hud, t => t.StartsWith("Build (B)"));
                if (b == null) { _log.Add("no Build button"); _index = _types.Count; return; }
                Click(b.GetGlobalRect().GetCenter());
                _step = 1;
                break;
            }
            case 1:
            {
                var b = FindButton(_s.Hud, t => t.StartsWith(name + "\n") || t.StartsWith(name + "  ("));
                if (b == null) { _log.Add($"{name}: no button in the build menu"); _step = 0; _index++; return; }
                if (b.Disabled) { _log.Add($"{name}: button disabled"); _step = 0; _index++; return; }
                Click(b.GetGlobalRect().GetCenter());
                _step = 2;
                break;
            }
            case 2:
            {
                if (_s.View.Placing != type) { _log.Add($"{name}: clicking the button did not start placement (Placing={_s.View.Placing})"); _step = 0; _index++; return; }
                SVec? spot = null;
                for (int i = 0; i < 400 && spot == null; i++)
                {
                    float a = i * 0.7f, d = 4f + i * 0.04f;
                    var p = me.Home + new SVec(MathF.Cos(a) * d, MathF.Sin(a) * d);
                    if (M.CanPlace(me, type, p) == null) spot = p;
                }
                if (spot == null) { _log.Add($"{name}: no legal spot near home"); _s.View.Placing = null; _step = 0; _index++; return; }
                _s.LookAt(spot.Value);
                _step = 3;
                _pending = spot.Value;
                _wait = 1.0f;
                break;
            }
            case 3:
            {
                int before = M.W.Buildings.Count(b => b.Type == BuildingType.BuildSite && b.Team == me.Team && b.SiteFor == type);
                var screen = _s.View.GetViewport().GetCanvasTransform() * MatchView.Px(_pending);
                Input.ParseInputEvent(new InputEventMouseMotion { Position = screen, GlobalPosition = screen });
                var hov = _s.GetViewport().GuiGetHoveredControl();
                _log.Add($"{name}: click at {screen}, hovered control: {(hov == null ? "none" : hov.GetPath() + " " + hov.GetType().Name)}");
                Click(screen);
                _before = before;
                _step = 4;
                break;
            }
            case 4:
            {
                int after = M.W.Buildings.Count(b => b.Type == BuildingType.BuildSite && b.Team == me.Team && b.SiteFor == type);
                _log.Add(after > _before ? $"{name}: OK" : $"{name}: map click did NOT place a site (Placing={_s.View.Placing}, CanPlace={M.CanPlace(me, type, _pending) ?? "ok"})");
                _s.View.Placing = null;
                _step = 0;
                _index++;
                break;
            }
        }
    }

    SVec _pending;
    int _before;
}
