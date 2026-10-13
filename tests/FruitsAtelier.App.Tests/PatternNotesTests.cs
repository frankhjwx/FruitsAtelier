using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class PatternNotesTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var ui = new Ui(overview: false); ui.Resize(1440, 900);
                var map = new MapDocument { DurationMs = 10000 };
                var track = new CurveTrack { Kind = CurveKind.Linear };
                track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 256 }, new Anchor { TimeMs = 2000, X = 256 }]);
                map.Tracks.Add(track); ui.LoadDocument(map); ui.OpenPreview();
                var preview = ui.View.DockTabBounds(EditorView.DockPanel.Preview);
                var notes = ui.View.DockTabBounds(EditorView.DockPanel.Notes);
                Check(notes.X > preview.X && ui.View.DockTabBounds(EditorView.DockPanel.PatternEditor).Width == 0,
                    "Default right tabs are Preview then Notes; Stream/Stack is opt-in");
                ui.Click(notes.X + 10, notes.Y + 10);
                ui.Click(ui.View.NotesBounds.X + 12, ui.View.NotesBounds.Y + 12);
                var unchangedConversion = ui.View.Conversion;
                ui.Type("Notes 中文"); ui.Key(13); ui.Type("second line");
                string text = "Notes 中文\nsecond line";
                Check(ReferenceEquals(unchangedConversion, ui.View.Conversion), "Notes input reuses beatmap conversion");
                Check(ui.View.CaptureProject().Notes == text && ui.View.IsDirty && ui.View.Document.ContentEquals(map),
                    "Multiline project notes are dirty independently of beatmap content");
                ui.Key('Z', ctrl: true); ui.Key('Y', ctrl: true);
                Check(ui.View.CaptureProject().Notes == text, "Notes have independent text undo/redo");
                int saves = 0; ui.View.RequestSave = () => saves++;
                ui.Key('S', ctrl: true); Check(saves == 1, "Notes focus retains the project save shortcut");
                var project = ui.View.CaptureProject();
                Check(ProjectSerializer.ReadProject(ProjectSerializer.Serialize(project)).Notes == text, "Compatibility project preserves notes");
                var legacy = BeatmapProject.FromDocuments([map]);
                Check(ProjectSerializer.ReadProject(ProjectSerializer.Serialize(legacy)).Notes == "", "Older empty-notes projects still roundtrip");
                string root = Path.GetFullPath(Path.Combine("artifacts", "tests", "notes-" + Guid.NewGuid().ToString("N")));
                var session = WorkspaceProject.Create(root, project, "");
                Check(WorkspaceProject.Open(session.Directory).Project.Notes == text, "Workspace manifest preserves project notes");
                project.Notes = "updated"; WorkspaceProject.Save(session, project);
                Check(WorkspaceProject.Open(session.Directory).Project.Notes == "updated", "Workspace save updates notes");
                ui.View.MarkSaved(); Check(!ui.View.IsDirty, "Notes baseline is saved");
                ui.Key(27);
                ui.View.AddDifficulty(); ui.Paint();
                Check(ui.View.CaptureProject().Notes == text, "Notes belong to the project across difficulty changes");
                ui.View.SwitchDifficulty(0); ui.Paint();
                ui.SelectTrack(track.Id);
                ui.ClickText(L.Get("dock.layout")); ui.ClickText(L.Get("dock.patternEditor"));
                Check(!ui.View.StreamDialogVisible && ui.Canvas.Texts.Any(t => t.Value == L.Get("dock.convertPrompt")),
                    "Plain sliders require conversion before dock editing");
                var originalCanvas = ui.View.PatternCanvasObjects(0, 10000).ToArray();
                var committedConversion = ui.View.Conversion;
                ui.ClickText(L.Get("conversion.title"));
                Check(ui.View.StreamSnapBounds.Width > 0 && ui.View.StackPreviewBounds.Width == 0 && !ui.View.StreamDialogVisible,
                    "Dock exposes conversion controls without modal or preview");
                ui.ClickText(L.Get("conversion.stackTab"));
                var graph = ui.View.StackGraphBounds;
                ui.View.PointerDown(graph.Right, graph.Bottom, 0, false, false);
                ui.View.PointerMove(graph.Right, graph.Bottom - graph.Height / 2, false, false);
                ui.View.PointerUp(graph.Right, graph.Bottom - graph.Height / 2, 0); ui.Paint();
                Check(ui.View.Document.Tracks[0].Stack is null, "Dock draft does not mutate content before Apply");
                var draftCanvas = ui.View.PatternCanvasObjects(0, 10000).ToArray();
                Check(draftCanvas.Length > 2 && draftCanvas.Any(o => Math.Abs(o.X - 256) > 1),
                    "Stack parameter changes preview fruit positions on the main canvas");
                Check(ReferenceEquals(ui.View.Conversion, committedConversion), "Canvas draft retains committed conversion");
                ui.ClickText(L.Get("mac.cancel"));
                Check(ui.View.PatternCanvasObjects(0, 10000).SequenceEqual(originalCanvas), "Cancel restores canvas objects");
                ui.ClickText(L.Get("conversion.title")); ui.ClickText(L.Get("conversion.stackTab"));
                graph = ui.View.StackGraphBounds;
                ui.View.PointerDown(graph.Right, graph.Bottom, 0, false, false);
                ui.View.PointerMove(graph.Right, graph.Bottom - graph.Height / 2, false, false);
                ui.View.PointerUp(graph.Right, graph.Bottom - graph.Height / 2, 0); ui.Paint();
                ui.ClickText(L.Get("library.apply"));
                Check(ui.View.Document.Tracks[0].Stack?.Points[^1].Distance == 16, "Dock applies stack envelope: " + ui.View.Document.Tracks[0].Stack?.Points[^1].Distance + " / " + ui.View.StatusMessage);
                ui.Key('Z', ctrl: true); Check(ui.View.Document.Tracks[0].Stack is null, "Stack dock apply is one undo transaction");
                ui.Key('Y', ctrl: true); ui.SelectTrack(track.Id);
                Check(ui.View.StackGraphBounds.Width > 0 && !ui.View.StreamDialogVisible, "Existing stack automatically reloads after redo");
                var stackSnapshot = ui.View.Document.DeepClone();
                ui.ClickText(L.Get("stream.convertBack"));
                Check(ui.View.Document.ContentEquals(map) && ui.Canvas.Texts.Any(t => t.Value == L.Get("dock.convertPrompt")),
                    "Dock restores the original slider and shows conversion entry");
                ui.Key('Z', ctrl: true);
                Check(ui.View.Document.ContentEquals(stackSnapshot), "Restoring slider undoes in one step");
                ui.SelectTrack(track.Id);
                ui.ClickText(L.Get("stream.breakFruits")); ui.ClickText(L.Get("library.apply"));
                Check(ui.View.Document.Tracks.Count == 0 && ui.Canvas.Texts.Any(t => t.Value == L.Get("dock.patternSelection")),
                    "Broken fruits no longer expose pattern editing");
                var bananaMap = new MapDocument { DurationMs = 10000 };
                bananaMap.BananaShowers.Add(new() { TimeMs = 1000, EndTimeMs = 3000 });
                ui.LoadDocument(bananaMap); ui.Key('A', ctrl: true); ui.Paint();
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("dock.convertPrompt")), "Plain shower requires conversion");
                ui.ClickText(L.Get("bananaEdit.title"));
                WaitBanana();
                Check(!ui.View.BananaEditVisible && ui.View.BananaPreviewBounds.Width == 0, "Banana dock omits modal preview");
                Check(ui.View.PatternCanvasObjects(0, 10000).SequenceEqual(ui.View.BananaEditPreview)
                    && ui.View.Document.ContentEquals(bananaMap), "Banana canvas previews the draft without changing content");
                ui.ClickText(L.Get("mac.cancel"));
                Check(ui.View.Document.ContentEquals(bananaMap)
                    && !ui.View.PatternCanvasObjects(0, 10000).SequenceEqual(ui.View.BananaEditPreview),
                    "Banana Cancel restores committed canvas output");
                ui.ClickText(L.Get("bananaEdit.title")); WaitBanana();
                var bananaDraft = ui.View.PatternCanvasObjects(0, 10000).Select(o => (o.TimeMs, o.X, o.Kind)).ToArray();
                ui.ClickText(L.Get("library.apply"));
                Check(ui.View.Document.BananaShowers.Single().Pattern is not null, "Banana dock applies pattern");
                WaitBanana();
                Check(ui.View.PatternCanvasObjects(0, 10000).Select(o => (o.TimeMs, o.X, o.Kind)).SequenceEqual(bananaDraft),
                    "Banana Apply retains previewed positions");
                ui.Key('Z', ctrl: true); Check(ui.View.Document.BananaShowers.Single().Pattern is null, "Banana pattern undoes");
                ui.Key('Y', ctrl: true); ui.Key('A', ctrl: true); WaitBanana();
                ui.Resize(980, 620);
                var dock = ui.View.DockResizeBounds(false);
                ui.View.PointerDown(dock.X + 4, dock.Y + 60, 0, false, false);
                ui.View.PointerMove(ui.Width - 180, dock.Y + 60, false, false);
                ui.View.PointerUp(ui.Width - 180, dock.Y + 60, 0); ui.Paint();
                var beforeScroll = ui.View.BananaNumeratorBounds[0];
                ui.View.Wheel(ui.Width - 60, 400, -1200, false, false); ui.Paint();
                Check(ui.View.BananaNumeratorBounds[0].Y < beforeScroll.Y
                    && ui.Canvas.Texts.Any(t => t.Value == L.Get("library.apply")), "Narrow banana dock scrolls to its apply controls");
                Check(ui.View.BananaNumeratorBounds[0].Width > 0 && !ui.View.BananaEditVisible, "Existing banana stack opens directly in dock");
                var bananaSnapshot = ui.View.Document.DeepClone();
                ui.ClickText(L.Get("bananaEdit.convertBack"));
                Check(ui.View.Document.ContentEquals(bananaMap) && ui.Canvas.Texts.Any(t => t.Value == L.Get("dock.convertPrompt")),
                    "Dock restores the original banana shower in a narrow panel");
                ui.Key('Z', ctrl: true);
                Check(ui.View.Document.ContentEquals(bananaSnapshot), "Restoring banana shower undoes in one step");
                var multiple = map.DeepClone();
                var secondTrack = track.DeepClone(); secondTrack.Id = Guid.NewGuid();
                foreach (var node in secondTrack.Nodes) { node.Id = Guid.NewGuid(); node.TimeMs += 3000; }
                multiple.Tracks.Add(secondTrack);
                ui.LoadDocument(multiple); ui.Key('A', ctrl: true); ui.Paint();
                ui.ClickText(L.Get("conversion.title"));
                var multiplePreview = ui.View.PatternCanvasObjects(0, 10000).ToArray();
                Check(multiplePreview.Select(o => o.SourceId).Distinct().Count() == 2
                    && multiplePreview.All(o => o.Kind == CatchObjectKind.Fruit)
                    && ui.View.Document.ContentEquals(multiple), "Canvas previews every selected slider without committing");
                void WaitBanana()
                {
                    var end = DateTime.UtcNow.AddSeconds(20);
                    while (!ui.View.BananaPreviewReady && DateTime.UtcNow < end) { Thread.Sleep(5); ui.Paint(); }
                    Check(ui.View.BananaPreviewReady, "Banana draft validation completes");
                }
            }
        }
        finally { L.SetLanguage(language); }
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
