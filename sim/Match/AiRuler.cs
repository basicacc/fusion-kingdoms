using System.Numerics;

namespace FusionKingdoms.Sim;

public enum AiPersonality { Breeder, Warden, Archmage }
public enum AiDifficulty { Easy, Normal, Hard }

// The AI ruler plays with exactly the player's tools: the Cradle, sites, the Altar (with Foresee), the Grounds,
// the Pit and the Dungeon, and it runs its economy with the same RTS orders. It keeps notes on what it learned for the post-match War Report.
public sealed class AiRuler
{
    readonly Match _m;
    readonly Kingdom _k;
    readonly Random _rng;
    public readonly AiPersonality Personality;
    public readonly List<string> Notes = new();
    readonly HashSet<string> _noted = new();
    float _fuseTimer = 150f, _mutateTimer = 300f, _drillTimer = 120f, _orderTimer;

    public readonly AiDifficulty Difficulty;
    public bool Smart = true;
    [Flags] public enum Feature { None = 0, Defend = 1, Campaign = 2, Improve = 4, Scout = 8, Spire = 16, Adapt = 32, BigArmy = 64, All = 127 }
    public Feature Features = Feature.All;   // for ablation tests
    public AiParams P;               // its strategy (personality defaults, or self-play tuned)
    AiParams L = null!;              // P adapted to what it has seen this match
    readonly HashSet<int> _scouts = new();
    float _scoutTimer = 60f, _adaptTimer;
    int _lastThefts, _lastLost;
    readonly Queue<(float t, int thefts, int lost)> _history = new();
    bool _beingRaided, _foeWeak, _foeStrong;   // defence, rescue and campaigns; off = the older economy-only ruler (for comparisons)
    readonly HashSet<int> _defenders = new(), _army = new();
    bool _attacking;
    int _armyStart, _enemyFightersSeen;
    float _campaignCooldown;

    public AiRuler(Match m, Kingdom k, AiPersonality personality, int seed, AiDifficulty difficulty = AiDifficulty.Normal)
    {
        _m = m;
        _k = k;
        _rng = new Random(seed);
        Personality = personality;
        Difficulty = difficulty;
        P = AiParams.For(personality);
        L = P.Clone();
        // Easy rulers work slower and wait before raiding; hard ones work harder and start richer.
        k.Bonus = difficulty switch { AiDifficulty.Easy => 0.75f, AiDifficulty.Hard => 1.25f, _ => 1f };
        // Easy rulers never march, race the Spire or research: they just build and defend.
        if (difficulty == AiDifficulty.Easy) Features &= ~(Feature.Campaign | Feature.Spire | Feature.Improve | Feature.Defend | Feature.Scout | Feature.Adapt);
        if (difficulty == AiDifficulty.Hard)
        {
            for (int i = 1; i < k.Stored.Length; i++) k.Stored[i] *= 1.5f;
            k.PopBonus = 10;
            // A head start: three extra subjects (copies of its first ones).
            foreach (var u in m.Subjects(k).Take(3).ToList()) if (u.Mind != null) m.Spawn(k, u.Mind, k.Home + new Vector2(2f, 2f));
        }
        if (difficulty == AiDifficulty.Easy) k.PopBonus = -10;
        foreach (var u in m.Subjects(k)) m.ApplyStats(u);
    }

    const float Tick = Rules.Dt * 5f;
    World W => _m.W;
    Kingdom Foe => _m.Enemy(_k);

    void Note(string key, string text)
    {
        if (_noted.Add(key)) Notes.Add(text);
    }

    public void Think()
    {
        KeepRulerHome();
        Captives();
        if (Smart)
        {
            if ((Features & Feature.Improve) != 0) Improve();
            if ((Features & Feature.Adapt) != 0) Adapt();
            if ((Features & Feature.Scout) != 0) Scout();
            if ((Features & Feature.Defend) != 0) Defend();
            if ((Features & Feature.Spire) != 0) SpireWatch();
            if ((Features & Feature.Campaign) != 0) Campaign();
        }
        Build();
        if (_k.Age < 3 && _m.Population(_k) >= 8 + 4 * _k.Age) _m.AgeUp(_k);
        Produce();
        _orderTimer -= Tick;
        if (_orderTimer <= 0f) { _orderTimer = 10f; Orders(); }
        _fuseTimer -= Tick;
        if (_fuseTimer <= 0f) { _fuseTimer = Smart ? L.FuseEvery : Personality == AiPersonality.Breeder ? 70f : 120f; CounterFuse(); }
        _drillTimer -= Tick;
        if (_drillTimer <= 0f) { _drillTimer = 90f; Drill(); }
        _mutateTimer -= Tick;
        if (_mutateTimer <= 0f) { _mutateTimer = Personality == AiPersonality.Archmage ? 60f : 180f; Mutate(); }
        if (_k.Lumen > 200f) _m.FeedSpire(_k);
        // Never get stuck without materials: barter grain at the Keep.
        if (_k.Timber < 12f && _k.Grain > 80f) _m.Trade(_k, Defs.FirstBarter, 1);
        if (_k.Stone < 10f && _k.Grain > 100f) _m.Trade(_k, Defs.FirstBarter + 1, 1);
        // Turn surplus grain into gold (Age 3 and the merchant need it).
        if (_k.Grain > 250f && _k.Gold < 120f) _m.Trade(_k, 0, (int)((_k.Grain - 200f) / 20f));
    }

    // ---------- defence ----------

    static bool Fighter(Unit u) => u.StrengthMul >= 1.1f || Classes.Of(u.Mind).A is 2 or 5;

