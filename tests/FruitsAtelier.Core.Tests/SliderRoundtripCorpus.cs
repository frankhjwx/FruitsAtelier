using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FruitsAtelier.Core;

internal static class SliderRoundtripCorpus
{
    public static int Run(string input, string outputDirectory)
    {
        string source = Path.GetFullPath(input), outputPath = Path.GetFullPath(outputDirectory);
        if (string.Equals(Path.GetDirectoryName(source), outputPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use a separate output directory.");
        byte[] bytes = File.ReadAllBytes(source), hash = SHA256.HashData(bytes);
        // Read the document directly; opening a workspace session can recover or synchronize files.
        var original = ProjectSerializer.Read(System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
        var snapshot = original.DeepClone();
        var stopwatch = Stopwatch.StartNew();
        long allocated = GC.GetTotalAllocatedBytes();
        var exported = OsuBeatmapWriter.Serialize(original);
        var imported = OsuBeatmapReader.Read(exported.Text);
        var sourceIds = imported.ImportedSliders.ToDictionary(s => s.Id, s => exported.ObjectSources[s.SourceOrder]);
        var before = OsuBeatmapWriter.Serialize(imported);
        double ratio = SliderImportEditing.CompensatedSliderRatio(imported);
        var initialized = SliderImportEditing.Initialize(imported, true);
        if (!initialized.Success) throw new Exception(string.Join("; ", initialized.Diagnostics));
        var converted = ImportedSliderEditing.ConvertAll(imported);
        var after = OsuBeatmapWriter.Serialize(imported);
        SliderRecoveryTests.Equivalent(before.PlayableObjects, after.PlayableObjects);
        SliderRecoveryTests.Equivalent(before.PlayableHardRockObjects, after.PlayableHardRockObjects);
        var tracks = converted.Tracks.ToDictionary(t => sourceIds[t.Id]);
        var rows = original.Tracks.Where(t => t.StreamSnapDivisor is null).Select(t =>
        {
            tracks.TryGetValue(t.Id, out var recovered);
            double? error = recovered is null ? null : SliderRecoveryTests.Error(t, recovered);
            return new
            {
                t.Id, StartMs = t.Nodes[0].TimeMs, t.SpanCount, OriginalAnchors = t.Nodes.Count,
                OriginalControls = Controls(t), RecoveredAnchors = recovered?.Nodes.Count,
                RecoveredControls = recovered is null ? (int?)null : Controls(recovered),
                MaximumPathError = error,
                Restored = recovered is not null && error <= 4 && recovered.Nodes.Count <= t.Nodes.Count
            };
        }).ToArray();
        stopwatch.Stop();
        var report = new
        {
            Source = source, SourceSha256 = Convert.ToHexString(hash), CompensatedDetectionRatio = ratio,
            Sliders = rows.Length, Converted = converted.Tracks.Count, RetainedLegacy = imported.ImportedSliders.Count,
            Restored = rows.Count(r => r.Restored), OriginalAnchors = rows.Sum(r => r.OriginalAnchors),
            RecoveredAnchors = converted.Tracks.Sum(t => t.Nodes.Count),
            NmMaximumPositionError = before.PlayableObjects.Zip(after.PlayableObjects).Max(p => Math.Abs(p.First.X - p.Second.X)),
            HrMaximumPositionError = before.PlayableHardRockObjects.Zip(after.PlayableHardRockObjects).Max(p => Math.Abs(p.First.X - p.Second.X)),
            ElapsedMs = stopwatch.Elapsed.TotalMilliseconds, AllocatedBytes = GC.GetTotalAllocatedBytes() - allocated,
            Objects = before.PlayableObjects.Count, Rows = rows
        };
        if (!original.ContentEquals(snapshot) || !SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(hash))
            throw new Exception("Source data changed during read-only validation.");
        Directory.CreateDirectory(outputPath);
        File.WriteAllText(Path.Combine(outputPath, "export.osu"), exported.Text);
        File.WriteAllText(Path.Combine(outputPath, "recovered.catchdiff"), ProjectSerializer.Serialize(imported));
        File.WriteAllText(Path.Combine(outputPath, "recovered.osu"), after.Text);
        File.WriteAllText(Path.Combine(outputPath, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Restored {report.Restored}/{report.Sliders} paths within 4px with no additional anchors; converted {report.Converted}.");
        Console.WriteLine($"Anchors {report.OriginalAnchors} -> {report.RecoveredAnchors}; NM {report.NmMaximumPositionError:F4}px, HR {report.HrMaximumPositionError:F4}px; {report.Objects} unchanged event kinds/times.");
        Console.WriteLine($"Time {report.ElapsedMs:F1}ms; allocation {report.AllocatedBytes / 1048576d:F1}MiB; detection {ratio:P1}; source SHA256 unchanged.");
        return 0;
    }

    private static int Controls(CurveTrack track) => track.Nodes.Sum(n => n.OutgoingCurve?.Controls.Count
        ?? (n.HandleIn != default ? 1 : 0) + (n.HandleOut != default ? 1 : 0));
}
