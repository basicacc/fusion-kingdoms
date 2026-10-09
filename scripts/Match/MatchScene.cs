using FusionKingdoms.Sim;
using Godot;
using SVec = System.Numerics.Vector2;

namespace FusionKingdoms;

public partial class MatchScene : Node
{
    public static AiPersonality Personality = AiPersonality.Breeder;
    public static AiDifficulty Difficulty = AiDifficulty.Normal;
    public static MapSettings Settings = MapSettings.Default;
    // AI vs AI showcase: both kingdoms are AI rulers, the camera directs itself.
    public static bool ShowcaseMode;
    public static AiPersonality BluePersonality = AiPersonality.Warden;
    public Showcase? Show;
    public static int Seed;
    public static NetRole Role = NetRole.Local;
    public static int CampaignRegion = -1;
    public Region? Region => CampaignRegion >= 0 && CampaignRegion < Campaign.Regions.Length ? Campaign.Regions[CampaignRegion] : null;

    public Match M = null!;
    public MatchView View = null!;
    public Camera2D Cam = null!;
    public MatchAudio Audio = null!;
    public Weather Weather = null!;
    public Hud Hud = null!;
    public float Speed = 1f;
    public bool Paused;
    public int Team;
    public Kingdom Me => M.Kingdoms[Team];
    public bool Networked => Role != NetRole.Local;

    float _acc;
    Vector2? _dragStart;
    bool _panning;
    readonly List<Unit>[] _groups = Enumerable.Range(0, 10).Select(_ => new List<Unit>()).ToArray();
    ulong _lastClick, _lastGroupKey;
    int _lastGroup = -1;
    bool _ended;
    Wire.HostState? _host;
    Wire.Mirror? _mirror;
    float _royalSend;

    public override void _Ready()
    {
        Audio = new MatchAudio(this);
        if (int.TryParse(OS.GetEnvironment("FK_REGION"), out var devRegion)) CampaignRegion = devRegion;
        var lib = MindLibrary.I;
        var net = NetSession.I;
        int seed = Seed != 0 ? Seed : (int)(GD.Randi() & 0x7fffffff);
        // Recording hooks: FK_SEED fixes the map, FK_MINUTES the match length.
        if (int.TryParse(OS.GetEnvironment("FK_SEED"), out var devSeed)) seed = devSeed;
        if (float.TryParse(OS.GetEnvironment("FK_MINUTES"), out var devMinutes)) Settings = Settings with { Minutes = devMinutes };
        switch (Role)
        {
            case NetRole.Host:
                var theirs = Wire.ReadLibrary(net.RemoteLibrary!, lib.Factory, lib.BaseMinds);
                M = MapGen.Create1v1(lib.Factory, lib.BaseMinds, lib.All, seed, net.LocalName, net.RemoteName, theirs, Settings);
                Team = 0;
                _host = new Wire.HostState();
                net.CommandReceived += OnRemoteCommand;
                net.RoyalReceived += OnRemoteRoyal;
                net.Disconnected += OnPeerLeft;
                break;
            case NetRole.Client:
                M = MapGen.Create1v1(lib.Factory, lib.BaseMinds, lib.BaseMinds.Values, seed, net.RemoteName, net.LocalName, lib.BaseMinds.Values, Settings);
                M.IsMirror = true;
                Team = 1;
                _mirror = new Wire.Mirror(lib.Factory);
                net.MindsReceived += OnMinds;
                net.SnapshotReceived += OnSnapshot;
                net.Disconnected += OnPeerLeft;
                break;
            default:
                if (Region is { } region)
                {
                    CampaignRun.Load();
                    var mine = lib.BaseMinds.Values.Concat(CampaignRun.LegacyMinds()).ToList();
                    M = MapGen.Create1v1(lib.Factory, lib.BaseMinds, mine, seed, "Your Crown", $"The {region.Foe}", null, Settings with { Minutes = region.Minutes });
                    M.Ai[1] = new AiRuler(M, M.Kingdoms[1], region.Foe, seed + 1, Difficulty);
                    Campaign.Setup(M, region);
                }
                else
                {
                    M = MapGen.Create1v1(lib.Factory, lib.BaseMinds, lib.All, seed, "Your Crown", $"The {Personality}", null, Settings);
                    M.Ai[1] = new AiRuler(M, M.Kingdoms[1], Personality, seed + 1, Difficulty);
                }
                if (ShowcaseMode || OS.GetEnvironment("FK_SHOWCASE") == "1")
                {
                    ShowcaseMode = true;
                    M.Kingdoms[0].Name = $"The {BluePersonality}";
                    M.Ai[0] = new AiRuler(M, M.Kingdoms[0], BluePersonality, seed + 2, Difficulty);
                }
                else if (OS.GetEnvironment("FK_AUTOPLAY") == "1") M.Ai[0] = new AiRuler(M, M.Kingdoms[0], AiPersonality.Warden, seed + 2);
                break;
        }

        // Recording hooks: FK_WARMUP=<seconds> plays the match forward before the first frame; FK_SPEED sets the pace.
        if (float.TryParse(OS.GetEnvironment("FK_WARMUP"), out var warm))
            for (int i = 0; i < warm / Rules.Dt && !M.Over; i++) M.Step();
        if (float.TryParse(OS.GetEnvironment("FK_SPEED"), out var startSpeed)) Speed = startSpeed;

        RenderingServer.SetDefaultClearColor(new Color("0d0d14"));
        View = new MatchView { Team = Team, Spectator = ShowcaseMode && Role == NetRole.Local };
        AddChild(View);
        View.Init(M);

        Cam = new Camera2D { Zoom = new Vector2(3f, 3f), PositionSmoothingEnabled = true, PositionSmoothingSpeed = 10f };
        AddChild(Cam);
        Cam.MakeCurrent();
        Cam.Position = MatchView.Px(Me.Home);

        var layer = new CanvasLayer();
        AddChild(layer);
        Hud = new Hud(this);
        layer.AddChild(Hud);
        if (ShowcaseMode && Role == NetRole.Local)
        {
            Hud.Visible = false;
            Show = new Showcase(this);
            layer.AddChild(Show);
            Cam.Zoom = new Vector2(1f, 1f);
        }
        if (OS.GetEnvironment("FK_UITEST") == "1") AddChild(new UiTest(this));
        Weather = new Weather(this, (ulong)seed);
        layer.AddChild(Weather);
        layer.MoveChild(Weather, 0);
    }

