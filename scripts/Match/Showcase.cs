using FusionKingdoms.Sim;
using Godot;
using SVec = System.Numerics.Vector2;

namespace FusionKingdoms;

// AI vs AI showcase: a versus bar for both kingdoms, banners for the big moments, and a director that moves the
// camera to wherever the most interesting thing is happening. WASD / mouse take over; Tab gives control back.
public partial class Showcase : Control
{
    readonly MatchScene _s;
    Match M => _s.M;

    // ---------- director ----------

    sealed class Hotspot
    {
        public SVec Pos;
        public float Interest, Time;
        public string Caption = "";
    }

    readonly List<Hotspot> _spots = new();
    Hotspot? _focus;
    float _shotTime, _manualUntil, _overviewIn = 40f;
    bool _overview;
    int _seenAlerts;
    public bool Auto = true;

    // ---------- UI ----------

    Label _caption = null!, _banner = null!, _clock = null!;
    readonly Label[] _side = new Label[2];
    float _bannerTime;
    readonly Queue<(string text, Color col)> _bannerQueue = new();

    public Showcase(MatchScene s)
    {
        _s = s;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Ready()
    {
        // Versus bar.
        var bar = Ui.Panel(null, 6);
        bar.SetAnchorsPreset(LayoutPreset.TopWide);
        var row = Ui.HBox(16);
        for (int t = 0; t < 2; t++)
        {
            _side[t] = Ui.Label("", 14, MatchView.TeamColors[t].Lightened(0.45f));
            _side[t].SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _side[t].HorizontalAlignment = t == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        }
        _clock = Ui.Label("", 15, Ui.Accent);
        _clock.HorizontalAlignment = HorizontalAlignment.Center;
        _clock.CustomMinimumSize = new Vector2(220, 0);
        row.AddChild(_side[0]);
        row.AddChild(_clock);
        row.AddChild(_side[1]);
        bar.AddChild(row);
        AddChild(bar);

        // Caption under the bar: what the camera is looking at.
        _caption = Ui.Label("", 15, Colors.White);
        _caption.HorizontalAlignment = HorizontalAlignment.Center;
        _caption.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        _caption.AddThemeConstantOverride("outline_size", 6);
        _caption.SetAnchorsPreset(LayoutPreset.TopWide);
        _caption.OffsetTop = 80;
        AddChild(_caption);

        // Big banner for major moments.
        _banner = Ui.Label("", 30, Ui.Accent);
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.AddThemeColorOverride("font_outline_color", new Color("2a1a08"));
        _banner.AddThemeConstantOverride("outline_size", 10);
        _banner.SetAnchorsPreset(LayoutPreset.CenterTop);
        _banner.AnchorLeft = 0f; _banner.AnchorRight = 1f;
        _banner.OffsetTop = 120;
        AddChild(_banner);

        var speed = Ui.HBox(4);
        foreach (var (txt, sp) in new[] { ("||", 0f), ("1x", 1f), ("2x", 2f), ("4x", 4f), ("8x", 8f) })
            speed.AddChild(Ui.Button(txt, () => _s.SetSpeed(sp)));
        speed.AddChild(Ui.Button("Menu", () => _s.ReturnToMenu()));
        AddChild(speed);
        speed.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight, LayoutPresetMode.KeepSize, 10);
        var help = Ui.Label("AI vs AI   ·   Tab: auto camera on/off   ·   WASD/drag: look around   ·   1/2: jump to a kingdom   ·   Space: pause", 11, Ui.Muted);
        help.SetAnchorsPreset(LayoutPreset.BottomWide);
        help.OffsetTop = -22;
        help.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(help);
    }

    public void TakeManual() => _manualUntil = Time.GetTicksMsec() / 1000f + 12f;

    void Banner(string text, Color col)
    {
        if (_bannerQueue.Count < 4) _bannerQueue.Enqueue((text, col));
    }

