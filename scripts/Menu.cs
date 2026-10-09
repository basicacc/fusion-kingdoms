using FusionKingdoms.Sim;
using Godot;

namespace FusionKingdoms;

public partial class Menu : Control
{
    Label _status = null!;
    Button _start = null!;

    Action? _autoStart;

    public override void _ExitTree()
    {
        NetSession.I.Changed -= Refresh;
        if (_autoStart != null) NetSession.I.Changed -= _autoStart;
    }

    void Refresh()
    {
        var net = NetSession.I;
        _status.Text = net.Status;
        _start.Visible = net.Role == NetRole.Host;
        _start.Disabled = net.RemoteLibrary == null;
    }

    Control LanBox()
    {
        var net = NetSession.I;
        var box = Ui.Panel(Ui.BgSoft, 10);
        var v = Ui.VBox(6);
        v.AddChild(Ui.Label("LAN 1v1", 14, Ui.Accent));
        var nameRow = Ui.HBox();
        nameRow.AddChild(Ui.Label("Your name", 12));
        var name = new LineEdit { Text = net.LocalName, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        name.TextChanged += t => net.LocalName = t.Length > 0 ? t : "Player";
        nameRow.AddChild(name);
        v.AddChild(nameRow);
        var row = Ui.HBox();
        row.AddChild(Ui.Button("Host", () => { var e = net.Host(); if (e != null) _status.Text = e; }, true));
        var ip = new LineEdit { PlaceholderText = "host address, e.g. 192.168.1.20", SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(200, 0) };
        row.AddChild(ip);
        row.AddChild(Ui.Button("Join", () => { var e = net.Join(ip.Text.Length > 0 ? ip.Text : "127.0.0.1"); if (e != null) _status.Text = e; }));
        v.AddChild(row);
        _start = Ui.Button("Start the match", () => net.StartMatch());
        v.AddChild(_start);
        _status = Ui.Label("", 11, Ui.Muted, true);
        v.AddChild(_status);
        box.AddChild(v);
        net.Changed += Refresh;
        Refresh();
        return box;
    }

    static OptionButton Choice(string[] items, int selected, Action<int> set, string tip)
    {
        var o = new OptionButton { TooltipText = tip, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var it in items) o.AddItem(it);
        o.Select(selected);
        o.ItemSelected += i => set((int)i);
        return o;
    }

    // The map is generated fresh every match; these shape it.
    static Control SetupGrid()
    {
        var g = new GridContainer { Columns = 3 };
        g.AddThemeConstantOverride("h_separation", 6);
        g.AddThemeConstantOverride("v_separation", 4);
        var s = MatchScene.Settings;
        void Set(MapSettings n) => MatchScene.Settings = n;
        float[] amounts = [0.6f, 1f, 1.6f];
        int Pick(float v, float[] opts) => Array.IndexOf(opts, opts.OrderBy(o => MathF.Abs(o - v)).First());
        float[] minutes = [10f, 15f, 20f, 30f];
        g.AddChild(Choice(["Map: Small", "Map: Normal", "Map: Large"], (int)s.Size, i => Set(MatchScene.Settings with { Size = (MapSize)i }), "Map size"));
        g.AddChild(Choice(["Style: Random", "Style: Valleys", "Style: Highlands", "Style: Wildwood", "Style: Lakes", "Style: Plains"], (int)s.Style,
            i => Set(MatchScene.Settings with { Style = (MapStyle)i }), "What kind of land to generate"));
        g.AddChild(Choice(["10 minutes", "15 minutes", "20 minutes", "30 minutes"], Pick(s.Minutes, minutes),
            i => Set(MatchScene.Settings with { Minutes = minutes[i] }), "Match length"));
        g.AddChild(Choice(["Resources: Scarce", "Resources: Normal", "Resources: Rich"], Pick(s.Resources, amounts),
            i => Set(MatchScene.Settings with { Resources = amounts[i] }), "How much timber, stone and Lumen the land holds"));
        g.AddChild(Choice(["Start: Poor", "Start: Normal", "Start: Wealthy"], Pick(s.StartingStock, amounts),
            i => Set(MatchScene.Settings with { StartingStock = amounts[i] }), "What each Keep starts with"));
        g.AddChild(Choice(["Wildlife: Few", "Wildlife: Normal", "Wildlife: Many"], Pick(s.Wildlife, amounts),
            i => Set(MatchScene.Settings with { Wildlife = amounts[i] }), "How many wild creatures roam"));
        return g;
    }

    // Window mode and size (F11 toggles fullscreen anywhere).
    static Control DisplayRow()
    {
        var row = Ui.HBox(6);
        row.AddChild(Ui.Label("Display", 12, Ui.Muted));
        var mode = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = "F11 toggles fullscreen at any time" };
        foreach (var m in new[] { "Windowed", "Fullscreen", "Borderless fullscreen" }) mode.AddItem(m);
        mode.Select((int)Display.I.Current);
        var size = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = "Window size (you can also drag the window edges)" };
        foreach (var s in Display.Sizes) size.AddItem($"{s.X} x {s.Y}");
        int cur = Array.IndexOf(Display.Sizes, Display.I.WindowSize);
        if (cur < 0) { size.AddItem($"{Display.I.WindowSize.X} x {Display.I.WindowSize.Y} (custom)"); cur = Display.Sizes.Length; }
        size.Select(cur);
        size.Disabled = Display.I.Current != Display.Mode.Windowed;
        mode.ItemSelected += i =>
        {
            Display.I.SetMode((Display.Mode)(int)i);
            size.Disabled = i != 0;
        };
        size.ItemSelected += i => { if (i < Display.Sizes.Length) Display.I.SetSize(Display.Sizes[i]); };
        row.AddChild(mode);
        row.AddChild(size);
        return row;
    }

    static Control VolumeRow()
    {
        var row = Ui.HBox(8);
        HSlider Slider(string name, float value, Action<float> set)
        {
            row.AddChild(Ui.Label(name, 12, Ui.Muted));
            var s = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = value, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0) };
            s.ValueChanged += x => set((float)x);
            row.AddChild(s);
            return s;
        }
        Slider("Music", Sound.I.MusicVolume, x => Sound.I.SetVolumes(x, Sound.I.SfxVolume));
        Slider("Sound", Sound.I.SfxVolume, x => { Sound.I.SetVolumes(Sound.I.MusicVolume, x); Sound.I.Play("coin", -4f, 0f, 0.15f); });
        return row;
    }

    public override void _Ready()
    {
        Sound.I.Music("menu");
        SetAnchorsPreset(LayoutPreset.FullRect);
        // A living kingdom behind the menu, in its own viewport so its camera doesn't move the UI.
        var holder = new SubViewportContainer { Stretch = true };
        holder.SetAnchorsPreset(LayoutPreset.FullRect);
        holder.MouseFilter = MouseFilterEnum.Ignore;
        var vp = new SubViewport { HandleInputLocally = false, CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Nearest };
        vp.AddChild(new MenuBackdrop());
        holder.AddChild(vp);
        AddChild(holder);
        var shade = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = new Gradient { Colors = [new Color(0.03f, 0.03f, 0.06f, 0.82f), new Color(0.03f, 0.03f, 0.06f, 0.1f)], Offsets = [0f, 1f] },
                Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.2f, 0.5f), FillTo = new Vector2(1.25f, 0.5f), Width = 256, Height = 256,
            },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        shade.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(shade);

        var v = Ui.VBox(8);
        v.CustomMinimumSize = new Vector2(460, 0);
        var title = Ui.Label("FUSION KINGDOMS", 52, Ui.Accent);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_outline_color", new Color("3a2410"));
        title.AddThemeConstantOverride("outline_size", 10);
        title.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
        title.AddThemeConstantOverride("shadow_offset_y", 5);
        v.AddChild(title);
        var sub = Ui.Label("Breed real AI minds to build a kingdom, then steal your rival's best creations.", 15, Ui.Muted, true);
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        v.AddChild(sub);
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });

        var personality = new OptionButton();
        personality.AddItem("The Breeder (fusion-heavy)");
        personality.AddItem("The Warden (defensive, Dungeon-heavy)");
        personality.AddItem("The Archmage (Mutation-heavy, chaotic)");
        personality.Select((int)MatchScene.Personality);
        var top = Ui.HBox(6);
        personality.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        top.AddChild(personality);
        top.AddChild(Choice(["Easy", "Normal", "Hard"], (int)MatchScene.Difficulty, i => MatchScene.Difficulty = (AiDifficulty)i, "How hard the AI ruler plays"));
        v.AddChild(Ui.Label("Opponent", 13, Ui.Muted));
        v.AddChild(top);
        v.AddChild(SetupGrid());
        var play = Ui.Button("Play vs the AI Ruler", () =>
        {
            MatchScene.Personality = (AiPersonality)personality.Selected;
            MatchScene.ShowcaseMode = false;
            MatchScene.Seed = 0;
            GetTree().ChangeSceneToFile("res://scenes/Match.tscn");
        });
        play.CustomMinimumSize = new Vector2(0, 48);
        v.AddChild(play);
        // AI vs AI: pick Blue's personality; Red uses the Opponent choice above.
        var watchRow = Ui.HBox(6);
        var blue = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = "Blue's ruler" };
        foreach (var n in new[] { "Blue: The Breeder", "Blue: The Warden", "Blue: The Archmage" }) blue.AddItem(n);
        blue.Select((int)MatchScene.BluePersonality);
        var watch = Ui.Button("Watch AI vs AI", () =>
        {
            MatchScene.Personality = (AiPersonality)personality.Selected;
            MatchScene.BluePersonality = (AiPersonality)blue.Selected;
            MatchScene.ShowcaseMode = true;
            MatchScene.Seed = 0;
            GetTree().ChangeSceneToFile("res://scenes/Match.tscn");
        }, true, "Two AI rulers play each other; the camera follows the action.");
        watch.CustomMinimumSize = new Vector2(0, 40);
        watchRow.AddChild(watch);
        watchRow.AddChild(blue);
        v.AddChild(watchRow);
        v.AddChild(Ui.Button("Campaign (ten realms, roguelite)", () => GetTree().ChangeSceneToFile("res://scenes/Campaign.tscn")));
        v.AddChild(LanBox());
        var lab = Ui.Button("Mind Lab (sandbox: fuse, train, mutate freely)", () => GetTree().ChangeSceneToFile("res://scenes/MindLab.tscn"));
        lab.CustomMinimumSize = new Vector2(0, 40);
        v.AddChild(lab);
        var lib0 = MindLibrary.I;
        v.AddChild(Ui.Button($"Codex ({lib0.Codex.Count} of {lib0.Factory.Traits.Traits.Count} traits discovered)", () => GetTree().ChangeSceneToFile("res://scenes/Codex.tscn")));
        v.AddChild(VolumeRow());
        v.AddChild(DisplayRow());
        var tutRow = Ui.HBox(8);
        var tutOn = new CheckBox { Text = "Show tutorial hints", ButtonPressed = Tutorial.Enabled };
        tutOn.Toggled += on => Tutorial.Enabled = on;
        tutRow.AddChild(tutOn);
        tutRow.AddChild(Ui.Button("Restart tutorial", () => { Tutorial.Reset(); tutOn.ButtonPressed = true; }));
        v.AddChild(tutRow);
        v.AddChild(Ui.Button("Quit", () => GetTree().Quit()));
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 2) });
        var lib = Ui.Label($"Your Library holds {MindLibrary.I.All.Count} minds. Minds you create in Mind Lab or win in matches carry over.", 12, Ui.Muted, true);
        lib.HorizontalAlignment = HorizontalAlignment.Center;
        v.AddChild(lib);

        // Developer hook: FK_LAN=host auto-hosts and starts when someone joins; FK_LAN=join joins 127.0.0.1.
        var lan = OS.GetEnvironment("FK_LAN");
        var net = NetSession.I;
        if (lan == "host" && net.Role == NetRole.Local)
        {
            net.Host();
            _autoStart = () => { if (net.Role == NetRole.Host && net.RemoteLibrary != null) net.StartMatch(); };
            net.Changed += _autoStart;
        }
        else if (lan == "join" && net.Role == NetRole.Local) net.Join("127.0.0.1");

        // The menu fits any window height: it scrolls when the window is short.
        var frame = Ui.Panel(null, 14);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        v.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(v);
        frame.AddChild(scroll);
        frame.SizeFlagsVertical = SizeFlags.ExpandFill;
        frame.CustomMinimumSize = new Vector2(v.CustomMinimumSize.X + 40, 0);
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 60);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        column.AddChild(frame);
        margin.AddChild(column);
        AddChild(margin);
    }
}
