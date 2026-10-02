using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Platform;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class VersionHistoryRenderCheck
{
    public static void Run(D2DCanvas canvas, int width, int height)
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/version-history-native", Guid.NewGuid().ToString("N")));
        var document = new MapDocument { IsDemo = false, DurationMs = 10000 };
        document.Fruits.Add(new Fruit { TimeMs = 1000, X = 100 });
        document.TimingPoints.Add(new TimingPoint { TimeMs = 0, BeatLengthMs = 500 });
        SongSetup.Set(document, "Metadata", "Version", "Kept");
        var deleted = document.DeepClone(); SongSetup.Set(deleted, "Metadata", "Version", "Removed");
        var project = BeatmapProject.FromDocuments([document, deleted]);
        var session = WorkspaceProject.Create(root, project, "");
        project.Difficulties[0].Document.Fruits[0].X = 400;
        WorkspaceProject.Save(session, project);
        WorkspaceAssociations.DeleteDifficulty(session, project, project.Difficulties[1].Id);
        var view = new EditorView(false);
        view.LibrarySettings.Workspace = root;
        view.LoadWorkspace(WorkspaceProject.Open(session.Directory));
        Paint(); view.ShowVersionHistory(); Wait();
        if (!view.VersionHistoryVisible) throw new InvalidOperationException("Native version history did not open.");
        var before = view.Document.DeepClone();
        view.KeyDown(39, false, false); Wait();
        float rightX = Math.Clamp(width * .27f, 220, 330) + 36, tabWidth = (width - rightX - 24) / 8;
        for (int i = 0; i < 8; i++)
        {
            view.PointerDown(rightX + (i + .5f) * tabWidth, 196, 0, false, false);
            view.PointerUp(rightX + (i + .5f) * tabWidth, 196, 0); Paint();
        }
        view.Wheel(width - 100, height - 160, 120, true); Paint();
        view.KeyDown(46, false, false); view.KeyDown('S', true, false); Paint();
        if (!view.Document.ContentEquals(before)) throw new InvalidOperationException("History browsing changed current authoring.");
        view.PointerDown(width - 120, height - 34, 0, false, false);
        view.PointerUp(width - 120, height - 34, 0); Wait();
        if (view.VersionHistoryVisible || view.DifficultyCount != 2 || view.Document.SourcePath is not null)
            throw new InvalidOperationException("Native deleted-version restoration failed.");
        view.StopFileMonitoring();
        AppLog.Write($"Version history native check passed: {L.Language}, {width}x{height}, preview, modal input and deleted difficulty restoration.");

        void Paint() { canvas.Begin(); view.Render(canvas, width, height); canvas.End(); }
        void Wait()
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            do { Paint(); if (!view.VersionHistoryBusy) return; Thread.Sleep(10); } while (DateTime.UtcNow < deadline);
            throw new TimeoutException("Native version history timed out.");
        }
    }
}
