using FruitsAtelier.Core;
using FruitsAtelier.Core.Patterns;
using L = FruitsAtelier.Localization.Strings;

internal static class PatternGalleryTagPreviewTests
{
    internal static void Run()
    {
        string directory = Path.GetFullPath(Path.Combine("artifacts/tests/pattern-gallery-tag-labels", Guid.NewGuid().ToString("N")));
        var store = new PatternGalleryStore(directory);
        var source = new MapDocument { IsDemo = false, DurationMs = 5000 };
        source.Fruits.Add(new() { TimeMs = 1000, X = 256 });
        var tagged = PatternSnapshot.Capture(source, source.Fruits.Select(f => f.Id), "00 Tagged").WithTags([
            new("Alpha", 0x58D8C1), new("Beta", 0x65A9FF), new("Corner", 0xB58DFF),
            new("Descending pattern", 0xFFB45C), new("Extended jump shape", 0xED8DBB)]);
        var untagged = PatternSnapshot.Capture(source, source.Fruits.Select(f => f.Id), "01 Untagged");
        var many = PatternSnapshot.Capture(source, source.Fruits.Select(f => f.Id), "02 Many tags")
            .WithTags(Enumerable.Range(0, 64).Select(i => new PatternTag(i == 63 ? "Tag63 " + new string('\u754c', 58) : $"Tag{i:00} with a long descriptive name", 0x58D8C1)));
        var full = PatternSnapshot.Capture(source, source.Fruits.Select(f => f.Id), "03 Full label")
            .WithTags([new("Comfortable full tag", 0x58D8C1)]);
        store.Add(tagged); store.Add(untagged); store.Add(many); store.Add(full);
        var ui = new Ui();
        ui.LoadDocument(source.DeepClone()); ui.View.ConfigurePatternGallery(directory);
        ui.ClickText(L.Get("dock.layout")); ui.ClickText("✓ " + L.Get("dock.gallery"));
        ui.ClickText(L.Get("dock.layout")); ui.ClickText(L.Get("dock.gallery")); Settle();
        var card = ui.View.PatternGalleryCardBounds(tagged.Id);
        var title = ui.Canvas.Texts.Single(t => t.Value == tagged.Name);
        var chipLabels = ui.Canvas.Texts.Where(t => card.Contains(t.X, t.Y) && t.Y < title.Y && t.Y > card.Bottom - 50).ToArray();
        Check(chipLabels.Select(t => t.Value).SequenceEqual(["Alpha", "Beta", "+3"]), "Card tags do not reserve space for an accurate +N overflow chip");
        Check(chipLabels.All(t => t.Y == chipLabels[0].Y), "Card tags wrap into multiple rows");
        var emptyCard = ui.View.PatternGalleryCardBounds(untagged.Id);
        Check(card.Width == emptyCard.Width && card.Height == emptyCard.Height, "Tag rows resize gallery cards");

        Open(tagged);
        var bounds = ui.View.PatternGalleryPreviewTagsBounds;
        var popupTitle = ui.Canvas.Texts.Last(t => t.Value == tagged.Name);
        Check(new[] { "Alpha", "Beta", "Corne…", "Desce…", "Exten…" }.All(label => ui.Canvas.Texts.Any(t => t.Value == label && bounds.Contains(t.X, t.Y))),
            "Expanded preview omits tags shown as +N on the card");
        Check(ui.Canvas.Texts.Where(t => bounds.Contains(t.X, t.Y)).Select(t => t.Y).Distinct().Count() == 1
            && bounds.Y > popupTitle.Y && bounds.Bottom < ui.View.PatternGalleryPreviewBounds.Y, "Shortened expanded tags do not fit between the title and graphic preview");
        Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("gallery.scrollHint")), "Expanded tags did not replace the scroll/zoom hint");
        int builds = ui.View.PatternGalleryPreviewBuildCount;
        ui.Paint(); ui.Paint();
        Check(builds == ui.View.PatternGalleryPreviewBuildCount, "Tag labels rebuild preview geometry on idle paint");
        ui.Key(27); Open(many);
        bounds = ui.View.PatternGalleryPreviewTagsBounds;
        Check(ui.View.PatternGalleryPreviewTagsMaximum > 0 && ui.View.PatternGalleryPreviewBounds.Height > 0,
            "A large tag list consumes the entire graphic preview");
        double before = ui.View.PatternGalleryPreviewScroll;
        ui.View.Wheel(bounds.X + 10, bounds.Y + 10, -120000, false, false, false); ui.Paint();
        string visibleTags = string.Concat(ui.Canvas.Texts.Where(t => bounds.Contains(t.X, t.Y)).OrderBy(t => t.Y).ThenBy(t => t.X).Select(t => t.Value));
        Check(visibleTags.Contains("Tag63…", StringComparison.Ordinal), "Final shortened tag is missing from the expanded preview");
        Check(ui.View.PatternGalleryPreviewScroll == before && !ui.View.IsDirty, "Scrolling tags changed the graphic viewport or map content");

        ui.Key(27); Open(full);
        bounds = ui.View.PatternGalleryPreviewTagsBounds;
        Check(ui.Canvas.Texts.Any(t => t.Value == "Comfortable full tag" && bounds.Contains(t.X, t.Y)),
            "A tag row that fits unnecessarily abbreviates a long name");

        store.ReorderTags(store.LoadContents().Tags.Select(t => t.Name).Reverse());
        ui.Key(27); ui.View.ConfigurePatternGallery(directory); ui.Paint(); Settle(); Open(tagged);
        bounds = ui.View.PatternGalleryPreviewTagsBounds;
        Check(ui.Canvas.Texts.Where(t => bounds.Contains(t.X, t.Y)).Select(t => t.Value)
            .SequenceEqual(["Exten…", "Desce…", "Corne…", "Beta", "Alpha"]),
            "Expanded preview did not follow the persisted catalog order");

        void Open(PatternSnapshot pattern)
        {
            var box = ui.View.PatternGalleryCardBounds(pattern.Id);
            ui.Click(box.X + 20, box.Y + 20); Settle();
        }
        void Settle()
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            do { ui.Paint(); if (!ui.View.PatternGalleryNeedsRedraw) return; Thread.Sleep(5); } while (DateTime.UtcNow < deadline);
            throw new Exception("Gallery tag labels did not settle: " + ui.View.StatusMessage);
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
