using Godot;
using Voxelerator.Core;

namespace Voxelerator.App.Ui;

/// The landing screen: recents grid (thumbnails from the registry), search,
/// Open folder…, New model. Models live wherever the user put them — this is
/// a view over recents.json, not a walled library.
public partial class LibraryScreen : Control
{
    private readonly LineEdit _search = new() { PlaceholderText = "search models…", CustomMinimumSize = new Vector2(240, 0) };
    private readonly GridContainer _grid = new() { Columns = 4 };
    private readonly VBoxContainer _emptyState = new();

    public override void _Ready()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 48);
        margin.AddThemeConstantOverride("margin_right", 48);
        margin.AddThemeConstantOverride("margin_top", 36);
        margin.AddThemeConstantOverride("margin_bottom", 40);
        AddChild(margin);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", Ui.GapL);
        margin.AddChild(col);

        // header
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", Ui.Gap);
        var titleBox = new VBoxContainer();
        var title = Ui.Title("VOXELERATOR", 26);
        var accent = new ColorRect { Color = Ui.Primary, CustomMinimumSize = new Vector2(64, 3) };
        titleBox.AddChild(title);
        titleBox.AddChild(accent);
        titleBox.AddChild(Ui.Dim("layer-stack voxel editor — the palette is law"));
        header.AddChild(titleBox);
        header.AddChild(Ui.Stretch());

        var openBtn = Ui.GhostButton("Open folder…");
        openBtn.Pressed += BrowseForModel;
        var newBtn = Ui.PrimaryButton("+  New model");
        newBtn.Pressed += () => NewModelDialog.Show(this);
        header.AddChild(_search);
        header.AddChild(openBtn);
        header.AddChild(newBtn);
        col.AddChild(header);
        col.AddChild(Ui.Divider());

        // grid
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _grid.AddThemeConstantOverride("h_separation", Ui.GapL);
        _grid.AddThemeConstantOverride("v_separation", Ui.GapL);
        _grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_grid);
        col.AddChild(scroll);

        // empty state
        _emptyState.Alignment = BoxContainer.AlignmentMode.Center;
        _emptyState.SizeFlagsVertical = SizeFlags.ExpandFill;
        var emptyTitle = Ui.Title("no models yet", 20);
        emptyTitle.HorizontalAlignment = HorizontalAlignment.Center;
        var emptyDim = Ui.Dim("create your first model, or open a folder of layer PNGs");
        emptyDim.HorizontalAlignment = HorizontalAlignment.Center;
        var emptyBtn = Ui.PrimaryButton("+  New model");
        emptyBtn.Pressed += () => NewModelDialog.Show(this);
        var btnRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        btnRow.AddChild(emptyBtn);
        _emptyState.AddChild(emptyTitle);
        _emptyState.AddChild(Ui.VSpace(4));
        _emptyState.AddChild(emptyDim);
        _emptyState.AddChild(Ui.VSpace(12));
        _emptyState.AddChild(btnRow);
        col.AddChild(_emptyState);

        _search.TextChanged += _ => Refresh();
        Refresh();
    }

    public void Refresh()
    {
        foreach (var c in _grid.GetChildren()) c.QueueFree();
        var recents = Registry.LoadRecents().Models;
        var filter = _search.Text.Trim();
        var shown = recents.Where(e =>
            filter.Length == 0 || e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        _emptyState.Visible = recents.Count == 0;
        foreach (var e in shown)
            _grid.AddChild(MakeCard(e.Path, e.Name, Directory.Exists(e.Path)));
    }

    private Control MakeCard(string path, string name, bool exists)
    {
        var card = new Button
        {
            CustomMinimumSize = new Vector2(220, 236),
            TooltipText = path,
        };
        card.AddThemeStyleboxOverride("normal", Ui.Flat(Ui.Bg1, Ui.StrokeSoft, Ui.RadiusL, 10));
        card.AddThemeStyleboxOverride("hover", Ui.Flat(Ui.Bg2, Ui.Primary, Ui.RadiusL, 10));
        card.AddThemeStyleboxOverride("pressed", Ui.Flat(Ui.Bg2, Ui.PrimaryActive, Ui.RadiusL, 10));

        var v = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        v.SetAnchorsPreset(LayoutPreset.FullRect);
        v.AddThemeConstantOverride("separation", 6);

        var thumb = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(0, 160),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var thumbPath = Registry.ThumbnailPath(path);
        if (exists && File.Exists(thumbPath))
        {
            var img = new Image();
            if (img.LoadPngFromBuffer(File.ReadAllBytes(thumbPath)) == Error.Ok)
                thumb.Texture = ImageTexture.CreateFromImage(img);
        }
        v.AddChild(thumb);

        var nameLabel = new Label { Text = name, MouseFilter = MouseFilterEnum.Ignore };
        nameLabel.AddThemeFontSizeOverride("font_size", 15);
        nameLabel.AddThemeColorOverride("font_color", exists ? Ui.Text : Ui.TextFaint);
        v.AddChild(nameLabel);

        string sub = "missing folder — click to forget";
        if (exists)
        {
            try
            {
                var manifest = Path.Combine(path, ModelStore.ManifestFile);
                if (File.Exists(manifest))
                {
                    var doc = VoxJson.Read(File.ReadAllText(manifest), VoxJson.Default.ManifestDoc);
                    sub = $"{doc.Size.W}×{doc.Size.D}×{doc.Size.H} · {doc.Palette}";
                }
                else sub = "layer PNGs (no manifest)";
            }
            catch { sub = "unreadable manifest"; }
        }
        var subLabel = Ui.Dim(sub, 11);
        subLabel.MouseFilter = MouseFilterEnum.Ignore;
        v.AddChild(subLabel);
        card.AddChild(v);

        card.Pressed += () =>
        {
            if (exists) AppHost.Instance.OpenModel(path);
            else { Registry.Forget(path); Refresh(); }
        };
        card.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
                ShowCardMenu(path, exists);
        };
        return card;
    }

    private void ShowCardMenu(string path, bool exists)
    {
        var menu = new PopupMenu();
        if (exists)
        {
            menu.AddItem("Rename…", 0);
            menu.AddItem("Duplicate…", 1);
            menu.AddSeparator();
            menu.AddItem("Delete (kept in backups)", 2);
        }
        menu.AddItem("Remove from library", 3);
        AddChild(menu);
        menu.IdPressed += id =>
        {
            switch (id)
            {
                case 0: AskName("Rename model", Path.GetFileName(path), newName =>
                {
                    var (m, _) = ModelStore.Load(path);
                    ModelStore.Save(m, newName, path);
                    Registry.Touch(path, newName);
                    Refresh();
                }); break;
                case 1: AskName("Duplicate model", Path.GetFileName(path) + "-copy", newName =>
                {
                    var dest = Path.Combine(Path.GetDirectoryName(path)!, newName);
                    if (Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any()) return;
                    var (m, _) = ModelStore.Load(path);
                    ModelStore.Save(m, newName, dest);
                    Registry.Touch(dest, newName);
                    Refresh();
                }); break;
                case 2:
                {
                    var confirm = new ConfirmationDialog
                    {
                        Title = "Delete model",
                        DialogText = $"Move '{Path.GetFileName(path)}' to backups and delete the folder?\nA snapshot is kept under the registry's backups.",
                    };
                    AddChild(confirm);
                    confirm.Confirmed += () =>
                    {
                        try
                        {
                            Registry.Snapshot(path);
                            Directory.Delete(path, recursive: true);
                        }
                        catch (IOException) { /* half-deleted folders still get forgotten */ }
                        Registry.Forget(path);
                        Refresh();
                    };
                    confirm.PopupCentered();
                    break;
                }
                case 3: Registry.Forget(path); Refresh(); break;
            }
        };
        menu.Position = (Vector2I)GetGlobalMousePosition();
        menu.Popup();
    }

    private void AskName(string title, string initial, Action<string> apply)
    {
        var dlg = new Window { Title = title, Size = new Vector2I(420, 150), Transient = true, Exclusive = true };
        dlg.CloseRequested += dlg.QueueFree;
        var panel = new PanelContainer { Theme = Ui.Build() };
        panel.SetAnchorsPreset(LayoutPreset.FullRect);
        panel.AddThemeStyleboxOverride("panel", Ui.Flat(Ui.Bg1, Ui.Stroke, 0, 16));
        dlg.AddChild(panel);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        panel.AddChild(col);
        var edit = new LineEdit { Text = initial };
        col.AddChild(edit);
        col.AddChild(Ui.Stretch());
        var ok = Ui.PrimaryButton("Apply");
        ok.Pressed += () =>
        {
            var name = edit.Text.Trim();
            if (name.Length > 0 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0) apply(name);
            dlg.QueueFree();
        };
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddChild(ok);
        col.AddChild(row);
        AddChild(dlg);
        dlg.PopupCentered();
    }

    private void BrowseForModel()
    {
        var dlg = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            Title = "Open a model folder (layer PNGs + model.json)",
        };
        AddChild(dlg);
        dlg.DirSelected += dir =>
        {
            try
            {
                var (_, doc) = ModelStore.Load(dir);       // validates before registering
                Registry.Touch(dir, doc.Name);
                AppHost.Instance.OpenModel(dir);
            }
            catch (Exception e) when (e is ModelFormatException or FormatException or IOException)
            {
                var err = new AcceptDialog { Title = "Not a valid model folder", DialogText = e.Message };
                AddChild(err);
                err.PopupCentered();
            }
        };
        dlg.PopupCentered(new Vector2I(900, 600));
    }
}
