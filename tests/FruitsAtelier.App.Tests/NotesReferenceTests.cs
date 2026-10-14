using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class NotesReferenceTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var map = new MapDocument { DurationMs = 12000 };
                map.Fruits.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 2000, X = 300 }]);
                var ui = new Ui(false); ui.Resize(1440, 900); ui.LoadDocument(map);
                string clipboard = "";
                ui.View.RequestCopyText = text => clipboard = text;
                ui.View.RequestPasteField = () => ui.View.PasteFieldText(clipboard);
                ui.Key('A', ctrl: true); ui.Key('C', ctrl: true);
                string reference = clipboard;
                Check(reference == "00:01:000 (1,2) - ", "Uses the external object-reference clipboard format");
                OpenNotes();
                ui.Key('V', ctrl: true);
                Check(ui.View.CaptureProject().Notes == reference && ui.View.Document.ContentEquals(map),
                    "Synchronous clipboard paste inserts text without duplicating map objects");
                ui.Key('Z', ctrl: true); Check(ui.View.CaptureProject().Notes == "", "Pasting notes is one text undo step");
                ui.Key('Y', ctrl: true); Check(ui.View.CaptureProject().Notes == reference, "Redo restores reference text");
                ui.Key('A', ctrl: true); ui.Key('C', ctrl: true);
                Check(clipboard == reference, "Notes copy preserves the same plain text");
                ui.Key(27); ui.View.MarkSaved(); ui.Paint();
                var conversion = ui.View.Conversion;
                var hit = ui.View.NotesReferenceBounds.First();
                ui.Click(hit.X + 2, hit.Y + 8);
                Check(ui.View.PlayheadMs == 1000 && ui.View.SelectedObjectIds.ToHashSet().SetEquals(map.Fruits.Select(f => f.Id)),
                    "Clicking a reference seeks and selects its objects");
                Check(!ui.View.IsDirty && ui.View.Document.ContentEquals(map) && ReferenceEquals(conversion, ui.View.Conversion),
                    "Navigation preserves content, notes, dirty state and cached conversion");

                ui.Key(39); double beforeDrag = ui.View.PlayheadMs;
                hit = ui.View.NotesReferenceBounds.First();
                ui.View.PointerDown(hit.X + 2, hit.Y + 8, 0, false, false);
                ui.View.PointerMove(hit.Right - 2, hit.Y + 8, false, false);
                ui.View.PointerUp(hit.Right - 2, hit.Y + 8, 0); ui.Key('C', ctrl: true);
                Check(clipboard.Length > 0 && beforeDrag != 1000 && ui.View.PlayheadMs == beforeDrag, "Dragging link text selects/copies without navigating");

                var saved = ui.View.CaptureProject();
                ui.View.LoadProject(saved); ui.Paint(); OpenNotes();
                Check(ui.View.NotesReferenceBounds.Count > 0, "Links are reconstructed from persisted plain text");
                var layout = ui.View.CaptureDockLayouts(); layout.Mapping!.Right!.Width = 180;
                ui.View.LibrarySettings.DockLayouts = layout; ui.View.RestoreDockLayouts(); ui.Paint();
                ui.Click(ui.View.NotesBounds.X + 10, ui.View.NotesBounds.Y + 70);
                ui.Key('A', ctrl: true);
                clipboard = "中文批注 abcdefghijklmnop 00:01:000 (1,2) - comment\n00:09:000 (99) - missing\n00:99:000 (1)\n9999999999999:00:000 (1)";
                ui.Key('V', ctrl: true); ui.Key(27); ui.Paint();
                Check(ui.View.NotesReferenceBounds.Count >= 2, $"Wrapped valid references remain clickable and invalid timestamps are ignored: {ui.View.CaptureProject().Notes}; bounds={ui.View.NotesBounds}; links={ui.View.NotesReferenceBounds.Count}");
                var missing = ui.View.NotesReferenceBounds.Last(); ui.Click(missing.X + 2, missing.Y + 8);
                Check(ui.View.PlayheadMs == 9000 && ui.View.SelectedObjectIds.Count == 0,
                    "A stale reference seeks to its timestamp without selecting unrelated objects");
                ui.View.NewProject();

                void OpenNotes()
                {
                    var dock = ui.View.CaptureDockLayouts(); dock.Mapping!.Right!.Active = "Notes"; dock.Mapping.Right.Collapsed = false;
                    ui.View.LibrarySettings.DockLayouts = dock; ui.View.RestoreDockLayouts(); ui.Paint();
                    ui.Click(ui.View.NotesBounds.X + 12, ui.View.NotesBounds.Y + 70);
                }
            }
        }
        finally { L.SetLanguage(language); }
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
}
