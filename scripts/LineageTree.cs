using FusionKingdoms.Sim;
using Godot;

namespace FusionKingdoms;

// A mind's family tree: the mind at the top, the minds it came from below. Click an ancestor to inspect it.
public partial class LineageTree : Control
{
    const float NodeW = 150f, NodeH = 46f, GapX = 14f, GapY = 34f;
    const int MaxDepth = 5;

    public Mind Root = null!;
    public Action<Mind>? Picked;
    readonly List<(Rect2 rect, Mind mind)> _boxes = new();

    int Leaves(Mind m, int depth) =>
        depth >= MaxDepth || m.Parents.Length == 0 ? 1 : m.Parents.Sum(p => Leaves(p, depth + 1));

    public void Layout()
    {
        _boxes.Clear();
        Place(Root, 0, 0f);
        float w = _boxes.Count == 0 ? 0 : _boxes.Max(b => b.rect.End.X), h = _boxes.Count == 0 ? 0 : _boxes.Max(b => b.rect.End.Y);
        CustomMinimumSize = new Vector2(w + 10, h + 10);
        QueueRedraw();
    }

    float Place(Mind m, int depth, float left)
    {
        float width = Leaves(m, depth) * (NodeW + GapX);
        var rect = new Rect2(left + width / 2 - NodeW / 2, depth * (NodeH + GapY), NodeW, NodeH);
        _boxes.Add((rect, m));
        if (depth + 1 < MaxDepth)
        {
            float x = left;
            foreach (var p in m.Parents)
            {
                Place(p, depth + 1, x);
                x += Leaves(p, depth + 1) * (NodeW + GapX);
            }
        }
        return width;
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        var byMind = new Dictionary<Mind, Rect2>();
        foreach (var (r, m) in _boxes) byMind.TryAdd(m, r);
        foreach (var (r, m) in _boxes)
        {
            for (int i = 0; i < m.Parents.Length; i++)
            {
                var pr = _boxes.FirstOrDefault(b => b.mind == m.Parents[i] && b.rect.Position.Y > r.Position.Y).rect;
                if (pr.Size == Vector2.Zero) continue;
                var a = new Vector2(r.GetCenter().X, r.End.Y);
                var b = new Vector2(pr.GetCenter().X, pr.Position.Y);
                DrawLine(a, b, new Color(1, 1, 1, 0.3f), 1.5f);
                string tag = m.Parents.Length == 2 ? $"{(i == 0 ? m.Ratio : 1 - m.Ratio) * 100:0}%" : StepOf(m);
                DrawString(font, (a + b) / 2 + new Vector2(4, 0), tag, fontSize: 10, modulate: new Color("f2d24b"));
            }
        }
        foreach (var (r, m) in _boxes)
        {
            DrawRect(r, new Color(0.1f, 0.1f, 0.14f));
            DrawRect(new Rect2(r.Position, new Vector2(4, r.Size.Y)), m.AuraColor());
            DrawRect(r, m == Root ? new Color("7ff0ff") : new Color(1, 1, 1, 0.3f), false, 1.5f);
            DrawString(font, r.Position + new Vector2(8, 16), m.Display, fontSize: 12);
            string sub = m.Traits.Count > 0 ? string.Join(", ", m.Traits.Take(2).Select(t => t.Name)) : m.Title ?? "";
            DrawString(font, r.Position + new Vector2(8, 32), $"gen {m.Generation}  {sub}", fontSize: 9, modulate: new Color(1, 1, 1, 0.6f), width: NodeW - 10);
        }
    }

    static string StepOf(Mind m)
    {
        var parent = m.Parents[0];
        var step = m.Drills.Except(parent.Drills).LastOrDefault();
        return step ?? "";
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;
        foreach (var (r, m) in _boxes)
            if (r.HasPoint(mb.Position)) { Picked?.Invoke(m); AcceptEvent(); return; }
    }

    // Opens a window with the tree on the left and the picked ancestor's Trial Card on the right.
    public static void Open(Node host, Mind root)
    {
        var win = new Window { Title = $"Family tree of {root.Name}", Size = new Vector2I(1000, 560), Exclusive = false, Transient = true };
        win.CloseRequested += win.QueueFree;
        var bg = new PanelContainer();
        bg.AddThemeStyleboxOverride("panel", Ui.Box(new Color("15151c"), 10));
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        var h = Ui.HBox(12);
        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        var tree = new LineageTree { Root = root };
        scroll.AddChild(tree);
        var side = new ScrollContainer { CustomMinimumSize = new Vector2(260, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var card = new TrialCard { ShowTreeButton = false };
        side.AddChild(card);
        h.AddChild(scroll);
        h.AddChild(side);
        bg.AddChild(h);
        win.AddChild(bg);
        host.GetTree().Root.AddChild(win);
        tree.Picked = m => card.Show(m);
        tree.Layout();
        card.Show(root);
        win.PopupCentered();
    }
}