    // Anyone hostile near our king, our buildings, or carrying one of ours off, gets caught by our strongest nearby
    // subjects: two per intruder, three for whoever threatens the king, everyone available for a kidnapper.
    void Defend()
    {
        var vision = _m.Vision[_k.Team];
        var ours = _m.BuildingsOf(_k).Where(b => b.Type != BuildingType.Farm || true).Select(b => b.Pos).ToList();
        var ruler = _k.Ruler;
        var threats = new List<(Unit u, int need)>();
        foreach (var o in W.Units)
        {
            if (!o.Active || o.Team == _k.Team || o.Subdued || (o.CarriedBy >= 0)) continue;
            bool kidnapping = o.CarryingUnit >= 0 && W.Units[o.CarryingUnit].Team == _k.Team;
            // Wildlife only matters when it grabs one of ours or goes for the king; foxes nibbling at stores aren't worth chasing.
            if (o.Team == 2 && !kidnapping && Vector2.Distance(o.Pos, ruler.Pos) > 5f) continue;
            bool kidnapper = o.CarryingUnit >= 0 && W.Units[o.CarryingUnit].Team == _k.Team;
            if (!kidnapper && !vision.Sees(o)) continue;
            // The leash: we fight on our own ground, never chasing into the enemy's.
            if (Vector2.Distance(o.Pos, _k.Home) > 24f && Vector2.Distance(o.Pos, ruler.Pos) > 9f) continue;
            bool atKing = Vector2.Distance(o.Pos, ruler.Pos) < 9f;
            bool atHome = ours.Any(p => Vector2.Distance(p, o.Pos) < 10f);
            if (!kidnapper && !atKing && !atHome) continue;
            threats.Add((o, kidnapper && W.Units[o.CarryingUnit].IsRuler ? 8 : kidnapper ? 4 : atKing ? L.Defenders + 1 : L.Defenders));
        }
        // Stand down defenders whose job is done; the economy takes them back.
        foreach (var id in _defenders.ToList())
        {
            var d = W.Units[id];
            // Still busy: chasing a live threat, or about to carry a downed intruder home as a captive.
            var tgt = d.OrderTarget >= 0 && d.OrderTarget < W.Units.Count ? W.Units[d.OrderTarget] : null;
            bool downed = tgt != null && tgt.Active && tgt.Subdued && tgt.CarriedBy < 0;
            bool tooFar = Vector2.Distance(d.Pos, _k.Home) > 28f && d.CarryingUnit < 0;
            bool stillOn = !tooFar && d.Active && d.Order == OrderKind.Capture && (threats.Any(t => t.u.Id == d.OrderTarget) || downed);
            if (tooFar && d.Active && d.CarryingUnit < 0) { _defenders.Remove(id); _m.Give(_k, [d], OrderKind.Move, _k.Home, -1, ResourceType.None); continue; }
            if (stillOn || d.CarryingUnit >= 0) continue;
            _defenders.Remove(id);
            if (d.Order == OrderKind.Capture && d.CarryingUnit < 0) d.Order = OrderKind.None;
        }
        if (threats.Count > 0) Note("defend", $"I always answered raids at home: my nearest strong subjects ran down every intruder.");
        foreach (var (t, need) in threats.OrderByDescending(t => t.need))
        {
            int on = W.Units.Count(d => d.Active && d.Team == _k.Team && d.Order == OrderKind.Capture && d.OrderTarget == t.Id);
            if (on >= need) continue;
            var pick = _m.Subjects(_k)
                .Where(d => d.CanAct && d.CarryingUnit < 0 && !(d.Order == OrderKind.Capture && _defenders.Contains(d.Id))
                            && (Fighter(d) || need >= 4 || Vector2.Distance(d.Pos, t.Pos) < 10f)
                            && Vector2.Distance(d.Pos, t.Pos) < (need >= 4 ? 40f : 24f))
                .OrderByDescending(d => d.StrengthMul * 6f - Vector2.Distance(d.Pos, t.Pos) * 0.4f)
                .Take(need - on).ToList();
            if (pick.Count == 0) continue;
            _m.Give(_k, pick, OrderKind.Capture, t.Pos, t.Id, ResourceType.None);
            foreach (var d in pick) { _defenders.Add(d.Id); _army.Remove(d.Id); }
        }
        // A downed king gets picked up again.
        if (ruler.Subdued && ruler.CarriedBy < 0
            && !W.Units.Any(d => d.Active && d.Team == _k.Team && d.Order == OrderKind.Heal && d.OrderTarget == ruler.Id))
        {
            var medic = _m.Subjects(_k).Where(d => d.CanAct && d.CarryingUnit < 0).OrderByDescending(d => d.HealMul * 4f - Vector2.Distance(d.Pos, ruler.Pos) * 0.3f).FirstOrDefault();
            if (medic != null) { _m.Give(_k, [medic], OrderKind.Heal, ruler.Pos, ruler.Id, ResourceType.None); _defenders.Add(medic.Id); }
        }
    }

    // ---------- research and upgrades ----------

    float _improveTimer = 30f;

    string[] TechOrder => Personality switch
    {
        AiPersonality.Warden => ["ploughs", "drill", "axes", "masonry", "cellars", "boots", "lenses", "crowns"],
        AiPersonality.Archmage => ["lenses", "ploughs", "axes", "boots", "cellars", "masonry", "drill", "crowns"],
        _ => ["ploughs", "axes", "boots", "drill", "cellars", "lenses", "masonry", "crowns"],
    };

    // Spend spare materials on research and upgrades, keeping a reserve for building.
    void Improve()
    {
        if ((_improveTimer -= Tick) > 0f) return;
        _improveTimer = 10f;
        if (W.Time < 240f || _m.Population(_k) < 15) return;   // build the kingdom first
        if (_k.Researching == null)
            foreach (var id in TechOrder)
            {
                var t = Progress.Tech(id)!;
                if (_k.Has(id) || _k.Age < t.Age) continue;
                if (_k.Timber >= t.Timber + 40f && _k.Stone >= t.Stone + 20f && _k.Lumen >= t.Lumen + 15f && _k.Gold >= t.Gold)
                    _m.StartResearch(_k, id);
                break;
            }
        if (_k.Age < 2) return;
        int pop = _m.Population(_k), cap = _m.PopulationCap(_k);
        foreach (var b in _m.BuildingsOf(_k).Where(b => b.Working && b.UpgradeTimer <= 0f).ToList())
        {
            var up = Progress.UpgradeFor(b);
            if (up == null || _k.Age < up.Age) continue;
            bool want = b.Type switch
            {
                BuildingType.House => pop >= cap - 3 && cap < Defs.PopulationMax,
                BuildingType.Farm => _k.Grain < 400f,
                BuildingType.GuardPost => _beingRaided || Personality == AiPersonality.Warden,
                BuildingType.Granary => true,
                BuildingType.Watchtower => Personality == AiPersonality.Warden,
                _ => false,
            };
            if (!want || _k.Timber < up.Timber + 30f || _k.Stone < up.Stone + 20f || _k.Lumen < up.Lumen + 10f) continue;
            _m.Upgrade(_k, b);
            return;   // one at a time
        }
    }

