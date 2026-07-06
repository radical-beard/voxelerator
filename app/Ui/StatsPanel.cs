using Godot;
using Voxelerator.Core;

namespace Voxelerator.App.Ui;

/// Live model stats: dimensions, fill, colors used, and the triangle count
/// from the real greedy mesher — perf budgets visible while authoring, not
/// discovered in-game.
public partial class StatsPanel : VBoxContainer
{
    private readonly EditorState _s;
    private readonly Label _dims = new(), _fill = new(), _tris = new(), _budget = new(), _perColor = new();
    private bool _dirty = true;
    private double _since;

    public StatsPanel(EditorState state)
    {
        _s = state;
        AddThemeConstantOverride("separation", 3);
        _s.Changed += () => _dirty = true;
        _s.StructureChanged += () => _dirty = true;
    }

    public override void _Ready()
    {
        AddChild(MakeHeader());
        foreach (var l in new[] { _dims, _fill, _tris, _budget, _perColor })
        {
            l.AddThemeFontSizeOverride("font_size", 12);
            l.AddThemeColorOverride("font_color", Ui.TextDim);
            AddChild(l);
        }
        _perColor.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _perColor.AddThemeFontSizeOverride("font_size", 11);
        Refresh();
    }

    private Label MakeHeader()
    {
        var h = new Label { Text = "MODEL" };
        h.AddThemeFontSizeOverride("font_size", 11);
        h.AddThemeColorOverride("font_color", Ui.TextDim);
        return h;
    }

    public override void _Process(double delta)
    {
        _since += delta;
        if (_dirty && _since > 0.3)
        {
            _dirty = false;
            _since = 0;
            Refresh();
        }
    }

    private void Refresh()
    {
        var s = Stats.Compute(_s.Model);
        _dims.Text = $"{s.W} × {s.D} × {s.H}  ·  {VoxelModel.VoxelMeters * s.W:0.#} m footprint";
        _fill.Text = $"{s.Filled:N0} voxels  ·  {s.ColorsUsed}/{_s.Model.Palette.Colors.Count} colors";
        _tris.Text = $"{s.Triangles:N0} triangles (greedy)";
        // NT budgets: standard buildings 600–1,500 · hero ≤6,000
        (string txt, Color c) = s.Triangles switch
        {
            <= 1500 => ("within standard-building budget", Ui.Ok),
            <= 6000 => ("hero-asset territory", Ui.TextDim),
            _ => ("over hero budget — consider simplifying", Ui.Danger),
        };
        _budget.Text = txt;
        _budget.AddThemeColorOverride("font_color", c);

        var used = new List<string>();
        for (int i = 1; i <= _s.Model.Palette.Colors.Count; i++)
            if (s.PerColor[i] > 0)
                used.Add($"{_s.Model.Palette.Colors[i - 1].Name} {s.PerColor[i]}");
        _perColor.Text = used.Count == 0 ? "" : string.Join(" · ", used);
    }
}
