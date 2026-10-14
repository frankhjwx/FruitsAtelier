using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private int patternDockKind;
    private bool patternDockConvert, patternDockSliders, patternDockBananas, patternDockExisting;
    private Guid[] patternDockTargets = [];
    private MapDocument? patternDockSnapshot;
    private Rect patternDockBounds;
    private float patternDockScroll;
    private CurveTrack[] patternPreviewTracks = [];
    private IReadOnlyList<ConvertedCatchObject> patternCanvasPreview = [];
    private bool PatternCanvasPreviewActive
    {
        get
        {
            if (!PatternDockEditing || !ReferenceEquals(patternDockSnapshot, convertedSnapshot)) return false;
            var selected = ClipboardSelectedParentIds();
            return patternDockTargets.Length == selected.Count && patternDockTargets.All(selected.Contains)
                && (patternDockKind == 1 ? streamError.Length == 0 : BananaPreviewReady && bananaEditError.Length == 0);
        }
    }

    internal IEnumerable<ConvertedCatchObject> PatternCanvasObjects(double start, double end)
    {
        bool preview = PatternCanvasPreviewActive;
        if (!preview || patternDockKind == 1)
            foreach (var item in ObjectsInTimeRange(start, end))
                if (!preview || !patternDockTargets.Contains(item.SourceId)) yield return item;
        if (!preview) yield break;
        int low = 0, high = patternCanvasPreview.Count;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (patternCanvasPreview[mid].TimeMs < start) low = mid + 1; else high = mid;
        }
        for (int i = low; i < patternCanvasPreview.Count && patternCanvasPreview[i].TimeMs <= end; i++)
            yield return patternCanvasPreview[i];
    }

    private void RefreshPatternCanvasPreview()
    {
        var objects = new List<ConvertedCatchObject>();
        foreach (var track in patternPreviewTracks)
        {
            if (track.Id == stackPreviewSource?.Id)
            {
                objects.AddRange(stackPreview);
                continue;
            }
            track.StreamSnapDivisor = StreamSnapDivisor;
            track.Stack = stackMode ? stackDraft : null;
            objects.AddRange(SliderFruitStream.Convert(Document, track, stackTiming!));
        }
        patternCanvasPreview = objects.OrderBy(o => o.TimeMs).ToArray();
    }
    private float PatternDockContentHeight => (patternDockKind == 2 ? patternDockBounds.Width < 260 ? 576 : 490 : 440)
        + (patternDockExisting ? 40 : 0);
    private bool PatternDockEditing => DockContentInputAvailable && !notesLocked && PanelVisible(DockPanel.PatternEditor) && patternDockKind != 0
        && !StreamDialogVisible && !BananaEditVisible && !TimingPageVisible;
    private void ResetPatternDock()
    {
        livePatternConvert = false; livePatternSnapshot = null;
        patternDockKind = 0; patternDockConvert = false; patternDockSnapshot = null;
        patternPreviewTracks = []; patternCanvasPreview = [];
        stackNumericField = bananaEditField = bananaEditDropdown = -1;
        streamSnapDragging = false; CancelStackDrag();
        CloseBananaEdit();
    }
    private void DrawPatternDock(ICanvas c, Rect bounds)
    {
        patternDockBounds = bounds;
        if (StreamDialogVisible || BananaEditVisible)
        {
            patternDockKind = 0; patternDockSnapshot = null;
            return;
        }
        stackGraph = StackPreviewBounds = StreamSnapBounds = StackPercentFieldBounds = StackDistanceFieldBounds = BananaPreviewBounds = default;
        Array.Clear(BananaNumeratorBounds);
        var selected = ClipboardSelectedParentIds();
        AdoptLiveDock(ref patternDockSnapshot, ref livePatternSnapshot);
        bool changed = !ReferenceEquals(patternDockSnapshot, convertedSnapshot)
            || selected.Count != patternDockTargets.Length || patternDockTargets.Any(id => !selected.Contains(id));
        if (changed)
        {
            ResetPatternDock(); patternDockScroll = 0;
            patternDockSnapshot = convertedSnapshot; patternDockTargets = selected.ToArray();
            var tracks = Document.Tracks.Where(t => selected.Contains(t.Id)).ToArray();
            var showers = Document.BananaShowers.Where(t => selected.Contains(t.Id)).ToArray();
            patternDockSliders = selected.Count > 0 && tracks.Length
                + Document.ImportedSliders.Count(t => selected.Contains(t.Id)) == selected.Count;
            patternDockBananas = selected.Count > 0 && showers.Length == selected.Count;
            patternDockExisting = patternDockSliders && tracks.Length == selected.Count && tracks.All(t => t.StreamSnapDivisor is not null)
                || patternDockBananas && showers.All(t => t.Pattern is not null);
        }
        if (notesLocked || !ClipboardInteractionReady)
        {
            c.Text(L.Get(notesLocked ? "assist.locked" : "dock.patternSelection"), bounds.X, bounds.Y + 16, 12, Muted, bounds.Width);
            return;
        }
        bool sliders = patternDockSliders, bananas = patternDockBananas, existing = patternDockExisting;
        if (!sliders && !bananas)
        {
            c.Text(L.Get("dock.patternSelection"), bounds.X, bounds.Y + 16, 12, Muted, bounds.Width);
            return;
        }
        if (!existing && !patternDockConvert)
        {
            c.Text(L.Get("dock.convertPrompt"), bounds.X, bounds.Y + 16, 12, Foreground, bounds.Width);
            Button(c, new(bounds.X, bounds.Y + 68, bounds.Width, 34),
                L.Get(bananas ? "bananaEdit.title" : "conversion.title"), () => { patternDockConvert = true; InitializePatternDock(); livePatternConvert = true; });
            return;
        }
        if (patternDockKind == 0) InitializePatternDock();
        float contentHeight = Math.Max(PatternDockContentHeight, bounds.Height);
        patternDockScroll = Math.Clamp(patternDockScroll, 0, contentHeight - bounds.Height);
        var content = new Rect(bounds.X, bounds.Y - patternDockScroll, bounds.Width, contentHeight);
        int firstHit = hits.Count;
        c.Clip(bounds);
        c.Text(L.Get(patternDockKind == 2 ? "dock.bananaStack" : stackMode ? "conversion.stackTab" : "conversion.streamTab"),
            content.X, content.Y + 20, 14, Foreground, content.Width, true);
        var controls = new Rect(content.X, content.Y, content.Width, content.Height - (existing ? 40 : 0));
        if (patternDockKind == 2) DrawBananaEdit(c, controls); else DrawStackDialog(c, controls);
        if (existing)
            Button(c, new(content.X, content.Bottom - 36, content.Width, 30),
                L.Get(bananas ? "bananaEdit.convertBack" : "stream.convertBack"), ConvertPatternDockBack);
        c.Unclip();
        if (contentHeight > bounds.Height)
        {
            float thumbHeight = Math.Max(20, bounds.Height * bounds.Height / contentHeight);
            c.Fill(new(bounds.Right + 5, bounds.Y + patternDockScroll / (contentHeight - bounds.Height) * (bounds.Height - thumbHeight), 3, thumbHeight), Muted, 1);
        }
        // Offscreen controls must not receive clicks through the canvas or timeline.
        for (int i = hits.Count - 1; i >= firstHit; i--)
            if (hits[i].Bounds.Y < bounds.Y || hits[i].Bounds.Bottom > bounds.Bottom) hits.RemoveAt(i);
    }
    private void ConvertPatternDockBack()
    {
        if (!PatternDockEditing || !ClipboardInteractionReady) return;
        if (patternDockKind == 2)
        {
            var ids = ClipboardSelectedParentIds();
            if (!Edit(L.Get("bananaEdit.convertBack"), () =>
            {
                foreach (var shower in Document.BananaShowers.Where(s => ids.Contains(s.Id))) shower.Pattern = null;
            })) return;
        }
        else ConvertStreamsBack();
        ResetPatternDock();
    }
    private bool PatternDockPointer(float x, float y, int button)
    {
        if (!PatternDockEditing || menu >= 0 || contextItems.Count > 0) return false;
        if (!new Rect(patternDockBounds.X - 8, patternDockBounds.Y, patternDockBounds.Width + 16, patternDockBounds.Height).Contains(x, y))
        {
            stackNumericField = bananaEditField = bananaEditDropdown = -1;
            return false;
        }
        if (patternDockKind == 1)
        {
            if (stackMode && !StackNumericPointerDown(x, y)) return true;
            if (stackMode && StackPointerDown(x, y, button)) return true;
            if (button == 0 && StreamSnapBounds.Contains(x, y))
            { streamSnapDragging = true; SetStreamSnap(x); return true; }
        }
        if (button == 0)
            for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); break; }
        return true;
    }
    private bool PatternDockKey(int key, bool ctrl, bool shift)
    {
        if (!PatternDockEditing || menu >= 0 || contextItems.Count > 0) return false;
        if (patternDockKind == 1 && stackNumericField >= 0) return StackNumericKey(key, ctrl, shift);
        if (patternDockKind == 2 && bananaEditField >= 0)
        {
            if (key == 27) { bananaEditField = -1; bananaNumericError = ""; }
            else BananaEditKey(key, ctrl, shift);
            return true;
        }
        if (key == 27 && (streamSnapDragging || stackPointDragging >= 0 || stackFruitDragging >= 0))
        { CancelInteraction(); return true; }
        return false;
    }
}