    // Everything happening this step adds interest to a spot on the map.
    public void OnEvents(IReadOnlyList<SimEvent> events)
    {
        foreach (var e in events)
        {
            var u = M.W.Units[e.Unit];
            float w = e.Kind switch
            {
                EventKind.TussleStarted => u.Team < 2 && e.Other >= 0 && M.W.Units[e.Other].Team < 2 && M.W.Units[e.Other].Team != u.Team ? 6f : 1.5f,
                EventKind.Subdued => u.IsRuler ? 40f : u.Team < 2 ? 5f : 1f,
                EventKind.Captured => M.W.Units[e.Other].IsRuler ? 100f : M.W.Units[e.Other].Team < 2 ? 12f : 2f,
                EventKind.Stolen => u.Team < 2 ? 0.3f : 0.05f,
                EventKind.Sabotaged => 10f,
                EventKind.Freed => 8f,
                _ => 0f,
            };
            if (w <= 0f) continue;
            string cap = e.Kind switch
            {
                EventKind.TussleStarted => $"{Side(u.Team)} {Who(u)} jumps a {Who(M.W.Units[e.Other])}",
                EventKind.Subdued => u.IsRuler ? $"{Side(u.Team)} ruler is down!" : $"A {Who(u)} of {Side(u.Team)} is knocked out",
                EventKind.Captured => $"{Side(u.Team)} drags a {Who(M.W.Units[e.Other])} into captivity",
                EventKind.Stolen => $"{Side(u.Team)} thieves loot the stores",
                EventKind.Sabotaged => $"Sabotage! A {Defs.NameOf(M.W.Buildings[e.Other].Type)} is put out of action",
                EventKind.Freed => $"{Side(u.Team)} rescues a captive",
                _ => "",
            };
            Add(u.Pos, w, cap);
        }
    }

    static string Side(int team) => team switch { 0 => "Blue", 1 => "Red", _ => "Wild" };

    static string Who(Unit u) => u.IsRuler ? "ruler" : u.Team == 2 ? (u.Mind?.Name ?? u.Species.ToString()) : Classes.Of(u.Mind).Name;

    void Add(SVec p, float w, string caption)
    {
        var near = _spots.FirstOrDefault(s => SVec.Distance(s.Pos, p) < 10f);
        if (near != null)
        {
            near.Interest += w;
            near.Pos = SVec.Lerp(near.Pos, p, 0.2f);
            if (w >= 2f || near.Caption.Length == 0) near.Caption = caption;
            near.Time = M.Time;
        }
        else _spots.Add(new Hotspot { Pos = p, Interest = w, Time = M.Time, Caption = caption });
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        UpdateBar();
        ReadAlerts();

        // Interest fades so the camera keeps finding fresh action.
        foreach (var s in _spots) s.Interest *= MathF.Pow(0.92f, dt * (_s.Paused ? 0f : MathF.Max(1f, _s.Speed)));
        _spots.RemoveAll(s => s.Interest < 0.3f && s != _focus);

        bool manual = !Auto || Time.GetTicksMsec() / 1000f < _manualUntil;
        if (!manual) Direct(dt);

        if (_bannerTime > 0f) _bannerTime -= dt;
        else if (_bannerQueue.Count > 0)
        {
            var (text, col) = _bannerQueue.Dequeue();
            _banner.Text = text;
            _banner.Modulate = col;
            _bannerTime = 4f;
        }
        _banner.Modulate = _banner.Modulate with { A = Math.Clamp(_bannerTime, 0f, 1f) };
        _caption.Modulate = new Color(1, 1, 1, manual ? 0.35f : 1f);
        if (manual && Auto) _caption.Text = "Manual camera (auto resumes shortly; Tab to toggle)";
        else if (!Auto) _caption.Text = "Manual camera (Tab: auto)";
    }

    void Direct(float dt)
    {
        _shotTime += dt;
        _overviewIn -= dt;
        var best = _spots.OrderByDescending(s => s.Interest).FirstOrDefault();
        // Cut to something much hotter at once; otherwise hold each shot for a while.
        bool cut = _focus == null || _shotTime > 9f || (best != null && best != _focus && best.Interest > (_focus?.Interest ?? 0f) * 2.5f + 5f);
        if (_overviewIn <= 0f && (best == null || best.Interest < 15f))
        {
            _overview = true;
            _overviewIn = 50f;
            _shotTime = 0f;
            _focus = null;
            _caption.Text = Standing();
        }
        else if (cut && best != null && (!_overview || _shotTime > 6f))
        {
            _overview = false;
            _focus = best;
            _shotTime = 0f;
            _caption.Text = best.Caption;
        }
        else if (cut && best == null && _shotTime > 9f)
        {
            // Nothing happening: visit a kingdom at work.
            var k = M.Kingdoms[(int)(M.Time / 20f) % 2];
            _focus = new Hotspot { Pos = k.Home + new SVec(4f, 4f), Interest = 1f, Caption = $"{Side(k.Team)} kingdom at work: {M.Population(k)} subjects, Age {k.Age}" };
            _shotTime = 0f;
            _caption.Text = _focus.Caption;
        }

        Vector2 target;
        float zoom;
        if (_overview || _focus == null)
        {
            var a = M.Kingdoms[0].Home;
            var b = M.Kingdoms[1].Home;
            target = MatchView.Px((a + b) / 2f);
            zoom = Math.Clamp(1100f / MathF.Max(400f, SVec.Distance(a, b) * 16f), 0.55f, 1.2f);
        }
        else
        {
            target = MatchView.Px(_focus.Pos);
            zoom = 2.6f;
        }
        var cam = _s.Cam;
        cam.Position = cam.Position.Lerp(target, 1f - MathF.Exp(-dt * 1.6f));
        float z = Mathf.Lerp(cam.Zoom.X, zoom, 1f - MathF.Exp(-dt * 1.2f));
        cam.Zoom = new Vector2(z, z);
    }

