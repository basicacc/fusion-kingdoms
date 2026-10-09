using Godot;

namespace FusionKingdoms;

// The interface skin (tools/art/ui.py) applied as one theme on the root window, so every control in every
// scene gets framed panels, slate buttons, parchment tooltips and gold sliders.
public partial class Skin : Node
{
    public static readonly Color Text = new("efe8d4");
    public static readonly Color TextHot = new("fff4c4");

    public static Texture2D Tex(string name) => ArtLibrary.Tex($"ui/{name}");

    // Textures are drawn at 2x, so a 1x margin of m pixels is 2m texels.
    public static StyleBoxTexture Box(string name, int margin, int pad, int padY = -1)
    {
        var s = new StyleBoxTexture { Texture = Tex(name) };
        s.TextureMarginLeft = s.TextureMarginRight = s.TextureMarginTop = s.TextureMarginBottom = margin * 2;
        s.ContentMarginLeft = s.ContentMarginRight = pad;
        s.ContentMarginTop = s.ContentMarginBottom = padY < 0 ? pad : padY;
        return s;
    }

    public override void _Ready()
    {
        var t = new Theme();
        var panel = Box("panel", 8, 12);
        foreach (var type in new[] { "PanelContainer", "Panel", "PopupPanel" }) t.SetStylebox("panel", type, panel);
        t.SetStylebox("panel", "PopupMenu", Box("panel", 8, 10, 8));
        t.SetStylebox("panel", "TooltipPanel", Box("tooltip", 4, 8, 5));
        t.SetColor("font_color", "TooltipLabel", new Color("2a2033"));
        t.SetFontSize("font_size", "TooltipLabel", 13);

        foreach (var type in new[] { "Button", "OptionButton", "MenuButton" })
        {
            t.SetStylebox("normal", type, Box("button_normal", 4, 10, 5));
            t.SetStylebox("hover", type, Box("button_hover", 4, 10, 5));
            var pressed = Box("button_pressed", 4, 10, 5);
            pressed.ContentMarginTop = 7;
            pressed.ContentMarginBottom = 3;
            t.SetStylebox("pressed", type, pressed);
            t.SetStylebox("hover_pressed", type, pressed);
            t.SetStylebox("disabled", type, Box("button_disabled", 4, 10, 5));
            t.SetStylebox("focus", type, new StyleBoxEmpty());
            t.SetColor("font_color", type, Text);
            t.SetColor("font_hover_color", type, TextHot);
            t.SetColor("font_pressed_color", type, new Color("d6cdb4"));
            t.SetColor("font_hover_pressed_color", type, TextHot);
            t.SetColor("font_focus_color", type, Text);
            t.SetColor("font_disabled_color", type, new Color(0.62f, 0.6f, 0.58f, 0.7f));
            t.SetColor("font_outline_color", type, new Color("15111c"));
            t.SetConstant("outline_size", type, 3);
        }
        t.SetIcon("arrow", "OptionButton", Tex("arrow"));

        foreach (var type in new[] { "LineEdit", "SpinBox" })
        {
            t.SetStylebox("normal", type, Box("field", 4, 8, 5));
            t.SetStylebox("focus", type, Box("field", 4, 8, 5));
            t.SetStylebox("read_only", type, Box("field", 4, 8, 5));
        }
        t.SetStylebox("panel", "ItemList", Box("field", 4, 6));
        t.SetStylebox("focus", "ItemList", new StyleBoxEmpty());
        t.SetStylebox("selected", "ItemList", new StyleBoxFlat { BgColor = new Color("3d4870") });
        t.SetStylebox("selected_focus", "ItemList", new StyleBoxFlat { BgColor = new Color("4b5a8c") });

        var track = Box("slider_track", 3, 0, 6);
        t.SetStylebox("slider", "HSlider", track);
        t.SetStylebox("grabber_area", "HSlider", Box("slider_fill", 3, 0, 6));
        t.SetStylebox("grabber_area_highlight", "HSlider", Box("slider_fill", 3, 0, 6));
        t.SetIcon("grabber", "HSlider", Tex("grabber"));
        t.SetIcon("grabber_highlight", "HSlider", Tex("grabber_hot"));

        foreach (var type in new[] { "VScrollBar", "HScrollBar" })
        {
            t.SetStylebox("scroll", type, Box("scroll_track", 2, 0));
            t.SetStylebox("grabber", type, Box("scroll_grab", 2, 0));
            t.SetStylebox("grabber_highlight", type, Box("scroll_grab_hot", 2, 0));
            t.SetStylebox("grabber_pressed", type, Box("scroll_grab_hot", 2, 0));
        }

        t.SetIcon("checked", "CheckBox", Tex("check_on"));
        t.SetIcon("unchecked", "CheckBox", Tex("check_off"));
        foreach (var st in new[] { "normal", "hover", "pressed", "hover_pressed", "focus" })
            t.SetStylebox(st, "CheckBox", new StyleBoxEmpty { ContentMarginLeft = 2, ContentMarginRight = 2 });

        t.SetStylebox("background", "ProgressBar", Box("field", 4, 2));
        t.SetStylebox("fill", "ProgressBar", new StyleBoxFlat { BgColor = new Color("f6c544"), BorderColor = new Color("1d1626"), BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2 });

        // Text everywhere gets a soft shadow so it reads over the map.
        t.SetColor("font_color", "Label", Text);
        t.SetColor("font_shadow_color", "Label", new Color(0, 0, 0, 0.55f));
        t.SetConstant("shadow_offset_x", "Label", 1);
        t.SetConstant("shadow_offset_y", "Label", 1);

        GetTree().Root.Theme = t;
        // Controls under a CanvasLayer don't inherit the window theme, so top-level controls get it directly.
        GetTree().NodeAdded += n =>
        {
            if (n is Control c && c.GetParent() is not Control && c.Theme == null) c.Theme = t;
        };
    }
}
