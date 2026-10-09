using FusionKingdoms.Sim;
using Godot;

namespace FusionKingdoms;

public partial class Hud : Control
{
    readonly MatchScene _s;
    Match M => _s.M;
    Kingdom Me => _s.Me;

    Label _res = null!, _clock = null!, _toast = null!;
    readonly Dictionary<string, Label> _stat = new();
    TextureRect _phase = null!;
    VBoxContainer _alerts = null!;
    PanelContainer _selPanel = null!, _buildMenu = null!, _help = null!;
    VBoxContainer _sel = null!;
    readonly List<Action> _refreshers = new();
    readonly List<Action> _persistent = new();
    int _seenAlerts;
    float _toastTime, _refreshTimer, _overviewTimer;
    bool _dirty = true;
    string _selKey = "";

    public Hud(MatchScene s)
    {
        _s = s;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Ready()
    {
        BuildTopBar();

        _alerts = Ui.VBox(3);
        _alerts.Position = new Vector2(10, 46);
        _alerts.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_alerts);

        _toast = Ui.Label("", 14, Colors.White);
        _toast.SetAnchorsPreset(LayoutPreset.CenterTop);
        _toast.Position = new Vector2(-150, 48);
        _toast.Size = new Vector2(300, 20);
        _toast.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_toast);

