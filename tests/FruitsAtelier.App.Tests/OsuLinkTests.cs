using FruitsAtelier.Core;
using FruitsAtelier.App.Platform;
using L = FruitsAtelier.Localization.Strings;

internal static class OsuLinkTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                Check(EditorTimestamp.TryParse("osu://edit/00:13:247%20(2,3)", out var parsed)
                    && parsed!.TimeMs == 13247 && parsed.ComboNumbers!.SequenceEqual([2, 3]), "encoded timestamp");
                Check(EditorTimestamp.TryParse("osu://edit/00:13.247%20%282%2C3%29", out parsed) && parsed!.TimeMs == 13247, "encoded punctuation");
                Check(EditorTimestamp.TryParse("osu://edit/01:32:989%20(5,1)", out parsed)
                    && parsed!.TimeMs == 92989 && parsed.ComboNumbers!.SequenceEqual([5, 1]), "reported editor link");
                foreach (string invalid in new[] { "osu://dl/123", "osu://edit/00:99:000", "osu://edit/00:00:000%zz", "osu://edit/00:00:000%2520(1)", "osu://edit/00:00:000 (2147483648)", "osu://edit/999999999999999999999999:00:000", "osu://edit/00:00:000?map=1" })
                    Check(!EditorTimestamp.TryParse(invalid, out _), "invalid input: " + invalid);
                var ui = new Ui();
                var map = new MapDocument { DurationMs = 60000 };
                foreach (var (time, flags) in new[] { (13247, 5), (14000, 1), (15000, 1), (16000, 5), (17000, 1) })
                    map.Fruits.Add(new Fruit { TimeMs = time, X = 100, OriginalLine = $"100,192,{time},{flags},0,0:0:0:0:" });
                ui.LoadDocument(map);
                double? sought = null; int paused = 0;
                ui.View.RequestSeek = time => sought = time; ui.View.RequestPausePlayback = () => paused++;
                ui.View.UpdateTransport(1000, 60000, true, true, false, null, null);
                Check(ui.View.HandleEditorLink("osu://edit/00:13:247%20(2,3,1,2)"), "editor link accepted");
                Check(paused == 1 && sought == 13247 && ui.View.SelectedObjectIds.ToHashSet().SetEquals(map.Fruits.Skip(1).Select(f => f.Id)), "ordered combo resets and paused transport");
                Check(ui.View.HandleEditorLink("osu://edit/00:14:000 (2,2)") && ui.View.SelectedObjectIds.ToHashSet().SetEquals(new[] { map.Fruits[1].Id, map.Fruits[4].Id }), "repeated combo numbers select distinct later objects");
                Check(ui.View.HandleEditorLink("osu://edit/00:13:248 (1)") && sought == 14000 && ui.View.SelectedObjectIds.Single() == map.Fruits[3].Id, "next object seek and forward matching");
                Check(ui.View.HandleEditorLink("osu://edit/00:59:000 (7)") && sought == 59000 && ui.View.SelectedObjectIds.Count == 0, "no match does not select unrelated objects");
                Check(ui.View.HandleEditorLink("osu://edit/99:00:000") && sought == 60000, "seek clamps to duration");
                Check(!ui.View.HandleEditorLink("osu://edit/00:99:000") && sought == 60000, "invalid link preserves position");
                ui.View.UpdateTransport(1000, 60000, true, false, false, null, null); ui.Paint();
                var display = ui.View.TimeDisplayBounds; ui.Click(display.X + 8, display.Y + 8);
                ui.View.PasteTimeJumpText("osu://edit/00:13:247%20(2,3)", ui.View.TimeJumpSession); ui.Key(13);
                Check(!ui.View.TimeJumpVisible && sought == 13247 && ui.View.SelectedObjectIds.Count == 2, "full link in jump dialog");
                Check(!ui.View.IsDirty && ui.View.Document.ContentEquals(map), "navigation preserves content and history");
                ui.View.OpenSettings(); ui.Paint();
                int choices = 0;
                string selectedId = "stable"; bool bound = true;
                var applications = new List<OsuLinkApplication> { new("stable", "osu!stable", "stable.png", @"E:\osu!\osu!.exe"), new("lazer", "osu!(lazer)", "lazer.png", @"C:\osu lazer\osu!.exe") };
                ui.View.ReadOsuLinkApplications = () => applications;
                ui.View.ReadOsuLinkSettings = () => (selectedId, bound, applications.Any(a => a.Id == selectedId));
                ui.View.RequestSelectOsuLinkFallback = id => { choices++; selectedId = id; };
                ui.ClickText(L.Get("osuLinks.title"));
                var selector = ui.View.OsuLinkTargetButtonBounds;
                ui.Click(selector.X + 12, selector.Y + 12);
                Check(ui.Canvas.Texts.Any(t => t.Value == "osu!stable") && ui.Canvas.Texts.Any(t => t.Value == "osu!(lazer)"), "registered application list");
                Check(ui.Canvas.Images.Any(i => i.Path == "stable.png") && ui.Canvas.Images.Any(i => i.Path == "lazer.png"), "application icons accompany list rows");
                var firstRow = ui.View.OsuLinkFallbackButtonBounds;
                ui.Click(firstRow.X + 20, firstRow.Y + 56 + 20);
                Check(choices == 0 && selectedId == "stable", "row selection remains pending until confirmation");
                ui.View.RefreshOsuLinkSettings(refreshApplications: true); ui.Paint();
                ui.ClickText(L.Get("osuLinks.confirm"));
                Check(choices == 1 && selectedId == "lazer" && ui.Canvas.Texts.Any(t => t.Value == "osu!(lazer)"), "fallback selected by application identity");
                Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("library.apply")), "fallback selection has no unrelated settings action");
                var pathLabel = ui.Canvas.Texts.Single(t => t.Value == @"C:\osu lazer\osu!.exe");
                var nameLabel = ui.Canvas.Texts.Single(t => t.Value == "osu!(lazer)");
                Check(pathLabel.X > nameLabel.X && pathLabel.Size < nameLabel.Size && pathLabel.Color != nameLabel.Color, "application path follows its name in smaller muted text");
                int clearRequests = 0; ui.View.RequestClearOsuLinkBinding = () => clearRequests++; ui.Paint();
                ui.ClickText(L.Get("osuLinks.clearBinding")); Check(clearRequests == 1, "settings can clear the FA association");
                double before = ui.View.PlayheadMs; var selection = ui.View.SelectedObjectIds.ToArray();
                Check(!ui.View.HandleEditorLink("osu://edit/00:00:000"), "settings is outside editing"); ui.Paint();
                Check(ui.View.PlayheadMs == before && ui.View.SelectedObjectIds.SequenceEqual(selection)
                    && ui.Canvas.Texts.Any(t => t.Value == L.Get("osuLinks.editModeRequired")), "non-editor notice without navigation");
                ui.Key(27); ui.View.ShowLibrary(); ui.Paint(); before = ui.View.PlayheadMs;
                Check(!ui.View.HandleEditorLink("osu://edit/00:00:000") && ui.View.PlayheadMs == before, "library rejects editor links");
                ui.View.CloseLibrary(); ui.Paint();
                ui.LoadDocument(map);
                ui.View.CloseLibrary(); ui.Paint();
                ui.View.UpdateTransport(5000, 60000, true, false, false, null, null); ui.Paint(); ui.Paint();
                Check(ui.View.PlayheadMs == 5000, "rejected links never replay on entry");
                ui.LoadDocument(DemoMap.Create()); ui.Key('A', ctrl: true); ui.Key('C', ctrl: true);
                var copiedIds = ui.View.SelectedObjectIds.ToHashSet();
                string reference = ui.View.ObjectClipboardText.TrimEnd(' ', '-');
                bool accepted = ui.View.HandleEditorLink("osu://edit/" + reference);
                Check(copiedIds.Count > 1 && accepted && ui.View.SelectedObjectIds.ToHashSet().SetEquals(copiedIds),
                    $"source slider and fruit copy/link roundtrip: {reference}, copied={copiedIds.Count}, accepted={accepted}, selected={ui.View.SelectedObjectIds.Count}, notice={ui.View.StatusMessage}");
                var converted = ui.View.Conversion;
                Check(ui.View.HandleEditorLink("osu://edit/" + reference) && ReferenceEquals(converted, ui.View.Conversion), "selection reuses unchanged conversion");
                ui.View.UpdateTransport(1000, 20000, true, false, false, null, null); ui.View.StartTestplay();
                Check(ui.View.IsTestplaying, "testplay started");
                before = ui.View.PlayheadMs;
                Check(!ui.View.HandleEditorLink("osu://edit/00:05:000") && ui.View.PlayheadMs == before && ui.View.IsTestplaying, "testplay only displays a notice");
                ui.View.StopTestplay();
                var simultaneous = new MapDocument { DurationMs = 10000 };
                simultaneous.Fruits.AddRange([new Fruit { TimeMs = 1000, X = 100, SourceOrder = 0 }, new Fruit { TimeMs = 1000, X = 200, SourceOrder = 1 }]);
                ui.LoadDocument(simultaneous);
                Check(ui.View.HandleEditorLink("osu://edit/00:01:000 (2,1)") && ui.View.SelectedObjectIds.Count == 2, "simultaneous combo matches retain equal-time candidates");
                bound = false; selectedId = ""; ui.View.RefreshOsuLinkSettings();
                Check(!ui.View.OsuLinkFallbackRequired, "no setup required before system association");
                bound = true; ui.View.RefreshOsuLinkSettings(); ui.Paint();
                Check(ui.View.OsuLinkFallbackRequired && ui.Canvas.Texts.Any(t => t.Value == "osu!stable"), "system association without fallback forces application list");
                var blocked = ui.View.Document.DeepClone();
                ui.Key(27); ui.Key(46); ui.Key('F');
                Check(ui.View.OsuLinkFallbackRequired && ui.View.Document.ContentEquals(blocked), "setup cannot be dismissed or edit the map");
                choices = 0; ui.Key(13);
                Check(choices == 0 && ui.View.OsuLinkFallbackRequired, "confirmation requires an application selection");
                var requiredRow = ui.View.OsuLinkFallbackButtonBounds;
                ui.Click(requiredRow.X + 20, requiredRow.Y + 20);
                Check(choices == 0 && ui.View.OsuLinkFallbackRequired, "required selection waits for confirmation");
                ui.Key(13);
                Check(choices == 1 && selectedId == "stable" && !ui.View.OsuLinkFallbackRequired, "registered selection completes required setup");
                applications.RemoveAt(0); ui.View.RefreshOsuLinkSettings(); ui.Paint();
                Check(ui.View.OsuLinkFallbackRequired && !ui.Canvas.Texts.Any(t => t.Value == "osu!stable"), "unregistered fallback requires setup again");
                applications.Clear(); ui.View.RefreshOsuLinkSettings(refreshApplications: true); ui.Paint();
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("osuLinks.none")), "no available handlers is explicit");
                bound = false; ui.View.RefreshOsuLinkSettings();
                Check(!ui.View.OsuLinkFallbackRequired, "removing FA association stops requiring fallback");
            }
