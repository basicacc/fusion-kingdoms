using FusionKingdoms.Sim;
using Godot;

namespace FusionKingdoms;

// Turns what happens in the match into sound: world events where the camera can see them, construction,
// births, alerts, ages, and music that follows the day, the night and the fighting.
public sealed class MatchAudio
{
    readonly MatchScene _s;
    readonly Dictionary<int, (BuildingType type, float progress)> _buildings = new();
    int _pop = -1, _age = -1, _alerts;
    float _battle;   // seconds of "battle" music left
    float _hammer;

    public MatchAudio(MatchScene s) => _s = s;

    Match M => _s.M;
    Kingdom Me => _s.Me;
    MatchView V => _s.View;

    public void OnEvents(IReadOnlyList<SimEvent> events)
    {
        foreach (var e in events)
        {
            var u = M.W.Units[e.Unit];
            if (!V.CanSee(u)) continue;
            var p = MatchView.Px(u.Pos);
            bool mine = u.Team == Me.Team;
            switch (e.Kind)
            {
                case EventKind.Gathered:
                    if ((e.Unit + M.W.StepCount) % 9 != 0) break;
                    string g = e.Res switch
                    {
                        ResourceType.Timber => "chop", ResourceType.Stone => "mine", ResourceType.Lumen => "lumen", _ => "harvest",
                    };
                    Sound.I.PlayAt(g, p, -10f, 0.12f, 0.22f);
                    break;
                case EventKind.Deposited: Sound.I.PlayAt("coin", p, -15f, 0.1f, 0.4f); break;
                case EventKind.TussleStarted:
                    Sound.I.PlayAt("tussle", p, -4f, 0.1f, 0.25f);
                    if (mine || M.W.Units[e.Other].Team == Me.Team) _battle = 25f;
                    break;
                case EventKind.Subdued: Sound.I.PlayAt("subdue", p, -2f); break;
                case EventKind.PickedUp: Sound.I.PlayAt("order", p, -8f); break;
                case EventKind.Captured: Sound.I.PlayAt("capture", p, -2f); break;
                case EventKind.Freed: Sound.I.PlayAt("rescue", p, -3f); break;
                case EventKind.Stolen: Sound.I.PlayAt("steal", p, -8f, 0.1f, 0.6f); break;
                case EventKind.Sabotaged: Sound.I.PlayAt("sabotage", MatchView.Px(M.W.Buildings[e.Other].Pos), -4f, 0.1f, 0.8f); break;
                case EventKind.Healed: Sound.I.PlayAt("heal", p, -12f, 0.05f, 1.5f); break;
                case EventKind.Revived: Sound.I.PlayAt("heal", p, -4f); break;
            }
        }
    }

    public void Tick(float dt)
    {
        // Construction: hammering while a visible site grows, a chord when it finishes.
        _hammer -= dt;
        foreach (var b in M.W.Buildings)
        {
            bool had = _buildings.TryGetValue(b.Id, out var was);
            _buildings[b.Id] = (b.Type, b.BuildProgress);
            if (!had) continue;
            bool visible = b.Team == Me.Team || V.CanSee(b.Pos);
            if (!visible) continue;
            if (b.Type == BuildingType.BuildSite && b.BuildProgress > was.progress && _hammer <= 0f)
            {
                Sound.I.PlayAt("hammer", MatchView.Px(b.Pos), -9f);
                _hammer = 1.2f;
            }
            if (was.type == BuildingType.BuildSite && b.Type != BuildingType.BuildSite && b.Team == Me.Team)
                Sound.I.Play("built", -3f);
        }

        int pop = M.W.Units.Count(u => u.Active && u.Team == Me.Team);
        if (_pop >= 0 && pop > _pop) Sound.I.Play("birth", -8f, 0.06f, 0.5f);
        _pop = pop;

        if (_age >= 0 && Me.Age > _age) Sound.I.Play("ageup");
        _age = Me.Age;

        for (; _alerts < M.Alerts.Count; _alerts++)
        {
            var a = M.Alerts[_alerts];
            if (a.Team != -1 && a.Team != Me.Team) continue;
            string t = a.Text.ToLowerInvariant();
            string[] danger = ["raiding", "subdued", "sabotaged", "dragged off", "broken out", "boiled over", "awoken", "eclipse", "granary is empty"];
            if (danger.Any(t.Contains) || (t.Contains(" captured ") && !a.Text.StartsWith(Me.Name)))
            {
                Sound.I.Play("alert", -4f, 0f, 6f);
                _battle = Math.Max(_battle, 20f);
            }
            else if (!t.Contains(" entered age ") && !t.Contains(" fused ")) Sound.I.Play("select", -6f, 0f, 1f);
        }

        // Hostile units inside our territory keep the drums going.
        var keep = Me.Keep.Pos;
        if (M.W.Units.Any(u => u.Active && u.Team != Me.Team && u.Team >= 0 && u.Team <= 1 && !u.Subdued
                               && (u.Pos - keep).LengthSquared() < 18f * 18f && V.CanSee(u)))
            _battle = Math.Max(_battle, 8f);
        _battle -= dt;

        if (M.Over) return;
        Sound.I.Music(_battle > 0f ? "battle" : M.W.IsNight ? "night" : "day", _battle > 0f ? 1.2f : 3f);
    }

    // Sound for the outcome of a command the player gave.
    public static void OnCommand(Command c, CommandResult r)
    {
        if (r.Error != null) { Sound.I.Play("error", -4f, 0f, 0.2f); return; }
        switch (c.Type)
        {
            case CmdType.Order: case CmdType.Station: case CmdType.MoveRuler: Sound.I.Play("order", -6f); break;
            case CmdType.PlaceSite: Sound.I.Play("place", -3f); break;
            case CmdType.Fuse: Sound.I.Play("fusion"); break;
            case CmdType.Mutate: case CmdType.ChooseMutant: Sound.I.Play("mutation", -2f); break;
            case CmdType.Infuse: case CmdType.Distill: case CmdType.Extract: case CmdType.FeedSpire: Sound.I.Play("lumen", -2f); break;
            case CmdType.Trade: case CmdType.BuyMerchant: case CmdType.Ransom: Sound.I.Play("coin", -4f); break;
            case CmdType.Foresee: case CmdType.ForeseeInfusion: case CmdType.Interrogate: Sound.I.Play("heal", -6f); break;
            case CmdType.BeginRoyal: case CmdType.Rush: Sound.I.Play("alert", -8f); break;
        }
    }

    public static void End(bool won)
    {
        Sound.I.Music("", 1f);
        Sound.I.Play(won ? "victory" : "defeat");
    }
}
