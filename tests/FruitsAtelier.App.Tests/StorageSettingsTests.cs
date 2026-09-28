using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class StorageSettingsTests
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
                string root = Path.GetFullPath(Path.Combine("artifacts/tests/storage-ui", Guid.NewGuid().ToString("N")));
                Directory.CreateDirectory(Path.Combine(root, "Resources"));
                string audio = Path.Combine(root, "Resources", "audio.mp3"); File.WriteAllText(audio, "preserved music");
                string reviews = Path.Combine(root, ".sync-history", "reviews"); Directory.CreateDirectory(reviews);
                string review = Path.Combine(reviews, "comparison.txt"); File.WriteAllText(review, "temporary preview");
                for (int folder = 0; folder < 10; folder++)
                { string path = Path.Combine(root, "folder" + folder); Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "asset.bin"), new string('x', 100 + folder)); }
                var ui = new Ui(false); ui.Resize(size.Item1, size.Item2);
                ui.View.LibrarySettings.Workspace = root; ui.View.LibrarySettings.OsuRoot = "";
                var before = ui.View.Document.DeepClone();
                ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("storage.title")); Wait();
                Check(ui.Canvas.Texts.Any(t => t.Value.StartsWith(L.Get("storage.total", ""))), "storage total is visible");
                Check(ui.Canvas.Texts.Any(t => t.Value.StartsWith(L.Get("storage.resources"))), "imported assets have a separate category");
                foreach (string key in new[] { "storage.refresh", "storage.cleanHistory", "storage.clearCache" })
                {
                    var text = ui.Canvas.Texts.Single(t => t.Value == L.Get(key));
                    Check(text.Y < ui.View.SettingsBounds.Bottom - 100 && text.X >= ui.View.SettingsBounds.X, "storage buttons fit narrow settings");
                }
                var topFolder = ui.Canvas.Texts.Single(t => t.Value == "folder9");
                ui.View.Wheel(topFolder.X + 10, topFolder.Y + 5, -120, false); ui.Paint();
                Check(!ui.Canvas.Texts.Any(t => t.Value == "folder9"), "folder usage list scrolls independently");
                ui.ClickText(L.Get("storage.clearCache")); Wait();
                Check(!File.Exists(review) && File.Exists(audio), "cache button clears previews and preserves assets");
                Check(ui.View.Document.ContentEquals(before), "storage maintenance does not edit the map");
                ui.Key(27); ui.View.ShowLibrary(); ui.View.EnableFileMonitoring();
                string unused = Path.Combine(root, ".sync-history", "resources", new string('E', 64));
                Directory.CreateDirectory(Path.GetDirectoryName(unused)!); File.WriteAllText(unused, "orphan audio");
                File.SetLastWriteTimeUtc(unused, DateTime.UtcNow.AddDays(-2));
                typeof(FruitsAtelier.App.Editor.EditorView).GetField("nextStorageMaintenance", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(ui.View, DateTime.MinValue);
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (File.Exists(unused) && DateTime.UtcNow < deadline) { Thread.Sleep(10); ui.Paint(); }
                Check(!File.Exists(unused), "idle library automatically reclaims unreferenced audio");
                ui.View.StopFileMonitoring();
                void Wait()
                {
                    var deadline = DateTime.UtcNow.AddSeconds(15);
                    do { Thread.Sleep(10); ui.Paint(); }
                    while (ui.Canvas.Texts.Any(t => t.Value == L.Get("storage.working")) && DateTime.UtcNow < deadline);
                    Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("storage.working")), "storage worker completes");
                }
            }
        }
        finally { L.SetLanguage(language); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