        _selPanel = Ui.Panel();
        _selPanel.SetAnchorsPreset(LayoutPreset.BottomLeft);
        _selPanel.GrowVertical = GrowDirection.Begin;
        _selPanel.Position = new Vector2(8, -8);
        _selPanel.CustomMinimumSize = new Vector2(400, 0);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(400, 330), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _sel = Ui.VBox();
        _sel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_sel);
        _selPanel.AddChild(scroll);
        AddChild(_selPanel);
        _selPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft, LayoutPresetMode.KeepSize, 8);

        var corner = Ui.VBox(4);
        var minimap = new Minimap(_s) { CustomMinimumSize = new Vector2(256, 192) };
        var row = Ui.HBox();
        row.AddChild(Ui.Button("Build (B)", ToggleBuild, true));
        row.AddChild(Ui.Button("Help (F1)", ToggleHelp, true));
        corner.AddChild(row);
        corner.AddChild(minimap);
        AddChild(corner);
        corner.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight, LayoutPresetMode.KeepSize, 8);

        _buildMenu = BuildMenu();
        _buildMenu.Visible = false;
        AddChild(_buildMenu);
        _buildMenu.SetAnchorsPreset(LayoutPreset.CenterRight);
        _buildMenu.OffsetLeft = -450; _buildMenu.OffsetRight = -8;
        _buildMenu.OffsetTop = -330; _buildMenu.OffsetBottom = 330;

        _help = HelpPanel();
        _help.Visible = false;
        AddChild(_help);
        _help.SetAnchorsAndOffsetsPreset(LayoutPreset.Center, LayoutPresetMode.KeepSize);

        Toast("Your Crown awaits. Press F1 for controls.");
        // First-match hints (not in networked or campaign games that start mid-way, and not for the autoplay demo).
        if (OS.GetEnvironment("FK_AUTOPLAY") != "1")
        {
            var tut = new Tutorial(_s);
            AddChild(tut);
            tut.AnchorLeft = 0.5f; tut.AnchorRight = 0.5f; tut.AnchorTop = 0f; tut.AnchorBottom = 0f;
            tut.OffsetLeft = -250; tut.OffsetRight = 250; tut.OffsetTop = 60;
        }
    }

    void BuildTopBar()
    {
        var bar = Ui.Panel(null, 6);
        bar.SetAnchorsPreset(LayoutPreset.TopWide);
        bar.CustomMinimumSize = new Vector2(0, 36);
        var h = Ui.HBox(14);
        foreach (var (icon, tip) in new[] { ("grain", "Grain"), ("timber", "Timber"), ("stone", "Stone"), ("lumen", "Lumen"), ("gold", "Gold"), ("pop", "Population / cap"), ("crown", "Age") })
        {
            var cell = Ui.HBox(4);
            cell.TooltipText = tip;
            cell.MouseFilter = MouseFilterEnum.Pass;
            cell.AddChild(Ui.Icon(icon, 24));
            var l = Ui.Label("", 15);
            cell.AddChild(l);
            _stat[icon] = l;
            h.AddChild(cell);
        }
        _res = Ui.Label("", 14, new Color("ff9090"));
        _res.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        h.AddChild(_res);
        _phase = Ui.Icon("sun", 24);
        h.AddChild(_phase);
        _clock = Ui.Label("", 14);
        h.AddChild(_clock);
        foreach (var (txt, sp) in new[] { ("||", 0f), ("1x", 1f), ("2x", 2f), ("4x", 4f) })
            h.AddChild(Ui.Button(txt, () => _s.SetSpeed(sp)));
        h.AddChild(Ui.Button("Menu", () => _s.ReturnToMenu()));
        bar.AddChild(h);
        AddChild(bar);
    }

    public void Dirty() => _dirty = true;

    public void Toast(string text)
    {
        _toast.Text = text;
        _toastTime = 3f;
    }

    public void ToggleBuild() => _buildMenu.Visible = !_buildMenu.Visible;
    public void ToggleHelp() => _help.Visible = !_help.Visible;

    public override void _Process(double delta)
    {
        float t = M.Time;
        int pop = M.Population(Me), cap = M.PopulationCap(Me);
        _stat["grain"].Text = $"{Me.Grain:0}";
        _stat["grain"].Modulate = M.W.Starving[Me.Team] ? new Color("ff9090") : Colors.White;
        _stat["timber"].Text = $"{Me.Timber:0}";
        _stat["stone"].Text = $"{Me.Stone:0}";
        _stat["lumen"].Text = $"{Me.Lumen:0}";
        _stat["gold"].Text = $"{Me.Gold:0}";
        _stat["pop"].Text = $"{pop}/{cap}";
        _stat["crown"].Text = $"Age {Me.Age}: {Defs.AgeName(Me.Age)}";
        _res.Text = M.W.Starving[Me.Team] ? "STARVING" : "";
        _phase.Texture = Skin.Tex(M.W.IsNight ? "icon_moon" : "icon_sun");
        string phase = M.W.EclipseTimer > 0 ? "Eclipse" : M.W.IsNight ? "Night" : "Day";
        _clock.Text = $"{(int)t / 60:00}:{(int)t % 60:00} / {(int)M.Length / 60:00}:{(int)M.Length % 60:00}   {phase}{(_s.Paused ? "   PAUSED" : "")}" +
                      (M.Royal[Me.Team] != null ? $"   ROYAL COMMAND {M.RoyalTimeLeft(Me.Team):0.0}s (WASD, Space tussle, E gather/drop, Q carry, F steal)" : "");

        _toastTime -= (float)delta;
        _toast.Modulate = new Color(1, 1, 1, Math.Clamp(_toastTime, 0f, 1f));

        for (; _seenAlerts < M.Alerts.Count; _seenAlerts++)
        {
            var a = M.Alerts[_seenAlerts];
            if (a.Team != -1 && a.Team != Me.Team) continue;
            var b = new Button { Text = $"{(int)a.Time / 60:00}:{(int)a.Time % 60:00}  {a.Text}", Flat = true, Alignment = HorizontalAlignment.Left };
            b.AddThemeFontSizeOverride("font_size", 12);
            if (a.At is { } at) b.Pressed += () => _s.LookAt(at);
            _alerts.AddChild(b);
            if (_alerts.GetChildCount() > 7) _alerts.GetChild(0).QueueFree();
        }
        foreach (var c in _alerts.GetChildren())
            if (c is Button btn) btn.Modulate = new Color(1, 1, 1, 0.85f);

        // The overview's roster changes as the kingdom grows; rebuild it now and then.
        if (_s.View.Selected.Count == 0 && _s.View.SelectedBuilding == null && (_overviewTimer -= (float)delta) <= 0f)
        {
            _overviewTimer = 3f;
            _dirty = true;
        }
        string key = SelectionKey();
        if (_dirty || key != _selKey)
        {
            _selKey = key;
            _dirty = false;
            RebuildSelection();
        }
        _refreshTimer -= (float)delta;
        if (_refreshTimer <= 0f)
        {
            _refreshTimer = 0.25f;
            foreach (var r in _refreshers) r();
            foreach (var r in _persistent) r();
        }
    }

    string SelectionKey() =>
        _s.View.SelectedBuilding is { } b ? $"b{b.Id}:{b.Type}" : "u" + string.Join(",", _s.View.Selected.Select(u => u.Id).OrderBy(i => i));

    // ---------- selection ----------

    void RebuildSelection()
    {
        Ui.Clear(_sel);
        _refreshers.Clear();
        var sel = _s.View.Selected.Where(u => u.Active).ToList();
        if (_s.View.SelectedBuilding is { } b) BuildingPanel(b);
        else if (sel.Count == 1) UnitPanel(sel[0]);
        else if (sel.Count > 1) GroupPanel(sel);
        else Overview();
    }

    void Add(Control c) => _sel.AddChild(c);

    Label Live(Func<string> text, int size = 12, Color? color = null)
    {
        var l = Ui.Label(text(), size, color, true);
        l.SizeFlagsHorizontal = SizeFlags.ExpandFill;   // a wrapping label in a row otherwise collapses to one letter wide
        _refreshers.Add(() => l.Text = text());
        return l;
    }

    // Nothing selected: the whole kingdom at a glance, and one-click ways to grab any group of subjects.
    void Overview()
    {
        Add(Ui.Header(Me.Name));
        Add(Live(() => $"Score {M.Score(Me):0}  vs  {M.Score(M.Enemy(Me)):0}    Captives {Me.Captives.Count}    Library {Me.Library.Count} minds", 12));
        var quick = Ui.HBox(4);
        quick.AddChild(Ui.Button("All (Ctrl+A)", () => _s.SelectWhere(_ => true, false), true));
        quick.AddChild(Ui.Button("Idle (.)", () => _s.SelectIdle(false), true));
        quick.AddChild(Ui.Button("Keep (H)", () => _s.LookAt(Me.Home), true));
        Add(quick);

        Add(Ui.Label("Jobs: click to select everyone doing it", 11, Ui.Muted));
        var jobs = new GridContainer { Columns = 2 };
        jobs.AddThemeConstantOverride("h_separation", 4);
        jobs.AddThemeConstantOverride("v_separation", 4);
        foreach (var (label, icon, pred) in JobFilters())
        {
            var b = Ui.Button("", () => _s.SelectWhere(pred, Input.IsKeyPressed(Key.Shift)), true);
            b.Icon = Skin.Tex($"icon_{icon}");
            b.AddThemeConstantOverride("icon_max_width", 18);
            b.Alignment = HorizontalAlignment.Left;
            _refreshers.Add(() => b.Text = $"{label}: {M.Subjects(Me).Count(pred)}");
            b.Text = $"{label}: {M.Subjects(Me).Count(pred)}";
            jobs.AddChild(b);
        }
        Add(jobs);

        Add(Ui.Label("Classes: click to select all of one (Shift adds). Double-click a subject does the same on screen.", 11, Ui.Muted, true));
        var classes = new HFlowContainer();
        classes.AddThemeConstantOverride("h_separation", 4);
        classes.AddThemeConstantOverride("v_separation", 4);
        foreach (var g in M.Subjects(Me).GroupBy(u => Classes.Of(u.Mind).Name).OrderByDescending(g => g.Count()))
        {
            string cls = g.Key;
            classes.AddChild(Ui.Button($"{cls} x{g.Count()}", () => _s.SelectWhere(u => Classes.Of(u.Mind).Name == cls, Input.IsKeyPressed(Key.Shift))));
        }
        Add(classes);
        Add(Ui.Label("Right-click with subjects selected: a crop/tree/rock = gather, a site = build, an enemy = capture, an enemy store = raid, ground = move (Shift: guard). " +
                     "G guard, C capture, Z hold, X stop, F Free Will, Ctrl+1-9 save a group, 1-9 recall it.", 11, Ui.Muted, true));
    }

    static readonly (string label, string icon, Func<Unit, bool> pred)[] JobFiltersList =
    [
        ("Grain", "grain", u => u.Order == OrderKind.Gather && u.OrderRes == ResourceType.Food),
        ("Timber", "timber", u => u.Order == OrderKind.Gather && u.OrderRes == ResourceType.Timber),
        ("Stone", "stone", u => u.Order == OrderKind.Gather && u.OrderRes == ResourceType.Stone),
        ("Lumen", "lumen", u => u.Order == OrderKind.Gather && u.OrderRes == ResourceType.Lumen),
        ("Building", "crown", u => u.Order == OrderKind.Build),
        ("Guarding", "star", u => u.Order is OrderKind.Guard or OrderKind.Hold),
        ("Raiding", "gold", u => u.Order is OrderKind.Raid or OrderKind.Sabotage or OrderKind.Capture),
        ("Idle", "moon", u => u.Order == OrderKind.None),
    ];

    static IEnumerable<(string, string, Func<Unit, bool>)> JobFilters() => JobFiltersList;

    static string OrderText(Unit u) => u.Order switch
    {
        OrderKind.None => u.FreeWill ? "No orders: following its own mind" : "Idle: waiting for orders",
        OrderKind.Move => "Moving",
        OrderKind.Gather => $"Gathering {(u.OrderRes == ResourceType.Food ? "grain" : u.OrderRes.ToString().ToLowerInvariant())}",
        OrderKind.Build => "Building",
        OrderKind.Capture => "Capturing",
        OrderKind.Raid => "Raiding",
        OrderKind.Sabotage => "Sabotaging",
        OrderKind.Guard => "Guarding a spot",
        OrderKind.Hold => "Holding position",
        OrderKind.Heal => "Tending a friend",
        _ => "",
    };

    // Buttons that work on whatever is selected.
    void CommandBar(List<Unit> units)
    {
        var row = Ui.HBox(4);
        foreach (var (icon, res, tip) in new[] { ("grain", ResourceType.Food, "Gather grain"), ("timber", ResourceType.Timber, "Gather timber"),
                     ("stone", ResourceType.Stone, "Gather stone"), ("lumen", ResourceType.Lumen, "Gather Lumen") })
        {
            var b = Ui.Button("", () => _s.GatherNearest(res), true, $"{tip} at the nearest spot");
            b.Icon = Skin.Tex($"icon_{icon}");
            b.AddThemeConstantOverride("icon_max_width", 18);
            b.IconAlignment = HorizontalAlignment.Center;
            row.AddChild(b);
        }
        Add(row);
        var row2 = Ui.HBox(4);
        row2.AddChild(Ui.Button("Guard (G)", () => { _s.View.Pending = OrderKind.Guard; Toast("Click the spot to guard"); }, true, "Hold a spot and catch anyone who comes near"));
        row2.AddChild(Ui.Button("Capture (C)", () => { _s.View.Pending = OrderKind.Capture; Toast("Click an enemy or a creature"); }, true, "Tussle someone down and carry them home"));
        row2.AddChild(Ui.Button("Hold (Z)", () => _s.Stop(OrderKind.Hold), true, "Stay put and fight back"));
        row2.AddChild(Ui.Button("Stop (X)", () => _s.Stop(OrderKind.None), true, "Drop all orders"));
        Add(row2);
        var row3 = Ui.HBox(4);
        var fw = Ui.Button("", _s.ToggleFreeWill, true, "Free Will on: idle subjects follow their own minds. Off: they wait for you.");
        _refreshers.Add(() => fw.Text = units.All(u => u.FreeWill) ? "Free Will: ON (F)" : units.Any(u => u.FreeWill) ? "Free Will: mixed (F)" : "Free Will: OFF (F)");
        fw.Text = units.All(u => u.FreeWill) ? "Free Will: ON (F)" : "Free Will: OFF (F)";
        row3.AddChild(fw);
        foreach (var (txt, st) in new[] { ("Careful", -1f), ("Normal", 0f), ("Bold", 1f) })
            row3.AddChild(Ui.Button(txt, () => _s.Send(C.Stance(units, st)), false, "How readily they pick fights when following their own minds"));
        Add(row3);
    }

    static string DoingText(Unit u) =>
        !u.Active ? "gone" : u.Subdued ? (u.CarriedBy >= 0 ? "being carried off!" : "subdued") :
        u.TussleTarget >= 0 ? "tussling" : u.CarryingUnit >= 0 ? "carrying a captive" :
        ((Act)u.LastAction) switch
        {
            Act.Gather => "gathering", Act.Deposit => "dropping off", Act.Build => "building", Act.Steal => "stealing",
            Act.Sabotage => "sabotaging", Act.Rescue => "helping an ally", Act.Carry => "lifting", Act.Tussle => "looking for a fight",
            _ => u.LastMove == 0 ? "idle" : "on the move",
        };

    void UnitPanel(Unit u)
    {
        bool mine = u.Team == Me.Team;
        string who = u.IsRuler ? (mine ? "Your Ruler" : "Enemy Ruler") : u.Team == 2 ? u.Mind?.Name ?? u.Species.ToString() : Classes.Label(u.Mind);
        string side = u.Team == Me.Team ? "yours" : u.Team == 2 ? "wild" : "enemy";
        Add(Ui.Label($"{who}  ({side})", 16, MatchView.TeamColors[Math.Min(u.Team, 2)].Lightened(0.35f)));
        if (!u.IsRuler && u.Team < 2) Add(Ui.Label(Classes.Of(u.Mind).Blurb, 11, Ui.Muted, true));
        if (!u.IsRuler && u.Team < 2) Add(Live(() => Classes.StatLine(u), 12, new Color("f6c544")));
        Add(Live(() => $"Will {u.Will:0}/{u.Body.Will:0}   Carrying {(u.CarryAmount > 0 ? $"{u.CarryAmount:0.0} {u.CarryType}" : "nothing")}   {DoingText(u)}" +
                       (mine && !u.IsRuler ? $"\n{OrderText(u)}" : "")));
        if (u.IsRuler && mine)
            Add(Ui.Label("Right-click to move your ruler. If enemies carry it into their Keep or Dungeon, you lose.", 11, Ui.Muted, true));
        if (mine && !u.IsRuler)
        {
            CommandBar([u]);
            Add(Ui.Button($"Royal Command (R, {Defs.RoyalCommandCost:0} Lumen): steer it yourself", _s.TryRoyal));
        }
        if (u.Mind != null && (mine || u.Team == 2 || Me.Intel.ContainsKey(u.Mind.Id)))
        {
            var card = new TrialCard();
            Add(card);
            card.Show(u.Mind);
        }
        else if (u.Mind != null) Add(Ui.Label("Capture and Interrogate it to learn how this mind thinks.", 11, Ui.Muted, true));
    }

    void GroupPanel(List<Unit> units)
    {
        var mine = units.Where(u => u.Team == Me.Team && !u.IsRuler).ToList();
        Add(Ui.Header($"{units.Count} selected"));
        // Narrow the selection down to one class or one job.
        var classes = new HFlowContainer();
        classes.AddThemeConstantOverride("h_separation", 4);
        classes.AddThemeConstantOverride("v_separation", 4);
        foreach (var g in mine.GroupBy(u => Classes.Of(u.Mind).Name).OrderByDescending(g => g.Count()))
        {
            var only = g.ToList();
            classes.AddChild(Ui.Button($"{g.Key} x{g.Count()}", () =>
            {
                _s.View.Selected.Clear();
                foreach (var u in only) _s.View.Selected.Add(u);
                Dirty();
            }, false, "Keep only these selected"));
        }
        Add(classes);
        Add(Live(() => string.Join("   ", mine.GroupBy(OrderText).OrderByDescending(g => g.Count()).Take(4).Select(g => $"{g.Count()} {g.Key.ToLowerInvariant()}")), 11, Ui.Muted));
        if (mine.Count > 0) CommandBar(mine);
        Add(Ui.Label("Right-click: crop/tree/rock = gather (they spread out), site = build, enemy = capture, enemy store = raid, ground = move, Shift = guard.", 11, Ui.Muted, true));
    }

    // ---------- buildings ----------

    void BuildingPanel(Building b)
    {
        bool mine = b.Team == Me.Team;
        string name = b.Type == BuildingType.Den ? $"{M.Tribes.FirstOrDefault(t => t.Den == b)?.Name ?? "Wild"} den" : Defs.NameOf(b.Type);
        Add(Ui.Label($"{name}  ({(mine ? "yours" : b.Team == 2 ? "wild" : "enemy")})", 15, MatchView.TeamColors[Math.Min(b.Team, 2)].Lightened(0.3f)));
        if (b.Type is not (BuildingType.Keep or BuildingType.Den or BuildingType.BuildSite))
            Add(Ui.Label(Defs.Of(b.Type).Description, 11, Ui.Muted, true));
        if (mine && Defs.Staffing.TryGetValue(b.Type, out var staffing))
            Add(Live(() => $"Staff: subjects that are {string.Join(" / ", staffing.traits.Take(3))} standing here make it better ({staffing.effect}). " +
                           (M.Staff(b) > 0 ? $"Staffed by {M.Staff(b)}: bonus active!" : "Not staffed."), 11, Ui.Muted));
        Add(Live(() => b.DisabledTimer > 0 ? $"SABOTAGED: out of action for {b.DisabledTimer:0}s" : b.SabotageProgress > 0 ? $"Being sabotaged! {b.SabotageProgress:0.0}/{Rules.SabotageTime:0}s" : "", 12, new Color("ff8060")));
        if (b.Type == BuildingType.AncientAltar)
        {
            Add(Ui.Label("An Ancient Altar from before the kingdoms. Whoever has more subjects around it fuses at half cost and foresees for free.", 12, null, true));
            Add(Live(() => M.AltarHolder(b) switch { -1 => "Nobody holds it.", var t when t == Me.Team => "You hold it!", _ => "Your rival holds it." }));
            return;
        }
        if (b.Type == BuildingType.Merchant)
        {
            Add(Ui.Label($"A Wandering Merchant. Bring a subject close and pay {Defs.MerchantPrice:0} gold to buy the mind on offer.", 12, null, true));
            if (M.MerchantMind is { } offer)
            {
                var card = new TrialCard();
                Add(card);
                card.Show(offer);
                Add(Ui.Button($"Buy for {Defs.MerchantPrice:0} gold", () => _s.Send(new Command { Type = CmdType.BuyMerchant }, _ => Dirty())));
            }
            return;
        }
        if (!mine) return;
        UpgradeRow(b);
        switch (b.Type)
        {
            case BuildingType.Market: MarketPanel(); break;
            case BuildingType.BuildSite: SitePanel(b); break;
            case BuildingType.Keep: KeepPanel(); break;
            case BuildingType.Cradle: CradlePanel(); break;
            case BuildingType.FusionAltar: AltarPanel(); break;
            case BuildingType.TrainingGrounds: GroundsPanel(); break;
            case BuildingType.MutationPit: PitPanel(); break;
            case BuildingType.Dungeon: CaptivesPanel(); break;
            case BuildingType.LumenSpire:
                Add(Live(() => $"Charge {b.Charge:0}/{Defs.SpireGoal:0} Lumen, {b.Stored[(int)ResourceType.Lumen]:0} waiting in the reservoir (charges {Defs.SpireRate:0.#}/s while it works). " +
                               "Sabotage drains 15% of the charge."));
                Add(Ui.Button("Feed all stored Lumen", () => _s.Send(C.Simple(CmdType.FeedSpire))));
                break;
            case BuildingType.Camp:
                Add(Live(() => $"{M.Subjects(Me).Count(u => u.Station == b)} subjects stationed here."));
                Add(Ui.Label("Select subjects and right-click this camp to station them. Builders gather timber and stone, Hoarders Lumen, Farmers grain.", 11, Ui.Muted, true));
                Add(Ui.Button("Release everyone stationed here", () => _s.Send(C.Station(M.Subjects(Me).Where(u => u.Station == b), null))));
                break;
        }
    }

    void MarketPanel()
    {
        Add(Ui.Label("Trade at fixed rates. Gold buys materials, pays for Age 3 and buys minds from the Wandering Merchant.", 11, Ui.Muted, true));
        var grid = new GridContainer { Columns = 2 };
        for (int i = 0; i < Defs.FirstBarter; i++)
        {
            var (give, ga, get, gt) = Defs.Trades[i];
            int idx = i;
            grid.AddChild(Ui.Button($"{ga:0} {give} -> {gt:0} {get}", () => _s.Send(new Command { Type = CmdType.Trade, I = idx, F = 1 }), true));
        }
        Add(grid);
    }

    void UpgradeRow(Building b)
    {
        if (b.Type != BuildingType.House && b.Level > 1) Add(Ui.Label($"Level {b.Level}", 11, Ui.Accent));
        var up = Progress.UpgradeFor(b);
        if (up == null) return;
        var cost = string.Join(", ", new[] { (up.Timber, "timber"), (up.Stone, "stone"), (up.Lumen, "Lumen") }.Where(c => c.Item1 > 0).Select(c => $"{c.Item1:0} {c.Item2}"));
        var btn = Ui.Button($"{up.Name} ({cost}{(up.Age > 1 ? $", Age {up.Age}" : "")})", () => _s.Send(new Command { Type = CmdType.Upgrade, Target = b.Id }, _ => Dirty()), false, up.Description);
        _refreshers.Add(() =>
        {
            btn.Disabled = b.UpgradeTimer > 0f || Me.Age < up.Age;
            if (b.UpgradeTimer > 0f) btn.Text = $"Upgrading... {b.UpgradeTimer:0}s";
        });
        Add(btn);
        Add(Ui.Label(up.Description, 11, Ui.Muted, true));
    }

    void SitePanel(Building b)
    {
        var def = Defs.Of(b.SiteFor);
        Add(Ui.Label($"Building a {def.Name}. {def.Description}", 12, null, true));
        Add(Live(() => $"Construction {b.BuildProgress:0}/{def.Work:0}.  {M.W.Units.Count(u => u.Active && u.Order == OrderKind.Build && u.OrderTarget == b.Id)} at work. " +
                       "Select subjects and right-click the site to build; Builder classes work fastest."));
        Add(Live(() => $"Or rush it now for {M.RushCost(b):0} timber/stone from storage."));
        Add(Ui.Button("Rush construction", () => _s.Send(C.OnBuilding(CmdType.Rush, b), _ => Dirty())));
    }

    void KeepPanel()
    {
        Add(Live(() => $"Age {Me.Age}: {Defs.AgeName(Me.Age)}.  Score {M.Score(Me):0}  vs  {M.Score(M.Enemy(Me)):0}"));
        if (Me.Age < 3)
        {
            var (s, t, l, g) = Defs.AgeCost(Me.Age + 1);
            string unlock = Me.Age == 1 ? "Dungeon, Mutation Pit, Market, Infirmary, Essence Infusion" : "Mutation Pit level 3, Lumen Spire, Essence Removal";
            Add(Ui.Button($"Advance to Age {Me.Age + 1} ({Ui.Res(timber: t, stone: s, lumen: l)}{(g > 0 ? $", {g:0} gold" : "")}): unlocks {unlock}",
                () => _s.Send(C.Simple(CmdType.AgeUp), _ => Dirty())));
        }
        // Research: one topic at a time, permanent bonuses for the whole kingdom.
        Add(Ui.Header("Research"));
        Add(Live(() => Me.Researching is { } r ? $"Researching {Progress.Tech(r.Id)!.Name}: {r.Remaining:0}s left" :
                       Me.Techs.Count > 0 ? "Known: " + string.Join(", ", Me.Techs.Select(t => Progress.Tech(t)!.Name)) : "Nothing researched yet.", 12));
        var techs = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        techs.AddThemeConstantOverride("h_separation", 4);
        techs.AddThemeConstantOverride("v_separation", 4);
        foreach (var t in Progress.Techs)
        {
            var tech = t;
            var cost = string.Join(", ", new[] { (t.Timber, "timber"), (t.Stone, "stone"), (t.Lumen, "Lumen"), (t.Gold, "gold") }.Where(c => c.Item1 > 0).Select(c => $"{c.Item1:0} {c.Item2}"));
            var b = Ui.Button($"{t.Name}{(t.Age > 1 ? $" (Age {t.Age})" : "")}", () => _s.Send(new Command { Type = CmdType.Research, S = tech.Id }, _ => Dirty()), true,
                $"{t.Description}\nCosts {cost}, takes {t.Time:0}s");
            b.ClipText = true;
            _refreshers.Add(() =>
            {
                b.Disabled = Me.Has(tech.Id) || Me.Researching != null || Me.Age < tech.Age;
                b.Text = Me.Has(tech.Id) ? $"✓ {tech.Name}" : $"{tech.Name}{(tech.Age > Me.Age ? $" (Age {tech.Age})" : "")}";
            });
            techs.AddChild(b);
        }
        Add(techs);
        Add(Ui.Label("Barter at the Keep (poor rates, always available):", 11, Ui.Muted));
        var barter = Ui.HBox(4);
        for (int i = Defs.FirstBarter; i < Defs.Trades.Length; i++)
        {
            var (give, ga, get, gt) = Defs.Trades[i];
            int idx = i;
            barter.AddChild(Ui.Button($"{ga:0} {give} -> {gt:0} {get}", () => _s.Send(new Command { Type = CmdType.Trade, I = idx, F = 1 }), true));
        }
        Add(barter);
        if (M.Working(Me, BuildingType.Dungeon) == null)
        {
            Add(Ui.Label("Captives are held here until you build a Dungeon. They escape after a minute.", 11, Ui.Muted, true));
            CaptivesPanel();
        }
    }

    void CradlePanel()
    {
        // Queue: click an entry to cancel it (refunded).
        var queue = new HFlowContainer();
        queue.AddThemeConstantOverride("h_separation", 4);
        string queueKey = "";
        void Refresh()
        {
            string k = string.Join(",", Me.Queue.Select(o => o.Mind.Id)) + (Me.Queue.Count > 0 ? $"{(int)Me.Queue[0].Remaining}" : "");
            if (k == queueKey) return;
            queueKey = k;
            Ui.Clear(queue);
            if (Me.Queue.Count == 0) queue.AddChild(Ui.Label("Queue empty.", 12, Ui.Muted));
            for (int i = 0; i < Me.Queue.Count; i++)
            {
                int idx = i;
                var o = Me.Queue[i];
                queue.AddChild(Ui.Button(i == 0 ? $"{o.Mind.Display} {o.Remaining:0}s ✕" : $"{o.Mind.Display} ✕",
                    () => _s.Send(new Command { Type = CmdType.CancelProduce, I = idx }), false, "Cancel (refunded)"));
            }
        }
        Refresh();
        _refreshers.Add(Refresh);
        Add(queue);
        Add(Live(() => M.Population(Me) >= M.PopulationCap(Me) ? "Population cap reached: build Houses or a Manor." : "", 11, new Color("ff9090")));

        // What new subjects do first, and where they go.
        var jobRow = Ui.HBox(4);
        jobRow.AddChild(Ui.Label("Newborns:", 12));
        var job = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var j in new[] { "Free Will", "Farm grain", "Cut timber", "Quarry stone", "Gather Lumen", "Guard the rally point" }) job.AddItem(j);
        job.Select((int)Me.Job);
        job.ItemSelected += i => _s.Send(new Command { Type = CmdType.SetRally, I = (int)i, B = Me.Rally.HasValue, X = Me.Rally?.X ?? 0, Y = Me.Rally?.Y ?? 0 });
        jobRow.AddChild(job);
        Add(jobRow);
        Add(Live(() => Me.Rally is { } r ? $"Rally point set. Right-click the map to move it." : "Right-click the map to set a rally point for new subjects.", 11, Ui.Muted));
        Add(Ui.Button("Clear rally point", () => _s.Send(new Command { Type = CmdType.SetRally, I = -1, B = false })));
        var repeat = Ui.Label("", 11, Ui.Accent, true);
        _refreshers.Add(() => repeat.Text = Me.Repeat is { } rm ? $"Repeating: {rm.Display} whenever the queue is empty." : "");
        Add(repeat);
        Add(Ui.Label("Produce a subject from your Library of Minds (Shift-click: keep producing it):", 12));
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var m in Me.Library.OrderBy(m => Classes.Of(m).Name).ThenByDescending(m => Classes.Stars(m)))
        {
            var btn = Ui.Button($"{m.Display}  g{m.Generation}",
                () =>
                {
                    // Shift-click: keep producing this one.
                    if (Input.IsKeyPressed(Key.Shift)) _s.Send(new Command { Type = CmdType.SetRepeat, I = Me.Repeat == m ? -1 : m.NetId });
                    else _s.Send(C.Produce(m));
                }, true,
                $"Costs {Ui.Res(Defs.GrainCost(m), lumen: Defs.LumenCost(m))}\n" + string.Join(", ", m.Traits.Select(t => t.Name)) +
                "\nBest at: " + string.Join(", ", m.TopSkills().Select(s => $"{s.name} {s.pct:0}%")) + "\n" + m.Lineage);
            btn.ClipText = true;
            btn.CustomMinimumSize = new Vector2(150, 0);
            btn.Modulate = m.AuraColor().Lightened(0.4f);
            grid.AddChild(btn);
        }
        Add(grid);
    }

    List<Unit> Donors() => M.Subjects(Me).Where(u => M.CanUseAsDonor(Me, u)).OrderBy(u => u.Mind!.Name).ToList();

    static string UnitLabel(Unit u) => $"{Classes.Label(u.Mind)} #{u.Id}";

    void AltarPanel()
    {
        var donors = Donors();
        if (donors.Count < 2) { Add(Ui.Label("You need at least two subjects to fuse.", 12)); return; }
        var a = new OptionButton();
        var b = new OptionButton();
        foreach (var u in donors) { a.AddItem(UnitLabel(u), u.Id); b.AddItem(UnitLabel(u), u.Id); }
        a.Select(0);
        b.Select(1);
        var ratio = new HSlider { MinValue = 10, MaxValue = 90, Step = 10, Value = 50 };
        var ratioLabel = Ui.Label("50 / 50");
        var vessel = new OptionButton();
        vessel.AddItem("Body of the first donor");
        vessel.AddItem("Body of the second donor");
        var card = new TrialCard();
        Mind? seen = null;
        Unit A() => M.W.Units[a.GetItemId(a.Selected)];
        Unit B() => M.W.Units[b.GetItemId(b.Selected)];
        void Reset() { seen = null; card.Show(null); }
        a.ItemSelected += _ => Reset();
        b.ItemSelected += _ => Reset();
        ratio.ValueChanged += v => { ratioLabel.Text = $"{v:0} / {100 - v:0}"; Reset(); };
        Add(a);
        Add(b);
        Add(ratio);
        Add(ratioLabel);
        Add(vessel);
        var row = Ui.HBox();
        row.AddChild(Ui.Button($"Foresee ({Defs.ForeseeCost:0} Lumen)", () =>
        {
            var donor = A();
            _s.Send(C.Foresee(donor, B(), (float)ratio.Value / 100f), r => { seen = r.Mind; card.Show(seen, donor.Mind); });
        }, true));
        row.AddChild(Ui.Button("Fuse", () =>
        {
            _s.Send(C.Fuse(A(), B(), (float)ratio.Value / 100f, vessel.Selected == 0, seen), r =>
            {
                if (r.Error != null) return;
                _s.View.Selected.Clear();
                _s.View.SelectedBuilding = null;
                Dirty();
            });
        }, true, "Sacrifices both subjects. Their minds blend into one new mind."));
        Add(row);
        Add(Ui.Label("Foresee shows the new mind's Trial before you sacrifice anyone. Fusing blind is cheaper.", 11, Ui.Muted, true));
        Add(card);
        EssencePanel();
    }

    void EssencePanel()
    {
        Add(Ui.Header("Essences"));
        if (Me.Age < 2) { Add(Ui.Label("Essence Infusion unlocks in Age 2, Essence Removal in Age 3.", 11, Ui.Muted, true)); return; }
        if (Me.Essences.Count == 0) { Add(Ui.Label("Distill captives in the Dungeon to collect Essences: a single skill, boiled out of a mind.", 11, Ui.Muted, true)); return; }
        var donors = Donors();
        if (donors.Count == 0) return;
        var who = new OptionButton();
        foreach (var u in donors) who.AddItem(UnitLabel(u), u.Id);
        who.Select(0);
        var ess = new OptionButton();
        foreach (var e in Me.Essences) ess.AddItem($"{e.Name} (from {e.Source})");
        ess.Select(0);
        float min = Me.Age >= 3 ? -1.5f : 0.25f;
        var strength = new HSlider { MinValue = min, MaxValue = 1.5, Step = 0.25, Value = 1.0 };
        var label = Ui.Label("Infuse at strength 1");
        var card = new TrialCard();
        Mind? seen = null;
        strength.ValueChanged += v =>
        {
            label.Text = v >= 0 ? $"Infuse at strength {v:0.##}" : $"Remove at strength {-v:0.##}";
            seen = null;
            card.Show(null);
        };
        who.ItemSelected += _ => { seen = null; card.Show(null); };
        ess.ItemSelected += _ => { seen = null; card.Show(null); };
        Unit U() => M.W.Units[who.GetItemId(who.Selected)];
        Add(who);
        Add(ess);
        Add(strength);
        Add(label);
        var row = Ui.HBox();
        row.AddChild(Ui.Button($"Foresee ({Defs.ForeseeCost:0} Lumen)", () =>
        {
            var u = U();
            _s.Send(C.ForeseeInfusion(u, ess.Selected, (float)strength.Value), r => { seen = r.Mind; card.Show(seen, u.Mind); });
        }, true));
        row.AddChild(Ui.Button($"Apply ({Defs.InfusionCost:0} Lumen)", () =>
            _s.Send(C.Infuse(U(), ess.Selected, (float)strength.Value, seen), _ => Dirty()), true, "Changes this subject's mind in place."));
        Add(row);
        Add(card);
    }

    static readonly (string name, MindKind kind)[] DrillList =
    [
        ("Harvest", MindKind.Farmer), ("Raid", MindKind.Thief), ("Hold the Line", MindKind.Guard), ("Hunt", MindKind.Hunter),
        ("Explore", MindKind.Scout), ("Build", MindKind.Builder), ("Mend", MindKind.Healer), ("Hoard", MindKind.Hoarder),
        ("Brawl (fight or flee)", MindKind.Guard),
    ];

    void GroundsPanel()
    {
        foreach (var job in Me.Training.ToList())
        {
            var row = Ui.HBox();
            row.AddChild(Live(() => $"{job.Unit.Mind?.Display}: {job.Drill}, generation {job.Iteration}, score {(job.Curve.Count > 0 ? job.Curve[^1] : 0):0.0}"));
            row.AddChild(Ui.Button("Finish", () => _s.Send(C.OnUnit(CmdType.FinishDrill, job.Unit), _ => Dirty())));
            Add(row);
            var curve = new LearningCurve { CustomMinimumSize = new Vector2(0, 50) };
            _refreshers.Add(() => { curve.Points.Clear(); curve.Points.AddRange(job.Curve); curve.QueueRedraw(); });
            Add(curve);
        }
        if (Me.Training.Count >= 4) return;
        var donors = Donors();
        if (donors.Count == 0) return;
        var who = new OptionButton();
        foreach (var u in donors) who.AddItem(UnitLabel(u), u.Id);
        who.Select(0);
        var drill = new OptionButton();
        foreach (var (n, _) in DrillList) drill.AddItem(n);
        // Mirror Drill: train against ghosts of the real enemy's units, recorded wherever you could see them.
        int mirrorAt = drill.ItemCount;
        if (Me.Age >= 2)
        {
            drill.AddItem($"Mirror: their raiders ({Me.MirrorRaiders} tapes)");
            drill.AddItem($"Mirror: their guards ({Me.MirrorGuards} tapes)");
        }
        drill.Select(0);
        Add(Ui.Label("Send a subject to drill. It stops working while it trains; its brain really learns.", 11, Ui.Muted, true));
        Add(who);
        Add(drill);
        Add(Ui.Button($"Start drill ({Defs.DrillCost:0} Lumen)", () =>
        {
            var u = M.W.Units[who.GetItemId(who.Selected)];
            if (drill.Selected >= mirrorAt) { _s.Send(C.StartMirror(u, (TapeMode)(drill.Selected - mirrorAt)), _ => Dirty()); return; }
            var (n, k) = DrillList[drill.Selected];
            _s.Send(C.StartDrill(u, k, n), _ => Dirty());
        }));
        Add(Ui.Label(Me.Age >= 2
            ? "Mirror Drills replay what the enemy's real units did: raiders seen near your Keep, guards seen at theirs. Needs 3 tapes."
            : "Age 2 unlocks the Mirror Drill: train against recordings of the real enemy.", 11, Ui.Muted, true));
        Add(Ui.Label("Training hard in one drill makes a subject forget other skills.", 11, Ui.Muted, true));
    }

    void PitPanel()
    {
        Add(Live(() => $"Instability {Me.MutationHeat:0}/100" + (Me.MutationHeat > 70 ? "  (dangerous!)" : "")));
        foreach (var (id, choices) in M.PitChoices.Where(kv => M.W.Units[kv.Key].Team == Me.Team).ToList())
        {
            var u = M.W.Units[id];
            Add(Ui.Label($"Choose what this {u.Mind?.Display} becomes:", 12));
            var grid = new GridContainer { Columns = 2 };
            foreach (var c in choices)
            {
                var v = Ui.VBox(2);
                v.AddChild(new RadarChart { Result = c.Trial, Compare = u.Mind?.Trial, Fill = c.AuraColor().Lightened(0.2f), CustomMinimumSize = new Vector2(180, 110) });
                v.AddChild(Ui.Badges(c));
                v.AddChild(Ui.Button("Keep this one", () => _s.Send(C.ChooseMutant(u, c), _ => Dirty())));
                grid.AddChild(v);
            }
            Add(grid);
            return;
        }
        var donors = Donors();
        if (donors.Count == 0) return;
        var who = new OptionButton();
        foreach (var d in donors) who.AddItem(UnitLabel(d), d.Id);
        who.Select(0);
        var level = new OptionButton();
        for (int lv = 1; lv <= (Me.Age >= 3 ? 3 : 2); lv++) level.AddItem($"Level {lv} ({Defs.MutationCost(lv):0} Lumen)");
        level.Select(0);
        Add(who);
        Add(level);
        Add(Ui.Button("Throw into the Pit", () =>
        {
            _s.Send(C.Mutate(M.W.Units[who.GetItemId(who.Selected)], level.Selected + 1), _ => Dirty());
        }));
        Add(Ui.Label("Six mutants come out; you keep one. Push Instability past 100 and an Aberration escapes.", 11, Ui.Muted, true));
    }

    void CaptivesPanel()
    {
        if (Me.Captives.Count == 0) { Add(Ui.Label("No captives. Subdue enemies or wild creatures and carry them home.", 11, Ui.Muted, true)); return; }
        foreach (var c in Me.Captives.ToList())
        {
            var box = Ui.Panel(Ui.BgSoft, 6);
            var v = Ui.VBox(4);
            string from = c.FromTeam == 2 ? "wild" : "enemy";
            v.AddChild(Ui.Label($"{c.Mind?.Display ?? c.Unit.Species.ToString()}  ({from})", 13));
            var status = Ui.Label("", 11, Ui.Muted);
            _refreshers.Add(() => status.Text = c.Convert >= 0 ? $"Converting {c.Convert:0}/{Defs.ConvertTime:0}s" : $"Held {c.Held:0}s");
            v.AddChild(status);
            if (c.Interrogated && c.Mind != null)
            {
                v.AddChild(new RadarChart { Result = c.Mind.Trial, Fill = c.Mind.AuraColor().Lightened(0.2f), CustomMinimumSize = new Vector2(200, 110) });
                v.AddChild(Ui.Badges(c.Mind));
            }
            var row = Ui.HBox(4);
            if (!c.Interrogated) row.AddChild(Ui.Button("Interrogate", () => _s.Send(C.OnCaptive(CmdType.Interrogate, c), _ => Dirty()), false, "Reveal its traits. Free."));
            row.AddChild(Ui.Button("Extract", () => _s.Send(C.OnCaptive(CmdType.Extract, c), _ => Dirty()), false, "Copy its mind into your Library. The captive is consumed."));
            row.AddChild(Ui.Button("Distill", () => _s.Send(C.OnCaptive(CmdType.Distill, c), _ => Dirty()), false, "Boil one skill out of its mind as an Essence. The captive is consumed."));
            if (c.Convert < 0) row.AddChild(Ui.Button("Convert", () => _s.Send(C.OnCaptive(CmdType.Convert, c), _ => Dirty()), false, "Slowly turn it to your side, skills intact."));
            row.AddChild(Ui.Button("Ransom", () => _s.Send(C.OnCaptive(CmdType.Ransom, c), _ => Dirty()), false, "Send it back for 20 Lumen."));
            v.AddChild(row);
            box.AddChild(v);
            Add(box);
        }
    }

    // Campaign: after a win, pick up to two new minds for the Legacy Library; a loss ends the run.
    void CampaignEnd(VBoxContainer v)
    {
        bool won = M.Winner == Me.Team;
        if (!won)
        {
            v.AddChild(Ui.Label("The run is over. Your Codex keeps every trait you discovered.", 14, new Color("ff9080"), true));
            v.AddChild(Ui.Button("Back to the campaign", () => { CampaignRun.Reset(); _s.ReturnToMenu(); }));
            return;
        }
        var legacy = CampaignRun.LegacyMinds();
        var fresh = Me.Library.Where(m => !MindLibrary.I.BaseMinds.ContainsValue(m) && !legacy.Contains(m) && !m.Stub).ToList();
        var picked = new List<Mind>();
        v.AddChild(Ui.Header($"Carry up to {Campaign.Keep} minds into the next realm"));
        if (fresh.Count == 0) v.AddChild(Ui.Label("You made no new minds this time.", 12, Ui.Muted));
        foreach (var m in fresh)
        {
            var cb = new CheckBox { Text = $"{m.Display}  ({string.Join(", ", m.Traits.Take(3).Select(t => t.Name))})" };
            cb.Toggled += on =>
            {
                if (on && picked.Count >= Campaign.Keep) { cb.ButtonPressed = false; return; }
                if (on) picked.Add(m); else picked.Remove(m);
            };
            v.AddChild(cb);
        }
        v.AddChild(Ui.Button("Continue the run", () =>
        {
            foreach (var m in picked)
            {
                if (!MindLibrary.I.All.Contains(m)) MindLibrary.I.Add(m);
                CampaignRun.Legacy.Add(m.Name);
            }
            CampaignRun.Region++;
            CampaignRun.Save();
            _s.ReturnToMenu();
        }));
    }

    // ---------- build menu, help, end ----------

    PanelContainer BuildMenu()
    {
        var p = Ui.Panel();
        var v = Ui.VBox(4);
        v.AddChild(Ui.Header("Build (B)"));
        foreach (var group in Defs.Buildings.GroupBy(d => d.Group))
        {
            v.AddChild(Ui.Label(group.Key, 12, Ui.Muted));
            var grid = new GridContainer { Columns = 2 };
            grid.AddThemeConstantOverride("h_separation", 4);
            grid.AddThemeConstantOverride("v_separation", 4);
            foreach (var def in group)
            {
                var d = def;
                var btn = Ui.Button($"{d.Name}{(d.Age > 1 ? $"  (Age {d.Age})" : "")}\n{(d.CostText.Length > 0 ? d.CostText : "free")}", () =>
                {
                    if (Me.Age < d.Age) { Toast($"{d.Name} needs Age {d.Age}. Advance at your Keep."); return; }
                    var missing = new[] { (d.Timber, Me.Timber, "timber"), (d.Stone, Me.Stone, "stone"), (d.Lumen, Me.Lumen, "Lumen") }
                        .Where(c => c.Item2 < c.Item1).Select(c => $"{c.Item1 - c.Item2:0} more {c.Item3}").ToList();
                    if (missing.Count > 0) { Toast($"{d.Name}: need {string.Join(" and ", missing)}. Send subjects to gather it."); return; }
                    _s.View.Placing = d.Type;
                    _buildMenu.Visible = false;
                    Toast("Click to place, right-click to cancel. Shift-click to place several.");
                }, true, d.Description);
                btn.Icon = MatchView.BuildingTexture(d.Type, Me.Team);
                btn.AddThemeConstantOverride("icon_max_width", 34);
                btn.AddThemeFontSizeOverride("font_size", 12);
                btn.Alignment = HorizontalAlignment.Left;
                btn.CustomMinimumSize = new Vector2(0, 44);
                btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                btn.ClipText = true;
                _persistent.Add(() =>
                {
                    btn.Disabled = Me.Age < d.Age;
                    bool afford = Me.Timber >= d.Timber && Me.Stone >= d.Stone && Me.Lumen >= d.Lumen;
                    btn.Modulate = afford ? Colors.White : new Color(1f, 0.6f, 0.55f);
                });
                grid.AddChild(btn);
            }
            v.AddChild(grid);
        }
        v.AddChild(Ui.Label("Costs come out of storage when you place a site; then select subjects and right-click the site to build it.", 11, Ui.Muted, true));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(410, 600), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        v.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(v);
        p.AddChild(scroll);
        return p;
    }

    PanelContainer HelpPanel()
    {
        var p = Ui.Panel(null, 16);
        var v = Ui.VBox(6);
        v.AddChild(Ui.Header("How to rule"));
        v.AddChild(Ui.Label(
            "Every subject thinks with its own trained neural network. Its class (from what it is measurably good at) sets how well it works; " +
            "your orders set what it does. With Free Will on, idle subjects follow their own minds.\n\n" +
            "Camera: WASD / arrows, mouse wheel to zoom, middle-drag to pan, H = home.\n" +
            "Select: click, drag a box, Shift adds. Double-click = that class on screen (Ctrl: whole map). Ctrl+A all, . idle. " +
            "Ctrl+1-9 saves a group, 1-9 recalls it (twice to jump there).\n" +
            "Right-click: crop/tree/rock = gather in a loop, site = build, enemy = capture, enemy store = raid, other enemy building = sabotage, ground = move (Shift: guard).\n" +
            "Keys: G guard spot, C capture, Z hold, X stop, F toggle Free Will.\n" +
            "Economy: build Farms for grain, a Lumber Mill by the forest, a Camp by rocks and wells. Granary cuts upkeep, Lumen Extractors mine wells for you.\n" +
            "Royal Command (R): steer one subject yourself for 8 seconds. Space tussle, E gather/drop off, Q carry, F steal, G sabotage, T rescue.\n" +
            "Fights: tussles drain Will. A subdued unit can be carried home and becomes your captive.\n" +
            "Win: carry the enemy ruler into your Dungeon or Keep, charge a Lumen Spire to 1,500 (it charges slowly; enemies can sabotage it), or lead on score when time runs out.\n" +
            "Space = pause, B = build, Esc = cancel, F11 = fullscreen.", 13, null, true));
        v.AddChild(Ui.Button("Close", ToggleHelp));
        p.AddChild(v);
        p.CustomMinimumSize = new Vector2(620, 0);
        return p;
    }

    public void ShowEnd()
    {
        var overlay = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(overlay);
        var p = Ui.Panel(null, 20);
        var v = Ui.VBox(10);
        string title = M.Winner == Me.Team ? "VICTORY" : M.Winner == -2 ? "DRAW" : "DEFEAT";
        v.AddChild(Ui.Label(title, 32, M.Winner == Me.Team ? new Color("f2d24b") : new Color("ff7060")));
        v.AddChild(Ui.Label($"{M.WinReason}.   Score {M.Score(Me):0} vs {M.Score(M.Enemy(Me)):0}", 15));
        v.AddChild(Ui.Label($"Minds you bred or stole this match: {Me.Library.Count(m => !MindLibrary.I.All.Contains(m))}. They join your Library.", 12, Ui.Muted, true));
        if (_s.Region != null) CampaignEnd(v);
        if (M.Ai[1] is { } ai)
        {
            v.AddChild(Ui.Header($"War Report from {M.Kingdoms[1].Name}"));
            v.AddChild(Ui.Label(ai.WarReport(M.Winner == 1), 13, null, true));
        }
        if (_s.Region == null) v.AddChild(Ui.Button("Back to the menu", () => _s.ReturnToMenu()));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(640, 520), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        v.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(v);
        p.AddChild(scroll);
        p.CustomMinimumSize = new Vector2(680, 0);
        AddChild(p);
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center, LayoutPresetMode.KeepSize);
    }
}

