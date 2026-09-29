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
                ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.workspace")); Wait();
                Check(ui.Canvas.Texts.Single(t => t.Value == L.Get("storage.title")).Y > ui.Canvas.Texts.Single(t => t.Value == L.Get("library.songs")).Y, "storage follows workspace paths");
                Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("storage.policy")), "storage omits the policy hint");
                float applyY = ui.Canvas.Texts.Single(t => t.Value == L.Get("library.apply")).Y;
                ui.View.Wheel(ui.View.SettingsBounds.X + 300, ui.View.SettingsBounds.Y + 200, -12000, false); ui.Paint();
                Check(ui.Canvas.Texts.Single(t => t.Value == L.Get("library.apply")).Y == applyY, "Apply stays fixed while workspace content scrolls");
                Check(ui.Canvas.Texts.Any(t => t.Value.StartsWith(L.Get("storage.total", ""))), "storage total is visible");
                Check(ui.Canvas.Texts.Any(t => t.Value.StartsWith(L.Get("storage.resources"))), "imported assets have a separate category");
                foreach (string key in new[] { "storage.refresh", "storage.cleanHistory", "storage.clearCache", "storage.openFolder" })
                {
                    var text = ui.Canvas.Texts.Single(t => t.Value == L.Get(key));
                    Check(text.Y < ui.View.SettingsBounds.Bottom - 100 && text.X >= ui.View.SettingsBounds.X, "storage buttons fit narrow settings");
                }
                string? opened = null;
                Check(!ui.Canvas.Texts.Any(t => t.Value is "folder0" or "folder1"), "usage ranking only shows the eight largest entries");
                Check(ui.Canvas.Texts.Single(t => t.Value == L.Get("storage.refresh")).X < ui.Canvas.Texts.Single(t => t.Value == L.Get("storage.openFolder")).X
                    && ui.Canvas.Texts.Single(t => t.Value == L.Get("storage.openFolder")).X < ui.Canvas.Texts.Single(t => t.Value == L.Get("storage.cleanHistory")).X, "open folder sits between refresh and clean history");
                ui.View.RequestOpenExternalPath = path => opened = path; ui.Paint();
                ui.ClickText(L.Get("storage.openFolder"));
                Check(opened == root, "open folder uses active workspace");
                var settings = ui.View.SettingsBounds;
                float scrollbarX = settings.Right - 13;
                ui.View.PointerDown(scrollbarX, settings.Y + 126, 0, false, false);
                Check(ui.View.WantsCapture, "workspace scrollbar captures drag");
                ui.View.PointerMove(scrollbarX, settings.Y + 125, false, false);
                ui.View.PointerUp(scrollbarX, settings.Y + 125, 0); ui.Paint();
                Check(!ui.View.WantsCapture && ui.Canvas.Texts.Single(t => t.Value == L.Get("library.workspace")).Y > settings.Y, "scrollbar returns to path fields");
                ui.View.Wheel(settings.X + 300, settings.Y + 200, -12000, false); ui.Paint();
                ui.ClickText(L.Get("storage.clearCache")); Wait();
                Check(!File.Exists(review) && File.Exists(audio), "cache button clears previews and preserves assets");
                Check(ui.View.Document.ContentEquals(before), "storage maintenance does not edit the map");
                var ready = DateTime.UtcNow.AddSeconds(15);
                while (ui.View.LibraryLoading && DateTime.UtcNow < ready) { Thread.Sleep(10); ui.Paint(); }
                string broken = Path.Combine(root, "broken.catchdiff"); File.WriteAllText(broken, "{bad");
                ui.ClickText(L.Get("storage.clearCache")); Wait();
                var scan = typeof(FruitsAtelier.App.Editor.EditorView).GetField("scanTask", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                Check(scan.GetValue(ui.View) is null, "failed reference parsing does not trigger an index rebuild");
                for (int frame = 0; frame < 8; frame++)
                {
                    Thread.Sleep(20); ui.Paint();
                    Check(!ui.View.StorageBusy && ui.Canvas.Texts.Single(t => t.Value == L.Get("storage.clearCache")).Color != 0x5B6777u, "failed cleanup leaves button state stable");
                }
                File.Delete(broken);
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
