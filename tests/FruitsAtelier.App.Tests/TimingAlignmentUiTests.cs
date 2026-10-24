using FruitsAtelier.Core;
using FruitsAtelier.App.Editor;
using L = FruitsAtelier.Localization.Strings;

internal static class TimingAlignmentUiTests
{
    internal static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var map = new MapDocument { IsDemo = false, DurationMs = 50000 };
                map.TimingPoints.AddRange([new() { TimeMs = 670, BeatLengthMs = 254.237288135593 },
                    new() { TimeMs = 12873, BeatLengthMs = 254.237288135593 },
                    new() { TimeMs = 38297, BeatLengthMs = -100, Uninherited = false, Volume = 35 }]);
                map.Fruits.Add(new() { TimeMs = 33212, X = 215 });
                var before = map.DeepClone();
                var ui = new Ui(false); ui.View.LoadDocument(map); ui.View.CloseLibrary();
                ui.View.CheckTimingAlignmentOnOpen(); ui.Paint();
                Check(ui.View.TimingAlignmentPromptVisible && ui.View.TimingAlignmentObjectCount == 1
                    && ui.View.TimingAlignmentPointCount == 1 && !ui.View.IsDirty,
                    $"Open prompt reports counts without editing: visible={ui.View.TimingAlignmentPromptVisible}, objects={ui.View.TimingAlignmentObjectCount}, timing={ui.View.TimingAlignmentPointCount}, dirty={ui.View.IsDirty}, library={ui.View.LibraryVisible}");
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("timingAlignment.title")), "Localized prompt is rendered");
                ui.Key(46); Check(ui.View.Document.ContentEquals(before), "Modal blocks object edits");
                ui.Key(27); Check(!ui.View.TimingAlignmentPromptVisible && !ui.View.IsDirty && ui.View.Document.ContentEquals(before), "Escape keeps source data");
                ui.Paint(); Check(!ui.View.TimingAlignmentPromptVisible, "Declining does not repeat during the same open");
                ui.View.LoadDocument(map); ui.View.CheckTimingAlignmentOnOpen(); ui.Paint();
                ui.Key(13); Check(ui.View.Document.Fruits[0].TimeMs == 33211 && ui.View.Document.TimingPoints[2].TimeMs == 38296
                    && ui.View.IsDirty && !ui.View.TimingAlignmentPromptVisible, "Enter applies both kinds of correction");
                ui.Key(90, true); Check(ui.View.Document.ContentEquals(before) && !ui.View.IsDirty, "One undo restores the opened map");
                ui.Key(89, true); Check(ui.View.Document.Fruits[0].TimeMs == 33211, "Redo applies alignment");
                ui.View.LoadDocument(map); ui.View.CheckTimingAlignmentOnOpen(); ui.Paint();
                ui.View.NewProject(); ui.Paint(); Check(!ui.View.TimingAlignmentPromptVisible, "Project replacement retires the prompt");
                string root = Path.GetFullPath(Path.Combine("artifacts", "tests", "timing-alignment", Guid.NewGuid().ToString("N")));
                string songs = Path.Combine(root, "Songs"); Directory.CreateDirectory(songs);
                var project = BeatmapProject.FromDocuments([map.DeepClone(), map.DeepClone()]);
                var session = WorkspaceProject.Create(Path.Combine(root, "Workspace"), project, songs);
                ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); ui.View.LibrarySettings.Songs = songs;
                ui.View.LoadWorkspace(session);
                Check(ui.View.SwitchDifficulty(1), "Initial difficulty selection completes before prompting");
                ui.Paint(); Check(ui.View.TimingAlignmentPromptVisible, "Workspace opening automatically requests the check");
                ui.View.AnswerTimingAlignment(false); ui.Paint();
                Check(ui.View.SwitchDifficulty(0), "Difficulty navigation resumes after answering");
                ui.Paint(); Check(ui.View.TimingAlignmentPromptVisible, "Another difficulty gets its own first-open check");
                ui.View.AnswerTimingAlignment(false); ui.Paint(); ui.View.SwitchDifficulty(1); ui.Paint();
                Check(!ui.View.TimingAlignmentPromptVisible, "Previously answered difficulties do not repeat");
                ui.View.StopFileMonitoring(); ui.View.NewProject();
                Console.WriteLine("PASS on-open timing alignment " + locale);
            }
        }
        finally { L.SetLanguage(language); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
