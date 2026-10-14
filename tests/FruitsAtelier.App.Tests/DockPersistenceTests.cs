using System.Text.Json;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class DockPersistenceTests
{
    public static void Run()
    {
        var ui = new Ui(false); ui.Resize(1440, 900); ui.LoadDocument(new MapDocument { DurationMs = 10000 });
        var original = ui.View.Document.DeepClone();
        var root = Path.GetFullPath(Path.Combine("artifacts", "tests", "dock-settings-" + Guid.NewGuid().ToString("N")));
        var settingsFile = Path.Combine(root, "library.json");
        ui.View.LibrarySettings.Workspace = Path.Combine(root, "workspace");
        int saves = 0;
        ui.View.RequestViewPreference = () => { ui.View.LibrarySettings.Save(settingsFile); saves++; };
        ui.View.ShowSongDock(); ui.Paint();
        var tab = ui.View.DockTabBounds(EditorView.DockPanel.SongSetup);
        ui.View.PointerDown(tab.X + 10, tab.Y + 10, 0, false, false);
        ui.View.PointerMove(8, 250, false, false); ui.View.PointerUp(8, 250, 0); ui.Paint();
        ui.View.ShowRandomDock(); ui.Paint();
        var handle = ui.View.DockResizeBounds(false);
        ui.View.PointerDown(handle.X + 4, handle.Y + 60, 0, false, false);
        int beforeDrag = saves;
        ui.View.PointerMove(ui.Width * .55f, handle.Y + 60, false, false); ui.Paint();
        Check(saves == beforeDrag, "Splitter motion does not write settings");
        ui.View.PointerUp(ui.Width * .55f, handle.Y + 60, 0); ui.Paint();
        Check(saves > beforeDrag && File.Exists(settingsFile), "Completed layout changes save user preferences");
        var expected = ui.View.CaptureDockLayouts();
        var loaded = LibrarySettings.Load(settingsFile);
        var reopened = new Ui(false); reopened.Resize(1440, 900); reopened.LoadDocument(original);
        reopened.View.LibrarySettings.DockLayouts = loaded.DockLayouts;
        reopened.View.RestoreDockLayouts(); reopened.Paint();
        Check(JsonSerializer.Serialize(expected) == JsonSerializer.Serialize(reopened.View.CaptureDockLayouts()), "Restart restores both workspaces, order, active tabs, widths and collapse state");
        Check(reopened.View.DockTabBounds(EditorView.DockPanel.SongSetup).X < 720
            && reopened.View.DockTabBounds(EditorView.DockPanel.RandomizeDroplets).X > 720, "Restored panels render on their saved sides");
        Check(reopened.View.Document.ContentEquals(original) && !reopened.View.IsDirty, "Restoring layout does not edit the project");
        reopened.Key(114); reopened.Key(27);
        Check(JsonSerializer.Serialize(expected) == JsonSerializer.Serialize(reopened.View.CaptureDockLayouts()), "Timing switches retain restored Mapping layout");
        reopened.ClickText(L.Get("dock.layout")); reopened.ClickText(L.Get("dock.resetLayout"));
        var reset = reopened.View.LibrarySettings.DockLayouts!;
        Check(reset.Mapping!.Left!.Panels!.SequenceEqual(["MovementStrain"])
            && reset.Mapping.Right!.Panels!.SequenceEqual(["Preview", "Notes", "SongSetup"]), "Reset replaces remembered layout with defaults");
        var oldSettings = JsonSerializer.Deserialize<LibrarySettings>("{}")!;
        Check(oldSettings.DockLayouts is null, "Existing settings need no migration");
        reopened.View.LibrarySettings.DockLayouts = new()
        {
            Mapping = new()
            {
                Left = new() { Panels = ["Preview", "Preview", "UnknownPanel"], Active = "UnknownPanel", Width = -20 },
                Right = new() { Panels = ["Preview", "Notes"], Active = "Preview", Width = float.NaN }
            },
            Timing = new() { Left = new() { Panels = [] }, Right = new() { Panels = [] } }
        };
        reopened.View.RestoreDockLayouts(); reopened.Paint();
        var sanitized = reopened.View.CaptureDockLayouts();
        Check(sanitized.Mapping!.Left!.Panels!.SequenceEqual(["Preview"])
            && sanitized.Mapping.Right!.Panels!.SequenceEqual(["Notes"])
            && sanitized.Mapping.Right.Active == "Notes" && float.IsFinite(sanitized.Mapping.Right.Width), "Unknown, duplicate and invalid entries normalize safely");
        Check(sanitized.Timing!.Left!.Panels!.Length == 0 && sanitized.Timing.Right!.Panels!.Length == 0, "Empty groups remain empty and workspaces remain independent");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
