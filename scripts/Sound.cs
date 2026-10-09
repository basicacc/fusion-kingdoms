using Godot;

namespace FusionKingdoms;

// Plays the synthesized sounds (tools/audio/synth.py): pooled effects, positional effects in the world,
// and crossfading music. Volumes live on the Music and Sfx buses and are saved between sessions.
public partial class Sound : Node
{
    public static Sound I { get; private set; } = null!;

    const string SettingsPath = "user://settings.cfg";
    readonly Dictionary<string, AudioStreamWav> _streams = new();
    readonly Dictionary<string, ulong> _last = new();
    readonly List<AudioStreamPlayer> _ui = new();
    readonly List<AudioStreamPlayer2D> _world = new();
    AudioStreamPlayer _musicA = null!, _musicB = null!;
    string _music = "";
    Tween? _fade;

    static readonly bool Log = OS.GetEnvironment("FK_SOUNDLOG") == "1";
    public float MusicVolume { get; private set; } = 0.6f;
    public float SfxVolume { get; private set; } = 0.8f;

    public override void _Ready()
    {
        I = this;
        ProcessMode = ProcessModeEnum.Always;
        foreach (var bus in new[] { "Music", "Sfx" })
        {
            if (AudioServer.GetBusIndex(bus) >= 0) continue;
            AudioServer.AddBus();
            int i = AudioServer.BusCount - 1;
            AudioServer.SetBusName(i, bus);
            AudioServer.SetBusSend(i, "Master");
        }
        for (int i = 0; i < 10; i++)
        {
            var p = new AudioStreamPlayer { Bus = "Sfx" };
            AddChild(p);
            _ui.Add(p);
        }
        for (int i = 0; i < 24; i++)
        {
            var p = new AudioStreamPlayer2D { Bus = "Sfx", MaxDistance = 900f, Attenuation = 1.4f };
            AddChild(p);
            _world.Add(p);
        }
        _musicA = new AudioStreamPlayer { Bus = "Music" };
        _musicB = new AudioStreamPlayer { Bus = "Music" };
        AddChild(_musicA);
        AddChild(_musicB);

        var cfg = new ConfigFile();
        if (cfg.Load(SettingsPath) == Error.Ok)
        {
            MusicVolume = (float)cfg.GetValue("audio", "music", MusicVolume);
            SfxVolume = (float)cfg.GetValue("audio", "sfx", SfxVolume);
        }
        Apply();

        // Every button in the game clicks.
        GetTree().NodeAdded += n =>
        {
            if (n is BaseButton b) b.Pressed += () => Play("click", -6f);
        };
    }

    public void SetVolumes(float music, float sfx)
    {
        MusicVolume = Math.Clamp(music, 0f, 1f);
        SfxVolume = Math.Clamp(sfx, 0f, 1f);
        Apply();
        var cfg = new ConfigFile();
        cfg.Load(SettingsPath);
        cfg.SetValue("audio", "music", MusicVolume);
        cfg.SetValue("audio", "sfx", SfxVolume);
        cfg.Save(SettingsPath);
    }

    void Apply()
    {
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Music"), Mathf.LinearToDb(MusicVolume * 0.8f));
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Sfx"), Mathf.LinearToDb(SfxVolume));
        AudioServer.SetBusMute(AudioServer.GetBusIndex("Music"), MusicVolume <= 0.001f);
        AudioServer.SetBusMute(AudioServer.GetBusIndex("Sfx"), SfxVolume <= 0.001f);
    }

    AudioStreamWav? Stream(string name)
    {
        if (_streams.TryGetValue(name, out var s)) return s;
        s = ResourceLoader.Exists($"res://assets/audio/{name}.wav") ? GD.Load<AudioStreamWav>($"res://assets/audio/{name}.wav") : null;
        if (s != null) _streams[name] = s;
        return s;
    }

