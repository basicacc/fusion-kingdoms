using FusionKingdoms.Sim;
using Godot;
using SVec = System.Numerics.Vector2;

namespace FusionKingdoms;

// Draws a match from one kingdom's point of view with the hand-made sprites: baked terrain, y-sorted
// buildings, units and effects, tree canopies above, day/night lighting, and fog of war.
public partial class MatchView : Node2D
{
    public const float Tile = 16f;

    public Match M = null!;   // null when drawing a bare world (the Mind Lab arena)
    public World W = null!;
    public int Team;
    public float Alpha;
    public readonly HashSet<Unit> Selected = new();
    public Building? SelectedBuilding;
    public BuildingType? Placing;
    public Vector2 MouseWorld;
    public Rect2? DragBox;

    Sprite2D _ground = null!, _canopy = null!;
    Overlay _overlay = null!;
    CanvasModulate _daylight = null!;
    readonly List<PointLight2D> _lights = new();
    readonly Dictionary<Building, PointLight2D> _buildingLights = new();
    Texture2D _lightTex = null!;
    ImageTexture _mini = null!;
    Image _fogImg = null!;
    ImageTexture _fog = null!;
    SVec[] _prev = [];
    Font _font = null!;
    readonly Dictionary<int, UnitAnim> _anim = new();
    readonly List<Fx> _fx = new();
    float _time;
    List<Vector2>? _farms;

    sealed class UnitAnim { public int Dir; public float Walk; public bool Moving; public Vector2 Last; }
    sealed record Fx(string Kind, Vector2 Pos, float Start, float Life, float Scale = 1f);

    public static readonly Color[] TeamColors = [new("4f86e6"), new("e05a4f"), new("6fb04a")];

    public void Init(Match m) => Init(m.W, m);

    public void Init(World w, Match? m)
    {
        M = m!;
        W = w;
        if (m == null) Spectator = true;
        _font = ThemeDB.FallbackFont;
        TextureFilter = TextureFilterEnum.Nearest;

        var baked = TerrainBaker.Bake(w, 17);
        _ground = new Sprite2D { Texture = ImageTexture.CreateFromImage(baked.Ground), Centered = false, ZIndex = -10 };
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://scripts/Art/water.gdshader") };
        mat.SetShaderParameter("water_mask", ImageTexture.CreateFromImage(baked.Water));
        mat.SetShaderParameter("map_tiles", new Vector2(w.Width, w.Height));
        _ground.Material = mat;
        AddChild(_ground);
        _canopy = new Sprite2D { Texture = ImageTexture.CreateFromImage(baked.Canopy), Centered = false, ZIndex = 5, Modulate = new Color(1, 1, 1, 0.94f) };
        AddChild(_canopy);
        _mini = ImageTexture.CreateFromImage(baked.Mini);

        _fogImg = Image.CreateEmpty(W.Width, W.Height, false, Image.Format.Rgba8);
        _fog = ImageTexture.CreateFromImage(_fogImg);
        _overlay = new Overlay { View = this, ZIndex = 20, TextureFilter = TextureFilterEnum.Linear };
        AddChild(_overlay);

        _daylight = new CanvasModulate();
        AddChild(_daylight);
        _lightTex = RadialLight();
        foreach (var n in W.Nodes.Where(n => n.Type == ResourceType.Lumen))
            AddLight(Px(n.Pos), new Color("8ff3ff"), 0.9f, 1.4f);

        Snapshot();
        UpdateFog();
    }

