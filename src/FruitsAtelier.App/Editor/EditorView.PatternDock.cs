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
    private float PatternDockContentHeight => patternDockKind == 2 ? patternDockBounds.Width < 260 ? 576 : 490 : 440;
    private bool PatternDockEditing => DockContentInputAvailable && !notesLocked && PanelVisible(DockPanel.PatternEditor) && patternDockKind != 0
        && !StreamDialogVisible && !BananaEditVisible && !TimingPageVisible;
    private void ResetPatternDock()
    {
        patternDockKind = 0; patternDockConvert = false; patternDockSnapshot = null;
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
                L.Get(bananas ? "bananaEdit.title" : "conversion.title"), () => patternDockConvert = true);
            return;
        }
        if (patternDockKind == 0)
        {
            if (bananas)
            {
                patternDockKind = 2; OpenBananaEdit(); BananaEditVisible = false;
            }
            else
            {
                OpenStreamDialog(); StreamDialogVisible = false; patternDockKind = 1;
            }
        }
        float contentHeight = Math.Max(PatternDockContentHeight, bounds.Height);
        patternDockScroll = Math.Clamp(patternDockScroll, 0, contentHeight - bounds.Height);
        var content = new Rect(bounds.X, bounds.Y - patternDockScroll, bounds.Width, contentHeight);
        int firstHit = hits.Count;
        c.Clip(bounds);
        c.Text(L.Get(patternDockKind == 2 ? "dock.bananaStack" : stackMode ? "conversion.stackTab" : "conversion.streamTab"),
            content.X, content.Y + 20, 14, Foreground, content.Width, true);
        if (patternDockKind == 2) DrawBananaEdit(c, content); else DrawStackDialog(c, content);
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
