using System.Numerics;

namespace FusionKingdoms.Sim;

// Path-aware directions for big maps. A brain's "direction to home" and "direction to the order" point at a
// waypoint a few tiles along the shortest walkable path, so rivers and rock walls don't trap it, and the target
// it sees is always close, like the ones it was trained on.
public sealed class Navigator
{
    public const float Lookahead = 9f;
    const int CacheSize = 96;

    readonly World _w;
    readonly Dictionary<int, int[]> _fields = new();
    readonly LinkedList<int> _order = new();
    readonly object _lock = new();

    public Navigator(World w) => _w = w;

    public void Invalidate()
    {
        lock (_lock) { _fields.Clear(); _order.Clear(); }
    }

    // Each cell stores the index of the next cell toward the goal (-1 = unreachable, itself = goal).
    int[] FieldTo(int goal)
    {
        lock (_lock)
        {
            if (_fields.TryGetValue(goal, out var f)) return f;
        }
        var field = Build(goal);
        lock (_lock)
        {
            _fields[goal] = field;
            _order.AddLast(goal);
            if (_order.Count > CacheSize) { _fields.Remove(_order.First!.Value); _order.RemoveFirst(); }
        }
        return field;
    }

    int[] Build(int goal)
    {
        int w = _w.Width, h = _w.Height, n = w * h;
        var cost = new float[n];
        var next = new int[n];
        Array.Fill(cost, float.MaxValue);
        Array.Fill(next, -1);
        var pq = new PriorityQueue<int, float>();
        cost[goal] = 0f;
        next[goal] = goal;
        pq.Enqueue(goal, 0f);
        Span<(int dx, int dy, float len)> dirs =
        [
            (1, 0, 1f), (-1, 0, 1f), (0, 1, 1f), (0, -1, 1f),
            (1, 1, 1.414f), (1, -1, 1.414f), (-1, 1, 1.414f), (-1, -1, 1.414f),
        ];
        while (pq.TryDequeue(out int cur, out float c))
        {
            if (c > cost[cur]) continue;
            int cx = cur % w, cy = cur / w;
            foreach (var (dx, dy, len) in dirs)
            {
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= w || y >= h) continue;
                int i = y * w + x;
                float mul = TerrainInfo.SpeedMul(_w.Map[i]);
                if (mul <= 0f) continue;
                // Walls block every path; gates are open (their owners walk through, enemies have to break them).
                if (_w.Walls.Length > 0 && _w.Walls[i] != 0)
                {
                    if ((_w.Walls[i] & 0x80) == 0) continue;
                    mul *= 0.5f;
                }
                if (dx != 0 && dy != 0 && (TerrainInfo.Blocked(_w.Map[cy * w + x]) || TerrainInfo.Blocked(_w.Map[y * w + cx]))) continue;
                float nc = c + len / mul;
                if (nc >= cost[i]) continue;
                cost[i] = nc;
                next[i] = cur;
                pq.Enqueue(i, nc);
            }
        }
        return next;
    }

    public Vector2 Waypoint(Vector2 from, Vector2 goal, float lookahead = Lookahead)
    {
        if (Vector2.Distance(from, goal) <= lookahead) return goal;
        int gx = Math.Clamp((int)goal.X, 0, _w.Width - 1), gy = Math.Clamp((int)goal.Y, 0, _w.Height - 1);
        int g = gy * _w.Width + gx;
        if (TerrainInfo.Blocked(_w.Map[g])) return goal;
        var field = FieldTo(g);
        int fx = Math.Clamp((int)from.X, 0, _w.Width - 1), fy = Math.Clamp((int)from.Y, 0, _w.Height - 1);
        int cur = fy * _w.Width + fx;
        if (field[cur] < 0) return goal;
        var start = from;
        var p = from;
        for (int i = 0; i < 40; i++)
        {
            int nxt = field[cur];
            if (nxt == cur || nxt < 0) return goal;
            cur = nxt;
            p = new Vector2(cur % _w.Width + 0.5f, cur / _w.Width + 0.5f);
            if (Vector2.Distance(start, p) >= lookahead) break;
        }
        return p;
    }
}
