using Godot;

namespace FusionKingdoms;

// Weather and ambience, for looks and sound only (the simulation doesn't change): spells of clear skies, rain and
// fog roll in over the match; rain brings thunder and lightning; birds sing by day and crickets chirp at night.
public partial class Weather : Control
{
    public enum Sky { Clear, Rain, Fog }

    readonly MatchScene _s;
    readonly RandomNumberGenerator _rng = new();
    Sky _sky = Sky.Clear;
    float _timer = 60f, _strength, _flash, _thunderIn = 20f, _time;
    readonly List<Vector2> _drops = new();

    public Sky Current => _sky;
    // How much the weather darkens the day (0 = not at all).
    public float Gloom => _sky switch { Sky.Rain => 0.28f, Sky.Fog => 0.12f, _ => 0f } * _strength;

    public Weather(MatchScene s, ulong seed)
    {
        _s = s;
        _rng.Seed = seed;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        // Developer hook: FK_WEATHER=rain|fog forces a sky for screenshots.
        var force = OS.GetEnvironment("FK_WEATHER");
        if (force.Length > 0 && Enum.TryParse<Sky>(force, true, out var sky)) { _sky = sky; _strength = 1f; _timer = 9999f; }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta * Math.Max(1f, _s.Speed) * (_s.Paused ? 0f : 1f);
        _time += (float)delta;
        _timer -= dt;
        if (_timer <= 0f)
        {
            float roll = _rng.Randf();
            _sky = roll < 0.55f ? Sky.Clear : roll < 0.85f ? Sky.Rain : Sky.Fog;
            _timer = _rng.RandfRange(80f, 170f);
        }
        _strength = Mathf.MoveToward(_strength, _sky == Sky.Clear ? 0f : 1f, (float)delta * 0.15f);

        bool night = _s.M.W.IsNight;
        Sound.I.Layer("ambience", night ? "ambient_night" : _sky == Sky.Rain && _strength > 0.5f ? "" : "ambient_day", night ? -12f : -14f);
        Sound.I.Layer("weather", _sky == Sky.Rain ? "rain" : "", -10f + 6f * _strength);

        if (_sky == Sky.Rain && _strength > 0.6f && (_thunderIn -= dt) <= 0f)
        {
            _thunderIn = _rng.RandfRange(15f, 45f);
            _flash = 1f;
            Sound.I.Play("thunder", -4f, 0.1f, 5f);
        }
        _flash = Mathf.MoveToward(_flash, 0f, (float)delta * 2.5f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        if (_sky == Sky.Rain && _strength > 0.05f)
        {
            // Streaks fall diagonally; the pattern scrolls with time so it never needs per-drop state.
            int count = (int)(260 * _strength);
            var col = new Color(0.8f, 0.86f, 1f, 0.5f * _strength);
            for (int i = 0; i < count; i++)
            {
                float fx = Hash(i * 3 + 1), fy = Hash(i * 7 + 2), sp = 0.6f + 0.4f * Hash(i * 11 + 5);
                float x = (fx * (size.X + 200f) + _time * 120f * sp) % (size.X + 200f) - 100f;
                float y = (fy * size.Y + _time * 700f * sp) % size.Y;
                DrawLine(new Vector2(x, y), new Vector2(x - 4f, y + 14f * sp), col, 1f);
            }
            DrawRect(new Rect2(Vector2.Zero, size), new Color(0.1f, 0.12f, 0.2f, 0.12f * _strength));
        }
        if (_sky == Sky.Fog && _strength > 0.05f)
        {
            // Soft drifting banks of fog.
            for (int i = 0; i < 14; i++)
            {
                float x = (Hash(i * 13) * (size.X + 600f) + _time * (10f + 8f * Hash(i))) % (size.X + 600f) - 300f;
                float y = Hash(i * 29 + 3) * size.Y;
                float r = 160f + 140f * Hash(i * 31);
                DrawCircle(new Vector2(x, y), r, new Color(0.85f, 0.88f, 0.92f, 0.07f * _strength));
                DrawCircle(new Vector2(x + r * 0.5f, y + 20f), r * 0.7f, new Color(0.85f, 0.88f, 0.92f, 0.06f * _strength));
            }
        }
        if (_flash > 0f) DrawRect(new Rect2(Vector2.Zero, size), new Color(0.92f, 0.95f, 1f, 0.55f * _flash * _flash));
    }

    static float Hash(int i)
    {
        uint h = (uint)i * 2654435761u;
        h ^= h >> 15;
        h *= 2246822519u;
        h ^= h >> 13;
        return (h & 0xFFFF) / 65535f;
    }
}
