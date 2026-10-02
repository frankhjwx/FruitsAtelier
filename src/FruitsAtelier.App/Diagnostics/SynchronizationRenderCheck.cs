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
                File.WriteAllText(source, text.Replace("Title:Native synchronization fixture", "Title:Native synchronization fixture updated")
                    .Replace("Artist:Artist", "Artist:Artist changed")
                    .Replace("Creator:Mapper", "Creator:Mapper changed")
                    .Replace("Version:Catch", "Version:Catch revised\nTags:" + string.Join(" ", Enumerable.Repeat("metadata long text", 100))));
                view.RefreshSynchronization(); Wait();
                if (!view.SynchronizationVisible) throw new InvalidOperationException("Native metadata rows were not shown.");
                var metadataBefore = view.Document.DeepClone();
                for (int tab = 0; tab < 8; tab++)
                {
                    float tabX = 36 + (width - 72) / 8f * (tab + .5f);
                    view.PointerDown(tabX, 98, 0, false, false); view.PointerUp(tabX, 98, 0); Paint();
                    if (!metadataBefore.ContentEquals(view.Document)) throw new InvalidOperationException("Native category navigation changed authoring.");
                }
                float metadataTabX = 36 + (width - 72) / 8f * 2.5f;
                view.PointerDown(metadataTabX, 98, 0, false, false); view.PointerUp(metadataTabX, 98, 0); Paint();
                view.Wheel(200, 260, -1200, false); Paint();
                view.Wheel(200, 260, 1200, false); Paint();
                if (!metadataBefore.ContentEquals(view.Document)) throw new InvalidOperationException("Native metadata scrolling changed authoring.");
                view.KeyDown(27, false, false);

                view.LoadWorkspace(WorkspaceProject.Open(session.Directory));
                string events = "Sprite,Foreground,Centre,\"sprite.png\",320,240\n F,0,0,500,0,1\n"
                    + string.Concat(Enumerable.Repeat("// storyboard command 01234567890123456789\n", 60000));
                File.WriteAllText(source, text.Replace("[HitObjects]", "[Events]\n" + events + "[HitObjects]"));
                view.RefreshSynchronization(); Wait();
                if (!view.SynchronizationVisible) throw new InvalidOperationException("Native section text review was not shown.");
                var sectionBefore = view.Document.DeepClone();
                view.Wheel(200, 260, -1200, false); Paint();
                view.PointerDown(width - 52, 131, 0, false, false); view.PointerUp(width - 52, 131, 0); Paint();
                if (!sectionBefore.ContentEquals(view.Document)) throw new InvalidOperationException("Native text paging changed authoring.");
                view.KeyDown(27, false, false);

                view.LoadWorkspace(WorkspaceProject.Open(session.Directory));
                File.WriteAllText(source, text.Replace("0,500,4,1,0,100,1,0", "0,500,4,1,0,100,1,0\n500,-100,4,1,0,100,0,0"));
                view.RefreshSynchronization(); Wait();
                if (!view.SynchronizationVisible) throw new InvalidOperationException("Native timing addition review was not shown.");
                var timingBefore = view.Document.DeepClone(); Paint();
                if (!timingBefore.ContentEquals(view.Document)) throw new InvalidOperationException("Native timing review changed authoring.");
                view.KeyDown(27, false, false);

                view.LoadWorkspace(WorkspaceProject.Open(session.Directory));
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
                view.ShowDeleteDifficulty(0, localOnly: true); Paint(); view.KeyDown(27, false, false);
                view.ShowDeleteProjectConfirmation(_ => { }); Paint(); view.KeyDown(27, false, false);
                VersionHistoryRenderCheck.Run(canvas, width, height);
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
