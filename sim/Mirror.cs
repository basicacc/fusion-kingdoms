using System.Numerics;

namespace FusionKingdoms.Sim;

// Mirror Drill: each kingdom records what the enemy's real units do where it can see them, then drills its own
// subjects against ghosts that replay those recordings. Raider tapes come from enemies prowling around our Keep;
// Guard tapes come from enemies defending their own Keep while our units were there to watch.
public enum TapeMode : byte { Raider, Guard }

// One clip of a real enemy unit, in a frame anchored at a Keep with +x pointing at the other Keep, so it can be
// replayed on any map.
public sealed class Tape
{
    public TapeMode Mode;
    public readonly List<Vector2> Pos = new();
    public readonly List<byte> Act = new();
    public int Count => Pos.Count;
}

public readonly record struct Frame(Vector2 Origin, Vector2 Axis)
{
    public Vector2 Perp => new(-Axis.Y, Axis.X);

    public static Frame Between(Vector2 origin, Vector2 toward)
    {
        var d = toward - origin;
        return new Frame(origin, d.LengthSquared() < 1e-6f ? Vector2.UnitX : Vector2.Normalize(d));
    }

    public Vector2 ToLocal(Vector2 p) => new(Vector2.Dot(p - Origin, Axis), Vector2.Dot(p - Origin, Perp));
    public Vector2 ToWorld(Vector2 l) => Origin + Axis * l.X + Perp * l.Y;
}

public sealed class MirrorRecorder
{
    public const int ClipSteps = 60, MinSteps = 15, MaxPerMode = 16;
    const float RaidRange = 16f, GuardRange = 12f;

    public readonly List<Tape> Tapes = new();
    readonly Dictionary<int, Tape> _open = new();

    public int CountOf(TapeMode mode) => Tapes.Count(t => t.Mode == mode);

    public void Record(Match m, Kingdom me)
    {
        var them = m.Enemy(me);
        var vision = m.Vision[me.Team];
        var raid = Frame.Between(me.Keep.Pos, them.Keep.Pos);
        var guard = Frame.Between(them.Keep.Pos, me.Keep.Pos);
        foreach (var u in m.W.Units)
        {
            TapeMode? mode = null;
            if (u.Team == them.Team && u.Species == Species.Human && !u.IsRuler && u.CanAct && vision.Sees(u))
            {
                if (Vector2.Distance(u.Pos, me.Keep.Pos) < RaidRange) mode = TapeMode.Raider;
                else if (Vector2.Distance(u.Pos, them.Keep.Pos) < GuardRange) mode = TapeMode.Guard;
            }
            _open.TryGetValue(u.Id, out var tape);
            if (tape != null && (mode != tape.Mode || tape.Count >= ClipSteps))
            {
                Close(tape);
                _open.Remove(u.Id);
                tape = null;
            }
            if (mode is not { } md) continue;
            if (tape == null) _open[u.Id] = tape = new Tape { Mode = md };
            var f = md == TapeMode.Raider ? raid : guard;
            tape.Pos.Add(f.ToLocal(u.Pos));
            tape.Act.Add((byte)Intent(u));
        }
    }

    // Brains sample their actions, so most frames carry a meaningless pick; keep only what the unit really did
    // or tried to do to someone.
    static Act Intent(Unit u)
    {
        if (u.TussleTarget >= 0) return Act.Tussle;
        if (u.CarryStolen) return Act.Steal;
        var a = (Act)u.LastAction;
        return a is Act.Tussle or Act.Steal or Act.Carry or Act.Sabotage or Act.Rescue ? a : Act.Idle;
    }

    void Close(Tape t)
    {
        if (t.Count < MinSteps) return;
        Tapes.Add(t);
        var same = Tapes.Where(x => x.Mode == t.Mode).ToList();
        if (same.Count > MaxPerMode) Tapes.Remove(same[0]);
    }
}

// Replays a tape: walk to each recorded position in turn and do what the real unit did there.
public sealed class GhostScript(Tape tape, Frame frame, int startStep, int mapSize) : IController
{
    public bool Finished;

    public (int move, int action) Decide(World w, Unit u)
    {
        int i = w.StepCount - startStep;
        if (i >= tape.Count) { Finished = true; return (0, u.TussleTarget >= 0 ? (int)Act.Tussle : 0); }
        var at = Clamp(frame.ToWorld(tape.Pos[Math.Max(0, i)]), mapSize);
        int act = tape.Act[Math.Max(0, i)];
        int move = Vector2.Distance(u.Pos, at) > 0.35f ? Steer.Toward(w, u, at) : 0;
        return (move, act);
    }

