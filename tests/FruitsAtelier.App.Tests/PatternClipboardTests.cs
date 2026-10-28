using System.Diagnostics;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using FruitsAtelier.Core.Patterns;
using L = FruitsAtelier.Localization.Strings;

internal static class PatternClipboardTests
{
    internal static void Run()
    {
        foreach (string language in new[] { "en", "zh-CN" }) RunLanguage(language);
        CompactSizeAndCompatibility();
        L.SetLanguage("en");
    }

    private static void RunLanguage(string language)
    {
        L.SetLanguage(language);
        string directory = Path.GetFullPath(Path.Combine("artifacts/tests/pattern-clipboard", Guid.NewGuid().ToString("N")));
        var source = new MapDocument { IsDemo = false, DurationMs = 10000, BeatLengthMs = 500 };
        source.Fruits.Add(new() { TimeMs = 1000, X = 120, SourceOrder = 0 });
        source.Fruits.Add(new() { TimeMs = 1500, X = 320, SourceOrder = 1 });
        var pattern = PatternSnapshot.Capture(source, source.Fruits.Select(f => f.Id), "测试 Pattern：Name");
        var store = new PatternGalleryStore(directory); store.Add(pattern);
        var ui = new Ui(); ui.LoadDocument(new MapDocument { IsDemo = false, DurationMs = 10000, BeatLengthMs = 400 });
        ui.View.ConfigurePatternGallery(directory);
        ui.ClickText(L.Get("dock.layout")); ui.ClickText("✓ " + L.Get("dock.gallery"));
        ui.ClickText(L.Get("dock.layout")); ui.ClickText(L.Get("dock.gallery")); Settle(ui);
        string copied = "";
        ui.View.RequestCopyText = text => copied = text;
        Click(ui.View.PatternGalleryCardBounds(pattern.Id));
        ui.Key('C', ctrl: true);
        Check(copied.StartsWith(pattern.Name + "："), "Copy lost the readable name");
        string payload = copied[(copied.LastIndexOf("：", StringComparison.Ordinal) + 1)..];
        Check(payload.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '='), "Content must be pure visible Base64");
        var decoded = PatternClipboard.Decode(copied);
        Check(decoded.HasSameGeometry(pattern) && decoded.Tags.Count == 0 && decoded.Source.Artist == "", "Clipboard round trip lost geometry or retained metadata");
        foreach (string invalid in new[] { copied[..^2], copied.Replace(payload, "!"), copied.Replace(payload, payload[..4] + "\n" + payload[4..]) })
        {
            try { PatternClipboard.Decode(invalid); throw new Exception("Invalid pattern text was accepted"); }
            catch (InvalidDataException) { }
        }
        ui.View.RequestPasteObjects = () => ui.View.PasteClipboardText(copied);
        ui.View.UpdateTransport(2250, 10000, true, false, false, null, "fixture.wav");
        ui.Key('V', ctrl: true); Settle(ui);
        Check(ui.View.Document.Fruits.Count == 2, "Gallery paste did not insert pattern objects");
        Near(2250, ui.View.Document.Fruits[0].TimeMs); Near(2650, ui.View.Document.Fruits[1].TimeMs);
        Near(120, ui.View.Document.Fruits[0].X); Near(320, ui.View.Document.Fruits[1].X);
        Check(ui.View.Document.Fruits.All(f => source.Fruits.All(s => s.Id != f.Id)), "Paste reused IDs");
        Check(ui.View.SelectedObjectIds.ToHashSet().SetEquals(ui.View.Document.Fruits.Select(f => f.Id)), "Pasted objects were not all selected");
        ui.Key('Z', ctrl: true); Check(ui.View.Document.Fruits.Count == 0, "Paste was not one undo step");
        ui.Key('Y', ctrl: true); Check(ui.View.Document.Fruits.Count == 2, "Paste redo failed");

        var other = new Ui(); other.LoadDocument(new MapDocument { IsDemo = false, DurationMs = 10000, BeatLengthMs = 250 });
        other.View.ConfigurePatternGallery(directory);
        other.View.PasteClipboardText(copied); Settle(other);
        Check(other.View.Document.Fruits.Count == 2 && store.Load().Count == 1, "Another editor did not reuse a matching local pattern");
        Near(250, other.View.Document.Fruits[1].TimeMs - other.View.Document.Fruits[0].TimeMs);