    // ---------- adapting ----------

    // Re-read the situation every few seconds and bend the plan: raided -> dig in; weak enemy -> strike early;
    // strong enemy -> more fighters.
    void Adapt()
    {
        if ((_adaptTimer -= Tick) > 0f) return;
        _adaptTimer = 5f;
        _history.Enqueue((W.Time, _k.TheftsSuffered, _k.Lost));
        while (_history.Count > 0 && _history.Peek().t < W.Time - 180f) _history.Dequeue();
        var old = _history.Peek();
        int thefts = _k.TheftsSuffered - old.thefts, lost = _k.Lost - old.lost;
        // Wild foxes nibble at every store all game; only kidnappings or heavy theft count as being raided.
        _beingRaided = thefts > 60 || lost > 0;
        int mine = _m.Subjects(_k).Count(Fighter);
        _foeWeak = W.Time > 200f && _enemyFightersSeen <= 2 && _scoutedOnce;
        _foeStrong = _enemyFightersSeen > mine;

        L = P.Clone();
        if (_beingRaided)
        {
            L.Defenders = Math.Min(4, P.Defenders + 1);
            L.ArmyFraction = MathF.Min(0.45f, P.ArmyFraction + 0.1f);
            Note("raided", $"Your raids made me dig in: more guards, more defenders per intruder.");
        }
        if (_foeStrong) L.ArmyFraction = MathF.Min(0.3f, L.ArmyFraction + 0.04f);
        if (_foeWeak && Difficulty != AiDifficulty.Easy)
        {
            L.AttackStart = MathF.Min(P.AttackStart, MathF.Max(200f, W.Time));
            L.AttackMin = Math.Min(P.AttackMin, 4);
            Note("weak", $"My scouts found your home lightly guarded, so I attacked early.");
        }
        if (Difficulty == AiDifficulty.Easy) L.AttackStart = MathF.Max(L.AttackStart, 600f);
    }

    bool _scoutedOnce;

    // Is a hostile fighter (not wildlife) within r tiles of this subject?
    bool Danger(Unit u, float r) => W.Units.Any(o => o.Active && o.Team == Foe.Team && !o.Subdued && Fighter(o) && Vector2.Distance(o.Pos, u.Pos) < r);

    // Every so often the quickest subject runs to the enemy's home and back, so the plan is based on what's there.
    void Scout()
    {
        foreach (var id in _scouts.ToList())
        {
            var s = W.Units[id];
            if (!s.Active || s.Subdued) { _scouts.Remove(id); continue; }
            if (Vector2.Distance(s.Pos, Foe.Home) < 12f) _scoutedOnce = true;
            // Seen enough the moment an enemy fighter comes close: turn for home.
            if (Danger(s, 7f) && !(s.Order == OrderKind.Move && Vector2.Distance(s.OrderPos, _k.Home) < 5f))
            {
                _scoutedOnce = true;
                _m.Give(_k, [s], OrderKind.Move, _k.Home, -1, ResourceType.None);
                continue;
            }
            if (s.Order == OrderKind.None || Vector2.Distance(s.Pos, Foe.Home) < 10f)
            {
                if (Vector2.Distance(s.Pos, _k.Home) < 10f) { _scouts.Remove(id); s.Order = OrderKind.None; continue; }
                if (s.Order != OrderKind.Move || Vector2.Distance(s.OrderPos, _k.Home) > 5f)
                    _m.Give(_k, [s], OrderKind.Move, _k.Home, -1, ResourceType.None);
            }
        }
        if ((_scoutTimer -= Tick) > 0f || _scouts.Count > 0) return;
        _scoutTimer = L.ScoutEvery;
        var runner = _m.Subjects(_k).Where(u => u.CanAct && u.CarryingUnit < 0 && !_defenders.Contains(u.Id) && !_army.Contains(u.Id))
            .OrderByDescending(u => u.SpeedMul * 2f + u.SightMul).FirstOrDefault();
        if (runner == null) return;
        _m.Give(_k, [runner], OrderKind.Move, Foe.Home + Vector2.Normalize(_k.Home - Foe.Home) * 8f, -1, ResourceType.None);
        _scouts.Add(runner.Id);
    }

    // ---------- the Lumen Spire race ----------

    readonly HashSet<int> _spireCrew = new();
    readonly Dictionary<int, float> _lieLow = new();
    float _crewCooldown;

    // An enemy Spire filling up is a countdown to defeat: keep it sabotaged (a disabled Spire can't be fed) and
    // steal their Lumen. Our own Spire gets fed the moment the stock can finish it.
    void SpireWatch()
    {
        var mine = _m.Working(_k, BuildingType.LumenSpire);
        if (mine != null && _k.Lumen > 50f) _m.FeedSpire(_k);

        var theirs = _m.BuildingsOf(Foe).FirstOrDefault(b => b.Type == BuildingType.LumenSpire);
        foreach (var id in _spireCrew.ToList())
        {
            var c = W.Units[id];
            if (!c.Active || c.Subdued) { _spireCrew.Remove(id); _crewCooldown = 90f; continue; }
            if (c.Order is not (OrderKind.Sabotage or OrderKind.Raid)) { _spireCrew.Remove(id); continue; }
            // Two or more defenders closing in: abandon the job and run.
            if (W.Units.Count(o => o.Active && o.Team == Foe.Team && !o.Subdued && Fighter(o) && Vector2.Distance(o.Pos, c.Pos) < 6f) >= 2)
            {
                _m.Give(_k, [c], OrderKind.Move, _k.Home, -1, ResourceType.None);
                _spireCrew.Remove(id);
            }
        }
        _crewCooldown -= Tick;
        if (theirs == null || theirs.Charge + theirs.Stored[(int)ResourceType.Lumen] * 0.5f < MathF.Max(L.SpireAlarm, Defs.SpireGoal * 0.4f)) return;
        if (_crewCooldown > 0f) return;
        Note("spire", $"Your Lumen Spire reached {theirs.Charge:0}: I kept sending saboteurs to shut it down.");
        int want = theirs.Charge > 600f ? 6 : 3;
        if (_spireCrew.Count >= want) return;
        var crew = _m.Subjects(_k).Where(u => u.CanAct && u.CarryingUnit < 0 && !_defenders.Contains(u.Id) && !_spireCrew.Contains(u.Id))
            .OrderByDescending(u => u.StealMul + u.SpeedMul + u.StrengthMul * 0.5f).Take(want - _spireCrew.Count).ToList();
        for (int i = 0; i < crew.Count; i++)
        {
            // Half sabotage the Spire, half raid their Keep for the Lumen they'd feed it.
            bool sabotage = i % 2 == 0;
            _m.Give(_k, [crew[i]], sabotage ? OrderKind.Sabotage : OrderKind.Raid, sabotage ? theirs.Pos : Foe.Home,
                sabotage ? theirs.Id : Foe.Keep.Id, ResourceType.None);
            _spireCrew.Add(crew[i].Id);
            _army.Remove(crew[i].Id);
        }
    }

