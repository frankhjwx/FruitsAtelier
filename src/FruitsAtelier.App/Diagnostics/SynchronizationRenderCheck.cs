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
                File.WriteAllText(source, text.Replace("0,500,4,1,0,100,1,0", "0,500,4,1,0,100,1,0\n500,-50,4,2,2,70,0,1"));
                view.RefreshSynchronization(); Wait();
                if (!view.SynchronizationVisible) throw new InvalidOperationException("Native timing addition review was not shown.");
                var timingBefore = view.Document.DeepClone(); Paint();
                CaptureTiming("unresolved");
                view.PointerDown(100, height - 162, 0, false, false); view.PointerUp(100, height - 162, 0); Paint();
                CaptureTiming("resolved");
                if (!timingBefore.ContentEquals(view.Document)) throw new InvalidOperationException("Native timing review changed authoring.");
                view.KeyDown(27, false, false);

                view.LoadWorkspace(WorkspaceProject.Open(session.Directory));
                File.WriteAllText(source, text.Replace("100,192", "400,192"));
                view.RefreshSynchronization(); Wait();
                if (!view.SynchronizationVisible) throw new InvalidOperationException("Native object conflict dialog was not shown.");
                CaptureTiming("objects-unresolved");
                var before = view.Document.DeepClone(); view.KeyDown(46, false, false); Paint();
                if (!before.ContentEquals(view.Document)) throw new InvalidOperationException("Native conflict input changed authoring.");
                float rangeY = 282 + .65f * Math.Max(40, height - 488);
                var choicesField = typeof(EditorView).GetField("syncChoices", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                foreach (bool external in new[] { false, true, false })
                {
                    float rangeX = external ? width / 2 + 100 : 130;
                    view.PointerDown(rangeX, rangeY, 0, false, false); view.PointerUp(rangeX, rangeY, 0); Paint();
                    var selected = (Dictionary<string, bool>)choicesField.GetValue(view)!;
                    if (selected.Count == 0 || selected.Values.Any(value => value != external))
                        throw new InvalidOperationException("Native conflict rectangle did not select its side.");
                }
                CaptureTiming("objects-resolved");
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
                view.ShowDeleteProjectConfirmation((_, _) => { }); Paint(); view.KeyDown(27, false, false);
                VersionHistoryRenderCheck.Run(canvas, width, height);
                string sliderSource = Path.Combine(set, "slider.osu");
                string sliderText = text.Replace("100,192,1000,1,0,0:0:0:0:", "100,192,1000,2,0,L|400:192,1,300");
                File.WriteAllText(sliderSource, sliderText);
                var sliderSession = WorkspaceProject.Create(Path.Combine(root, "SliderWorkspace"), BeatmapProject.FromDocuments([OsuBeatmapReader.ReadFile(sliderSource)]), songs);
                view.LoadWorkspace(sliderSession);
                File.WriteAllText(sliderSource, sliderText.Replace("L|400:192", "L|450:260"));
                view.RefreshSynchronization(); Wait();
                if (!view.SynchronizationVisible) throw new InvalidOperationException("Playable slider comparison was not shown.");
                CaptureTiming("slider-unresolved");
                view.PointerDown(100, height - 162, 0, false, false); view.PointerUp(100, height - 162, 0); Paint();
                CaptureTiming("slider-resolved");
                view.KeyDown(27, false, false);
                AppLog.Write($"Synchronization rendering passed: {locale}, {width}x{height}, object conflicts, missing state, deletion confirmation and input isolation.");

                void CaptureTiming(string state)
                {
                canvas.Begin(); view.Render(canvas, width, height);
                canvas.End(capture: () =>
                {
                    string folder = Path.GetFullPath("artifacts/tests/semantic-sync-native"); Directory.CreateDirectory(folder);
                    var frame = canvas.CapturePixels();
                    using var writer = new BinaryWriter(File.Create(Path.Combine(folder, $"{locale}-{width}-{state}.bmp")));
                    writer.Write((ushort)0x4D42); writer.Write(54 + frame.Pixels.Length); writer.Write(0); writer.Write(54);
                    writer.Write(40); writer.Write(frame.Width); writer.Write(-frame.Height); writer.Write((ushort)1); writer.Write((ushort)32);
                    writer.Write(0); writer.Write(frame.Pixels.Length); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(frame.Pixels);
                });
                }
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
