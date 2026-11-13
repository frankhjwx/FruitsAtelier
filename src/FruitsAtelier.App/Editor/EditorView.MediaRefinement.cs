using System.Numerics;
using FruitsAtelier.Core;
using FruitsAtelier.App.Media;
using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private StoryboardImageCache.Pixels? mediaPalettePixels;
    private uint[]? mediaRefinement;
    private uint[] mediaRecommendations = [];
    private Vector2[] mediaPickPoints = [];
    private int mediaPickIndex;
    private bool mediaPickDragging;
    internal bool MediaRefinementVisible => mediaRefinement is not null;
    internal Rect MediaRefinementImageBounds { get; private set; }
    private readonly List<(Rect Bounds, Action Action)> mediaRefinementHits = [];

    private void OpenMediaRefinement()
    {
        if (mediaPalettePixels is null || mediaPalette.Length == 0 || !PrepareFileOperation() || !MediaPaletteCurrent()) return;
        mediaRefinement = (uint[])mediaPalette.Clone();
        mediaPickIndex = 0;
        mediaPickDragging = false;
        mediaRefinementHits.Clear();
        menu = -1; contextItems.Clear(); heldEditorKeys.Clear();
        LocateMediaPickPoints();
    }

    private bool MediaPaletteCurrent()
    {
        if (ReferenceEquals(mediaPaletteOwner, history)
            && mediaPalettePath == MediaPath(BeatmapMedia.Read(Document).Background)
            && mediaPaletteVersion == MediaFileVersion.Read(mediaPalettePath!)) return true;
        mediaPalette = []; mediaRecommendations = []; mediaPalettePixels = null;
        mediaPanelError = L.Get("media.regenerate");
        return false;
    }

    private void CloseMediaRefinement(bool accept)
    {
        if (accept && mediaRefinement is { } draft && MediaPaletteCurrent()) mediaPalette = draft;
        mediaRefinement = null;
        mediaPickPoints = [];
        mediaPickDragging = false;
        mediaRefinementHits.Clear();
        // Closing must retire modal hit targets before the next editor repaint.
        hits.Clear();
    }

    private void LocateMediaPickPoints()
    {
        if (mediaPalettePixels is not { } pixels || mediaRefinement is not { } colours) return;
        mediaPickPoints = new Vector2[colours.Length];
        var distances = Enumerable.Repeat(long.MaxValue, colours.Length).ToArray();
        int step = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(pixels.Width * (double)pixels.Height / 16384)));
        for (int y = 0; y < pixels.Height; y += step)
        for (int x = 0; x < pixels.Width; x += step)
        {
            if (!TryMediaPixel(pixels, x, y, out uint colour)) continue;
            for (int i = 0; i < colours.Length; i++)
            {
                long r = (int)(colour >> 16 & 255) - (int)(colours[i] >> 16 & 255);
                long g = (int)(colour >> 8 & 255) - (int)(colours[i] >> 8 & 255);
                long b = (int)(colour & 255) - (int)(colours[i] & 255);
                long distance = r * r + g * g + b * b;
                if (distance >= distances[i]) continue;
                distances[i] = distance;
                mediaPickPoints[i] = new((x + .5f) / pixels.Width, (y + .5f) / pixels.Height);
            }
        }
    }

    private static bool TryMediaPixel(StoryboardImageCache.Pixels pixels, int x, int y, out uint colour)
    {
        int offset = (y * pixels.Width + x) * 4;
        int alpha = pixels.Data[offset + 3];
        colour = 0;
        if (alpha < 128) return false;
        uint Channel(int channel) => (uint)Math.Min(255, (pixels.Data[offset + channel] * 255 + alpha / 2) / alpha);
        colour = Channel(2) << 16 | Channel(1) << 8 | Channel(0);
        return true;
    }

    private void PickMediaColour(float x, float y)
    {
        if (mediaPalettePixels is not { } pixels || mediaRefinement is not { } draft) return;
        var bounds = MediaRefinementImageBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        int px = Math.Clamp((int)((x - bounds.X) / bounds.Width * pixels.Width), 0, pixels.Width - 1);
        int py = Math.Clamp((int)((y - bounds.Y) / bounds.Height * pixels.Height), 0, pixels.Height - 1);
        if (!TryMediaPixel(pixels, px, py, out uint colour)) return;
        draft[mediaPickIndex] = colour;
        mediaPickPoints[mediaPickIndex] = new((px + .5f) / pixels.Width, (py + .5f) / pixels.Height);
    }

    private void MediaRefinementPointerDown(float x, float y, int button)
    {
        mouseX = x; mouseY = y;
        if (button != 0) return;
        for (int i = mediaRefinementHits.Count - 1; i >= 0; i--)
            if (mediaRefinementHits[i].Bounds.Contains(x, y)) { mediaRefinementHits[i].Action(); return; }
        var image = MediaRefinementImageBounds;
        if (!image.Contains(x, y)) return;
        for (int pass = 0; pass < mediaPickPoints.Length; pass++)
        {
            int i = pass == 0 ? mediaPickIndex : pass <= mediaPickIndex ? pass - 1 : pass;
            var point = mediaPickPoints[i];
            if (Vector2.Distance(new(x, y), new(image.X + point.X * image.Width, image.Y + point.Y * image.Height)) <= 14)
            { mediaPickIndex = i; mediaPickDragging = true; return; }
        }
        mediaPickDragging = true;
        PickMediaColour(x, y);
    }

    private void DrawMediaRefinement(ICanvas c)
    {
        if (mediaRefinement is not { } colours || mediaPalettePixels is not { } pixels) return;
        hits.Clear(); mediaRefinementHits.Clear();
        c.Fill(new(0, 0, width, height), 0, opacity: .65f);
        float w = Math.Min(1000, width - 32), h = Math.Min(660, height - 32);
        var box = new Rect((width - w) / 2, (height - h) / 2, w, h);
        c.Fill(box, Panel, 10); c.Stroke(box, Grid, 1, 10);
        c.Text(L.Get("media.refine"), box.X + 22, box.Y + 18, 20, Foreground, w - 70, true);
        Control(new(box.Right - 48, box.Y + 12, 32, 32), "×", () => CloseMediaRefinement(false));
        float top = box.Y + 56;
        foreach (var line in WrapSyncText(c, L.Get("media.refineHelp"), w - 44))
        { c.Text(line.Text, box.X + 22, top, 12, Muted, w - 44); top += 18; }
        top += 12;
        float paletteWidth = Math.Clamp(w * .26f, 150, 230);
        var imageArea = new Rect(box.X + 22, top, w - paletteWidth - 60, box.Bottom - 76 - top);
        c.Fill(imageArea, Surface, 5);
        float scale = Math.Min(imageArea.Width / pixels.Width, imageArea.Height / pixels.Height);
        var image = MediaRefinementImageBounds = new Rect(imageArea.X + (imageArea.Width - pixels.Width * scale) / 2,
            imageArea.Y + (imageArea.Height - pixels.Height * scale) / 2, pixels.Width * scale, pixels.Height * scale);
        c.BackgroundImage(mediaPalettePath!, image);
        for (int i = 0; i < colours.Length; i++)
        {
            int index = i;
            float row = imageArea.Height / colours.Length;
            var swatch = new Rect(box.Right - 22 - paletteWidth, top + i * row, paletteWidth, row - 6);
            c.Fill(swatch, colours[i], 5);
            uint ink = BackgroundPalette.PerceivedBrightness(colours[i]) > 145 ? 0x101010u : 0xFFFFFFu;
            c.Text($"{i + 1}   #{colours[i]:X6}", swatch.X + 12, swatch.Y + swatch.Height / 2 - 7, 13, ink, swatch.Width - 24, true);
            if (i == mediaPickIndex) c.Stroke(swatch, Foreground, 3, 5);
            mediaRefinementHits.Add((swatch, () => mediaPickIndex = index));
        }
        // Selected markers own the hit test and draw on top when recommendations overlap.
        for (int pass = 0; pass < colours.Length; pass++)
        {
            int i = pass == colours.Length - 1 ? mediaPickIndex : pass >= mediaPickIndex ? pass + 1 : pass;
            var point = mediaPickPoints[i];
            float x = image.X + point.X * image.Width, y = image.Y + point.Y * image.Height;
            c.Circle(x, y, i == mediaPickIndex ? 15 : 12, 0x101010);
            c.Circle(x, y, i == mediaPickIndex ? 13 : 10, 0xFFFFFF);
            c.Circle(x, y, i == mediaPickIndex ? 10 : 7, colours[i]);
        }
        Control(new(box.X + 22, box.Bottom - 54, 200, 32), L.Get("media.resetRefinement"), () =>
        { mediaRefinement = (uint[])mediaRecommendations.Clone(); LocateMediaPickPoints(); });
        Control(new(box.Right - 234, box.Bottom - 54, 100, 32), L.Get("browser.cancel"), () => CloseMediaRefinement(false));
        Control(new(box.Right - 124, box.Bottom - 54, 102, 32), L.Get("media.keepRefinement"), () => CloseMediaRefinement(true));
        void Control(Rect rect, string text, Action action)
        {
            Button(c, rect, text, action, fontSize: 12);
            mediaRefinementHits.Add((rect, action));
        }
    }
}