    // ---------- campaigns ----------

    // Build up fighters, attack when clearly stronger than the defence we've seen, pick off enemy workers and the
    // king when it's exposed, and fall back when the attack goes badly.
    void Campaign()
    {
        var foeHome = Foe.Home;
        int seen = W.Units.Count(o => o.Active && o.Team == Foe.Team && !o.Subdued && Fighter(o) && _m.Vision[_k.Team].Sees(o)
                                      && Vector2.Distance(o.Pos, foeHome) < 20f);
        _enemyFightersSeen = Math.Max(seen, (int)(_enemyFightersSeen * 0.98f));
        _campaignCooldown -= Tick;
        float start = L.AttackStart;
        if (W.Time < start) return;

        var fighters = _m.Subjects(_k).Where(u => u.CanAct && Fighter(u) && !_defenders.Contains(u.Id)).ToList();
        bool lateGame = W.Time > _m.Length * 0.7f;
        if (!_attacking)
        {
            // Only march when clearly stronger: a lost army just feeds the enemy captives.
            int needed = Math.Max(Math.Max(L.AttackMin, 10), (int)(_enemyFightersSeen * MathF.Max(2f, L.AttackRatio)) + 4);
            if (_campaignCooldown > 0f || fighters.Count < needed) return;
            // Keep two fighters home.
            var go = fighters.OrderByDescending(u => u.StrengthMul).Skip(2).ToList();
            if (go.Count < needed - 2) return;
            _army.Clear();
            foreach (var u in go) _army.Add(u.Id);
            _armyStart = _army.Count;
            _attacking = true;
            Note($"attack{(int)W.Time / 60}", $"At minute {W.Time / 60f:0} I marched {_armyStart} fighters on your kingdom.");
        }

        var army = _army.Select(id => W.Units[id]).Where(u => u.Active && u.Team == _k.Team).ToList();
        int standing = army.Count(u => !u.Subdued);
        if (standing < _armyStart * MathF.Max(0.6f, L.RetreatAt))
        {
            _attacking = false;
            _campaignCooldown = 120f;
            var back = army.Where(u => u.CanAct).ToList();
            if (back.Count > 0) _m.Give(_k, back, OrderKind.Move, _k.Home, -1, ResourceType.None);
            _army.Clear();
            Note($"retreat{(int)W.Time / 60}", $"At minute {W.Time / 60f:0} your defenders beat my attack back.");
            return;
        }
        // Soft targets: enemy subjects we can see out in the field, away from their guards and guard posts.
        var posts = _m.BuildingsOf(Foe).Where(b => b.Type is BuildingType.GuardPost or BuildingType.Keep).Select(b => b.Pos).ToList();
        var foeFighters = W.Units.Where(o => o.Active && o.Team == Foe.Team && !o.Subdued && Fighter(o)).ToList();
        var foes = W.Units.Where(o => o.Active && o.Team == Foe.Team && !o.Subdued && o.CarriedBy < 0 && _m.Vision[_k.Team].Sees(o)
                                      && Vector2.Distance(o.Pos, foeHome) < 45f).ToList();
        float Danger(Unit o) => foeFighters.Count(f => f != o && Vector2.Distance(f.Pos, o.Pos) < 7f) * 3f
                                + (posts.Any(p => Vector2.Distance(p, o.Pos) < 8f) ? 6f : 0f) + (Fighter(o) ? 2f : 0f);
        var king = Foe.Ruler;
        int kingGuards = foeFighters.Count(o => Vector2.Distance(o.Pos, king.Pos) < 6f);
        bool goForKing = king.Active && foes.Contains(king) && (lateGame || standing >= 3 * (kingGuards + 1));
        // Staging point: between home and theirs, two-thirds of the way.
        var stage = _m.Walkable(_k.Home + (foeHome - _k.Home) * 0.6f);
        var targets = foes.Where(o => Danger(o) <= 4f).OrderBy(o => Danger(o) + Vector2.Distance(o.Pos, stage) * 0.1f).ToList();
        var assigned = new Dictionary<int, int>();
        foreach (var u in army.Where(u => u.CanAct && u.CarryingUnit < 0))
        {
            bool busy = u.Order == OrderKind.Capture && u.OrderTarget >= 0 && W.Units[u.OrderTarget].Active && !W.Units[u.OrderTarget].Subdued;
            if (busy && !(goForKing && u.OrderTarget != king.Id)) { assigned[u.OrderTarget] = assigned.GetValueOrDefault(u.OrderTarget) + 1; continue; }
            // Pairs or threes on each target, never one alone.
            Unit? target = goForKing ? king : targets.FirstOrDefault(o => assigned.GetValueOrDefault(o.Id) < 3);
            if (target != null)
            {
                assigned[target.Id] = assigned.GetValueOrDefault(target.Id) + 1;
                _m.Give(_k, [u], OrderKind.Capture, target.Pos, target.Id, ResourceType.None);
            }
            else if (u.Order != OrderKind.Guard || Vector2.Distance(u.OrderPos, stage) > 4f)
                _m.Give(_k, [u], OrderKind.Guard, stage, -1, ResourceType.None);   // wait at the staging point and catch what passes
        }
        if (goForKing) Note("crown", $"At minute {W.Time / 60f:0} I saw your ruler exposed and sent {standing} fighters after it.");
    }

    void KeepRulerHome()
    {
        if (Vector2.Distance(_k.Ruler.Pos, _k.Home) > 4f) _m.MoveRuler(_k, _k.Home + new Vector2(0f, -3f));
    }

