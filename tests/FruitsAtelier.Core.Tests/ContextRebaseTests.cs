using FruitsAtelier.Core;

internal static class ContextRebaseTests
{
    public static void History()
    {
        string path = Path.GetFullPath("artifacts/tests/context/map.osu");
        var before = OsuBeatmapReader.Read("""
            osu file format v14
            [General]
            AudioFilename:music.wav
            Mode:2
            [Metadata]
            Title:Song
            Version:Original
            Tags:original
            [Difficulty]
            CircleSize:4
            SliderMultiplier:1.4
            SliderTickRate:1
            [TimingPoints]
            0,500,4,1,0,100,1,0
            """, path);
        var after = before.DeepClone();
        after.Fruits.Add(new Fruit { TimeMs = 1000, X = 200 });
        var snapshot = before.DeepClone();
        var timing = snapshot.TimingPoints.Single();
        var sections = snapshot.OriginalSections.ToArray();
        var unchanged = WorkspaceSynchronization.PrepareContextRebase(before, after);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) unchanged(snapshot);
        Check(GC.GetAllocatedBytesForCurrentThread() - allocated < 1024,
            "Content-only history rebasing must not allocate per snapshot.");
        Check(ReferenceEquals(timing, snapshot.TimingPoints.Single()) && sections.SequenceEqual(snapshot.OriginalSections)
            && snapshot.ContentEquals(before), "Content-only saves retain exact context and authoring.");

        var history = new EditorHistory(before);
        history.Begin("local version"); SongSetup.Set(history.Document, "Metadata", "Version", "Local"); history.Commit();
        history.Begin("note"); history.Document.Fruits.Add(new Fruit { TimeMs = 1000, X = 200 }); history.Commit();
        history.Undo();
        after = history.Document.DeepClone();
        SongSetup.Set(after, "Metadata", "Tags", "synchronized");
        after.CircleSize = 5;
        after.TimingPoints[0].BeatLengthMs = 600;
        after.AudioPath = Path.Combine(Path.GetDirectoryName(path)!, "replacement.wav");
        after.SourcePath = Path.Combine(Path.GetDirectoryName(path)!, "moved.osu");
        var rebase = WorkspaceSynchronization.PrepareContextRebase(history.Document, after);
        after.CircleSize = 9;
        after.AudioPath = null;
        history.RebaseSharedMetadata(rebase);
        history.MarkSaved();
        void Verify(string version, int fruits)
        {
            var document = history.Document;
            Check(document.CircleSize == 5 && document.TimingPoints[0].BeatLengthMs == 600
                && OsuBeatmapReader.Setting(document, "Metadata", "Tags") == "synchronized"
                && document.AudioPath!.EndsWith("replacement.wav") && document.SourcePath!.EndsWith("moved.osu"),
                "Prepared context changes are frozen and survive undo and redo.");
            Check(OsuBeatmapReader.Setting(document, "Metadata", "Version") == version && document.Fruits.Count == fruits,
                "Context rebasing preserves unrelated historical metadata and notes.");
        }
        Verify("Local", 0);
        history.Redo(); Verify("Local", 1);
        history.Undo(); history.Undo(); Verify("Original", 0);
        history.Redo(); Verify("Local", 0);
        var saved = history.Document.DeepClone();
        history.Begin("newer note"); history.Document.Fruits.Add(new Fruit { TimeMs = 2000, X = 300 });
        history.MarkSaved(saved);
        Check(history.HasActiveTransaction && history.IsDirty, "Acknowledging a background snapshot retains an active newer edit.");
        history.Commit(); history.Undo();
        Check(!history.IsDirty, "Undo of the newer edit returns to the saved snapshot.");

        var local = new MapDocument { IsDemo = false, BeatLengthMs = 437.5, TimingOffsetMs = 123.25 };
        SongSetup.Set(local, "Metadata", "Title", "Local song");
        var updated = local.DeepClone();
        SongSetup.Set(updated, "Metadata", "Tags", "synchronized");
        var localHistory = new EditorHistory(local);
        localHistory.Begin("note"); localHistory.Document.Fruits.Add(new Fruit { TimeMs = 1000, X = 200 }); localHistory.Commit();
        localHistory.RebaseSharedMetadata(WorkspaceSynchronization.PrepareContextRebase(local, updated));
        Check(SongSetup.Get(localHistory.Document, "General", "Mode") == "2"
            && SongSetup.Get(localHistory.Document, "Metadata", "Tags") == "synchronized"
            && localHistory.Document.TimingPoints.Single().TimeMs == 123.25
            && localHistory.Document.TimingPoints.Single().BeatLengthMs == 437.5,
            "Raw-section-free authoring context rebases as Catch with its stored timing fallback");
        localHistory.Undo();
        Check(localHistory.Document.Fruits.Count == 0 && SongSetup.Get(localHistory.Document, "General", "Mode") == "2",
            "Catch context remains valid in older raw-section-free undo snapshots");
        localHistory.Redo();
        Check(localHistory.Document.Fruits.Count == 1 && OsuBeatmapWriter.Serialize(localHistory.Document).ReadBack.Fruits.Single().X == 200,
            "Rebased local authoring retains editable content and export through redo");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
