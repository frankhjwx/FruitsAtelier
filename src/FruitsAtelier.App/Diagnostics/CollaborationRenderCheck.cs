using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class CollaborationRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string folder = Path.GetFullPath("artifacts/tests/collaboration/native");
        Directory.CreateDirectory(folder); string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                var a = Difficulty("Mapper A", 500); var b = Difficulty("Mapper B", 1500);
                var c = Difficulty("Merged C", 2500); var d = Difficulty("Merged D", 3000);
                foreach (var difficulty in new[] { a, b, c, d }) difficulty.Document.TimingPoints[0].TimeMs = .75;
                c.Document.TimingPoints.Add(new() { TimeMs = 1000, BeatLengthMs = 500, SourceOrder = int.MinValue });
                a.Document.BananaShowers.Add(new() { TimeMs = 500, EndTimeMs = 1500 });
                c.Document.CollaborationReferences = [new(Guid.NewGuid(), a.Id, 0, 1000), new(Guid.NewGuid(), b.Id, 1000, 2000),
                    new(Guid.NewGuid(), Guid.Empty, 2000, 5000)];
                d.Document.CollaborationReferences = [new(Guid.NewGuid(), a.Id, 0, 1000), new(Guid.NewGuid(), a.Id, 3000, 4000)];
                var editor = new EditorView(false);
                editor.LoadProject(new BeatmapProject { Name = "Collaboration", Difficulties = [a, b, c, d] });
                editor.SwitchDifficulty(2); editor.ShowAimod();
                if (!editor.AimodIssues.Any(i => i.Code == "conflictTiming")) throw new InvalidOperationException("Native initial AiMod report missing.");
                editor.ShowCollaborationPanel();
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                Paint("target");
                var before = editor.Document.DeepClone();
                var input = editor.CollaborationStartBounds; editor.PointerDown(input.X + 10, input.Y + 10, 0, false, false);
                editor.PointerUp(input.X + 10, input.Y + 10, 0);
                editor.KeyDown('A', true, false); editor.TextInput('5'); editor.TextInput('0'); editor.KeyDown(13, false, false);
                if (editor.Document.CollaborationReferences![0].StartMs != 50) throw new InvalidOperationException("Native reference range input failed.");
                Paint("edited");
                editor.KeyDown('Z', true, false);
                if (!editor.Document.ContentEquals(before)) throw new InvalidOperationException("Native reference undo failed.");
                editor.SynchronizeCollaboration();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (editor.CollaborationBusy && watch.Elapsed.TotalSeconds < 15) { Paint(); Thread.Sleep(1); }
                if (editor.CollaborationBusy || editor.Document.Fruits.Count != 3) throw new InvalidOperationException("Native merge failed.");
                if (editor.AimodIssues.Any(i => i.Code == "conflictTiming")) throw new InvalidOperationException("Native AiMod report did not refresh after sync.");
                if (editor.Document.TimingPoints.Count != 1 || editor.Document.TimingPoints[0].TimeMs != .75
                    || Math.Abs(TimingMap.Snap(editor.Document, 1250, 4) - 1250.75) > 1e-7)
                    throw new InvalidOperationException("Native merge changed the target beat grid.");
                Paint("synced");
                if (!editor.CollaborationReportVisible) throw new InvalidOperationException("Native exclusions report missing.");
                var report = editor.CollaborationReportBounds;
                editor.PointerDown(report.X + 50, report.Y + 16, 0, false, false);
                editor.PointerMove(report.X + 80, report.Y + 36, false, false);
                editor.PointerUp(report.X + 80, report.Y + 36, 0); Paint("report-moved");
                var moved = editor.CollaborationReportBounds;
                if (moved.X != report.X + 30 || moved.Y != report.Y + 20) throw new InvalidOperationException("Native report drag failed.");
                editor.PointerDown(moved.X + 25, moved.Y + 110, 0, false, false);
                editor.PointerUp(moved.X + 25, moved.Y + 110, 0); Paint("report-source");
                if (editor.CurrentDifficultyName != "Mapper A" || editor.PlayheadMs != 500) throw new InvalidOperationException("Native report navigation failed.");
                editor.KeyDown(27, false, false);
                editor.SwitchDifficulty(0); Paint("source");
                editor.ToggleCollaborationSectionLock(d.Id); Paint("locked");
                if (editor.Document.CollaborationEditableRanges?.Count != 2) throw new InvalidOperationException("Native section lock failed.");
                editor.ToggleCollaborationSectionLock(d.Id); Paint("unlocked");
                Console.WriteLine($"PASS collaboration native {locale} {size.Item1}x{size.Item2} {dpi} DPI");
                void Paint(string? state = null)
                {
                    canvas.Begin(); editor.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: state is null ? null : () =>
                    {
                        var frame = canvas.CapturePixels();
                        using var output = new BinaryWriter(File.Create(Path.Combine(folder, $"{state}-{locale}-{size.Item1}-{dpi}.bmp")));
                        output.Write((ushort)0x4D42); output.Write(54 + frame.Pixels.Length); output.Write(0); output.Write(54);
                        output.Write(40); output.Write(frame.Width); output.Write(-frame.Height); output.Write((ushort)1); output.Write((ushort)32);
                        output.Write(0); output.Write(frame.Pixels.Length); output.Write(0); output.Write(0); output.Write(0); output.Write(0); output.Write(frame.Pixels);
                    });
                }
            }
        }
        finally { L.SetLanguage(language); }
    }
    private static ProjectDifficulty Difficulty(string name, double time)
    {
        var document = new MapDocument { Name = name, DurationMs = 5000, IsDemo = false };
        SongSetup.Set(document, "Metadata", "Version", name);
        document.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500 });
        document.Fruits.Add(new() { TimeMs = time, X = 180 });
        return new() { Name = name, Document = document };
    }
}