    // ---------- building ----------

    int Nodes(ResourceType t, Vector2 near, float r) => W.Nodes.Count(n => n.Type == t && n.Amount > 0f && Vector2.Distance(n.Pos, near) < r);
    bool HasNear(BuildingType t, Vector2 p, float r) => _m.BuildingsOf(_k).Any(b =>
        (b.Type == t || (b.Type == BuildingType.BuildSite && b.SiteFor == t)) && Vector2.Distance(b.Pos, p) < r);
    int CountAll(BuildingType t) => _m.BuildingsOf(_k).Count(b => b.Type == t || (b.Type == BuildingType.BuildSite && b.SiteFor == t));

    IEnumerable<(BuildingType type, Vector2 near)> Wishlist()
    {
        int pop = _m.Population(_k), cap = _m.PopulationCap(_k);
        bool Has(BuildingType t) => CountAll(t) > 0;
        int farmers = _m.Subjects(_k).Count(u => u.Order == OrderKind.Gather && u.OrderRes == ResourceType.Food);
        // Food first: every crop feeds about two farmers.
        if (Nodes(ResourceType.Food, _k.Home, 20f) * 2 < Math.Max(farmers, pop / 3) + 2) yield return (BuildingType.Farm, _k.Home);
        if (pop >= cap - 3 && cap < Defs.PopulationMax)
            yield return (_k.Age >= 2 && CountAll(BuildingType.House) >= 6 ? BuildingType.Manor : BuildingType.House, _k.Home);
        if (!Has(BuildingType.Cradle)) yield return (BuildingType.Cradle, _k.Home);
        // The Spire is a win condition: start it as soon as the Age allows (Archmage first of all).
        if (_k.Age >= 3 && !Has(BuildingType.LumenSpire) && (Smart || Personality == AiPersonality.Archmage)) yield return (BuildingType.LumenSpire, _k.Home);
        if (_m.NearestNode(ResourceType.Timber, _k.Home) is { } wood && Vector2.Distance(wood.Pos, _k.Home) > 7f && !HasNear(BuildingType.LumberMill, wood.Pos, 9f)
            && CountAll(BuildingType.LumberMill) < 3)
            yield return (BuildingType.LumberMill, wood.Pos);
        if (pop >= 8 && _m.NearestNode(ResourceType.Stone, _k.Home) is { } rock && Vector2.Distance(rock.Pos, _k.Home) > 7f
            && !HasNear(BuildingType.LumberMill, rock.Pos, 9f) && !HasNear(BuildingType.Camp, rock.Pos, 9f) && CountAll(BuildingType.Camp) < 4)
            yield return (BuildingType.Camp, rock.Pos);
        if (pop >= 12 && _m.NearestNode(ResourceType.Lumen, _k.Home) is { } well && !HasNear(BuildingType.Camp, well.Pos, 9f) && CountAll(BuildingType.Camp) < 4)
            yield return (BuildingType.Camp, well.Pos);
        if (!Has(BuildingType.FusionAltar)) yield return (BuildingType.FusionAltar, _k.Home);
        if (pop >= 10 && !Has(BuildingType.Granary)) yield return (BuildingType.Granary, _k.Home);
        if (!Has(BuildingType.TrainingGrounds) && pop >= 8) yield return (BuildingType.TrainingGrounds, _k.Home);
        if (!Has(BuildingType.Watchtower)) yield return (BuildingType.Watchtower, _k.Home);
        if (!Has(BuildingType.GuardPost) && pop >= 14) yield return (BuildingType.GuardPost, (_k.Home * 3 + Foe.Home) / 4);
        if (!Has(BuildingType.Shrine) && pop >= 18) yield return (BuildingType.Shrine, _k.Home);
        if (_k.Age >= 2)
        {
            if (!Has(BuildingType.LumenExtractor) && _m.NearestNode(ResourceType.Lumen, _k.Home) is { } w2) yield return (BuildingType.LumenExtractor, w2.Pos);
            if (!Has(BuildingType.Market)) yield return (BuildingType.Market, _k.Home);
            if (!Has(BuildingType.Dungeon)) yield return (BuildingType.Dungeon, _k.Home);
            if (!Has(BuildingType.MutationPit)) yield return (BuildingType.MutationPit, _k.Home);
            if (!Has(BuildingType.Infirmary) && Personality != AiPersonality.Archmage) yield return (BuildingType.Infirmary, _k.Home);
        }
        if (_k.Age >= 3 && !Has(BuildingType.LumenSpire)) yield return (BuildingType.LumenSpire, _k.Home);
        // Rich and nothing else to build: fortify the side facing the enemy.
        if (Smart && _k.Timber > 250f && _k.Stone > 150f && CountAll(BuildingType.GuardPost) + CountAll(BuildingType.Watchtower) < 8)
        {
            var front = _k.Home + Vector2.Normalize(Foe.Home - _k.Home) * (8f + 3f * CountAll(BuildingType.GuardPost));
            yield return (CountAll(BuildingType.GuardPost) <= CountAll(BuildingType.Watchtower) ? BuildingType.GuardPost : BuildingType.Watchtower, front);
        }
    }

    readonly List<(Building site, float age)> _sites = new();