    static Texture2D RadialLight()
    {
        var g = new Gradient();
        g.SetColor(0, new Color(1, 1, 1, 1));
        g.SetColor(1, new Color(1, 1, 1, 0));
        return new GradientTexture2D { Gradient = g, Width = 128, Height = 128, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f) };
    }

    PointLight2D AddLight(Vector2 at, Color col, float energy, float scale)
    {
        var l = new PointLight2D { Texture = _lightTex, Color = col, Energy = energy, TextureScale = scale, Position = at };
        AddChild(l);
        _lights.Add(l);
        return l;
    }

    public Texture2D TerrainTexture => _mini;
    public Texture2D FogTexture => _fog;

    public void Snapshot()
    {
        if (_prev.Length < W.Units.Count) Array.Resize(ref _prev, W.Units.Count * 2);
        for (int i = 0; i < W.Units.Count; i++) _prev[i] = W.Units[i].Pos;
    }

    public void UpdateFog()
    {
        if (M == null) return;
        var v = M.Vision[Team];
        for (int y = 0; y < W.Height; y++)
        for (int x = 0; x < W.Width; x++)
        {
            int i = y * W.Width + x;
            float a = Spectator || v.Visible[i] ? 0f : v.Explored[i] != 0 ? 0.45f : 0.9f;
            _fogImg.SetPixel(x, y, new Color(0.03f, 0.03f, 0.07f, a));
        }
        _fog.Update(_fogImg);
    }

    public static Vector2 Px(SVec p) => new(p.X * Tile, p.Y * Tile);
    public static SVec ToWorld(Vector2 px) => new(px.X / Tile, px.Y / Tile);

    public Vector2 DrawPos(Unit u)
    {
        if (u.Id >= _prev.Length) return Px(u.Pos);
        var a = _prev[u.Id];
        if (SVec.Distance(a, u.Pos) > 3f) return Px(u.Pos);
        return Px(SVec.Lerp(a, u.Pos, Math.Clamp(Alpha, 0f, 1f)));
    }

    // Spectator: no fog, everything visible (the menu backdrop).
    public bool Spectator;

    bool Explored(SVec p)
    {
        if (Spectator) return true;
        int x = (int)p.X, y = (int)p.Y;
        return x >= 0 && y >= 0 && x < W.Width && y < W.Height && M.Vision[Team].Explored[y * W.Width + x] != 0;
    }

    public bool CanSee(Unit u) => Spectator || u.Team == Team || M.Vision[Team].Sees(u);
    public bool CanSee(System.Numerics.Vector2 p) => Spectator || M.Vision[Team].Sees(p);

    // ---------- effects ----------

    public void OnEvents(IReadOnlyList<SimEvent> events)
    {
        foreach (var e in events)
        {
            var u = W.Units[e.Unit];
            if (!CanSee(u)) continue;
            var p = Px(u.Pos);
            switch (e.Kind)
            {
                case EventKind.TussleStarted: Spawn("dust", p, 0.5f); break;
                case EventKind.Subdued: Spawn("star", p + new Vector2(0, -10), 1.6f); Spawn("dust", p, 0.5f); break;
                case EventKind.Captured: Spawn("swirl", p, 1.0f, 1.5f); break;
                case EventKind.Deposited: Spawn("coin", p + new Vector2(0, -12), 0.7f); break;
                case EventKind.Stolen: if (W.StepCount % 3 == 0) Spawn("coin", p + new Vector2(0, -12), 0.6f); break;
                case EventKind.Healed: if (W.StepCount % 4 == 0) Spawn("heart", p + new Vector2(0, -12), 0.8f); break;
                case EventKind.Revived: Spawn("heart", p + new Vector2(0, -12), 1.0f, 1.4f); break;
                case EventKind.Delivered: Spawn("dust", p, 0.4f); break;
                case EventKind.Sabotaged: Spawn("dust", Px(W.Buildings[e.Other].Pos), 0.8f, 2f); break;
                case EventKind.Gathered:
                    if ((e.Unit + W.StepCount) % 6 == 0) Spawn(e.Res == ResourceType.Lumen ? "sparkle" : "leaf", p + new Vector2(4, -6), 0.5f);
                    break;
            }
        }
    }

    public void Spawn(string kind, Vector2 at, float life, float scale = 1f) => _fx.Add(new Fx(kind, at, _time, life, scale));

    // ---------- per frame ----------

    public override void _Process(double delta)
    {
        _time += (float)delta;
        _fx.RemoveAll(f => _time - f.Start > f.Life);
        foreach (var u in W.Units)
        {
            if (!_anim.TryGetValue(u.Id, out var a)) _anim[u.Id] = a = new UnitAnim { Last = DrawPos(u) };
            var p = DrawPos(u);
            var d = p - a.Last;
            a.Moving = d.LengthSquared() > 0.01f;
            if (a.Moving)
            {
                a.Dir = MathF.Abs(d.X) > MathF.Abs(d.Y) ? (d.X > 0 ? 2 : 3) : (d.Y > 0 ? 0 : 1);
                a.Walk += (float)delta * 9f;
            }
            a.Last = p;
        }
        UpdateDaylight();
        QueueRedraw();
        _overlay.QueueRedraw();
    }

    void UpdateDaylight()
    {
        float t = W.Time % Rules.DayCycle;
        Color day = new(1, 1, 1), dusk = new(1f, 0.82f, 0.68f), night = new(0.36f, 0.42f, 0.68f);
        Color c = W.EclipseTimer > 0 ? night
            : t < 165f ? day
            : t < 180f ? day.Lerp(dusk, (t - 165f) / 15f)
            : t < 188f ? dusk.Lerp(night, (t - 180f) / 8f)
            : t < 232f ? night
            : night.Lerp(day, (t - 232f) / 8f);
        // Bad weather dims the day a little.
        if (Gloom > 0f) c = c.Darkened(Gloom);
        _daylight.Color = c;

        // Warm light from the windows of finished buildings.
        foreach (var b in W.Buildings)
        {
            bool lit = b.Type is BuildingType.Keep or BuildingType.House or BuildingType.Cradle or BuildingType.LumenSpire
                or BuildingType.FusionAltar or BuildingType.Infirmary or BuildingType.Market;
            if (!lit || _buildingLights.ContainsKey(b)) continue;
            var col = b.Type is BuildingType.LumenSpire ? new Color("8ff3ff") : b.Type == BuildingType.FusionAltar ? new Color("c89aff") : new Color("ffcf7a");
            _buildingLights[b] = AddLight(Px(b.Pos) + new Vector2(0, -6), col, 0.85f, b.Type == BuildingType.Keep ? 2.6f : 1.4f);
        }
        float glow = c.R < 0.8f ? 1f : 0.25f;
        // Lit windows and wells flicker a little, each at its own pace.
        for (int i = 0; i < _lights.Count; i++)
            _lights[i].Energy = glow * (0.9f + 0.1f * MathF.Sin(_time * (5f + i % 7) + i * 1.7f) * MathF.Sin(_time * 2.3f + i));
    }

    // ---------- drawing ----------

    public override void _Draw()
    {
        var items = new List<(float y, Action draw)>();
        if (M != null && M.FarmsChanged) { _farms = null; M.FarmsChanged = false; }
        // Farms lie flat on the ground, under everything else.
        foreach (var fb in W.Buildings)
            if (fb.Type == BuildingType.Farm && (fb.Team == Team || Explored(fb.Pos)))
            {
                var ft = BuildingTexture(BuildingType.Farm, fb.Team);
                DrawTexture(ft, Px(fb.Pos) - new Vector2(24, 24));
            }
        _farms ??= Enumerable.Range(0, W.Width * W.Height).Where(i => W.Map[i] == Terrain.Farmland)
            .Select(i => new Vector2(i % W.Width, i / W.Width)).ToList();
        var res = ArtLibrary.Tex("terrain/resources");
        foreach (var f in _farms)
        {
            if (!Explored(new SVec(f.X + 0.5f, f.Y + 0.5f))) continue;
            var at = f * Tile;
            int frame = (int)(_time * 1.2f + f.X * 0.37f + f.Y * 0.21f) % 2;
            items.Add((at.Y + 12, () => DrawTextureRectRegion(res, new Rect2(at, new Vector2(16, 16)), new Rect2(frame * 16, 0, 16, 16))));
        }

        foreach (var n in W.Nodes)
        {
            if (n.Amount <= 0f || !Explored(n.Pos)) continue;
            var node = n;
            items.Add((Px(n.Pos).Y + 4, () => DrawNode(node)));
        }
        foreach (var b in W.Buildings)
        {
            if (b.Team != Team && !Explored(b.Pos)) continue;
            if (b.Type == BuildingType.Merchant && M?.MerchantMind == null) continue;
            if (b.Type == BuildingType.Farm || b.Team > 2) continue;
            var bb = b;
            items.Add((Px(b.Pos).Y + b.Radius * Tile, () => DrawBuilding(bb)));
        }
        foreach (var u in W.Units)
        {
            if (!u.Active || u.CarriedBy >= 0 || !CanSee(u)) continue;
            var uu = u;
            items.Add((DrawPos(u).Y, () => DrawUnit(uu)));
        }
        foreach (var (y, draw) in items.OrderBy(i => i.y)) draw();

        foreach (var u in Selected)
        {
            if (!u.Active) continue;
            if (u.Order == OrderKind.None) continue;
            var target = u.Order switch
            {
                OrderKind.Gather when u.OrderTarget >= 0 && u.OrderTarget < W.Nodes.Count => W.Nodes[u.OrderTarget].Pos,
                OrderKind.Capture or OrderKind.Heal when u.OrderTarget >= 0 && u.OrderTarget < W.Units.Count => W.Units[u.OrderTarget].Pos,
                _ => u.OrderPos,
            };
            var col = u.Order switch
            {
                OrderKind.Capture or OrderKind.Raid or OrderKind.Sabotage => new Color("ff7050"),
                OrderKind.Guard or OrderKind.Hold => new Color("f2d24b"),
                OrderKind.Build => new Color("c9925f"),
                OrderKind.Gather => new Color("8ad67a"),
                _ => new Color("ffffff"),
            };
            DrawDashedLine(DrawPos(u), Px(target), col with { A = 0.5f }, 1f, 4f);
            if (u.Order == OrderKind.Guard) DrawEllipse(Px(u.OrderPos), 7f * Tile, 7f * Tile * 0.5f, col with { A = 0.25f });
        }
        // The Cradle's rally flag.
        if (M != null && M.Kingdoms[Team].Rally is { } rally)
        {
            var rp = Px(rally);
            DrawLine(rp, rp + new Vector2(0, -14), new Color("63432c"), 1.5f);
            DrawColoredPolygon([rp + new Vector2(0, -14), rp + new Vector2(8, -11), rp + new Vector2(0, -8)], TeamColors[Team]);
            if (SelectedBuilding is { Type: BuildingType.Cradle } c) DrawDashedLine(Px(c.Pos), rp, TeamColors[Team] with { A = 0.6f }, 1f, 4f);
        }
        // Fireflies drift along forest edges at night.
        if (W.IsNight)
        {
            _fireflySpots ??= Enumerable.Range(0, W.Width * W.Height)
                .Where(i => W.Map[i] == Terrain.Forest && i % 7 == 0)
                .Select(i => new Vector2(i % W.Width, i / W.Width) * Tile + new Vector2(8, 8)).ToList();
            for (int i = 0; i < _fireflySpots.Count; i++)
            {
                var p0 = _fireflySpots[i];
                if (!Explored(new SVec(p0.X / Tile, p0.Y / Tile))) continue;
                float ph = i * 1.37f;
                var p1 = p0 + new Vector2(MathF.Sin(_time * 0.7f + ph) * 10f, MathF.Cos(_time * 0.5f + ph * 1.3f) * 7f);
                float a = MathF.Max(0f, MathF.Sin(_time * 1.6f + ph * 2.1f));
                if (a < 0.05f) continue;
                DrawCircle(p1, 2.2f, new Color(0.85f, 1f, 0.5f, 0.18f * a));
                DrawCircle(p1, 0.9f, new Color(0.95f, 1f, 0.7f, 0.9f * a));
            }
        }
        foreach (var f in _fx) DrawFx(f);
    }

    void DrawNode(ResourceNode n)
    {
        var p = Px(n.Pos);
        var res = ArtLibrary.Tex("terrain/resources");
        switch (n.Type)
        {
            case ResourceType.Food:
            {
                int frame = (int)(_time * 1.5f + p.X * 0.1f) % 2;
                DrawTextureRectRegion(res, new Rect2(p - new Vector2(8, 10), new Vector2(16, 16)), new Rect2(frame * 16, 0, 16, 16));
                break;
            }
            case ResourceType.Timber:
                DrawTextureRectRegion(res, new Rect2(p - new Vector2(8, 10), new Vector2(16, 16)), new Rect2(48, 0, 16, 16));
                break;
            case ResourceType.Stone:
                DrawTextureRectRegion(res, new Rect2(p - new Vector2(8, 10), new Vector2(16, 16)), new Rect2(32, 0, 16, 16));
                break;
            case ResourceType.Lumen:
            {
                int frame = (int)(_time * 3f + p.Y) % 3;
                DrawTextureRectRegion(ArtLibrary.Tex("terrain/lumen"), new Rect2(p - new Vector2(12, 18), new Vector2(24, 24)), new Rect2(frame * 24, 0, 24, 24));
                break;
            }
        }
    }

    static string Art(BuildingType t) => t switch
    {
        BuildingType.Keep => "Keep", BuildingType.House => "House", BuildingType.Cradle => "Cradle", BuildingType.Camp => "Camp",
        BuildingType.FusionAltar => "FusionAltar", BuildingType.TrainingGrounds => "TrainingGrounds", BuildingType.Watchtower => "Watchtower",
        BuildingType.Dungeon => "Dungeon", BuildingType.MutationPit => "MutationPit", BuildingType.LumenSpire => "LumenSpire",
        BuildingType.Market => "Market", BuildingType.Infirmary => "Infirmary", BuildingType.AncientAltar => "AncientAltar",
        BuildingType.Merchant => "Merchant", BuildingType.Den => "Den", BuildingType.Farm => "Farm", BuildingType.Manor => "Manor",
        BuildingType.Granary => "Granary", BuildingType.LumberMill => "LumberMill", BuildingType.GuardPost => "GuardPost",
        BuildingType.Shrine => "Shrine", BuildingType.LumenExtractor => "LumenExtractor",
        BuildingType.Wall => "Wall", BuildingType.Gate => "Gate", _ => "House",
    };

    public static Texture2D BuildingTexture(BuildingType t, int team) => ArtLibrary.Tex($"buildings/{Art(t)}_{ArtLibrary.TeamName(team)}");

    void DrawBuilding(Building b)
    {
        var foot = Px(b.Pos) + new Vector2(0, b.Radius * Tile);
        if (b == SelectedBuilding) DrawEllipse(foot - new Vector2(0, b.Radius * Tile * 0.4f), b.Radius * Tile + 4, b.Radius * Tile * 0.5f + 2, Colors.White);
        if (b.Type == BuildingType.BuildSite && Match.IsWall(b.SiteFor))
        {
            var wt = BuildingTexture(b.SiteFor, b.Team);
            float wf = Math.Clamp(b.BuildProgress / Defs.Of(b.SiteFor).Work, 0f, 1f);
            DrawTexture(wt, foot - new Vector2(wt.GetWidth() / 2f, wt.GetHeight()), new Color(1, 1, 1, 0.3f + 0.5f * wf));
            DrawRect(new Rect2(foot + new Vector2(-6, 1), new Vector2(12 * wf, 2)), new Color("f2d24b"));
            return;
        }
        if (b.Type == BuildingType.BuildSite)
        {
            float work = Defs.Buildings.FirstOrDefault(d => d.Type == b.SiteFor)?.Work ?? 100f;
            float f = Math.Clamp(b.BuildProgress / work, 0f, 1f);
            var site = ArtLibrary.Tex("buildings/site");
            int frame = Math.Min(3, (int)(f * 4));
            var ghost = BuildingTexture(b.SiteFor, b.Team);
            DrawTexture(ghost, foot - new Vector2(ghost.GetWidth() / 2f, ghost.GetHeight()), new Color(1, 1, 1, 0.15f + 0.5f * f));
            DrawTextureRectRegion(site, new Rect2(foot - new Vector2(20, 36), new Vector2(40, 36)), new Rect2(frame * 40, 0, 40, 36));
            DrawRect(new Rect2(foot + new Vector2(-14, 2), new Vector2(28, 3)), new Color(0, 0, 0, 0.6f));
            DrawRect(new Rect2(foot + new Vector2(-14, 2), new Vector2(28 * f, 3)), new Color("f2d24b"));
            return;
        }
        var tex = BuildingTexture(b.Type, b.Team);
        var mod = b.DisabledTimer > 0 ? new Color(0.6f, 0.5f, 0.5f) : Colors.White;
        DrawTexture(tex, foot - new Vector2(tex.GetWidth() / 2f, tex.GetHeight()), mod);
        if (b.Type == BuildingType.AncientAltar && M?.AltarHolder(b) is int holder and >= 0)
            DrawEllipse(foot - new Vector2(0, 4), 18, 6, TeamColors[holder]);
        if (b.Type == BuildingType.LumenSpire && b.Charge > 0)
        {
            DrawRect(new Rect2(foot + new Vector2(-18, 3), new Vector2(36, 3)), new Color(0, 0, 0, 0.6f));
            DrawRect(new Rect2(foot + new Vector2(-18, 3), new Vector2(36 * Math.Min(1f, b.Charge / Defs.SpireGoal), 3)), new Color("8ff3ff"));
        }
        if (b.DisabledTimer > 0 && (int)(_time * 4) % 2 == 0)
            DrawTextureRectRegion(ArtLibrary.Tex("fx/dust"), new Rect2(foot - new Vector2(8, 24), new Vector2(16, 16)), new Rect2(0, 0, 16, 16), new Color(1, 0.6f, 0.4f));
        if (b.SabotageProgress > 0f)
            DrawRect(new Rect2(foot + new Vector2(-14, -tex.GetHeight() - 4), new Vector2(28 * b.SabotageProgress / Rules.SabotageTime, 2)), Colors.OrangeRed);
    }

    void DrawEllipse(Vector2 c, float rx, float ry, Color col, float width = 1.2f)
    {
        var pts = new Vector2[25];
        for (int i = 0; i <= 24; i++)
        {
            float a = i / 24f * Mathf.Tau;
            pts[i] = c + new Vector2(MathF.Cos(a) * rx, MathF.Sin(a) * ry);
        }
        DrawPolyline(pts, col, width);
    }

    void FilledEllipse(Vector2 c, float rx, float ry, Color col)
    {
        var pts = new Vector2[20];
        for (int i = 0; i < 20; i++)
        {
            float a = i / 20f * Mathf.Tau;
            pts[i] = c + new Vector2(MathF.Cos(a) * rx, MathF.Sin(a) * ry);
        }
        DrawColoredPolygon(pts, col);
    }

    void DrawUnit(Unit u)
    {
        var feet = DrawPos(u);
        _anim.TryGetValue(u.Id, out var a);
        int fs = ArtLibrary.FrameSize(u);
        var sheet = ArtLibrary.SheetFor(u);
        bool hover = u.Species is Species.Sprite or Species.MothKin;
        var team = TeamColors[Math.Min(u.Team, 2)];
        var aura = u.Mind?.AuraColor() ?? team;

        // Shadow, aura glow and team ring under the feet.
        FilledEllipse(feet + new Vector2(0, 1), fs * 0.32f, fs * 0.12f, new Color(0, 0, 0, hover ? 0.18f : 0.32f));
        if (u.Mind != null) FilledEllipse(feet, fs * 0.42f, fs * 0.17f, aura with { A = u.Mind.Has(Tier.Legendary) ? 0.5f : 0.22f });
        DrawEllipse(feet, fs * 0.38f, fs * 0.15f, team with { A = 0.85f }, 1.2f);
        if (Selected.Contains(u)) DrawEllipse(feet, fs * 0.5f, fs * 0.21f, Colors.White, 1.5f);
        if (M != null && (u == M.Royal[0] || u == M.Royal[1])) DrawEllipse(feet, fs * 0.6f, fs * 0.26f, new Color("ffd700"), 2f);

        if (u.Subdued)
        {
            DrawSetTransform(feet + new Vector2(0, -4), Mathf.Pi / 2f, Vector2.One);
            DrawTextureRectRegion(sheet, new Rect2(-fs / 2f, -fs / 2f, fs, fs), new Rect2(0, 0, fs, fs), new Color(0.65f, 0.65f, 0.75f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            DrawString(_font, feet + new Vector2(4, -10 - (_time * 6 % 6)), "z", fontSize: 9, modulate: new Color(1, 1, 1, 0.8f));
            return;
        }

        int dir = a?.Dir ?? 0;
        bool moving = a?.Moving ?? false;
        int col = moving ? 2 + (int)a!.Walk % 4 : (int)(_time * 2f + u.Id * 0.37f) % 2;
        var offset = Vector2.Zero;
        if (u.TussleTarget >= 0) offset = new Vector2(MathF.Sin(_time * 40f + u.Id) * 1.2f, 0);
        else if (u.LastAction is (int)Act.Gather or (int)Act.Build or (int)Act.Steal or (int)Act.Sabotage && !moving)
            offset = new Vector2(0, -MathF.Abs(MathF.Sin(_time * 8f + u.Id)) * 1.5f);
        if (hover) offset.Y -= 3 + MathF.Sin(_time * 3f + u.Id) * 1.5f;
        var dst = new Rect2(feet + offset - new Vector2(fs / 2f, fs - 1), new Vector2(fs, fs));
        DrawTextureRectRegion(sheet, dst, new Rect2(col * fs, dir * fs, fs, fs));

        // A captive being carried rides on the carrier's shoulders.
        if (u.CarryingUnit >= 0)
        {
            var cu = W.Units[u.CarryingUnit];
            int cfs = ArtLibrary.FrameSize(cu);
            DrawSetTransform(feet + new Vector2(0, -fs - 2), Mathf.Pi / 2f, Vector2.One * 0.85f);
            DrawTextureRectRegion(ArtLibrary.SheetFor(cu), new Rect2(-cfs / 2f, -cfs / 2f, cfs, cfs), new Rect2(0, 0, cfs, cfs), new Color(0.7f, 0.7f, 0.8f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
        if (u.CarryAmount > 0f) DrawCarry(u, feet + new Vector2(fs * 0.35f, -fs + 2));

        if (u.Will < u.Body.Will)
        {
            float f = u.Will / u.Body.Will;
            var bar = new Rect2(feet + new Vector2(-6, -fs - 4), new Vector2(12, 2));
            DrawRect(bar, new Color(0, 0, 0, 0.6f));
            DrawRect(new Rect2(bar.Position, new Vector2(12 * f, 2)), Color.FromHsv(f * 0.33f, 0.8f, 0.95f));
        }
    }

    void DrawCarry(Unit u, Vector2 at)
    {
        var c = u.CarryType switch
        {
            ResourceType.Food => new Color("f0cf5a"), ResourceType.Lumen => new Color("8ff3ff"),
            ResourceType.Timber => new Color("94653f"), ResourceType.Gold => new Color("f6c544"), _ => new Color("9796a8"),
        };
        DrawRect(new Rect2(at - new Vector2(2.5f, 2.5f), new Vector2(5, 5)), new Color("1d1626"));
        DrawRect(new Rect2(at - new Vector2(1.5f, 1.5f), new Vector2(3, 3)), c);
    }

    void DrawFx(Fx f)
    {
        float t = (_time - f.Start) / f.Life;
        int frame = Math.Min(3, (int)(t * 4));
        var tex = ArtLibrary.Tex($"fx/{f.Kind}");
        float s = 16 * f.Scale;
        var p = f.Pos + new Vector2(0, f.Kind is "coin" or "heart" or "leaf" ? -t * 10 : 0);
        DrawTextureRectRegion(tex, new Rect2(p - new Vector2(s / 2, s / 2), new Vector2(s, s)), new Rect2(frame * 16, 0, 16, 16), new Color(1, 1, 1, 1 - t * 0.6f));
    }

    // Drawn above the canopy: hidden-in-forest markers, fog, placement ghost, drag box.
    sealed partial class Overlay : Node2D
    {
        public MatchView View = null!;

        public override void _Draw()
        {
            var v = View;
            var m = v.M;
            foreach (var u in v.W.Units)
            {
                if (!u.Active || u.Team != v.Team || u.CarriedBy >= 0) continue;
                if (v.W.TerrainAt(u.Pos) != Terrain.Forest) continue;
                DrawCircle(v.DrawPos(u) + new Vector2(0, -8), 2.5f, TeamColors[u.Team] with { A = 0.75f });
            }
            var size = new Vector2(v.W.Width, v.W.Height) * Tile;
            DrawTextureRect(v._fog, new Rect2(Vector2.Zero, size), false);

            if (v.Placing is { } wt && Match.IsWall(wt) && v.WallDragStart is { } ws)
            {
                var k = m.Kingdoms[v.Team];
                foreach (var tile in WallLine(ToWorld(ws), ToWorld(v.MouseWorld)))
                {
                    var ok = m.CanPlace(k, wt, tile) == null;
                    var tex = BuildingTexture(wt, v.Team);
                    var foot = Px(tile) + new Vector2(0, 8);
                    DrawTexture(tex, foot - new Vector2(tex.GetWidth() / 2f, tex.GetHeight()), ok ? new Color(0.7f, 1f, 0.7f, 0.6f) : new Color(1f, 0.5f, 0.5f, 0.45f));
                }
            }
            else if (v.Placing is { } t)
            {
                var def = Defs.Of(t);
                var at = Match.Snap(t, ToWorld(v.MouseWorld));
                var err = m.CanPlace(m.Kingdoms[v.Team], t, at);
                var tex = BuildingTexture(t, v.Team);
                var foot = Px(at) + new Vector2(0, def.Radius * Tile);
                // Show the footprint tile by tile: green where it can go, red where it can't.
                float r = t == BuildingType.Farm ? 1.5f : def.Radius;
                for (int ty = (int)MathF.Floor(at.Y - r + 0.01f); ty <= (int)MathF.Floor(at.Y + r - 0.01f); ty++)
                for (int tx = (int)MathF.Floor(at.X - r + 0.01f); tx <= (int)MathF.Floor(at.X + r - 0.01f); tx++)
                {
                    var tt = m.W.TerrainAt(new SVec(tx + 0.5f, ty + 0.5f));
                    bool ok = tt == Terrain.Grass || (tt == Terrain.Hills && t != BuildingType.Farm);
                    DrawRect(new Rect2(tx * Tile, ty * Tile, Tile, Tile), ok ? new Color(0.4f, 1f, 0.4f, 0.18f) : new Color(1f, 0.3f, 0.3f, 0.35f));
                }
                DrawTexture(tex, foot - new Vector2(tex.GetWidth() / 2f, tex.GetHeight()),
                    err == null ? new Color(0.7f, 1f, 0.7f, 0.7f) : new Color(1f, 0.5f, 0.5f, 0.55f));
                DrawString(v._font, foot + new Vector2(-30, 12), err ?? def.Name, fontSize: 9, modulate: Colors.White);
            }
            if (v.DragBox is { } box)
            {
                DrawRect(box, new Color(1, 1, 1, 0.08f));
                DrawRect(box, new Color(1, 1, 1, 0.7f), false, 1f);
            }
        }
    }

    public Unit? UnitAt(Vector2 world, float radius = 10f)
    {
        Unit? best = null;
        float bestD = radius;
        foreach (var u in W.Units)
        {
            if (!u.Active || !CanSee(u)) continue;
            float d = (DrawPos(u) + new Vector2(0, -6)).DistanceTo(world);
            if (d < bestD) { bestD = d; best = u; }
        }
        return best;
    }

    // Tile centres on a straight line between two points (for laying walls).
    public static List<SVec> WallLine(SVec a, SVec b)
    {
        int x0 = (int)a.X, y0 = (int)a.Y, x1 = (int)b.X, y1 = (int)b.Y;
        var list = new List<SVec>();
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        for (int guard = 0; guard < 200; guard++)
        {
            list.Add(new SVec(x0 + 0.5f, y0 + 0.5f));
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
        return list;
    }

    public int NodeAt(Vector2 world, float radius = 11f)
    {
        int best = -1;
        float bestD = radius;
        for (int i = 0; i < W.Nodes.Count; i++)
        {
            var n = W.Nodes[i];
            if (n.Amount <= 0f || !Explored(n.Pos)) continue;
            float d = (Px(n.Pos) + new Vector2(0, -4)).DistanceTo(world);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    // A pending targeted order (G = guard spot, C = capture): the next left click picks the target.
    public OrderKind? Pending;
    public Vector2? WallDragStart;
    public float Gloom;               // set by the weather
    List<Vector2>? _fireflySpots;   // world px where a wall line drag began

    public Building? BuildingAt(Vector2 world)
    {
        foreach (var b in W.Buildings.OrderByDescending(b => b.Pos.Y))
        {
            if (b.Team != Team && !Explored(b.Pos)) continue;
            if (b.Type == BuildingType.Merchant && M?.MerchantMind == null) continue;
            var foot = Px(b.Pos) + new Vector2(0, b.Radius * Tile);
            var tex = b.Type == BuildingType.BuildSite ? null : BuildingTexture(b.Type, b.Team);
            float w = tex?.GetWidth() ?? b.Radius * Tile * 2, h = tex?.GetHeight() ?? b.Radius * Tile * 2;
            if (new Rect2(foot - new Vector2(w / 2, h), new Vector2(w, h)).HasPoint(world)) return b;
        }
        return null;
    }
}
