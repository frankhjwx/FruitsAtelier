using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Platform;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class SyncTabsTests
{
    public static void Run()
    {
        Check(SyncObjectDifferences.FormatTime(257090) == "04:17:090" && SyncObjectDifferences.FormatTime(59999.9) == "01:00:000", "millisecond clock format and carry");
        var combo = SyncObjectDifferences.Compare("304,192,257090,1,0,0:0:0:0:", "304,192,257090,5,0,0:0:0:0:");
        Check(combo.Count == 1 && combo[0].Message == "sync.diff.combo", "combo-only difference is explicit");
        Check(SyncObjectDifferences.Compare("", "304,192,257090,1,0,0:0:0:0:")[0].Message == "sync.diff.externalOnly", "addition is explicit");
        Check(SyncObjectDifferences.Compare("304,192,257090,1,0,0:0:0:0:", "")[0].Message == "sync.diff.localOnly", "deletion is explicit");
        var slider = SyncObjectDifferences.Compare("100,192,1000,2,0,L|200:192,1,100", "100,192,1000,2,0,L|300:192,2,200");
        Check(slider.Select(d => d.Message).Contains("sync.diff.path") && slider.Any(d => d.Message == "sync.diff.repeats") && slider.Any(d => d.Message == "sync.diff.length"), "slider differences retain parameter names");
        string previous = L.Language;
        try
        {
            foreach (string language in new[] { "en", "zh-CN" })
            foreach (int width in new[] { 980, 1440 })
            {
                L.SetLanguage(language);
                string root = Path.GetFullPath(Path.Combine("artifacts/tests/sync-tabs", Guid.NewGuid().ToString("N")));
                string songs = Path.Combine(root, "Songs"), source = Path.Combine(songs, "set", "map.osu");
                Directory.CreateDirectory(Path.GetDirectoryName(source)!);
                string original = "osu file format v14\n[General]\nAudioFilename:music.wav\nMode:2\nSampleSet:Soft\nPreviewTime:-1\n[Metadata]\nVersion:Catch\nArtist:Artist\nTitle:Tabs\n[Difficulty]\nApproachRate:5\nCircleSize:5\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,2,0,100,1,0\n[HitObjects]\n100,192,1000,1,0,0:0:0:0:\n304,192,257090,1,0,0:0:0:0:\n200,192,260000,1,0,0:0:0:0:\n";
                File.WriteAllText(source, original);
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(source)!, "music.wav"), [1, 2, 3, 4]);
                var ui = new Ui(false); ui.Resize(width, 900);
                ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); ui.View.LibrarySettings.Songs = songs;
                ui.View.LoadWorkspace(LibraryOperations.ImportPath(source, ui.View.LibrarySettings)); Wait(ui);
                int audioReloads = 0;
                ui.View.RequestDifficultyChanged = () => audioReloads++;
                ui.View.UpdateTransport(0, 300000, true, false, false, null, ui.View.Document.AudioPath);
                ui.View.UpdateTransport(9000, 300000, true, false, false, null, ui.View.Document.AudioPath);
                ui.Paint();
                double position = ui.View.PlayheadMs, viewStart = ui.View.ViewStartMs;
                try
                {
                    File.WriteAllText(source, original.Replace("SampleSet:Soft", "SampleSet:None").Replace("PreviewTime:-1", "PreviewTime:1000")
                        .Replace("257090,1", "257090,5").Replace("200,192,260000", "240,192,260000"));
                    ui.View.RefreshSynchronization(); Wait(ui);
                    var before = ui.View.Document.DeepClone();
                    Check(TabColour("General") == 0xED737B && TabColour("Objects") == 0xED737B && TabColour("Metadata") != 0xED737B, "tabs identify conflicted and clean categories");
                    Check(ui.Canvas.Texts.Any(t => t.Value == "Mode"), "unchanged fixed field is included");
                    Check(Row("PreviewTime") < Row("SampleSet") && Row("SampleSet") < Row("Mode"), "General uses fixed field order, not source order");
                    Select("SampleSet", true); Check(TabColour("General") == 0xD5A34D, "partially resolved tab is amber");
                    Select("PreviewTime", false); Check(TabColour("General") == 0x70D69B, "fully resolved tab is green");
                    ui.ClickText("Metadata"); Check(ui.Canvas.Texts.Any(t => t.Value == "Title") && Row("Title") < Row("Artist"), "clean category shows complete ordered context");
                    ui.ClickText("Events"); ui.ClickText("Timing"); ui.ClickText("Objects");
                    Check(ui.Canvas.Texts.Any(t => t.Value.Contains("04:17:090") && t.Value.Contains(L.Get("sync.diff.on"))), "object group explains the New Combo edit");
                    Check(ui.Canvas.Texts.Any(t => System.Text.RegularExpressions.Regex.IsMatch(t.Value, @"^\d{2,}:\d{2}:\d{3}$")), "canvas axis uses three-digit milliseconds");
                    ui.ClickText(L.Get("sync.chooseLocal")); Check(TabColour("Objects") == 0xD5A34D, "object group choice makes category partially resolved");
                    Check(ui.Canvas.Texts.Any(t => t.Value.Contains("200") && t.Value.Contains("240")), "next group explains X change");
                    ui.ClickText(L.Get("sync.chooseLocal")); Check(TabColour("Objects") == 0x70D69B, "all object groups resolved");
                    Check(before.ContentEquals(ui.View.Document), "tabs, scrolling and choices do not apply edits early");
                    var previousVersions = WorkspaceVersionHistory.List(ui.View.WorkspaceSession!).Select(v => v.Path).ToHashSet();
                    ui.ClickText(L.Get("sync.applyChoices")); Wait(ui);
                    Check(!ui.View.SynchronizationVisible && OsuBeatmapReader.Setting(ui.View.Document, "General", "SampleSet") == "None"
                        && ui.View.Document.Fruits.Single(f => f.TimeMs == 260000).X == 200, "mixed category choices apply correctly");
                    Check(audioReloads == 0 && ui.View.AudioReady && ui.View.PlayheadMs == position && ui.View.ViewStartMs == viewStart,
                        $"mixed object resolution retains unchanged audio and nonzero transport position: reloads={audioReloads}, ready={ui.View.AudioReady}, position={position}/{ui.View.PlayheadMs}, view={viewStart}/{ui.View.ViewStartMs}");
                    var archived = WorkspaceVersionHistory.List(ui.View.WorkspaceSession!).Where(v => !previousVersions.Contains(v.Path)).ToArray();
                    Check(archived.Length == 2 && archived.All(v => v.Operation == "resolution") && archived.Any(v => v.WorkingCopy),
                        "Apply keeps saved and working snapshots in one recovery round");
                    uint TabColour(string name) => ui.Canvas.Texts.Single(t => t.Value == name && t.Y == 90).Color;
                    float Row(string key) => ui.Canvas.Texts.Single(t => t.X == 36 && (t.Value == key || t.Value.StartsWith(key + " · "))).Y;
                    void Select(string key, bool external) { float y = Row(key); ui.Click(external ? width / 2 + 40 : 60, y + 40); }
                }
                finally { ui.View.StopFileMonitoring(); }
            }
        }
        finally { L.SetLanguage(previous); }
    }
    private static void Wait(Ui ui)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        do { ui.Paint(); if (!ui.View.SynchronizationBusy) return; Thread.Sleep(10); } while (DateTime.UtcNow < deadline);
        throw new Exception("Sync tab fixture timed out.");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