    void Build()
    {
        for (int i = _sites.Count - 1; i >= 0; i--)
        {
            var (site, age) = _sites[i];
            if (site.Type != BuildingType.BuildSite) { _sites.RemoveAt(i); continue; }
            _sites[i] = (site, age + Tick);
            if (age > 90f) _m.Rush(_k, site);
        }
        if (_sites.Count >= 2) return;
        foreach (var (type, near) in Wishlist())
        {
            if (_m.CanPlace(_k, type, near + new Vector2(30f, 0f)) is { } err && err.StartsWith("Needs")) continue;   // can't afford
            // Farms grow as one tidy block: try right beside the farms we already have.
            if (type == BuildingType.Farm)
            {
                var spots = _m.BuildingsOf(_k).Where(b => b.Type == BuildingType.Farm || (b.Type == BuildingType.BuildSite && b.SiteFor == BuildingType.Farm))
                    .SelectMany(b => new[] { new Vector2(3, 0), new Vector2(-3, 0), new Vector2(0, 3), new Vector2(0, -3) }.Select(o => b.Pos + o))
                    .OrderBy(p => Vector2.Distance(p, _k.Home)).ToList();
                var spot = spots.FirstOrDefault(p => _m.CanPlace(_k, type, p) == null);
                if (spot != default && _m.PlaceSite(_k, type, spot) is { } fs) { _sites.Add((fs, 0f)); return; }
            }
            bool close = type is BuildingType.Camp or BuildingType.LumberMill or BuildingType.LumenExtractor;
            for (int i = 0; i < 60; i++)
            {
                float a = _rng.NextSingle() * MathF.Tau, d = close ? 1.5f + _rng.NextSingle() * 3.5f : 4f + _rng.NextSingle() * (type == BuildingType.Farm ? 12f : 9f);
                var p = near + new Vector2(MathF.Cos(a), MathF.Sin(a)) * d;
                if (_m.CanPlace(_k, type, p) != null) continue;
                if (_m.PlaceSite(_k, type, p) is { } site) _sites.Add((site, 0f));
                return;
            }
        }
    }

    // ---------- the army ----------

    Dictionary<string, int> Wanted()
    {
        var w = BaseWanted();
        // A smart ruler keeps a real army: about one fighter in five once the kingdom is growing.
        if (Smart && (Features & Feature.BigArmy) != 0 && _m.Population(_k) > 20)
        {
            int army = (int)(_m.Population(_k) * L.ArmyFraction);
            w["Guard"] = Math.Max(w["Guard"], army / 2 + 1);
            w["Hunter"] = Math.Max(w["Hunter"], army / 2);
        }
        return w;
    }

    Dictionary<string, int> BaseWanted() => Personality switch
    {
        AiPersonality.Warden => new() { ["Farmer"] = 3, ["Builder"] = 4, ["Hoarder"] = 2, ["Guard"] = 5, ["Hunter"] = 2, ["Healer"] = 2, ["Scout"] = 1, ["Thief"] = 1 },
        AiPersonality.Archmage => new() { ["Farmer"] = 3, ["Builder"] = 4, ["Hoarder"] = 4, ["Guard"] = 2, ["Thief"] = 2, ["Hunter"] = 2, ["Scout"] = 1, ["Healer"] = 1 },
        _ => new() { ["Farmer"] = 3, ["Builder"] = 4, ["Hoarder"] = 2, ["Guard"] = 3, ["Thief"] = 3, ["Hunter"] = 2, ["Scout"] = 1, ["Healer"] = 1 },
    };

    static readonly string[] Roles = ["Farmer", "Builder", "Guard", "Scout", "Thief", "Hunter", "Healer", "Hoarder"];

    // A mind's role is the job it is measurably best at; farming only counts if it can do nothing else.
    static string RoleOf(Mind m)
    {
        if (m.Skill.Length == Roles.Length)
        {
            int best = -1;
            for (int i = 1; i < Roles.Length; i++)
                if (m.Skill[i] >= 30f && (best < 0 || m.Skill[i] > m.Skill[best])) best = i;
            return Roles[best < 0 ? 0 : best];
        }
        var root = m;
        while (root.Parents.Length > 0) root = root.Parents[0];
        return root.Name;
    }

    void Produce()
    {
        if (_k.Queue.Count > 1 || _m.Population(_k) + _k.Queue.Count >= _m.PopulationCap(_k)) return;
        // Keep a grain reserve: more mouths are no use if the granary is empty.
        if (_k.Grain < 30f + 1.5f * _m.Population(_k)) return;
        var have = _m.Subjects(_k).GroupBy(u => RoleOf(u.Mind!)).ToDictionary(g => g.Key, g => g.Count());
        foreach (var o in _k.Queue) have[RoleOf(o.Mind)] = have.GetValueOrDefault(RoleOf(o.Mind)) + 1;
        var role = Wanted().OrderByDescending(kv => kv.Value - have.GetValueOrDefault(kv.Key)).First();
        if (role.Value - have.GetValueOrDefault(role.Key) <= 0 && _k.Grain < 120f) return;
        // Prefer the strongest bred version of that role, if it's affordable.
        var mind = _k.Library.Where(m => RoleOf(m) == role.Key && _k.Lumen >= Defs.LumenCost(m) + 10f)
            .OrderByDescending(m => m.Traits.Sum(t => (int)t.Tier)).FirstOrDefault()
            ?? _k.Library.FirstOrDefault(m => m.Name == role.Key);
        if (mind != null) _m.Produce(_k, mind);
    }

