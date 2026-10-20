using System.Reflection;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class FullSynchronizationTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                string root = Path.GetFullPath(Path.Combine("artifacts/tests/full-sync", Guid.NewGuid().ToString("N")));
                string songs = Path.Combine(root, "Songs"), source = Path.Combine(songs, "set", "map.osu");
                Directory.CreateDirectory(Path.GetDirectoryName(source)!);
                File.WriteAllText(source, "osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Title\nArtist:Artist\nCreator:Mapper\nVersion:Catch\n[Difficulty]\nCircleSize:5\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,1,0,0:0:0:0:\n");
                var session = WorkspaceProject.Create(Path.Combine(root, "Workspace"), BeatmapProject.FromDocuments([OsuBeatmapReader.ReadFile(source)]), songs);
                var document = session.Project.Difficulties[0].Document;
                var stream = new CurveTrack { Kind = CurveKind.Bezier, StreamSnapDivisor = 4 };
                stream.Nodes.AddRange([new() { TimeMs = 3000, X = 100, HandleOut = new(60, 20) }, new() { TimeMs = 4000, X = 250 }]);
                document.Tracks.Add(stream); document.DurationMs = 10000;
                var output = OsuBeatmapWriter.Serialize(document);
                File.WriteAllText(source, output.Text);
                session.Manifest.Difficulties[0].Sync = WorkspaceSynchronization.Capture(source, document, session.Directory, output.Text, output.ObjectSources);
                session.Manifest.Difficulties[0].ExportConfirmed = true;
                WorkspaceProject.Save(session, session.Project);
                var ui = new Ui(false); ui.Resize(size.Item1, size.Item2);
                ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); ui.View.LibrarySettings.Songs = songs;
                ui.View.LoadWorkspace(session); Wait(ui);
                try
                {
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    var history = (EditorHistory)ui.View.GetType().GetProperty("history", flags)!.GetValue(ui.View)!;
                    history.Begin("Edit fruit"); ui.View.Document.Fruits[0].X = 120; history.Commit();
                    var before = ui.View.Document.DeepClone();
                    File.WriteAllText(source, output.Text.Replace("100,192,1000", "200,192,1000"));
                    ui.View.RefreshSynchronization(); Wait(ui);
                    int versions = WorkspaceVersionHistory.List(session).Count;
                    byte[] saved = File.ReadAllBytes(Path.Combine(session.Directory, session.Manifest.Difficulties[0].File));
                    string external = File.ReadAllText(source);
                    ui.ClickText(L.Get("sync.allExternal"));
                    Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.fullConfirmTitle")), "entire osu version requires confirmation");
                    Check(ui.View.Document.ContentEquals(before) && File.ReadAllText(source) == external
                        && WorkspaceVersionHistory.List(session).Count == versions, "confirmation writes neither content nor backup");
                    ui.Key('S', ctrl: true); ui.Key(46); ui.Key('Z', ctrl: true);
                    Check(ui.View.Document.ContentEquals(before), "confirmation isolates editing commands");
                    ui.ClickText(L.Get("sync.fullConfirmBack"));
                    Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.allExternal")), "back retains the review");
                    ui.ClickText(L.Get("sync.allExternal")); ui.Key(27);
                    Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.allExternal")), "Escape returns to review without syncing");
                    ui.ClickText(L.Get("sync.allExternal"));
                    if (locale == "en" && size.Item1 == 980)
                    {
                        string difficultyFile = Path.Combine(session.Directory, session.Manifest.Difficulties[0].File);
                        using (var locked = new FileStream(difficultyFile, FileMode.Open, FileAccess.Read, FileShare.None))
                        {
                            ui.ClickText(L.Get("sync.fullConfirmApply")); Wait(ui);
                            Check(ui.View.Document.ContentEquals(before) && File.ReadAllText(source) == external,
                                "backup failure leaves live authoring and osu source untouched");
                            Check(!WorkspaceVersionHistory.List(session).Any(v => v.Operation == "before-full-sync" && v.WorkingCopy),
                                "failed backup never exposes an incomplete working recovery point");
                        }
                        Check(File.ReadAllBytes(difficultyFile).SequenceEqual(saved), "backup failure leaves saved authoring untouched");
                        ui.ClickText(L.Get("sync.refresh")); Wait(ui);
                        ui.ClickText(L.Get("sync.allExternal"));
                    }
                    ui.ClickText(L.Get("sync.fullConfirmApply")); Wait(ui);
                    Check(!ui.View.SynchronizationVisible && ui.View.Document.Fruits.Single().X == 200, "confirmed full synchronization applies external edits");
                    Check(ui.View.Document.Tracks.Single().Id == stream.Id && ui.View.Document.Tracks[0].Nodes[0].Id == stream.Nodes[0].Id,
                        "unchanged stream retains its editable connection and anchors");
                    var working = WorkspaceVersionHistory.List(ui.View.WorkspaceSession!).Single(v => v.Operation == "before-full-sync" && v.WorkingCopy);
                    Check(WorkspaceVersionHistory.Read(ui.View.WorkspaceSession!, working).Difficulties[0].Document.ContentEquals(before),
                        "named compressed working backup includes unsaved edits");
                    source = WorkspaceSynchronization.Target(ui.View.WorkspaceSession!.Manifest.Difficulties[0])!;
                    string synchronized = File.ReadAllText(source);
                    ui.ClickText(L.Get("sync.restoreFullBackup")); WaitHistory(ui);
                    Check(ui.Canvas.Texts.Any(t => t.Value.Contains(L.Get("history.operation.before-full-sync"))), "history labels full-sync backups");
                    int index = (int)ui.View.GetType().GetField("versionIndex", flags)!.GetValue(ui.View)!;
                    var list = (IReadOnlyList<WorkspaceVersion>)ui.View.GetType().GetField("versions", flags)!.GetValue(ui.View)!;
                    Check(list[index].Path == working.Path, "recovery opens the precise working version rather than the saved version");
                    ui.ClickText(L.Get("history.restore")); WaitHistory(ui);
                    Check(ui.View.Document.Fruits.Single().X == 120 && ui.View.IsDirty && ui.View.Document.Tracks[0].Id == stream.Id,
                        "recovery restores unsaved authoring as an editable pending change");
                    Check(File.ReadAllText(source) == synchronized, "viewing and restoring a backup do not overwrite osu");
                    ui.Key('Z', ctrl: true);
                    Check(ui.View.Document.Fruits.Single().X == 200, "recovery can be undone");
                    File.Delete(working.Path + WorkspaceHistoryFile.Extension);
                    ui.ClickText(L.Get("sync.restoreFullBackup")); WaitHistory(ui);
                    Check(ui.View.GetType().GetField("versionProject", flags)!.GetValue(ui.View) is null
                        && !string.IsNullOrEmpty((string)ui.View.GetType().GetField("versionError", flags)!.GetValue(ui.View)!),
                        "an expired recovery point reports an error instead of selecting another snapshot");
                    ui.Key(27);
                    var other = WorkspaceProject.Create(Path.Combine(root, "Other"), BeatmapProject.FromDocuments([new MapDocument { IsDemo = false }]), "");
                    ui.View.LoadWorkspace(other); Wait(ui);
                    Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.restoreFullBackup")), "recovery shortcut is scoped to its project");
                }
                finally { ui.View.StopFileMonitoring(); }
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void Wait(Ui ui)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        do { ui.Paint(); if (!ui.View.SynchronizationBusy) { ui.Paint(); return; } Thread.Sleep(5); } while (DateTime.UtcNow < deadline);
        throw new TimeoutException("Full synchronization did not finish.");
    }
    private static void WaitHistory(Ui ui)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        do { ui.Paint(); if (!ui.View.VersionHistoryBusy) { ui.Paint(); return; } Thread.Sleep(5); } while (DateTime.UtcNow < deadline);
        throw new TimeoutException("Full synchronization recovery did not finish.");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