    public override void _ExitTree()
    {
        var net = NetSession.I;
        net.CommandReceived -= OnRemoteCommand;
        net.RoyalReceived -= OnRemoteRoyal;
        net.Disconnected -= OnPeerLeft;
        net.MindsReceived -= OnMinds;
        net.SnapshotReceived -= OnSnapshot;
    }

    // ---------- commands ----------

    public void Send(Command c, Action<CommandResult>? done = null)
    {
        if (Role == NetRole.Client)
        {
            NetSession.I.SendCommand(c, (err, info, mindId) =>
            {
                var r = new CommandResult(err, _mirror!.Find(mindId), info);
                Report(r);
                MatchAudio.OnCommand(c, r);
                done?.Invoke(r);
            });
            return;
        }
        var res = Executor.Run(M, Me, c);
        Report(res);
        MatchAudio.OnCommand(c, res);
        done?.Invoke(res);
    }

    void Report(CommandResult r)
    {
        if (r.Error != null) Hud.Toast(r.Error);
        else if (r.Info != null) Hud.Toast(r.Info);
    }

    void OnRemoteCommand(int request, byte[] data)
    {
        var them = M.Kingdoms[1];
        var r = Executor.Run(M, them, Command.FromBytes(data));
        if (r.Mind != null && !_host!.MindsSent.Contains(r.Mind.NetId))
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                w.Write(1);
                Wire.WriteMind(w, r.Mind, them.Library.Contains(r.Mind));
            }
            _host.MindsSent.Add(r.Mind.NetId);
            NetSession.I.SendState(ms.ToArray(), Wire.Snapshot(M, them, _host));
        }
        NetSession.I.Reply(request, r);
    }

    void OnRemoteRoyal(int move, int action)
    {
        M.RoyalInput[1].Move = move;
        M.RoyalInput[1].Action = action;
    }

    void OnMinds(byte[] data) => _mirror!.ReadMinds(data);

    void OnSnapshot(byte[] data)
    {
        View.Snapshot();
        _mirror!.Apply(M, Me, data);
        M.Vision[Team].Update();
        View.UpdateFog();
        _acc = 0f;
    }

    void OnPeerLeft()
    {
        if (M.Over) return;
        M.Winner = Team;
        M.WinReason = "Your opponent left the game";
    }

    public void SetSpeed(float speed)
    {
        if (Networked) { Hud.Toast("Speed is fixed in LAN games"); return; }
        if (speed == 0f) Paused = !Paused;
        else { Speed = speed; Paused = false; }
    }

    public void LookAt(SVec p) => Cam.Position = MatchView.Px(p);

    // Developer hook: FK_SHOT=<png path> [FK_SHOT_AT=<game seconds>] [FK_SPEED=<x>] saves a screenshot and quits.
    float _shotAt = -1f;
    bool _selectedForShot;
    bool _clientTested;
    string? _shotPath;

    void DevHook()
    {
        if (_shotAt < 0f)
        {
            _shotPath = OS.GetEnvironment("FK_SHOT");
            if (string.IsNullOrEmpty(_shotPath)) { _shotAt = float.MaxValue; return; }
            _shotAt = float.TryParse(OS.GetEnvironment("FK_SHOT_AT"), out var at) ? at : 30f;
            if (float.TryParse(OS.GetEnvironment("FK_SPEED"), out var sp)) Speed = sp;
        }
        if (Role == NetRole.Client && OS.GetEnvironment("FK_CLIENT_TEST") == "1" && !_clientTested && M.Time > 8f)
        {
            _clientTested = true;
            for (int i = 0; i < 60; i++)
            {
                var p = Me.Home + new SVec(MathF.Cos(i) * (5 + i % 6), MathF.Sin(i) * (5 + i % 6));
                if (M.CanPlace(Me, BuildingType.House, p) != null) continue;
                Send(C.PlaceSite(BuildingType.House, p));
                break;
            }
            var farmer = Me.Library.FirstOrDefault(m => m.Name == "Farmer");
            if (farmer != null) { Send(C.Produce(farmer)); Send(C.Produce(farmer)); }
            var subs = M.Subjects(Me).ToList();
            var crop = OrderController.NearestNode(M.W, ResourceType.Food, Me.Home, 999f);
            if (crop != null) Send(C.Give(subs, OrderKind.Gather, crop.Pos, M.W.Nodes.IndexOf(crop), ResourceType.Food));
        }
        if (M.Time < _shotAt || _shotPath == null) return;
        var select = OS.GetEnvironment("FK_SELECT");
        if (!string.IsNullOrEmpty(select) && !_selectedForShot)
        {
            _selectedForShot = true;
            if (select == "unit") View.Selected.Add(M.Subjects(Me).First());
            else if (select == "group") foreach (var u in M.Subjects(Me).Take(12)) View.Selected.Add(u);
            else if (select == "tree" && Me.Library.Where(m => m.Parents.Length > 0).OrderByDescending(m => m.Generation).FirstOrDefault() is { } deep)
                LineageTree.Open(this, deep);
            else if (Enum.TryParse<BuildingType>(select, out var bt)) View.SelectedBuilding = M.BuildingsOf(Me).FirstOrDefault(b => b.Type == bt);
            if (View.SelectedBuilding != null) LookAt(View.SelectedBuilding.Pos);
            Paused = true;
            if (OS.GetEnvironment("FK_BUILDMENU") == "1") Hud.ToggleBuild();
            return;
        }
        GetViewport().GetTexture().GetImage().SavePng(_shotPath);
        _shotPath = null;
        GetTree().Quit();
    }

    public override void _Process(double delta)
    {
        DevHook();
        if (Role == NetRole.Client) _acc += (float)delta;
        else if (!Paused && !M.Over)
        {
            _acc += (float)delta * Speed;
            int steps = 0;
            while (_acc >= Rules.Dt && steps < 64)
            {
                View.Snapshot();
                M.Step();
                View.OnEvents(M.W.Events);
                Audio.OnEvents(M.W.Events);
                Show?.OnEvents(M.W.Events);
                if (M.W.StepCount % 3 == 0) View.UpdateFog();
                if (_host != null) NetSession.I.SendState(Wire.NewMinds(M, M.Kingdoms[1], _host), Wire.Snapshot(M, M.Kingdoms[1], _host));
                _acc -= Rules.Dt;
                steps++;
            }
            if (steps == 64) _acc = 0f;
        }
        View.Alpha = _acc / Rules.Dt;
        View.MouseWorld = View.GetGlobalMousePosition();

        Audio.Tick((float)delta);
        View.Gloom = Weather.Gloom;
        if (M.Royal[Team] != null) SteerRoyal((float)delta);
        else PanCamera((float)delta);

        foreach (var u in View.Selected.Where(u => !u.Active).ToList()) View.Selected.Remove(u);
        if (M.Over && !_ended)
        {
            _ended = true;
            MatchAudio.End(M.Winner == Me.Team || Show != null);
            if (Show != null) Show.ShowEnd(Show);
            else Hud.ShowEnd();
        }
    }

    void PanCamera(float dt)
    {
        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) dir.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) dir.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1;
        if (dir != Vector2.Zero)
        {
            Cam.Position += dir.Normalized() * 700f / Cam.Zoom.X * dt;
            Show?.TakeManual();
        }
        var max = new Vector2(M.W.Width, M.W.Height) * MatchView.Tile;
        Cam.Position = Cam.Position.Clamp(Vector2.Zero, max);
    }

    // Royal Command: WASD steers the chosen subject; its brain is switched off for a few seconds.
    void SteerRoyal(float dt)
    {
        var u = M.Royal[Team]!;
        var input = Role == NetRole.Client ? new ExternalController() : M.RoyalInput[Team];
        int dx = (Input.IsKeyPressed(Key.D) ? 1 : 0) - (Input.IsKeyPressed(Key.A) ? 1 : 0);
        int dy = (Input.IsKeyPressed(Key.S) ? 1 : 0) - (Input.IsKeyPressed(Key.W) ? 1 : 0);
        input.Move = (dx, dy) switch
        {
            (0, -1) => 1, (1, -1) => 2, (1, 0) => 3, (1, 1) => 4, (0, 1) => 5, (-1, 1) => 6, (-1, 0) => 7, (-1, -1) => 8, _ => 0,
        };
        input.Action = (int)(Input.IsKeyPressed(Key.Space) ? Act.Tussle
            : Input.IsKeyPressed(Key.E) ? (u.CarryingUnit >= 0 || u.CarryAmount > 0 ? Act.Deposit : Act.Gather)
            : Input.IsKeyPressed(Key.Q) ? Act.Carry
            : Input.IsKeyPressed(Key.F) ? Act.Steal
            : Input.IsKeyPressed(Key.G) ? Act.Sabotage
            : Input.IsKeyPressed(Key.T) ? Act.Rescue
            : Act.Idle);
        Cam.Position = View.DrawPos(u);
        if (Role == NetRole.Client && (_royalSend -= dt) <= 0f)
        {
            _royalSend = 0.1f;
            NetSession.I.SendRoyal(input.Move, input.Action);
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            var world = View.GetCanvasTransform().AffineInverse() * mb.Position;
            switch (mb.ButtonIndex)
            {
                case MouseButton.WheelUp when mb.Pressed: Zoom(1.15f); Show?.TakeManual(); break;
                case MouseButton.WheelDown when mb.Pressed: Zoom(1 / 1.15f); Show?.TakeManual(); break;
                case MouseButton.Middle: _panning = mb.Pressed; break;
                case MouseButton.Left when mb.Pressed:
                    if (View.Pending is { } pend)
                    {
                        PendingOrder(pend, world);
                        View.Pending = null;
                    }
                    else if (View.Placing is { } wt && Match.IsWall(wt))
                        View.WallDragStart = world;   // walls: drag out a line, placed on release
                    else if (View.Placing is { } t)
                    {
                        var at = MatchView.ToWorld(world);
                        if (M.CanPlace(Me, t, at) == null) { Send(C.PlaceSite(t, at)); if (!mb.ShiftPressed) View.Placing = null; }
                        else Hud.Toast(M.CanPlace(Me, t, at)!);
                        Hud.Dirty();
                    }
                    else _dragStart = world;
                    break;
                case MouseButton.Left when !mb.Pressed && View.WallDragStart is { } ws && View.Placing is { } wallType:
                {
                    int placed = 0;
                    foreach (var tile in MatchView.WallLine(MatchView.ToWorld(ws), MatchView.ToWorld(world)))
                        if (M.CanPlace(Me, wallType, tile) == null && Me.Stone >= Defs.Of(wallType).Stone * (placed + 1)) { Send(C.PlaceSite(wallType, tile)); placed++; }
                    View.WallDragStart = null;
                    Hud.Toast(placed > 0 ? $"{placed} {Defs.NameOf(wallType)} segment{(placed == 1 ? "" : "s")} placed. Right-click them with subjects to build." : "Can't build a wall there");
                    if (!mb.ShiftPressed) View.Placing = null;
                    Hud.Dirty();
                    break;
                }
                case MouseButton.Left when !mb.Pressed && _dragStart is { } start:
                    if (start.DistanceTo(world) < 6f)
                    {
                        bool dbl = Time.GetTicksMsec() - _lastClick < 350;
                        _lastClick = Time.GetTicksMsec();
                        if (dbl) SelectSameClass(world, mb.CtrlPressed, mb.ShiftPressed);
                        else ClickSelect(world, mb.ShiftPressed);
                    }
                    else BoxSelect(new Rect2(start, world - start).Abs(), mb.ShiftPressed);
                    _dragStart = null;
                    View.DragBox = null;
                    break;
                case MouseButton.Right when mb.Pressed:
                    if (View.Placing != null) View.Placing = null;
                    else if (View.Pending != null) View.Pending = null;
                    else IssueOrder(world, mb.ShiftPressed);
                    break;
            }
        }
        else if (e is InputEventMouseMotion mm)
        {
            if (_panning) { Cam.Position -= mm.Relative / Cam.Zoom; Show?.TakeManual(); }
            if (_dragStart is { } s) View.DragBox = new Rect2(s, View.GetGlobalMousePosition() - s).Abs();
        }
        else if (e is InputEventKey { Pressed: true, Echo: false } sk && Show != null)
        {
            switch (sk.Keycode)
            {
                case Key.Tab: Show.Auto = !Show.Auto; break;
                case Key.Key1: Show.TakeManual(); LookAt(M.Kingdoms[0].Home); break;
                case Key.Key2: Show.TakeManual(); LookAt(M.Kingdoms[1].Home); break;
                case Key.Space: SetSpeed(Paused ? 1f : 0f); break;
            }
        }
        else if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            switch (k.Keycode)
            {
                case Key.Escape:
                    if (View.Pending != null) View.Pending = null;
                    else if (View.Placing != null) View.Placing = null;
                    else { View.Selected.Clear(); View.SelectedBuilding = null; Hud.Dirty(); }
                    break;
                case Key.B: Hud.ToggleBuild(); break;
                case Key.R when M.Royal[Team] == null: TryRoyal(); break;
                case Key.Space when M.Royal[Team] == null: SetSpeed(0f); break;
                case Key.H: LookAt(Me.Home); break;
                case Key.G when Mine().Count > 0 && M.Royal[Team] == null: View.Pending = OrderKind.Guard; Hud.Toast("Guard: click the spot to hold"); break;
                case Key.C when Mine().Count > 0 && M.Royal[Team] == null: View.Pending = OrderKind.Capture; Hud.Toast("Capture: click an enemy or a creature"); break;
                case Key.X when M.Royal[Team] == null: Stop(OrderKind.None); break;
                case Key.Z when M.Royal[Team] == null: Stop(OrderKind.Hold); break;
                case Key.F when M.Royal[Team] == null: ToggleFreeWill(); break;
                case Key.Period: SelectIdle(k.ShiftPressed); break;
                case Key.A when k.CtrlPressed: SelectWhere(_ => true, false); break;
                case >= Key.Key0 and <= Key.Key9: Group((int)(k.Keycode - Key.Key0), k.CtrlPressed, k.ShiftPressed); break;
                case Key.F1: Hud.ToggleHelp(); break;
            }
        }
    }

    void Zoom(float f)
    {
        float z = Math.Clamp(Cam.Zoom.X * f, 0.6f, 5f);
        Cam.Zoom = new Vector2(z, z);
    }

    public void TryRoyal()
    {
        var mine = View.Selected.Where(u => u.Team == Me.Team && !u.IsRuler).ToList();
        if (mine.Count != 1) return;
        Send(C.OnUnit(CmdType.BeginRoyal, mine[0]));
    }

    void ClickSelect(Vector2 world, bool add)
    {
        if (!add) { View.Selected.Clear(); View.SelectedBuilding = null; }
        var u = View.UnitAt(world);
        if (u != null)
        {
            if (u.Team != Me.Team) View.Selected.Clear();
            else View.Selected.RemoveWhere(x => x.Team != Me.Team);
            View.Selected.Add(u);
            View.SelectedBuilding = null;
            Sound.I.Play("select", -6f);
        }
        else if (!add) View.SelectedBuilding = View.BuildingAt(world);
        Hud.Dirty();
    }

    void BoxSelect(Rect2 box, bool add)
    {
        if (!add) View.Selected.Clear();
        View.SelectedBuilding = null;
        var inBox = M.W.Units.Where(u => u.Active && u.Team == Me.Team && box.HasPoint(View.DrawPos(u))).ToList();
        if (inBox.Any(u => !u.IsRuler)) inBox.RemoveAll(u => u.IsRuler);
        foreach (var u in inBox) View.Selected.Add(u);
        Hud.Dirty();
    }

    // ---------- selection ----------

    public List<Unit> Mine() => View.Selected.Where(u => u.Team == Me.Team && u.Active && !u.IsRuler).ToList();

    public void SelectWhere(Func<Unit, bool> pred, bool add, bool onScreen = false)
    {
        if (!add) { View.Selected.Clear(); View.SelectedBuilding = null; }
        var rect = ScreenWorldRect();
        foreach (var u in M.Subjects(Me))
            if (pred(u) && (!onScreen || rect.HasPoint(View.DrawPos(u)))) View.Selected.Add(u);
        Sound.I.Play("select", -6f);
        Hud.Dirty();
    }

    Rect2 ScreenWorldRect()
    {
        var size = GetViewport().GetVisibleRect().Size / Cam.Zoom;
        return new Rect2(Cam.GetScreenCenterPosition() - size / 2, size);
    }

    // Double-click: everyone of that class on screen (Ctrl: on the whole map).
    void SelectSameClass(Vector2 world, bool wholeMap, bool add)
    {
        var u = View.UnitAt(world);
        if (u == null || u.Team != Me.Team || u.IsRuler) { ClickSelect(world, add); return; }
        string cls = Classes.Of(u.Mind).Name;
        SelectWhere(x => Classes.Of(x.Mind).Name == cls, add, !wholeMap);
        Hud.Toast($"{View.Selected.Count} {cls}{(View.Selected.Count == 1 ? "" : "s")} selected");
    }

    public void SelectIdle(bool add)
    {
        SelectWhere(u => u.Order == OrderKind.None && u.CanAct, add);
        if (View.Selected.Count == 0) Hud.Toast("Nobody is idle");
        else LookAt(MatchView.ToWorld(View.Selected.Select(View.DrawPos).Aggregate((a, b) => a + b) / View.Selected.Count));
    }

    // Ctrl+number saves a group, number recalls it (twice quickly to jump there), Shift+number adds it.
    void Group(int n, bool save, bool add)
    {
        if (save)
        {
            _groups[n] = Mine();
            Hud.Toast($"Group {n}: {_groups[n].Count} subjects");
            return;
        }
        _groups[n].RemoveAll(u => !u.Active || u.Team != Me.Team);
        if (_groups[n].Count == 0) return;
        bool again = _lastGroup == n && Time.GetTicksMsec() - _lastGroupKey < 400;
        _lastGroup = n;
        _lastGroupKey = Time.GetTicksMsec();
        if (!add) View.Selected.Clear();
        View.SelectedBuilding = null;
        foreach (var u in _groups[n]) View.Selected.Add(u);
        if (again) LookAt(_groups[n][0].Pos);
        Hud.Dirty();
    }

    public int GroupOf(Unit u)
    {
        for (int i = 1; i < 10; i++) if (_groups[i].Contains(u)) return i;
        return _groups[0].Contains(u) ? 0 : -1;
    }

    // ---------- orders ----------

    public void Stop(OrderKind kind)
    {
        var mine = Mine();
        if (mine.Count == 0) return;
        Send(C.Give(mine, kind, default, -1, ResourceType.None));
        Hud.Toast(kind == OrderKind.Hold ? $"{mine.Count} holding position" : $"{mine.Count} stopped");
    }

    public void ToggleFreeWill()
    {
        var mine = Mine();
        if (mine.Count == 0) return;
        bool on = !mine.All(u => u.FreeWill);
        Send(C.FreeWill(mine, on));
        Hud.Toast(on ? "Free Will on: when idle they follow their own minds" : "Free Will off: when idle they wait for orders");
        Hud.Dirty();
    }

    // Send everyone selected to gather the nearest node of a kind (from the command bar).
    public void GatherNearest(ResourceType res)
    {
        var mine = Mine();
        if (mine.Count == 0) return;
        var center = mine.Select(u => u.Pos).Aggregate((a, b) => a + b) / mine.Count;
        var node = OrderController.NearestNode(M.W, res, center, 999f);
        if (node == null) { Hud.Toast($"No {res} anywhere you know of"); return; }
        Send(C.Give(mine, OrderKind.Gather, node.Pos, M.W.Nodes.IndexOf(node), res));
    }

    void PendingOrder(OrderKind kind, Vector2 world)
    {
        var mine = Mine();
        if (mine.Count == 0) return;
        if (kind == OrderKind.Guard)
        {
            Send(C.Give(mine, OrderKind.Guard, MatchView.ToWorld(world), -1, ResourceType.None));
            Hud.Toast($"{mine.Count} guarding");
            return;
        }
        var t = View.UnitAt(world, 14f);
        if (t == null || t.Team == Me.Team) { Hud.Toast("Click an enemy or a creature"); return; }
        Send(C.Give(mine, OrderKind.Capture, t.Pos, t.Id, ResourceType.None));
    }

    // Right-click does the obvious thing with whatever is under the cursor.
    void IssueOrder(Vector2 world, bool shift)
    {
        // With the Cradle selected, right-click sets where new subjects go.
        if (View.SelectedBuilding is { Type: BuildingType.Cradle } cr && cr.Team == Me.Team && View.Selected.Count == 0)
        {
            var rp = M.Walkable(MatchView.ToWorld(world));
            Send(new Command { Type = CmdType.SetRally, I = -1, B = true, X = rp.X, Y = rp.Y });
            Hud.Toast("Rally point set for new subjects");
            return;
        }
        var all = View.Selected.Where(u => u.Team == Me.Team && u.Active).ToList();
        if (all.Count == 0) return;
        var p = MatchView.ToWorld(world);
        if (all.Any(u => u.IsRuler)) Send(C.MoveRuler(p));
        var subjects = all.Where(u => !u.IsRuler).ToList();
        if (subjects.Count == 0) return;

        var b = View.BuildingAt(world);
        var target = View.UnitAt(world);
        int node = View.NodeAt(world);
        string what;
        if (target != null && target.Team != Me.Team)
        {
            Send(C.Give(subjects, OrderKind.Capture, target.Pos, target.Id, ResourceType.None));
            what = target.Subdued ? "Carry them home" : $"Capture the {(target.Team == 2 ? target.Species.ToString() : Classes.Of(target.Mind).Name)}";
        }
        else if (target != null && target.Team == Me.Team && !subjects.Contains(target) && (target.Subdued || target.Will < target.Body.Will * 0.9f))
        {
            Send(C.Give(subjects, OrderKind.Heal, target.Pos, target.Id, ResourceType.None));
            what = target.Subdued ? "Reviving them" : "Healing them";
        }
        else if (b != null && b.Team == Me.Team && b.Type == BuildingType.BuildSite)
        {
            Send(C.Give(subjects, OrderKind.Build, b.Pos, b.Id, ResourceType.None));
            what = $"Building the {Defs.NameOf(b.SiteFor)}";
        }
        else if (b != null && b.Team != Me.Team && b.Type is not (BuildingType.AncientAltar or BuildingType.Merchant))
        {
            bool store = b.IsStore;
            Send(C.Give(subjects, store ? OrderKind.Raid : OrderKind.Sabotage, b.Pos, b.Id, ResourceType.None));
            what = store ? $"Raiding the {Defs.NameOf(b.Type)}" : $"Sabotaging the {Defs.NameOf(b.Type)}";
        }
        else if (node >= 0)
        {
            var n = M.W.Nodes[node];
            Send(C.Give(subjects, OrderKind.Gather, n.Pos, node, n.Type));
            what = $"Gathering {(n.Type == ResourceType.Food ? "grain" : n.Type.ToString().ToLowerInvariant())}";
        }
        else
        {
            Send(C.Give(subjects, shift ? OrderKind.Guard : OrderKind.Move, p, -1, ResourceType.None));
            what = shift ? "Guarding this spot" : "";
            View.Spawn("dust", world, 0.4f);
        }
        if (what.Length > 0) Hud.Toast($"{subjects.Count}: {what}");
        Hud.Dirty();
    }

    public void ReturnToMenu()
    {
        ShowcaseMode = false;
        foreach (var m in Me.Library) if (!m.Stub && !MindLibrary.I.All.Contains(m)) MindLibrary.I.Add(m);
        foreach (var k in M.Kingdoms) foreach (var j in k.Training) j.Stop = true;
        if (Networked) NetSession.I.Close();
        Role = NetRole.Local;
        if (CampaignRegion >= 0)
        {
            CampaignRegion = -1;
            GetTree().ChangeSceneToFile("res://scenes/Campaign.tscn");
            return;
        }
        GetTree().ChangeSceneToFile("res://scenes/Menu.tscn");
    }
}