    // Jobs: (what, how many). Workers are picked by their class stats for each job.
    void Orders()
    {
        var subjects = _m.Subjects(_k).Where(u => u.CanAct && u.CarryingUnit < 0 && !_defenders.Contains(u.Id) && !_army.Contains(u.Id) && !_spireCrew.Contains(u.Id) && !_scouts.Contains(u.Id)).ToList();
        int n = subjects.Count;
        if (n == 0) return;

        // Fighters first, by class.
        var raiders = subjects.Where(u => RoleOf(u.Mind!) == "Thief").ToList();
        var guards = subjects.Where(u => RoleOf(u.Mind!) is "Guard" or "Hunter").ToList();
        bool endgame = !Smart && W.Time > 900f && guards.Count >= 4;
        if (endgame)
        {
            Give(guards, OrderKind.Capture, Foe.Ruler.Pos, Foe.Ruler.Id);
            Note("crown", $"At minute {W.Time / 60f:0} I sent {guards.Count} fighters after your ruler.");
        }
        else
        {
            var post = _m.BuildingsOf(_k).FirstOrDefault(b => b.Type == BuildingType.GuardPost)?.Pos ?? _k.Home + Vector2.Normalize(Foe.Home - _k.Home) * 5f;
            foreach (var g in guards.Where(g => g.Order != OrderKind.Guard)) Give([g], OrderKind.Guard, post, -1);
        }
        bool mayRaid = Difficulty != AiDifficulty.Easy || W.Time > 480f;
        // Smart raiders who meet guards slip away instead of being dragged off, and lie low for a while.
        if (Smart)
        {
            foreach (var r in raiders.Where(r => r.Order == OrderKind.Raid && !r.CarryStolen && Danger(r, 6f)).ToList())
            {
                _m.Give(_k, [r], OrderKind.Move, _k.Home, -1, ResourceType.None);
                _lieLow[r.Id] = W.Time + 120f;
            }
            raiders = raiders.Where(r => _lieLow.GetValueOrDefault(r.Id) < W.Time).ToList();
            // Only raid when their defence looks light next to our raiding party.
            if (_enemyFightersSeen * 2 > raiders.Count + 1) mayRaid = false;
        }
        if (mayRaid && raiders.Count >= 2 && (W.IsNight || raiders.Count >= 4 || Smart && L.RaidNightOnly < 0.5f))
        {
            foreach (var r in raiders.Where(r => r.Order != OrderKind.Raid)) Give([r], OrderKind.Raid, Foe.Home, Foe.Keep.Id);
        }
        else foreach (var r in raiders.Where(r => r.Order == OrderKind.Raid && r.CarryAmount <= 0f)) r.Order = OrderKind.None;

        var workers = subjects.Except(raiders).Except(guards).ToList();
        if (workers.Count == 0) return;

        // Builders on every open site.
        foreach (var (site, _) in _sites)
        {
            int on = workers.Count(u => u.Order == OrderKind.Build && u.OrderTarget == site.Id);
            foreach (var u in workers.Where(u => u.Order != OrderKind.Build).OrderByDescending(u => u.BuildMul).Take(Math.Max(0, 2 - on)).ToList())
                Give([u], OrderKind.Build, site.Pos, site.Id);
        }
        var free = workers.Where(u => u.Order != OrderKind.Build).ToList();

        // Then split the rest across resources.
        float food = _k.Grain < 60f + 2f * n ? MathF.Max(0.5f, L.FoodShare) : _k.Grain > 2000f ? L.FoodShare * 0.15f : _k.Grain > 600f + 20f * n ? L.FoodShare * 0.45f : L.FoodShare;
        var want = new (ResourceType res, float share)[]
        {
            (ResourceType.Food, food), (ResourceType.Timber, L.TimberShare), (ResourceType.Stone, L.StoneShare + (_k.Age >= 2 ? 0.05f : 0f)),
            (ResourceType.Lumen, Smart ? L.LumenShare : Personality == AiPersonality.Archmage ? 0.25f : 0.15f),
        };
        float total = want.Sum(w => w.share);
        // Never fewer farmers than a quarter of the kingdom plus one.
        int minFarm = _k.Grain < 300f + 5f * n ? n / 4 + 1 : 1;
        if (free.Count > 0 && want[0].share / total * free.Count < minFarm)
            want[0] = (ResourceType.Food, total * minFarm / free.Count);
        total = want.Sum(w => w.share);
        foreach (var (res, share) in want)
        {
            int target = (int)MathF.Round(free.Count * share / total);
            var on = free.Where(u => u.Order == OrderKind.Gather && u.OrderRes == res).ToList();
            if (on.Count > target)
                foreach (var u in on.OrderBy(u => World.GatherMul(u, res)).Take(on.Count - target)) u.Order = OrderKind.None;
        }
        foreach (var (res, share) in want)
        {
            int target = (int)MathF.Round(free.Count * share / total);
            int on = free.Count(u => u.Order == OrderKind.Gather && u.OrderRes == res);
            if (on >= target) continue;
            var node = BestNode(res);
            if (node == null) continue;
            var pick = free.Where(u => u.Order == OrderKind.None || u.Order == OrderKind.Move)
                .OrderByDescending(u => World.GatherMul(u, res)).Take(target - on).ToList();
            if (pick.Count > 0) _m.Give(_k, pick, OrderKind.Gather, node.Pos, W.Nodes.IndexOf(node), res);
        }
        // Anyone left over farms.
        var idle = free.Where(u => u.Order == OrderKind.None).ToList();
        if (idle.Count > 0 && BestNode(ResourceType.Food) is { } f) _m.Give(_k, idle, OrderKind.Gather, f.Pos, W.Nodes.IndexOf(f), ResourceType.Food);
    }

    // The nearest node of a kind to any of our drop-offs.
    ResourceNode? BestNode(ResourceType res)
    {
        var drops = _m.BuildingsOf(_k).Where(b => b.IsStore && World.Accepts(b, res)).Select(b => b.Pos).ToList();
        if (drops.Count == 0) drops.Add(_k.Home);
        return W.Nodes.Where(nd => nd.Type == res && nd.Amount > 0f)
            .OrderBy(nd => drops.Min(d => Vector2.DistanceSquared(d, nd.Pos))).FirstOrDefault();
    }

    void Give(List<Unit> us, OrderKind kind, Vector2 pos, int target) => _m.Give(_k, us, kind, pos, target, ResourceType.None);

    // ---------- the Dungeon ----------

    void Captives()
    {
        foreach (var c in _k.Captives.ToList())
        {
            if (!c.Interrogated && _m.Interrogate(_k, c) is { } trial && c.FromTeam == Foe.Team) LearnFrom(c.Mind!, trial);
            if (c.Mind == null) continue;
            bool known = _k.Library.Contains(c.Mind);
            if (!known && _m.Working(_k, BuildingType.Dungeon) != null)
            {
                _m.Extract(_k, c);
                Note($"extract{c.Mind.Id}", $"I extracted your {c.Mind.Name} and bred it into my own army.");
            }
            else if (_m.Working(_k, BuildingType.Dungeon) == null && c.Held > Defs.CaptiveEscapeTime - 10f && c.FromTeam < 2) _m.Ransom(_k, c);
            else if (known && _m.Working(_k, BuildingType.Dungeon) != null && c.Convert < 0f) _m.StartConvert(_k, c);
        }
    }

    static readonly string[] AxisNames = Enum.GetNames<Axis>();

    void LearnFrom(Mind m, TrialResult t)
    {
        var hi = Enumerable.Range(0, Trial.AxisCount).Where(i => t.Scores[i] >= 67f).Select(i => AxisNames[i]).ToList();
        var lo = Enumerable.Range(0, Trial.AxisCount).Where(i => t.Scores[i] <= 20f).Select(i => AxisNames[i]).ToList();
        if (hi.Count > 0 && lo.Count > 0)
            Note($"intel{m.Id}", $"I interrogated your {m.Name}: {string.Join(" and ", hi.Take(2))}, but never {lo[0]}.");
    }