    string Standing()
    {
        float a = M.Score(M.Kingdoms[0]), b = M.Score(M.Kingdoms[1]);
        string lead = MathF.Abs(a - b) < 0.05f * (a + b) ? "Neck and neck" : a > b ? "Blue leads" : "Red leads";
        return $"{lead}: {a:0} to {b:0}";
    }

    void UpdateBar()
    {
        for (int t = 0; t < 2; t++)
        {
            var k = M.Kingdoms[t];
            var ai = M.Ai[t];
            string name = ai != null ? $"{Side(t)} · The {ai.Personality}" : $"{Side(t)} · {k.Name}";
            var spire = M.BuildingsOf(k).FirstOrDefault(b => b.Type == BuildingType.LumenSpire);
            _side[t].Text = $"{name}   Age {k.Age}   Pop {M.Population(k)}   Score {M.Score(k):0}\n" +
                            $"Grain {k.Grain:0}  Timber {k.Timber:0}  Stone {k.Stone:0}  Lumen {k.Lumen:0}   Captured {k.MindsCaptured}  Lost {k.Lost}" +
                            (spire != null ? $"   Spire {spire.Charge / Defs.SpireGoal * 100f:0}%" : "");
        }
        float tm = M.Time;
        _clock.Text = $"{(int)tm / 60:00}:{(int)tm % 60:00} / {(int)M.Length / 60:00}:00{(_s.Paused ? "\nPAUSED" : $"\nx{_s.Speed:0}")}";
    }

    void ReadAlerts()
    {
        for (; _seenAlerts < M.Alerts.Count; _seenAlerts++)
        {
            var a = M.Alerts[_seenAlerts];
            var t = a.Text;
            var col = t.StartsWith(M.Kingdoms[0].Name) ? MatchView.TeamColors[0].Lightened(0.4f) : t.StartsWith(M.Kingdoms[1].Name) ? MatchView.TeamColors[1].Lightened(0.4f) : Ui.Accent;
            bool big = t.Contains(" entered Age ") || t.Contains(" fused ") || t.Contains("Spire is") || t.Contains("Sealed One") || t.Contains("ruler has been subdued")
                       || t.Contains("Legendary") || t.Contains("upgraded a Manor") == false && t.Contains("finished researching");
            if (big) Banner(Rename(t), col);
            if (a.At is { } at) Add(at, big ? 25f : 2f, Rename(t));
        }
    }

    // Kingdom names in alerts read as Blue/Red on screen.
    string Rename(string text) => text.Replace(M.Kingdoms[0].Name, "Blue").Replace(M.Kingdoms[1].Name, "Red");

    public void ShowEnd(Control host)
    {
        var p = Ui.Panel(null, 20);
        var v = Ui.VBox(10);
        string title = M.Winner == -2 ? "DRAW" : $"{Side(M.Winner)} WINS";
        v.AddChild(Ui.Label(title, 34, M.Winner >= 0 ? MatchView.TeamColors[M.Winner].Lightened(0.4f) : Ui.Accent));
        v.AddChild(Ui.Label($"{M.WinReason}.   Score {M.Score(M.Kingdoms[0]):0} (Blue) vs {M.Score(M.Kingdoms[1]):0} (Red)", 15));
        for (int t = 0; t < 2; t++)
            if (M.Ai[t] is { } ai)
            {
                v.AddChild(Ui.Header($"{Side(t)} · The {ai.Personality} reports"));
                v.AddChild(Ui.Label(Rename(ai.WarReport(M.Winner == t)), 12, null, true));
            }
        var again = Ui.HBox(8);
        again.AddChild(Ui.Button("Watch another match", () => { MatchScene.Seed = 0; _s.GetTree().ReloadCurrentScene(); }, true));
        again.AddChild(Ui.Button("Back to the menu", () => _s.ReturnToMenu(), true));
        v.AddChild(again);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(720, 560), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        v.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(v);
        p.AddChild(scroll);
        host.AddChild(p);
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center, LayoutPresetMode.KeepSize);
    }
}
