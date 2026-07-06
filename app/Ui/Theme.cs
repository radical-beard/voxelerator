using Godot;

namespace Voxelerator.App.Ui;

/// The design system in one file: classic dark, dark-purple primary.
/// Every screen pulls tokens from here — no ad-hoc colors in views.
public static class Ui
{
    // ---- color tokens ---------------------------------------------------
    public static readonly Color Bg0 = new("0e0d12");        // window
    public static readonly Color Bg1 = new("16141c");        // panels / rails
    public static readonly Color Bg2 = new("1e1b26");        // cards / inputs
    public static readonly Color Bg3 = new("272334");        // hover
    public static readonly Color Stroke = new("2e2940");     // borders
    public static readonly Color StrokeSoft = new("241f33");

    public static readonly Color Text = new("e8e5f2");
    public static readonly Color TextDim = new("9b94ac");
    public static readonly Color TextFaint = new("655e7a");

    public static readonly Color Primary = new("7c3aed");    // dark purple
    public static readonly Color PrimaryHover = new("8b5cf6");
    public static readonly Color PrimaryActive = new("6d28d9");
    public static readonly Color PrimaryDim = new(0x7c / 255f, 0x3a / 255f, 0xed / 255f, 0.16f);
    public static readonly Color OnPrimary = new("f5f3ff");
    public static readonly Color Glow = new("a78bfa");

    public static readonly Color Danger = new("e5484d");
    public static readonly Color Ok = new("52a760");
    public static readonly Color CanvasDark = new("0a090d"); // checkerboard base
    public static readonly Color CanvasLight = new("121016");

    // ---- metrics ----------------------------------------------------------
    public const int RadiusL = 10, Radius = 8, RadiusS = 6;
    public const int Gap = 8, GapL = 16, GapS = 4;

    // ---- theme -------------------------------------------------------------

    public static Theme Build()
    {
        var t = new Theme { DefaultFontSize = 14 };

        // Buttons
        t.SetStylebox("normal", "Button", Flat(Bg2, Stroke, Radius));
        t.SetStylebox("hover", "Button", Flat(Bg3, Stroke, Radius));
        t.SetStylebox("pressed", "Button", Flat(PrimaryActive with { A = 0.35f }, Primary, Radius));
        t.SetStylebox("focus", "Button", FlatBorderOnly(Primary, Radius));
        t.SetStylebox("disabled", "Button", Flat(Bg1, StrokeSoft, Radius));
        t.SetColor("font_color", "Button", Text);
        t.SetColor("font_hover_color", "Button", Text);
        t.SetColor("font_pressed_color", "Button", OnPrimary);
        t.SetColor("font_disabled_color", "Button", TextFaint);
        t.SetColor("icon_normal_color", "Button", TextDim);

        // Labels
        t.SetColor("font_color", "Label", Text);

        // LineEdit / SpinBox
        t.SetStylebox("normal", "LineEdit", Flat(Bg2, StrokeSoft, RadiusS, 6));
        t.SetStylebox("focus", "LineEdit", Flat(Bg2, Primary, RadiusS, 6));
        t.SetColor("font_color", "LineEdit", Text);
        t.SetColor("font_placeholder_color", "LineEdit", TextFaint);
        t.SetColor("caret_color", "LineEdit", Glow);
        t.SetColor("selection_color", "LineEdit", PrimaryDim with { A = 0.45f });

        // Panels
        t.SetStylebox("panel", "PanelContainer", Flat(Bg1, StrokeSoft, RadiusL));
        t.SetStylebox("panel", "Panel", Flat(Bg1, StrokeSoft, 0));

        // Popups & dialogs
        t.SetStylebox("panel", "PopupPanel", Flat(Bg1, Stroke, RadiusL));
        t.SetStylebox("panel", "PopupMenu", Flat(Bg1, Stroke, RadiusS));
        t.SetStylebox("hover", "PopupMenu", Flat(PrimaryDim, PrimaryDim, RadiusS));
        t.SetColor("font_color", "PopupMenu", Text);
        t.SetColor("font_hover_color", "PopupMenu", OnPrimary);
        t.SetStylebox("panel", "AcceptDialog", Flat(Bg1, Stroke, RadiusL));
        t.SetColor("title_color", "AcceptDialog", Text);

        // ItemList (library grid uses custom cards; this covers pickers)
        t.SetStylebox("panel", "ItemList", Flat(Bg2, StrokeSoft, RadiusS));
        t.SetColor("font_color", "ItemList", Text);
        t.SetColor("font_selected_color", "ItemList", OnPrimary);
        t.SetStylebox("selected", "ItemList", Flat(PrimaryDim, Primary, RadiusS));
        t.SetStylebox("selected_focus", "ItemList", Flat(PrimaryDim, Primary, RadiusS));

        // Sliders
        t.SetStylebox("slider", "HSlider", Flat(Bg2, StrokeSoft, 4));
        t.SetStylebox("grabber_area", "HSlider", Flat(Primary, Primary, 4));
        t.SetStylebox("grabber_area_highlight", "HSlider", Flat(PrimaryHover, PrimaryHover, 4));

        // Checkbox / OptionButton inherit Button styles well enough
        t.SetColor("font_color", "CheckBox", Text);
        t.SetColor("font_hover_color", "CheckBox", Text);
        t.SetColor("font_color", "OptionButton", Text);
        t.SetColor("font_hover_color", "OptionButton", Text);

        // Tooltips
        t.SetStylebox("panel", "TooltipPanel", Flat(Bg3, Stroke, RadiusS));
        t.SetColor("font_color", "TooltipLabel", Text);

        // ScrollBars — slim, quiet
        var track = Flat(Colors.Transparent, Colors.Transparent, 3);
        var grab = Flat(Stroke, Stroke, 3);
        var grabHl = Flat(Primary, Primary, 3);
        foreach (var cls in new[] { "VScrollBar", "HScrollBar" })
        {
            t.SetStylebox("scroll", cls, track);
            t.SetStylebox("grabber", cls, grab);
            t.SetStylebox("grabber_highlight", cls, grabHl);
            t.SetStylebox("grabber_pressed", cls, grabHl);
        }
        return t;
    }

