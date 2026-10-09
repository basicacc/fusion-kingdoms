using Godot;

namespace FusionKingdoms;

// Window mode and size, remembered in user://settings.cfg. F11 toggles fullscreen anywhere in the game.
public partial class Display : Node
{
    public enum Mode { Windowed, Fullscreen, Borderless }

    const string Settings = "user://settings.cfg";
    public static Display I { get; private set; } = null!;

    public static readonly Vector2I[] Sizes =
    [
        new(1280, 720), new(1366, 768), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3840, 2160),
    ];

    public Mode Current { get; private set; } = Mode.Windowed;
    public Vector2I WindowSize { get; private set; } = new(1600, 900);
    bool _testRun;

    public override void _Ready()
    {
        I = this;
        ProcessMode = ProcessModeEnum.Always;
        // Screenshot and test hooks run at the default window; don't touch it then.
        _testRun = OS.GetEnvironment("FK_SHOT").Length > 0 || OS.GetEnvironment("FK_UITEST").Length > 0 || OS.GetEnvironment("FK_RECORD").Length > 0;
        if (_testRun) return;
        var cfg = new ConfigFile();
        if (cfg.Load(Settings) == Error.Ok)
        {
            Current = (Mode)(int)cfg.GetValue("display", "mode", (int)Mode.Windowed);
            WindowSize = (Vector2I)cfg.GetValue("display", "size", WindowSize);
        }
        Apply();
    }

    public void SetMode(Mode m)
    {
        Current = m;
        Apply();
        Save();
    }

    public void SetSize(Vector2I size)
    {
        WindowSize = size;
        if (Current != Mode.Windowed) Current = Mode.Windowed;
        Apply();
        Save();
    }

    void Apply()
    {
        switch (Current)
        {
            case Mode.Fullscreen:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
                break;
            case Mode.Borderless:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
                break;
            default:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.ResizeDisabled, false);
                // Never larger than the screen the window is on; keep it centred.
                var screen = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
                var size = new Vector2I(Math.Min(WindowSize.X, screen.Size.X), Math.Min(WindowSize.Y, screen.Size.Y));
                DisplayServer.WindowSetSize(size);
                DisplayServer.WindowSetPosition(screen.Position + (screen.Size - size) / 2);
                break;
        }
    }

    void Save()
    {
        var cfg = new ConfigFile();
        cfg.Load(Settings);
        cfg.SetValue("display", "mode", (int)Current);
        cfg.SetValue("display", "size", WindowSize);
        cfg.Save(Settings);
    }

    // When the player drags the window edges, remember that size too.
    public override void _Process(double delta)
    {
        if (_testRun || Current != Mode.Windowed || DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed) return;
        var now = DisplayServer.WindowGetSize();
        if (now != WindowSize && Input.GetMouseButtonMask() == 0)
        {
            WindowSize = now;
            Save();
        }
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F11 })
        {
            SetMode(Current == Mode.Windowed ? Mode.Borderless : Mode.Windowed);
            GetViewport().SetInputAsHandled();
        }
    }
}
