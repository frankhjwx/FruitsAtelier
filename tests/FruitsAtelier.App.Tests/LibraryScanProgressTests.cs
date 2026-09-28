using FruitsAtelier.Core;

internal static class LibraryScanProgressTests
{
    public static void Run()
    {
        string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "atelier-scan-progress-" + Guid.NewGuid());
        try
        {
            string songs = Path.Combine(root, "Songs"); Directory.CreateDirectory(songs);
            string oversized = Path.Combine(songs, "oversized.osu");
            using (var file = File.Create(oversized)) file.SetLength(OsuBeatmapReader.MaximumFileBytes + 1);
            File.WriteAllText(Path.Combine(songs, "valid.osu"), "osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Valid\n[HitObjects]\n");
            File.WriteAllText(Path.Combine(songs, "standard.osu"), "osu file format v14\n[General]\nMode:0\n");
            var db = new LibraryDatabase(Path.Combine(root, "Workspace"), songs);
            var progress = new List<LibraryScanProgress>();
            bool visibleDuringScan = false;
            var scan = db.Scan(progress: p =>
            {
                progress.Add(p);
                if (p.Indexed > 0) visibleDuringScan |= db.Search("").Count > 0;
            });
            if (!visibleDuringScan) throw new Exception("Indexed maps must be searchable before Scan returns");
            if (scan.Count != 1 || scan.Errors.Count != 1 || !scan.Errors[0].Contains(oversized)
                || progress[^1] != new LibraryScanProgress(3, 1, 1) || db.Search("").Count != 1)
                throw new Exception("Oversized map must not abort scanning or hide its path; progress must include all examined files");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