    // Rate limit: the same sound never stacks closer than `gap` seconds.
    bool Ready(string name, float gap)
    {
        ulong now = Time.GetTicksMsec();
        if (_last.TryGetValue(name, out var t) && now - t < (ulong)(gap * 1000)) return false;
        _last[name] = now;
        return true;
    }

    float Pitch(float spread) => 1f + (float)GD.RandRange(-spread, spread);

    public void Play(string name, float db = 0f, float spread = 0.04f, float gap = 0.05f)
    {
        var s = Stream(name);
        if (s == null || !Ready(name, gap)) return;
        if (Log) GD.Print($"[sound] {name}");
        var p = _ui.FirstOrDefault(x => !x.Playing) ?? _ui[0];
        p.Stream = s;
        p.VolumeDb = db;
        p.PitchScale = Pitch(spread);
        p.Play();
    }

    // A sound with recorded variants (name_1, name_2...) picks one at random each time.
    AudioStreamWav? Variant(string name)
    {
        int n = 0;
        while (Stream($"{name}_{n + 1}") != null) n++;
        int pick = (int)(GD.Randi() % (uint)(n + 1));
        return pick == 0 ? Stream(name) : Stream($"{name}_{pick}");
    }

    public void PlayAt(string name, Vector2 world, float db = 0f, float spread = 0.08f, float gap = 0.08f)
    {
        var s = Variant(name);
        if (s == null || !Ready(name, gap)) return;
        var p = _world.FirstOrDefault(x => !x.Playing);
        if (p == null) return;
        if (Log) GD.Print($"[sound@] {name}");
        p.Stream = s;
        p.GlobalPosition = world;
        p.VolumeDb = db;
        p.PitchScale = Pitch(spread);
        p.Play();
    }

    // Looping background layers: ambience (birds, crickets) and weather (rain), each crossfading on change.
    readonly Dictionary<string, (AudioStreamPlayer player, string current)> _layers = new();

    public void Layer(string layer, string name, float db = -8f, float fade = 3f)
    {
        if (!_layers.TryGetValue(layer, out var l))
        {
            var p = new AudioStreamPlayer { Bus = "Sfx", VolumeDb = -60f };
            AddChild(p);
            l = (p, "");
            _layers[layer] = l;
        }
        if (l.current == name) return;
        _layers[layer] = (l.player, name);
        var player = l.player;
        var tw = CreateTween();
        if (player.Playing) tw.TweenProperty(player, "volume_db", -60f, fade * 0.5f);
        tw.TweenCallback(Callable.From(() =>
        {
            var s = name.Length > 0 ? Stream(name) : null;
            if (s == null) { player.Stop(); return; }
            s.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            s.LoopEnd = (int)(s.GetLength() * s.MixRate);
            player.Stream = s;
            player.Play();
        }));
        if (name.Length > 0) tw.TweenProperty(player, "volume_db", db, fade * 0.5f);
    }

    // Crossfade to another looping track ("" = silence).
    public void Music(string name, float fade = 2f)
    {
        if (name == _music) return;
        _music = name;
        if (Log) GD.Print($"[music] {name}");
        _fade?.Kill();
        var (from, to) = _musicA.Playing && _musicA.VolumeDb > -40f ? (_musicA, _musicB) : (_musicB, _musicA);
        _fade = CreateTween().SetParallel();
        if (from.Playing) _fade.TweenProperty(from, "volume_db", -60f, fade);
        var s = name.Length > 0 ? Stream($"music_{name}") : null;
        if (s != null)
        {
            s.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            s.LoopBegin = 0;
            s.LoopEnd = (int)(s.GetLength() * s.MixRate);
            to.Stream = s;
            to.VolumeDb = -40f;
            to.Play();
            _fade.TweenProperty(to, "volume_db", 0f, fade * 0.75f);
        }
        _fade.Chain().TweenCallback(Callable.From(() => { if (from.VolumeDb <= -59f) from.Stop(); }));
    }
}
