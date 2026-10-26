using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Platform;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class SyncTabsTests
{
    public static void Run()
    {
        var eventReview = EditorView.SplitSyncEvents("//Break Periods\n2,54850,57406\n//Storyboard Layer 0\nSprite,Foreground,Centre,\"sprite.png\",320,240\n F,0,0,500,0,1\nBreak,247104,256536 // end\n2,248656,256611\n2,invalid,4");
        Check(eventReview.Breaks == "54850,57406\n247104,256536\n248656,256611", "breaks are collected across event comments and storyboard commands");
        Check(eventReview.Other == "Sprite,Foreground,Centre,\"sprite.png\",320,240\n F,0,0,500,0,1\n2,invalid,4", "other events retain command order, indentation and malformed break data");
        var id = Guid.NewGuid();
        ConvertedCatchObject Event(int index, CatchObjectKind kind, double time, double x) => new(id, index, kind, time, x, x, x, 0);
        var events = new[] { Event(0, CatchObjectKind.Fruit, 1000, 100), Event(1, CatchObjectKind.TinyDroplet, 1050, 110), Event(2, CatchObjectKind.Droplet, 1100, 120) };
        var changed = new[] { events[0], events[1] with { X = 111.5 }, events[2] with { TimeMs = 1101 } };
        var playable = SyncObjectDifferences.ComparePlayable("", "", events, changed);
        Check(playable.Count == 2 && playable[0].Playable is { XChanged: true, TimeChanged: false, KindChanged: false }
            && playable[1].Playable is { XChanged: false, TimeChanged: true }, "only changed playable attributes are flagged");
        Check(SyncObjectDifferences.ComparePlayable("", "", events, [events[0], events[1] with { X = 111 }, events[2]]).Count == 0, "one pixel tolerance matches slider conflict detection");
        var added = SyncObjectDifferences.ComparePlayable("", "", events, [events[0], Event(3, CatchObjectKind.TinyDroplet, 1025, 105), events[1], events[2]]);
        Check(added.Count == 1 && added[0].Playable is { Local: null, External.TimeMs: 1025 }, "an inserted droplet does not shift later event pairings");
        Check(SyncObjectDifferences.ComparePlayable("100,192,1000,2,0,L|200:192,1,100", "100,192,1000,2,0,B|300:192,1,200", events, events).Count == 0,
            "equivalent events never display raw slider paths or lengths");
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
                string original = "osu file format v14\n[General]\nAudioFilename:music.wav\nMode:2\nSampleSet:Soft\nStackLeniency:0.7\nPreviewTime:-1\n[Metadata]\nVersion:Catch\nArtist:Artist\nTitle:Tabs\n[Difficulty]\nApproachRate:5\nCircleSize:5\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,2,0,100,1,0\n[HitObjects]\n100,192,1000,1,0,0:0:0:0:\n304,192,257090,1,0,0:0:0:0:\n200,192,260000,1,0,0:0:0:0:\n";
                original = original.Replace("[HitObjects]", "[Events]\n0,0,\"bg.png\",0,0\n//Break Periods\n2,54850,57406\n2,247104,250245\n//Storyboard Layer 0\n[HitObjects]");
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
                    File.WriteAllText(source, original.Replace("SampleSet:Soft", "SampleSet:None").Replace("StackLeniency:0.7", "StackLeniency:0.5").Replace("PreviewTime:-1", "PreviewTime:1000")
                        .Replace("2,247104,250245", "2,247104,256536\n2,248656,256611").Replace("257090,1", "257090,5").Replace("200,192,260000", "240,192,260000"));
                    ui.View.RefreshSynchronization(); Wait(ui);
                    var before = ui.View.Document.DeepClone();
                    Check(TabColour("General") == 0xED737B && TabColour("Objects") == 0xED737B && TabColour("Metadata") != 0xED737B, "tabs identify conflicted and clean categories");
                    Check(ui.Canvas.Texts.Any(t => t.Value == "Mode"), "unchanged fixed field is included");
                    Check(!ui.Canvas.Texts.Any(t => t.Value.StartsWith("SampleSet")), "General SampleSet is omitted from synchronization review");
                    Check(Row("PreviewTime") < Row("StackLeniency") && Row("StackLeniency") < Row("Mode"), "General uses fixed field order, not source order");
                    Select("StackLeniency", true); Check(TabColour("General") == 0xD5A34D, "partially resolved tab is amber");
                    Select("PreviewTime", false); Check(TabColour("General") == 0x70D69B, "fully resolved tab is green");
                    ui.ClickText("Metadata"); Check(ui.Canvas.Texts.Any(t => t.Value == "Title") && Row("Title") < Row("Artist"), "clean category shows complete ordered context");
                    ui.ClickText("Events");
                    Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.breakStart")) && ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.breakDuration")), "Events shows readable break columns");
                    Check(ui.Canvas.Texts.Any(t => t.Value == "04:10:245" && t.Color == 0xED737B)
                        && ui.Canvas.Texts.Any(t => t.Value == "04:07:104" && t.Color != 0xED737B), "break end changes highlight only changed cells");
                    Check(ui.Canvas.Texts.Any(t => t.Value == "04:08:656" && t.Color == 0xED737B), "added break is shown in its aligned row");
                    Check(!ui.Canvas.Texts.Any(t => t.Value.Contains("//Break") || t.Value.StartsWith("2,247104")), "Events hides raw breaks and empty comment headings");
                    CheckBackground();
                    ClickBreak("00:54:850", true);
                    Check(TabColour("Events") == 0xED737B, "unchanged break rows do not resolve Events");
                    ClickBreak("04:07:104", false);
                    Check(TabColour("Events") == 0xD5A34D, "choosing one break leaves other breaks unresolved");
                    Check(ui.Canvas.Outlines.Any(o => o.Color == 0x59D3C3 && o.Bounds.X == 42 && o.Bounds.Y == 257 && o.Bounds.Height == 23), "FA selection outlines only its break row");
                    Check(!ui.Canvas.Outlines.Any(o => o.Color == 0x59D3C3 && o.Bounds.Y == 202 && o.Bounds.Height > 23), "break choices do not outline an entire Events pane");
                    ClickBreak("04:16:536", true);
                    Check(ui.Canvas.Outlines.Any(o => o.Color == 0x59D3C3 && o.Bounds.X > width / 2 && o.Bounds.Y == 257 && o.Bounds.Height == 23), "end time click switches only that break to osu");
                    ClickBreak("3141", false);
                    ClickBreak("7955", true);
                    Check(TabColour("Events") == 0x70D69B, "duration and added-row clicks can revisit a resolved choice");
                    CheckBackground();
                    ui.ClickText("Timing"); ui.ClickText("Objects");
                    Check(ui.Canvas.Texts.Any(t => t.Value.Contains("04:17:090") && t.Value.Contains(L.Get("sync.diff.on"))), "object group explains the New Combo edit");
                    Check(ui.Canvas.Texts.Any(t => System.Text.RegularExpressions.Regex.IsMatch(t.Value, @"^\d{2,}:\d{2}:\d{3}$")), "canvas axis uses three-digit milliseconds");
                    ClickRange(false); Check(TabColour("Objects") == 0xD5A34D, "left rectangle chooses FA and advances to next group");
                    Check(ui.Canvas.Texts.Any(t => t.Value == "200") && ui.Canvas.Texts.Any(t => t.Value == "240"), "next group explains X change");
                    Check(ui.Canvas.Texts.Any(t => t.Value == "200" && t.Color == 0xED737B)
                        && ui.Canvas.Texts.Any(t => t.Value == "260000" && t.Color != 0xED737B), "only the changed X attribute is red");
                    ClickRange(true); Check(TabColour("Objects") == 0x70D69B, "right rectangle chooses osu and resolves category");
                    Check(ui.Canvas.Texts.Any(t => t.Value == "240" && t.Color == 0x70D69B), "resolved X attribute is green");
                    ClickRange(false); Check(TabColour("Objects") == 0x70D69B, "resolved rectangle can switch its choice back to FA");
                    Check(before.ContentEquals(ui.View.Document), "tabs, scrolling and choices do not apply edits early");
                    var previousVersions = WorkspaceVersionHistory.List(ui.View.WorkspaceSession!).Select(v => v.Path).ToHashSet();
                    ui.ClickText(L.Get("sync.applyChoices")); Wait(ui);
                    Check(!ui.View.SynchronizationVisible && OsuBeatmapReader.Setting(ui.View.Document, "General", "SampleSet") == "Soft"
                        && OsuBeatmapReader.Setting(ui.View.Document, "General", "StackLeniency") == "0.5"
                        && OsuTimeline.Breaks(ui.View.Document).Any(b => b.StartMs == 247104 && b.EndMs == 250245)
                        && OsuTimeline.Breaks(ui.View.Document).Any(b => b.StartMs == 248656 && b.EndMs == 256611)
                        && ui.View.Document.Fruits.Single(f => f.TimeMs == 260000).X == 200, "mixed category choices apply correctly");
                    Check(audioReloads == 0 && ui.View.AudioReady && ui.View.PlayheadMs == position && ui.View.ViewStartMs == viewStart,
                        $"mixed object resolution retains unchanged audio and nonzero transport position: reloads={audioReloads}, ready={ui.View.AudioReady}, position={position}/{ui.View.PlayheadMs}, view={viewStart}/{ui.View.ViewStartMs}");
                    var archived = WorkspaceVersionHistory.List(ui.View.WorkspaceSession!).Where(v => !previousVersions.Contains(v.Path)).ToArray();
                    Check(archived.Length == 2 && archived.All(v => v.Operation == "resolution") && archived.Any(v => v.WorkingCopy),
                        "Apply keeps saved and working snapshots in one recovery round");
                    File.WriteAllText(source, File.ReadAllText(source).Replace("bg.png", "changed.png").Replace("248656,256611", "248656,256612"));
                    ui.View.RefreshSynchronization(); Wait(ui); ui.ClickText("Events");
                    var beforeOtherChoice = ui.View.Document.DeepClone();
                    ClickBreak("0,0,\"changed.png\",0,0", true);
                    Check(TabColour("Events") == 0xD5A34D && ui.Canvas.Texts.Any(t => t.Value == "04:16:612" && t.Color == 0xED737B),
                        "choosing the other event block leaves a changed break unresolved");
                    Check(beforeOtherChoice.ContentEquals(ui.View.Document), "individual event choices do not edit content before Apply");
                    ui.View.KeyDown(27, false, false);
                    uint TabColour(string name) => ui.Canvas.Texts.Single(t => t.Value == name && t.Y == 90).Color;
                    void ClickBreak(string value, bool external)
                    {
                        var cell = ui.Canvas.Texts.First(t => t.Value == value && (t.X > width / 2) == external);
                        ui.Click(cell.X + 2, cell.Y + 8);
                    }
                    void CheckBackground()
                    {
                        var backgrounds = ui.Canvas.Texts.Where(t => t.Value == "0,0,\"bg.png\",0,0").ToArray();
                        Check(backgrounds.Length == 2 && backgrounds.All(t => t.Color != 0xED737B && t.Color != 0x70D69B)
                            && backgrounds.All(t => !ui.Canvas.Outlines.Any(o => o.Bounds.Contains(t.X, t.Y) && o.Color is 0xED737B or 0x70D69B)),
                            "unchanged backgrounds retain neutral text and borders before and after resolving breaks");
                    }
                    float Row(string key) => ui.Canvas.Texts.Single(t => t.X == 36 && (t.Value == key || t.Value.StartsWith(key + " · "))).Y;
                    void Select(string key, bool external) { float y = Row(key); ui.Click(external ? width / 2 + 40 : 60, y + 40); }
                    void ClickRange(bool external)
                    {
                        var area = ui.Canvas.Outlines.First(o => o.Bounds.Y > 230 && o.Bounds.Width > 100
                            && o.Color is 0xED737B or 0x70D69B or 0x9AA8BC && (o.Bounds.X > width / 2) == external).Bounds;
                        ui.Click(area.X + 10, Math.Clamp(area.Y + area.Height / 2, 292, 690));
                    }
                }
                finally { ui.View.StopFileMonitoring(); }
            }
        }
        finally { L.SetLanguage(previous); }
    }
    private static void Wait(Ui ui)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        do { ui.Paint(); if (!ui.View.SynchronizationBusy && !ui.View.LibraryOpening) return; Thread.Sleep(10); } while (DateTime.UtcNow < deadline);
        throw new Exception("Sync tab fixture timed out.");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
