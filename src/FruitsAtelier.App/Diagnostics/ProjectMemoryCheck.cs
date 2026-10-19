using System.Diagnostics;
using System.Runtime.CompilerServices;
using FruitsAtelier.App.Audio;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Diagnostics;

internal static class ProjectMemoryCheck
{
    internal static void Run(D2DCanvas canvas, EditorView view, string folder)
    {
        Directory.CreateDirectory("artifacts/tests/project-memory");
        using var log = new StreamWriter("artifacts/tests/project-memory/native.txt");
        view.RequestPreloadHitsounds = null;
        view.RequestPrepareHitsound = _ => { };
        view.RequestScheduleHitsound = (_, _) => { };
        view.RequestStopHitsounds = null;
        view.RequestDifficultyChanged = null;
        canvas.Resize(2520, 1575, 168);
        Report("baseline");
        for (int round = 0; round < 2; round++)
        {
            Open(view, folder);
            using var audio = new AudioTransport(0);
            if (view.Document.AudioPath is { } path && !audio.LoadAsync(path).GetAwaiter().GetResult())
                throw new InvalidOperationException(audio.State.Error);
            view.UpdateTransport(0, audio.State.DurationMs, audio.State.CanPlay, false, false, null, audio.State.FilePath);
            Report($"round {round + 1} loaded");
            var timer = Stopwatch.StartNew();
            do { Draw(); Thread.Sleep(20); }
            while (view.StarRatingsRefreshing && timer.Elapsed.TotalMinutes < 5);
            if (view.StarRatingsRefreshing) throw new TimeoutException("Difficulty ratings did not finish.");
            for (int i = 0; i < 10; i++) { Draw(); Thread.Sleep(20); }
            Report($"round {round + 1} settled editing");
            view.ToggleOsuCanvas();
            view.UpdateTransport(50836, audio.State.DurationMs, audio.State.CanPlay, false, false, null, audio.State.FilePath);
            for (int i = 0; i < 5; i++) Draw();
            Report($"round {round + 1} alternate canvas");
            for (int i = 0; i < 600; i++)
            {
                view.UpdateTransport(50836 + i * 1000d / 60, audio.State.DurationMs, audio.State.CanPlay, true, false, null, audio.State.FilePath);
                Draw();
            }
            view.UpdateTransport(60836, audio.State.DurationMs, audio.State.CanPlay, false, false, null, audio.State.FilePath);
            Report($"round {round + 1} ten seconds simulated playback");
            if (view.IsDirty) throw new InvalidOperationException("Opening changed map content.");
            canvas.Begin(); view.Render(canvas, 1440, 900);
            canvas.End(capture: () =>
            {
                var frame = canvas.CapturePixels();
                using var writer = new BinaryWriter(File.Create($"artifacts/tests/project-memory/editing-{round + 1}.bmp"));
                writer.Write((ushort)0x4D42); writer.Write(54 + frame.Pixels.Length); writer.Write(0); writer.Write(54);
                writer.Write(40); writer.Write(frame.Width); writer.Write(-frame.Height);
                writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0); writer.Write(frame.Pixels.Length);
                writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(frame.Pixels);
            });
            view.ShowLibrary(); audio.Dispose();
            for (int i = 0; i < 10; i++) { Draw(); Thread.Sleep(20); }
            if (!view.LibraryVisible || view.HasEditorProject) throw new InvalidOperationException("Editor did not close.");
            Report($"round {round + 1} returned to library");
        }
        void Draw() { canvas.Begin(); view.Render(canvas, 1440, 900); canvas.End(); }
        void Report(string phase)
        {
            using var p = Process.GetCurrentProcess();
            string line = $"{phase}: working={p.WorkingSet64 / 1048576d:F1}, private={p.PrivateMemorySize64 / 1048576d:F1}, peak={p.PeakWorkingSet64 / 1048576d:F1}, managed={GC.GetTotalMemory(false) / 1048576d:F1} MiB";
            Console.WriteLine(line); log.WriteLine(line); log.Flush();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Open(EditorView view, string folder)
    {
        var maps = Directory.GetFiles(folder, "*.osu").Order().Select(OsuBeatmapReader.ReadFile).ToArray();
        view.LoadProject(BeatmapProject.FromDocuments(maps)); view.CloseLibrary();
    }
}