        string incomingDirectory = Path.Combine(directory, "incoming");
        var receiver = new Ui(); receiver.LoadDocument(new MapDocument { IsDemo = false, DurationMs = 10000 });
        receiver.View.ConfigurePatternGallery(incomingDirectory);
        receiver.View.PasteClipboardText(copied); Settle(receiver);
        Check(receiver.View.Document.Fruits.Count == 0 && receiver.Canvas.Texts.Any(t => t.Value == L.Get("gallery.importNewFolders")),
            "Unknown pattern must enter import without editing the map");
        receiver.ClickText(L.Get("gallery.importNewFolders")); Settle(receiver);
        Check(new PatternGalleryStore(incomingDirectory).Load().Single().HasSameGeometry(pattern), "Text import did not persist pattern");
        receiver.View.PasteClipboardText(copied); Settle(receiver);
        Check(receiver.View.Document.Fruits.Count == 2, "Imported pattern did not become pasteable");
        receiver.View.PasteClipboardText(copied.Replace(payload, "!")); Settle(receiver);
        Check(receiver.View.Document.Fruits.Count == 2, "Malformed text changed the map");

        var changed = pattern.CreateDocument(); changed.Fruits[0].X += 10;
        string changedText = PatternClipboard.Encode(pattern.WithDocument(changed));
        receiver.View.PasteClipboardText(changedText); Settle(receiver);
        Check(receiver.View.Document.Fruits.Count == 2 && receiver.Canvas.Texts.Any(t => t.Value == L.Get("gallery.importNewFolders")),
            "Changed geometry must enter import");
        receiver.ClickText(L.Get("gallery.importExistingFolders")); receiver.ClickObjectAction(PatternFolder.DefaultName); Settle(receiver);
        Check(new PatternGalleryStore(incomingDirectory).Load().Count == 2, "Changed content import overwrote the existing pattern");
        receiver.View.PasteClipboardText(changedText); Settle(receiver);
        Check(receiver.View.Document.Fruits.Count == 4, "Imported content with a remapped ID entered import again");

        var retiring = new Ui(); retiring.LoadDocument(new MapDocument { IsDemo = false, DurationMs = 10000 });
        retiring.View.ConfigurePatternGallery(directory);
        retiring.View.PasteClipboardText(copied);
        retiring.LoadDocument(new MapDocument { IsDemo = false, DurationMs = 10000 }); Settle(retiring);
        Check(retiring.View.Document.Fruits.Count == 0, "Pending paste escaped its project session");

        var editing = new Ui(); editing.LoadDocument(source); editing.View.ConfigurePatternGallery(directory);
        Check(editing.View.BeginPatternEditing(pattern), "Could not open pattern editing fixture"); editing.Paint();
        editing.ClickMap(1000, 120);
        Check(editing.View.CopySelection(), "Pattern editing selection could not be copied");
        editing.Key('V', ctrl: true);
        Check(editing.View.Document.Fruits.Count == 3, "Ordinary object paste stopped working inside pattern editing");
        editing.View.DiscardPatternEditing();