    // What the enemy army leans toward, from everything interrogated so far.
    float[] EnemyProfile()
    {
        var p = new float[Trial.AxisCount];
        if (_k.Intel.Count == 0) return p;
        foreach (var t in _k.Intel.Values)
            for (int i = 0; i < p.Length; i++) p[i] += t.Scores[i] / _k.Intel.Count;
        return p;
    }

    // Counters: Cunning raiders are beaten by Patient, Territorial guards; Brave brawlers by Cunning ambushers.
    float CounterScore(Mind m, float[] enemy)
    {
        float s = m.Traits.Sum(t => t.Tier switch { Tier.Flaw => -4f, Tier.Common => 1f, Tier.Uncommon => 2f, Tier.Rare => 3f, _ => 5f });
        var t = m.Trial.Scores;
        s += enemy[(int)Axis.Cunning] / 50f * (t[(int)Axis.Patience] + t[(int)Axis.Territoriality]) / 50f;
        s += enemy[(int)Axis.Courage] / 50f * (t[(int)Axis.Cunning] + t[(int)Axis.Patience]) / 50f;
        s += enemy[(int)Axis.Teamwork] / 50f * t[(int)Axis.Aggression] / 50f;
        return s;
    }

    void CounterFuse()
    {
        if (_m.Working(_k, BuildingType.FusionAltar) == null) return;
        var donors = _m.Subjects(_k).Where(u => _m.CanUseAsDonor(_k, u) && RoleOf(u.Mind!) != "Farmer").ToList();
        if (donors.Count < 2) return;
        var enemy = EnemyProfile();
        Mind? best = null;
        (Unit? a, Unit? b, float r) pick = default;
        float bestScore = float.MinValue;
        for (int tries = 0; tries < 4 && _k.Lumen >= Defs.ForeseeCost + 20f; tries++)
        {
            var a = donors[_rng.Next(donors.Count)];
            var b = donors[_rng.Next(donors.Count)];
            if (a == b || a.Mind == b.Mind) continue;
            float r = 0.4f + 0.1f * _rng.Next(3);
            var seen = _m.Foresee(_k, a, b, r);
            if (seen == null) continue;
            float s = CounterScore(seen, enemy);
            if (s > bestScore) { bestScore = s; best = seen; pick = (a, b, r); }
        }
        if (best == null || best.Has(Tier.Flaw)) return;
        var made = _m.Fuse(_k, pick.a!, pick.b!, pick.r, true, best);
        if (made == null) return;
        string why = _k.Intel.Count > 0 ? " to counter what I learned from your captured subjects" : "";
        Note($"fuse{best.Id}", $"I fused {pick.a!.Mind!.Name} and {pick.b!.Mind!.Name} into {best.Name} ({string.Join(", ", best.Traits.Take(3).Select(t => t.Name))}){why}.");
    }

    void Drill()
    {
        if (_m.Working(_k, BuildingType.TrainingGrounds) == null) return;
        foreach (var job in _k.Training.ToList())
            if (job.Iteration >= 40) _m.FinishDrill(_k, job);
        if (_k.Training.Count > 0) return;
        var guard = _m.Subjects(_k).FirstOrDefault(u => RoleOf(u.Mind!) == "Guard" && _m.CanUseAsDonor(_k, u));
        var thief = _m.Subjects(_k).FirstOrDefault(u => RoleOf(u.Mind!) == "Thief" && _m.CanUseAsDonor(_k, u));
        // Once it has watched enough of the enemy, the AI drills against the real thing.
        if (guard != null && _k.Mirror.CountOf(TapeMode.Raider) >= Match.MirrorMinTapes && _m.StartMirrorDrill(_k, guard, TapeMode.Raider) == null) return;
        if (thief != null && _k.Mirror.CountOf(TapeMode.Guard) >= Match.MirrorMinTapes && _m.StartMirrorDrill(_k, thief, TapeMode.Guard) == null) return;
        if (Personality == AiPersonality.Warden && guard != null) _m.StartDrill(_k, guard, MindKind.Guard, "Hold the Line");
        else if (thief != null) _m.StartDrill(_k, thief, MindKind.Thief, "Raid");
    }

    void Mutate()
    {
        foreach (var (id, choices) in _m.PitChoices.ToList())
        {
            var u = W.Units[id];
            if (u.Team != _k.Team) continue;
            var best = choices.OrderByDescending(m => m.Traits.Sum(t => t.Tier == Tier.Flaw ? -3 : (int)t.Tier)).First();
            _m.ChooseMutant(_k, u, best);
            if (best.Has(Tier.Legendary)) Note($"mut{best.Id}", $"My Mutation Pit produced {best.Name}, a Legendary mind.");
        }
        if (_m.Working(_k, BuildingType.MutationPit) == null || _k.MutationHeat > (Personality == AiPersonality.Archmage ? 90f : 30f)) return;
        var subject = _m.Subjects(_k).Where(u => _m.CanUseAsDonor(_k, u) && RoleOf(u.Mind!) != "Farmer").OrderBy(_ => _rng.Next()).FirstOrDefault();
        if (subject != null) _m.Mutate(_k, subject, Personality == AiPersonality.Archmage && _k.Age >= 3 ? 3 : 2);
    }

    public string WarReport(bool aiWon)
    {
        var lines = new List<string> { aiWon ? "I won this time. Here is what I saw." : "You beat me. Here is what I saw." };
        var foe = Foe;
        var foeMinds = _m.Subjects(foe).Select(u => u.Mind).Where(m => m != null).ToList();
        if (foeMinds.Count > 0)
        {
            var avg = new float[Trial.AxisCount];
            foreach (var m in foeMinds) for (int i = 0; i < avg.Length; i++) avg[i] += m!.Trial.Scores[i] / foeMinds.Count;
            int hi = Array.IndexOf(avg, avg.Max()), lo = Array.IndexOf(avg, avg.Min());
            lines.Add($"Your army leaned {AxisNames[hi]} ({avg[hi]:0}) and was weakest in {AxisNames[lo]} ({avg[lo]:0}).");
        }
        lines.AddRange(Notes);
        if (foe.Lost > 0) lines.Add($"You lost {foe.Lost} subjects to captures.");
        if (_k.Intel.Count == 0) lines.Add("I never managed to interrogate one of yours.");
        return string.Join("\n", lines);
    }
}
