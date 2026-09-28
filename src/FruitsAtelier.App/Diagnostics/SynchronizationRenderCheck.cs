using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Platform;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class SynchronizationRenderCheck
{
    public static void Run(D2DCanvas canvas, int width, int height)
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(AppLog.Path)!, "..", "sync-render", Guid.NewGuid().ToString("N")));
                string songs = Path.Combine(root, "Songs"), set = Path.Combine(songs, "set"), source = Path.Combine(set, "map.osu");
                Directory.CreateDirectory(set);
                const string text = "osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Native synchronization fixture\nArtist:Artist\nCreator:Mapper\nVersion:Catch\n[Difficulty]\nCircleSize:5\nApproachRate:5\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,1,0,0:0:0:0:\n";
                File.WriteAllText(source, text);
                var view = new EditorView(false);
                view.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); view.LibrarySettings.Songs = songs;
                var session = WorkspaceProject.Create(view.LibrarySettings.Workspace, BeatmapProject.FromDocuments([OsuBeatmapReader.ReadFile(source)]), songs);
                view.LoadWorkspace(session);
                File.WriteAllText(source, text.Replace("100,192", "400,192"));
                view.RefreshSynchronization(); Wait();
                if (!view.SynchronizationVisible) throw new InvalidOperationException("Native object conflict dialog was not shown.");
                var before = view.Document.DeepClone(); view.KeyDown(46, false, false); Paint();
                if (!before.ContentEquals(view.Document)) throw new InvalidOperationException("Native conflict input changed authoring.");
                view.Wheel(200, 260, 120, false); Paint();
                view.Wheel(200, 260, 120, true); Paint();
                if (!before.ContentEquals(view.Document)) throw new InvalidOperationException("Native comparison navigation changed authoring.");
                view.KeyDown(27, false, false);
                var retained = WorkspaceProject.Open(session.Directory);
                var merge = WorkspaceSynchronization.Merge(retained.Manifest.Difficulties[0], retained.Project.Difficulties[0].Document,
                    WorkspaceSynchronization.ReadStable(source), retained.Directory, true);
                var choices = merge.Conflicts.ToDictionary(c => c.Key, _ => false);
                WorkspaceSynchronization.Accept(retained, retained.Manifest.Difficulties[0], merge.External, retained.Project.Difficulties[0].Document, true, review: merge, choices: choices);
                WorkspaceProject.Save(retained, retained.Project);
                view.LoadWorkspace(WorkspaceProject.Open(session.Directory)); view.RefreshSynchronization(reviewResolved: true); Wait();
                if (!view.SynchronizationVisible) throw new InvalidOperationException("Native resolved interval review was not shown.");
                view.KeyDown(27, false, false);
                var legacy = WorkspaceProject.Open(session.Directory); legacy.Manifest.Difficulties[0].Sync = null;
                File.WriteAllText(source, text.Replace("100,192", "450,192"));
                WorkspaceProject.Save(legacy, legacy.Project);
                view.LoadWorkspace(WorkspaceProject.Open(session.Directory)); view.RefreshSynchronization(); Wait();
                if (view.DifficultySyncState(0) != WorkspaceSyncState.NeedsBaseline) throw new InvalidOperationException("Native legacy comparison was not shown.");
                view.KeyDown(27, false, false);
                view.LoadWorkspace(WorkspaceProject.Open(session.Directory));
                File.Delete(source); view.RefreshSynchronization(reviewResolved: true); Wait();
                if (view.DifficultySyncState(0) != WorkspaceSyncState.Missing) throw new InvalidOperationException("Native missing difficulty state was not shown.");
                view.KeyDown(27, false, false);
                view.LoadWorkspace(WorkspaceProject.Open(session.Directory));
                view.ShowDeleteDifficulty(0); Paint(); view.KeyDown(27, false, false);
                AppLog.Write($"Synchronization rendering passed: {locale}, {width}x{height}, object conflicts, missing state, deletion confirmation and input isolation.");

                void Paint() { canvas.Begin(); view.Render(canvas, width, height); canvas.End(); }
                void Wait()
                {
                    var deadline = DateTime.UtcNow.AddSeconds(15);
                    do { Paint(); if (!view.SynchronizationBusy) return; Thread.Sleep(10); } while (DateTime.UtcNow < deadline);
                    throw new TimeoutException("Native synchronization check timed out.");
                }
            }
        }
        finally { L.SetLanguage(language); }
    }
}