#if WINDOWS
            WindowsRouting();
#endif
        }
        finally { L.SetLanguage(language); }
    }

#if WINDOWS
    private static void WindowsRouting()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "tests", "osu-links")); Directory.CreateDirectory(root);
        string exe = Path.Combine(root, "osu client.exe"); File.WriteAllBytes(exe, []);
        string preferencePath = Path.Combine(root, "preferences.json");
        var preferences = new OsuLinkPreferences { TargetId = "Test.Osu" };
        preferences.Save(preferencePath); preferences = OsuLinkPreferences.Load(preferencePath);
        Check(preferences.TargetId == "Test.Osu", "fallback application identity roundtrip");
        Check(OsuLinkAssociation.TryTarget("\"C:\\osu lazer\\osu!.exe\" --url \"%1\"", out string path, out string[] arguments)
            && path == @"C:\osu lazer\osu!.exe" && arguments.SequenceEqual(["--url", "%1"]), "registered shell command parsing");
        string registryPath = @"Software\FruitsAtelier.Tests\OsuLinks-" + Guid.NewGuid().ToString("N");
        try
        {
            using var hive = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(registryPath);
            using var classes = hive.CreateSubKey("Classes");
            using (var command = classes.CreateSubKey(@"Test.Osu\shell\open\command")) command.SetValue("", "\"" + exe + "\" --url \"%1\"");
            using (var registered = hive.CreateSubKey(@"Software\RegisteredApplications")) registered.SetValue("TestOsu", @"Software\TestOsu\Capabilities");
            using (var capability = hive.CreateSubKey(@"Software\TestOsu\Capabilities")) capability.SetValue("ApplicationName", "osu!test");
            using (var urls = hive.CreateSubKey(@"Software\TestOsu\Capabilities\URLAssociations")) urls.SetValue("osu", "Test.Osu");
            string url = "osu://mp/123/password%20with%20spaces%L";
            var info = OsuLinkAssociation.ForwardInfo(url, preferences, classes);
            Check(!info.UseShellExecute && info.FileName == exe && info.ArgumentList.SequenceEqual(["--url", url]), "direct forwarding preserves URI and registered arguments");
            var apps = OsuLinkApplications.Discover([hive], classes, Path.Combine(root, "icons"));
            Check(apps.Count == 1 && apps[0].Id == "Test.Osu" && apps[0].Name == "osu!test", "registered handlers form the application list");
            using (var command = classes.CreateSubKey(@"Test.Osu\shell\open\command")) command.SetValue("", "\"" + Environment.ProcessPath + "\" \"%1\"");
            bool rejected = false;
            try { OsuLinkAssociation.ForwardInfo(url, preferences, classes); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && OsuLinkApplications.Discover([hive], classes, root).Count == 0, "self forwarding is excluded from the list");
            using (var original = classes.CreateSubKey(@"osu\shell\open\command")) original.SetValue("", "original handler");
            OsuLinkAssociation.Register(classes, @"C:\FA Folder\FruitsAtelier.App.exe");
            using var fa = classes.OpenSubKey(OsuLinkAssociation.ProgId + @"\shell\open\command");
            using var unchanged = classes.OpenSubKey(@"osu\shell\open\command");
            Check(fa?.GetValue("") as string == "\"C:\\FA Folder\\FruitsAtelier.App.exe\" \"%1\""
                && unchanged?.GetValue("") as string == "original handler", "candidate registration preserves existing protocol handlers");
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(registryPath, false); }
        string channel = "FruitsAtelier-osu-link-test-" + Guid.NewGuid().ToString("N");
        using var cancellation = new CancellationTokenSource(); var listener = FazFileAssociation.Listen(cancellation.Token, channel);
        try
        {
            const string link = "osu://edit/00:13:247%20(2,3)";
            Check(FazFileAssociation.Forward(link, channel), "running instance accepts link activation");
            Check(SpinWait.SpinUntil(() => !FazFileAssociation.PendingLinks.IsEmpty, 3000), "activation arrives");
            Check(FazFileAssociation.PendingLinks.TryDequeue(out string? received) && received == link, "activation preserves encoded URL");
        }
        finally { cancellation.Cancel(); Check(listener.Wait(3000), "listener shuts down"); }
    }
#endif
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