    public static StyleBoxFlat Flat(Color bg, Color border, int radius, int margin = 8)
    {
        var sb = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(radius);
        sb.SetContentMarginAll(margin);
        return sb;
    }

    public static StyleBoxFlat FlatBorderOnly(Color border, int radius)
    {
        var sb = new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = border, DrawCenter = false };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(radius);
        return sb;
    }

    // ---- widget helpers -------------------------------------------------------

    public static Label Title(string text, int size = 22)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", Text);
        return l;
    }

    public static Label Dim(string text, int size = 13)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", TextDim);
        return l;
    }

    /// Solid purple call-to-action button.
    public static Button PrimaryButton(string text)
    {
        var b = new Button { Text = text };
        b.AddThemeStyleboxOverride("normal", Flat(Primary, Primary, Radius));
        b.AddThemeStyleboxOverride("hover", Flat(PrimaryHover, PrimaryHover, Radius));
        b.AddThemeStyleboxOverride("pressed", Flat(PrimaryActive, PrimaryActive, Radius));
        b.AddThemeColorOverride("font_color", OnPrimary);
        b.AddThemeColorOverride("font_hover_color", OnPrimary);
        b.AddThemeColorOverride("font_pressed_color", OnPrimary);
        return b;
    }

    /// Quiet bordered button for secondary actions.
    public static Button GhostButton(string text)
    {
        var b = new Button { Text = text };
        b.AddThemeStyleboxOverride("normal", Flat(Colors.Transparent, Stroke, Radius));
        b.AddThemeStyleboxOverride("hover", Flat(Bg3, Stroke, Radius));
        return b;
    }

    /// Tool-rail toggle: lights purple when active.
    public static Button ToolButton(string text, string tooltip)
    {
        var b = new Button
        {
            Text = text,
            ToggleMode = true,
            TooltipText = tooltip,
            CustomMinimumSize = new Vector2(44, 40),
        };
        b.AddThemeStyleboxOverride("normal", Flat(Bg2, StrokeSoft, RadiusS, 4));
        b.AddThemeStyleboxOverride("hover", Flat(Bg3, Stroke, RadiusS, 4));
        b.AddThemeStyleboxOverride("pressed", Flat(PrimaryDim, Primary, RadiusS, 4));
        b.AddThemeColorOverride("font_pressed_color", Glow);
        return b;
    }

    public static PanelContainer Card(Control content, int margin = 12)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", Flat(Bg1, StrokeSoft, RadiusL, margin));
        p.AddChild(content);
        return p;
    }

    public static Control VSpace(int px) => new() { CustomMinimumSize = new Vector2(0, px) };
    public static Control HSpace(int px) => new() { CustomMinimumSize = new Vector2(px, 0) };

    public static Control Stretch()
        => new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };

    public static ColorRect Divider()
        => new() { Color = StrokeSoft, CustomMinimumSize = new Vector2(0, 1) };
}