    public static Vector2 Clamp(Vector2 p, int size) => Vector2.Clamp(p, new Vector2(1f), new Vector2(size - 1f));
}

// The drill itself. Against Raider tapes the learner defends home; against Guard tapes it raids their Keep.
public sealed class MirrorScenario(TapeMode mode, IReadOnlyList<Tape> tapes) : Scenario
{
    protected override MindKind Kind => mode == TapeMode.Raider ? MindKind.Guard : MindKind.Thief;
    readonly List<(Unit unit, GhostScript script)> _ghosts = new();
    float _respawn, _lastD;
    Frame _frame;

    protected override void AddContext()
    {
        var k1 = AddEnemyKeep(true);
        if (k1.Stored[(int)ResourceType.Food] <= 0f) k1.Stored[(int)ResourceType.Food] = 40f + Rng.Next(40);
        _frame = mode == TapeMode.Raider ? Frame.Between(Keep0.Pos, k1.Pos) : Frame.Between(k1.Pos, Keep0.Pos);
        _ghosts.Clear();
        for (int i = 0; i < 2; i++) SpawnGhost(null);
        if (mode == TapeMode.Guard)
        {
            Learner.Command = CommandType.Raid;
            Learner.CommandTarget = k1.Pos;
        }
        _lastD = -1f;
        _respawn = 0f;
    }

    void SpawnGhost(Unit? reuse)
    {
        if (tapes.Count == 0) return;
        var tape = tapes[Rng.Next(tapes.Count)];
        var script = new GhostScript(tape, _frame, W.StepCount, Size);
        var at = GhostScript.Clamp(_frame.ToWorld(tape.Pos[0]), Size);
        if (TerrainInfo.Blocked(W.TerrainAt(at))) at = FreePos(at, 0.5f, 3f);
        if (reuse != null)
        {
            Respawn(reuse, at);
            reuse.Controller = script;
            _ghosts[_ghosts.FindIndex(g => g.unit == reuse)] = (reuse, script);
        }
        else _ghosts.Add((AddHostile(script, at), script));
    }

    protected override void Tick()
    {
        if (!Ctx) return;
        _respawn -= Rules.Dt;
        if (_respawn > 0f) return;
        foreach (var (u, s) in _ghosts.ToList())
        {
            bool gone = !u.Active || (u.Subdued && u.CarriedBy < 0 && u.SubduedTimer < 20f);
            if (!s.Finished && !gone) continue;
            if (u.CarriedBy >= 0 || u.TussleTarget >= 0) continue;
            SpawnGhost(u);
            _respawn = 1.5f + Rng.NextSingle() * 2f;
            break;
        }
    }

    protected override float SubduedPenalty => mode == TapeMode.Raider ? 0.5f : 1.5f;

    protected override float Special()
    {
        float r = 0f;
        if (mode == TapeMode.Guard)
        {
            float d = Vector2.Distance(Learner.Pos, Learner.CarryStolen ? Keep0.Pos : Keep1!.Pos);
            r += Approach(ref _lastD, d) + 0.05f * Sum(EventKind.Stolen) + 0.25f * Sum(EventKind.Deposited, e => e.Other == 1);
            foreach (var e in W.Events)
                if (e.Kind == EventKind.TussleStarted && e.Other == Learner.Id) r -= 0.1f;   // got caught by a guard
            return r;
        }

        Unit? threat = null;
        float best = 10f;
        foreach (var (u, _) in _ghosts)
        {
            if (!u.Active || u.Subdued) continue;
            float d = Vector2.Distance(u.Pos, Keep0.Pos);
            if (d < best) { best = d; threat = u; }
        }
        if (threat != null)
        {
            r += Approach(ref _lastD, Vector2.Distance(Learner.Pos, threat.Pos));
            if (Learner.TussleTarget >= 0 && W.Units[Learner.TussleTarget].Team == 1) r += 0.05f;
        }
        else _lastD = -1f;
        foreach (var e in W.Events)
        {
            if (e.Kind == EventKind.Subdued && e.Other == Learner.Id && IsHostile(e.Unit)) r += 1.5f;
            if (e.Kind == EventKind.Stolen && W.Units[e.Unit].Team == 1) r -= 0.1f * e.Amount;
            if (e.Kind == EventKind.Captured && e.Unit == Learner.Id) r += 1f;
            if (e.Kind == EventKind.Subdued && e.Unit == Ruler.Id) r -= 2f;
        }
        return r;
    }
}