// Builders for player commands.
public static class C
{
    static int[] Ids(IEnumerable<Unit> us) => us.Select(u => u.Id).ToArray();

    public static Command Simple(CmdType t) => new() { Type = t };
    public static Command Order(IEnumerable<Unit> us, CommandType cmd, SVec p) => new() { Type = CmdType.Order, Units = Ids(us), I = (int)cmd, X = p.X, Y = p.Y };
    public static Command Station(IEnumerable<Unit> us, Building? camp) => new() { Type = CmdType.Station, Units = Ids(us), Target = camp?.Id ?? -1 };
    public static Command Stance(IEnumerable<Unit> us, float s) => new() { Type = CmdType.Stance, Units = Ids(us), F = s };
    public static Command Give(IEnumerable<Unit> us, OrderKind kind, SVec p, int target, ResourceType res) =>
        new() { Type = CmdType.Give, Units = Ids(us), I = (int)kind, X = p.X, Y = p.Y, Target = target, F = (int)res };
    public static Command FreeWill(IEnumerable<Unit> us, bool on) => new() { Type = CmdType.FreeWill, Units = Ids(us), B = on };
    public static Command MoveRuler(SVec p) => new() { Type = CmdType.MoveRuler, X = p.X, Y = p.Y };
    public static Command PlaceSite(BuildingType t, SVec p) => new() { Type = CmdType.PlaceSite, I = (int)t, X = p.X, Y = p.Y };
    public static Command OnBuilding(CmdType t, Building b) => new() { Type = t, Target = b.Id };
    public static Command OnUnit(CmdType t, Unit u) => new() { Type = t, Units = [u.Id], Target = u.Id };
    public static Command OnCaptive(CmdType t, Captive c) => new() { Type = t, Target = c.Unit.Id };
    public static Command Produce(Mind m) => new() { Type = CmdType.Produce, I = m.NetId };
    public static Command Foresee(Unit a, Unit b, float ratio) => new() { Type = CmdType.Foresee, Units = [a.Id], Target = b.Id, F = ratio };
    public static Command Fuse(Unit a, Unit b, float ratio, bool vesselA, Mind? seen) =>
        new() { Type = CmdType.Fuse, Units = [a.Id], Target = b.Id, F = ratio, B = vesselA, I = seen?.NetId ?? -1 };
    public static Command StartDrill(Unit u, MindKind k, string name) => new() { Type = CmdType.StartDrill, Units = [u.Id], I = (int)k, S = name };
    public static Command StartMirror(Unit u, TapeMode mode) => new() { Type = CmdType.StartDrill, Units = [u.Id], I = Executor.MirrorDrillBase + (int)mode };
    public static Command Mutate(Unit u, int level) => new() { Type = CmdType.Mutate, Units = [u.Id], I = level };
    public static Command ChooseMutant(Unit u, Mind m) => new() { Type = CmdType.ChooseMutant, Target = u.Id, I = m.NetId };
    public static Command ForeseeInfusion(Unit u, int essence, float s) => new() { Type = CmdType.ForeseeInfusion, Units = [u.Id], I = essence, F = s };
    public static Command Infuse(Unit u, int essence, float s, Mind? seen) =>
        new() { Type = CmdType.Infuse, Units = [u.Id], I = essence, F = s, Target = seen?.NetId ?? -1 };
}
