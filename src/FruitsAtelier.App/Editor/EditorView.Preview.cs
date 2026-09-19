using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool catchPreviewVisible;
    private float previewWidth = 360;
    private const float MinimumPreviewWidth = 344;
    private int previewMod;
    private int previewDisplayMode;
    private CatchAutoPreview? previewAutoplay;
    private CatchPlatePreview? previewPlate;
    private HashSet<(Guid SourceId, int EventIndex)> previewComboEnds = [];
    public Rect PreviewViewport { get; private set; }
    public double PreviewCatcherX { get; private set; }
    private CatchConversionResult? previewSource;
    private int cachedPreviewMod = -1;
    private IReadOnlyList<ConvertedCatchObject> previewObjects = [];
    private HashSet<(Guid SourceId, int EventIndex)> previewHyperdash = [];
    public bool CatchPreviewVisible => catchPreviewVisible;
    public Rect PreviewToggleBounds { get; private set; }
    public Rect PreviewResizeBounds { get; private set; }
    public bool PreviewResizeCursor => !TimeJumpVisible && !LibraryVisible && !ExportVisible && !ErrorVisible && !DiscardConfirmationVisible && !SliderDialogVisible && !languageMenuOpen && menu < 0
        && catchPreviewVisible && (drag == DragKind.PreviewResize || PreviewResizeBounds.Contains(mouseX, mouseY));
    public Rect LegacyConversionBounds { get; private set; }
    public double PreviewApproachRate => previewMod switch { 1 => Document.ApproachRate * .5, 2 => Math.Min(10, Document.ApproachRate * 1.4), _ => Document.ApproachRate };
    public double PreviewCircleSize => previewMod switch { 1 => Document.CircleSize * .5, 2 => Math.Min(10, Document.CircleSize * 1.3), _ => Document.CircleSize };
    private string PreviewModName => previewMod switch { 1 => "EZ", 2 => "HR", _ => "NM" };
    private void DrawPreviewSidebar(ICanvas c)
    {
        PreviewResizeBounds = catchPreviewVisible ? new(rightPanel.X - 4, rightPanel.Y + 38, 8, rightPanel.Height - 38) : default;
        float edge = catchPreviewVisible ? rightPanel.X : width;
        PreviewToggleBounds = new(edge - 22, canvas.Y + canvas.Height / 2 - 22, 22, 44);
        if (catchPreviewVisible)
        {
            DrawPreview(c, new(rightPanel.X + 16, rightPanel.Y + 56, rightPanel.Width - 32, rightPanel.Height - 70));
            c.Fill(new(rightPanel.X - 1, rightPanel.Y + 38, 2, rightPanel.Height - 38), PreviewResizeBounds.Contains(mouseX, mouseY) || drag == DragKind.PreviewResize ? Accent : Grid);
        }
        c.Fill(PreviewToggleBounds, Surface, 5);
        Button(c, PreviewToggleBounds, catchPreviewVisible ? "›" : "‹", () => catchPreviewVisible = !catchPreviewVisible);
        if (PreviewToggleBounds.Contains(mouseX, mouseY))
            c.Text(L.Get("ui.preview"), PreviewToggleBounds.X - 112, PreviewToggleBounds.Y + 14, 12, Foreground, 108);
    }
    private void DrawPreviewMods(ICanvas c, Rect r)
    {
        string[] keys = ["preview.normal", "preview.easy", "preview.hardRock"];
        c.Text(L.Get("preview.mode"), r.X, r.Y + 50, 10, Muted, 65);
        float start = r.X + 66, buttonWidth = (r.Width - 66) / 3;
        for (int i = 0; i < keys.Length; i++)
        {
            int mod = i;
            Button(c, new(start + i * buttonWidth, r.Y + 43, buttonWidth - 3, 26), L.Get(keys[i]), () => previewMod = mod, previewMod == mod);
        }
    }
    private IReadOnlyList<ConvertedCatchObject> PreviewObjects()
    {
        if (!ReferenceEquals(previewSource, conversion) || cachedPreviewMod != previewMod)
        {
            previewSource = conversion; cachedPreviewMod = previewMod;
            previewObjects = previewMod == 2 ? CatchPreviewMods.HardRock(Document, conversion!) : conversion!.Objects;
            previewHyperdash = HyperDashCalculator.GetHyperDashStarts(previewObjects, PreviewCircleSize);
            previewAutoplay = new(previewObjects, PreviewCircleSize);
            var parents = ClipboardParents(Document).OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).ToArray();
            var numbers = ComboNumbers();
            var tails = previewObjects.GroupBy(item => item.SourceId).ToDictionary(group => group.Key, group => group.Last());
            var ends = previewComboEnds = new HashSet<(Guid, int)>();
            for (int i = 0; i < parents.Length; i++)
                if ((i == parents.Length - 1 || numbers[parents[i + 1].Id] == 1) && tails.TryGetValue(parents[i].Id, out var tail))
                    ends.Add((tail.SourceId, tail.EventIndex));
            previewPlate = new(previewObjects, previewAutoplay, PreviewCircleSize, ends);
        }
        return previewObjects;
    }
    private IEnumerable<ConvertedCatchObject> PreviewObjectsInRange(double start, double end)
    {
        var objects = PreviewObjects();
        int low = 0, high = objects.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (objects[middle].TimeMs < start) low = middle + 1; else high = middle;
        }
        for (int i = low; i < objects.Count && objects[i].TimeMs <= end; i++) yield return objects[i];
    }
    private void DrawPreviewDisplayModes(ICanvas c, Rect r)
    {
        string[] keys = ["preview.ratio43", "preview.ratio169", "preview.fit"];
        c.Text(L.Get("preview.resolution"), r.X, r.Y + 81, 10, Muted, 65);
        float start = r.X + 66, buttonWidth = (r.Width - 66) / 3;
        for (int i = 0; i < keys.Length; i++)
        {
            int mode = i;
            Button(c, new(start + i * buttonWidth, r.Y + 74, buttonWidth - 3, 26), L.Get(keys[i]), () => previewDisplayMode = mode, previewDisplayMode == mode);
        }
    }
    private static readonly HashSet<(Guid SourceId, int EventIndex)> NoHyperdash = [];
    private void DrawPreviewPlate(ICanvas c, float fieldLeft, float fieldWidth, float catchY)
        => DrawCaughtPlate(c, previewPlate!.At(playhead, PreviewCatcherX), fieldLeft, fieldWidth, catchY);
    private void DrawCaughtPlate(ICanvas c, IEnumerable<CatchPlateSprite> sprites, float fieldLeft, float fieldWidth, float catchY)
    {
        foreach (var sprite in sprites)
            DrawCatchObject(c, sprite.Object, fieldLeft + (float)(sprite.X / 512) * fieldWidth,
                catchY + (float)(sprite.Y / 512) * fieldWidth, fieldWidth * .5f, sprite.Opacity,
                PreviewCircleSize, previewHyperdash, animated: true, caught: true);
    }
    private void DrawPreviewCatcher(ICanvas c, float fieldLeft, float fieldWidth, float catchY)
    {
        PreviewObjects();
        var frame = previewAutoplay!.At(playhead);
        PreviewCatcherX = frame.X;
        float x = fieldLeft + (float)(frame.X / 512) * fieldWidth;
        uint hyperColour = skin?.HyperDashColour ?? 0xFF0000;
        // Sample map time, not render history, so pause, seek and playback speed produce identical trails.
        for (double at = Math.Max(0, Math.Ceiling((playhead - 800) / 16) * 16); at <= playhead; at += 16)
        {
            var past = previewAutoplay.At(at);
            bool hyper = previewAutoplay.HyperDashingAt(at);
            if (!past.Dashing && !hyper) continue;
            DrawCatcherTrail(c, new(at, past.X, previewAutoplay.FacingLeftAt(at), hyper, false), fieldLeft, fieldWidth, catchY);
        }
        foreach (double start in previewAutoplay.HyperStarts(playhead - 1200, playhead))
            DrawCatcherTrail(c, new(start, previewAutoplay.At(start).X, previewAutoplay.FacingLeftAt(start), true, true), fieldLeft, fieldWidth, catchY);
        DrawCatcherBody(c, x, catchY, fieldWidth, 0xFFFFFF, 1, false, previewAutoplay.FacingLeftAt(playhead));
        float tint = (float)previewAutoplay.HyperTintAt(playhead);
        if (tint > .001f) DrawCatcherBody(c, x, catchY, fieldWidth, hyperColour, tint, false, previewAutoplay.FacingLeftAt(playhead));
    }
    private Guid legacyButtonSlider;
    private void DrawLegacyConversionButton(ICanvas c)
    {
        if (SelectedImportedSlider is not { } selected || drag != DragKind.None || menu >= 0 || ExportVisible || SliderDialogVisible)
        { LegacyConversionBounds = default; legacyButtonSlider = Guid.Empty; return; }
        bool retained = legacyButtonSlider == selected.Id && new Rect(LegacyConversionBounds.X - 16, LegacyConversionBounds.Y - 10, LegacyConversionBounds.Width + 32, LegacyConversionBounds.Height + 20).Contains(mouseX, mouseY);
        bool hovered = !retained && plot.Contains(mouseX, mouseY)
            && (HitCatchObject(mouseX, mouseY, selected.Id) is not null || showTargets && HitSliderLocation(mouseX, mouseY, selected.Id) is not null);
        if (!retained && !hovered) { LegacyConversionBounds = default; legacyButtonSlider = Guid.Empty; return; }
        if (!retained)
            LegacyConversionBounds = new(Math.Clamp(mouseX + 10, plot.X, Math.Max(plot.X, plot.Right - 224)), Math.Clamp(mouseY - 16, plot.Y, plot.Bottom - 32), 224, 32);
        legacyButtonSlider = selected.Id;
        c.Fill(LegacyConversionBounds, Surface, 4);
        Button(c, LegacyConversionBounds, L.Get("preview.convertSlider"), EditImportedSlider);
    }
}
