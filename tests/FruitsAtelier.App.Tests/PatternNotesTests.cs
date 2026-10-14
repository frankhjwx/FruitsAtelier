using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class PatternNotesTests
{
    public static void Run()
    {
        NotesReferenceTests.Run();
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
                ui.ClickText(L.Get("conversion.title"));
                Check(ui.View.Document.Tracks[0].StreamSnapDivisor is not null, "Conversion button commits a managed stream");
                Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("library.apply") || t.Value == L.Get("mac.cancel")), "Pattern panel has no confirmation controls");
                ui.ConversionMode(true);
                Check(ui.View.Document.Tracks[0].Stack is not null, "Stack mode changes content immediately");
                var stackSnapshot = ui.View.Document.DeepClone();
                ui.ClickText(L.Get("stream.convertBack"));
                Check(ui.View.Document.Tracks[0].StreamSnapDivisor is null, "Revert button restores an ordinary slider");
                ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(stackSnapshot), "Revert is undoable");
                ui.SelectTrack(track.Id); ui.ClickText(L.Get("stream.breakFruits"));
                Check(ui.View.Document.Tracks.Count == 0 && ui.View.Document.Fruits.Count > 0, "Break button immediately creates independent fruits");
                ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(stackSnapshot), "Breaking is undoable");
                var bananaMap = new MapDocument { DurationMs = 10000 };
                bananaMap.BananaShowers.Add(new() { TimeMs = 1000, EndTimeMs = 3000 });
                ui.LoadDocument(bananaMap); ui.Key('A', ctrl: true); ui.Paint();
                ui.ClickText(L.Get("bananaEdit.title"));
                Check(ui.View.Document.BananaShowers.Single().Pattern is not null, "Banana conversion commits immediately");
                var field = ui.View.BananaNumeratorBounds[0]; ui.Click(field.X + 4, field.Y + 8); ui.Key('A', ctrl: true); ui.View.PasteFieldText("2"); ui.Paint();
                Check(ui.View.Document.BananaShowers.Single().Pattern!.LengthNumerator == 2, "Banana numeric edit is immediate");
                ui.Key(27); ui.Key('Z', ctrl: true);
                Check(ui.View.Document.BananaShowers.Single().Pattern!.LengthNumerator == 1, "Banana numeric edit undoes");
                ui.Key('A', ctrl: true); ui.Paint(); ui.ClickText(L.Get("bananaEdit.split"));
                Check(ui.View.Document.BananaShowers.Count > 1 && ui.View.Document.BananaShowers.All(b => b.Pattern is null), "Break button creates independent banana showers");
                ui.Key('Z', ctrl: true); Check(ui.View.Document.BananaShowers.Single().Pattern is not null, "Banana breaking undoes");
            }
        }
        finally { L.SetLanguage(language); }
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