public partial class Minimap : Control
{
    readonly MatchScene _s;

    public Minimap(MatchScene s) => _s = s;

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var m = _s.M;
        var r = new Rect2(Vector2.Zero, Size);
        float sx = Size.X / m.W.Width, sy = Size.Y / m.W.Height;
        DrawTextureRect(_s.View.TerrainTexture, r, false);
        foreach (var b in m.W.Buildings)
        {
            if (b.Team != _s.Team && !_s.M.Vision[_s.Team].Sees(b.Pos) && b.Team != 2) continue;
            DrawRect(new Rect2(b.Pos.X * sx - 2, b.Pos.Y * sy - 2, 4, 4), MatchView.TeamColors[Math.Min(b.Team, 2)]);
        }
        foreach (var u in m.W.Units)
            if (u.Active && _s.View.CanSee(u))
                DrawRect(new Rect2(u.Pos.X * sx - 1, u.Pos.Y * sy - 1, 2, 2), MatchView.TeamColors[Math.Min(u.Team, 2)].Lightened(0.3f));
        DrawTextureRect(_s.View.FogTexture, r, false);
        var vp = GetViewportRect().Size / _s.Cam.Zoom;
        var c = _s.Cam.Position / MatchView.Tile;
        DrawRect(new Rect2((c.X - vp.X / MatchView.Tile / 2) * sx, (c.Y - vp.Y / MatchView.Tile / 2) * sy, vp.X / MatchView.Tile * sx, vp.Y / MatchView.Tile * sy), Colors.White, false, 1f);
        DrawRect(r, new Color(1, 1, 1, 0.3f), false, 1f);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click) Jump(click.Position);
        else if (e is InputEventMouseMotion drag && (drag.ButtonMask & MouseButtonMask.Left) != 0) Jump(drag.Position);
    }

    void Jump(Vector2 pos)
    {
        _s.LookAt(new System.Numerics.Vector2(pos.X / Size.X * _s.M.W.Width, pos.Y / Size.Y * _s.M.W.Height));
        AcceptEvent();
    }
}