        void Click(Rect rect) { ui.View.PointerDown(rect.X + 10, rect.Y + 10, 0, false, false); ui.View.PointerUp(rect.X + 10, rect.Y + 10, 0); ui.Paint(); }
    }

    private static void CompactSizeAndCompatibility()
    {
        L.SetLanguage("en");
        var map = new MapDocument { Name = "Pattern source", IsDemo = false, DurationMs = 180000, BeatLengthMs = 400 };
        var metadata = new OsuSection { Name = "Metadata" };
        metadata.Lines.AddRange(["Artist:Example Artist", "Title:Example Song", "Creator:Mapper", "Version:Expert"]);
        metadata.Lines.Add("Tags:" + string.Join(" ", Enumerable.Range(0, 200).Select(_ => Guid.NewGuid().ToString("N"))));
        map.OriginalSections.Add(metadata);
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 400 });
        for (int i = 1; i <= 500; i++)
        {
            map.TimingPoints.Add(new() { TimeMs = i * 100, BeatLengthMs = -100, Uninherited = false, SourceOrder = i });
            map.TimingPoints.Add(new() { TimeMs = 100000 + i * 100, BeatLengthMs = -100, Uninherited = false, SourceOrder = i + 500 });
        }
        for (int i = 0; i < 20; i++)
        {
            int time = 60000 + i * 100, x = 128 + (int)(80 * Math.Sin(i * .4));
            map.Fruits.Add(new() { TimeMs = time, X = x, SourceOrder = 100 + i, OriginalLine = $"{x},192,{time},1,0,0:0:0:0:" });
        }
        var pattern = PatternSnapshot.Capture(map, map.Fruits.Select(f => f.Id), "BBKKBKK");
        string text = PatternClipboard.Encode(pattern);
        using var legacy = new MemoryStream(); PatternArchive.Write([pattern], legacy);
        string oldText = "【" + pattern.Name + "】：【" + Convert.ToBase64String(legacy.ToArray()) + "】";
        Console.WriteLine($"20-fruit clipboard: {text.Length} characters (archive: {oldText.Length}).");
        Check(text.Length <= 100, "A simple 20-fruit pattern exceeded 100 characters");
        Check(PatternClipboard.Decode(oldText).HasSameGeometry(pattern), "Previous archive clipboard is no longer readable");
        L.SetLanguage("zh-CN");
        var restored = PatternClipboard.Decode(text);
        Check(restored.HasSameGeometry(pattern), "Language switching changed decoded authoring data");
        Check(restored.Source.Artist == "" && restored.Tags.Count == 0 && restored.CreateDocument().OriginalSections.Count == 0,
            "Geometry sharing retained source metadata or tags");
        Check(restored.CreateDocument().TimingPoints.Count == 0, "Geometry sharing retained unrelated timing");
        var unusual = pattern.CreateDocument();
        unusual.Fruits[0].X = 120.25; unusual.Fruits[0].Y = 64;
        unusual.Fruits[0].OriginalLine = "00120,064,001000,5,2,2:3:4:70:custom.wav";
        var unusualCopy = PatternClipboard.Decode(PatternClipboard.Encode(pattern.WithDocument(unusual))).CreateDocument();
        Check(unusualCopy.Fruits[0].OriginalLine is null && unusualCopy.Fruits[0].X == 120.25,
            "Geometry sharing changed fractional X or retained source samples");
        var shortMap = new MapDocument { BeatLengthMs = 500, IsDemo = false, DurationMs = 5000 };
        var first = new CurveTrack { Kind = CurveKind.Linear, SourceOrder = 0 };
        first.Nodes.AddRange([new() { TimeMs = 1000, X = 192 }, new() { TimeMs = 1250, X = 320 }]);
        var second = new CurveTrack { Kind = CurveKind.Linear, SourceOrder = 2 };
        second.Nodes.AddRange([new() { TimeMs = 1750, X = 320 }, new() { TimeMs = 2000, X = 192 }]);
        shortMap.Tracks.AddRange([first, second]); shortMap.Fruits.Add(new() { TimeMs = 1500, X = 256, SourceOrder = 1 });
        var shortPattern = PatternSnapshot.Capture(shortMap, new[] { first.Id, shortMap.Fruits[0].Id, second.Id }, "Some hyper")
            .WithTags([new("JEMZU", 0x58D8C1), new("qweqw", 0xD84B71)]);
        string shortText = PatternClipboard.Encode(shortPattern);
        Console.WriteLine($"Two sliders and one fruit: {shortText.Length} characters: {shortText}");
        Check(shortText.Length < 100 && PatternClipboard.Decode(shortText).HasSameGeometry(shortPattern), "Short mixed pattern exceeds 100 characters or changed shape");
        Check(PatternClipboard.Encode(shortPattern.WithTags([])) == shortText, "Tags changed geometry text");
        var shifted = shortMap.DeepClone(); shifted.BeatLengthMs = 250; shifted.DurationMs = 20000;
        foreach (var fruit in shifted.Fruits) fruit.TimeMs = 8000 + (fruit.TimeMs - 1000) / 2;
        foreach (var track in shifted.Tracks)
            foreach (var node in track.Nodes) node.TimeMs = 8000 + (node.TimeMs - 1000) / 2;
        Check(shortPattern.HasSameGeometry(shortPattern.WithDocument(shifted)), "Source offset or BPM changed beat-relative geometry identity");
        Check(PatternClipboard.Decode(shortText.Replace("：", ":")).HasSameGeometry(shortPattern), "ASCII colon sharing did not decode");
        string shortPayload = shortText[(shortText.LastIndexOf('：') + 1)..];
        Check(PatternClipboard.Decode("[Some hyper]：[" + shortPayload + "]").HasSameGeometry(shortPattern), "Bracketed text compatibility failed");
        var target = new MapDocument { IsDemo = false, DurationMs = 10000, BeatLengthMs = 500 };
        var originalPlacement = PatternPlacement.Prepare(pattern, target, 1000).CreateDocument();
        var restoredPlacement = PatternPlacement.Prepare(restored, target, 1000).CreateDocument();
        Check(originalPlacement.Fruits.Select(f => (f.TimeMs, f.X)).SequenceEqual(
            restoredPlacement.Fruits.Select(f => (f.TimeMs, f.X))), "Compact encoding changed placed fruits");
    }

    private static void Settle(Ui ui)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            ui.Paint();
            if (timer.Elapsed.TotalSeconds > 20) throw new Exception("Pattern clipboard timed out: " + ui.View.StatusMessage);
            Thread.Sleep(1);
        } while (ui.View.PatternGalleryNeedsRedraw);
    }
    private static void Near(double expected, double actual) => Check(Math.Abs(expected - actual) < .001, $"Expected {expected}, got {actual}");
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
