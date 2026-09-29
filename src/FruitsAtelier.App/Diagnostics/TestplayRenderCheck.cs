using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class TestplayRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string language = L.Language;
        string folder = Path.GetFullPath("artifacts/tests/testplay-native");
        Directory.CreateDirectory(folder);
        string image = Path.Combine(AppContext.BaseDirectory, "assets", "branding", "mark.png");
        foreach (string component in new[] { "play-skip", "pause-continue", "pause-retry", "pause-back", "pause-overlay" })
            File.Copy(image, Path.Combine(folder, component + ".png"), true);
        var cases = new List<object>();
        try
        {
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (var size in new[] { (1440, 900), (980, 620), (600, 900) })
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                var view = new EditorView();
                var map = new MapDocument { SourcePath = Path.Combine(folder, "map.osu") };
                map.OriginalSections.Add(new OsuSection { Name = "Events", Lines = { "0,0,\"pause-overlay.png\",0,0", "2,12000,16000" } });
                map.Fruits.AddRange([new Fruit { TimeMs = 10000, X = 256 }, new Fruit { TimeMs = 20000, X = 256 }]);
                view.LoadDocument(map); view.LoadSkin(folder);
                view.StartTestplay(); Paint();
                if (!view.TestplaySkipVisible) throw new InvalidOperationException("Native Skip is missing.");
                var skip = view.TestplaySkipBounds;
                view.PointerDown(skip.X + skip.Width / 2, skip.Y + skip.Height / 2, 0, false, false);
                view.PointerUp(skip.X + skip.Width / 2, skip.Y + skip.Height / 2, 0); Paint();
                if (view.TestplaySkipVisible || view.PlayheadMs < 7000) throw new InvalidOperationException("Native Skip click failed.");
                view.KeyDown(27, false, false); view.KeyUp(27); Paint();
                if (!view.TestplayPauseMenuVisible) throw new InvalidOperationException("Native pause menu is missing.");
                view.KeyDown(40, false, false); view.KeyUp(40);
                view.KeyDown(13, false, false); view.KeyUp(13); Paint();
                if (view.TestplayPaused || !view.TestplaySkipVisible) throw new InvalidOperationException("Native Retry failed.");
                view.KeyDown(32, false, false); view.KeyUp(32); Paint();
                if (view.TestplaySkipVisible) throw new InvalidOperationException("Native Space skip failed.");
                view.KeyDown(27, false, false); view.KeyUp(27); Paint();
                view.KeyDown(38, false, false); view.KeyUp(38);
                view.KeyDown(13, false, false); view.KeyUp(13); Paint();
                if (view.IsTestplaying) throw new InvalidOperationException("Native Back failed.");
                cases.Add(new { dpi, width = size.Item1, height = size.Item2, locale });
                void Paint()
                {
                    canvas.Begin();
                    if (!canvas.BackgroundImage(image, new(0, 0, size.Item1, size.Item2)))
                        throw new InvalidOperationException("Native background decode failed.");
                    view.Render(canvas, size.Item1, size.Item2); canvas.End();
                }
            }
            CheckBackgroundCache(canvas, folder, image);
            File.WriteAllText(Path.Combine(folder, "report.json"), System.Text.Json.JsonSerializer.Serialize(cases));
            AppLog.Write($"Testplay render check passed: {cases.Count} native cases; silent callbacks.");
        }
        finally { L.SetLanguage(language); }
    }
    private static void CheckBackgroundCache(D2DCanvas canvas, string folder, string sprite)
    {
        string path = Path.Combine(folder, "large-background.png");
        using (var file = File.Create(path))
        {
            file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            var header = new byte[13];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, 4096);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), 4096);
            header[8] = 8; header[9] = 6;
            Chunk("IHDR", header);
            using var data = new MemoryStream();
            using (var zipped = new System.IO.Compression.ZLibStream(data, System.IO.Compression.CompressionLevel.Fastest, true))
            {
                var row = new byte[1 + 4096 * 4];
                for (int x = 1; x < row.Length; x += 4) { row[x] = 32; row[x + 1] = 64; row[x + 2] = 96; row[x + 3] = 255; }
                for (int y = 0; y < 4096; y++) zipped.Write(row);
            }
            Chunk("IDAT", data.ToArray()); Chunk("IEND", []);
            void Chunk(string type, byte[] bytes)
            {
                var count = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(count, bytes.Length); file.Write(count);
                byte[] name = System.Text.Encoding.ASCII.GetBytes(type); file.Write(name); file.Write(bytes);
                uint crc = uint.MaxValue;
                foreach (byte b in name.Concat(bytes))
                {
                    crc ^= b;
                    for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
                }
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(count, ~crc); file.Write(count);
            }
        }
        canvas.Resize(1440, 900, 96);
        var results = new List<object>();
        foreach (bool resident in new[] { false, true })
        {
            Paint(); int decoded = canvas.ImageDecodeCount;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            for (int frame = 0; frame < 12; frame++) Paint();
            timer.Stop(); int reloads = canvas.ImageDecodeCount - decoded;
            results.Add(new { resident, frames = 12, reloads, meanMs = timer.Elapsed.TotalMilliseconds / 12,
                allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated });
            if (resident && reloads != 0) throw new InvalidOperationException("Background or scene sprites were evicted during warm playback.");
            void Paint()
            {
                canvas.Begin();
                bool loaded = resident ? canvas.BackgroundImage(path, new(0, 0, 1440, 900)) : canvas.Image(path, new(0, 0, 1440, 900));
                if (!loaded || !canvas.Image(sprite, new(600, 600, 96, 96))) throw new InvalidOperationException("Cache fixture decode failed.");
                canvas.End();
            }
        }
        File.WriteAllText(Path.Combine(folder, "background-cache.json"), System.Text.Json.JsonSerializer.Serialize(results));
    }

}
