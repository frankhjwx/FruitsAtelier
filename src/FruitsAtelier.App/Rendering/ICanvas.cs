namespace FruitsAtelier.App.Rendering;

public readonly record struct Rect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public bool Contains(float x, float y) => x >= X && y >= Y && x < Right && y < Bottom;
}

public interface ICanvas
{
    void Fill(Rect r, uint color, float radius = 0, float opacity = 1);
    void Stroke(Rect r, uint color, float width = 1, float radius = 0);
    void StrokeOpacity(Rect r, uint color, float width, float radius, float opacity)
    {
        if (opacity > 0) Stroke(r, color, width, radius);
    }
    void Line(float x1, float y1, float x2, float y2, uint color, float width = 1, float opacity = 1);
    void Polyline(IReadOnlyList<System.Numerics.Vector2> points, float x, float y, float scale,
        uint color, float width, float opacity)
    {
        for (int i = 1; i < points.Count; i++)
            Line(x + points[i - 1].X * scale, y + points[i - 1].Y * scale,
                x + points[i].X * scale, y + points[i].Y * scale, color, width, opacity);
    }
    void PrepareSliderBodies(int denseBodyCount, bool deferred = false) { }
    void ReleaseDifficultyResources() { }
    void SliderBody(IReadOnlyList<System.Numerics.Vector2> points, float x, float y, float scale,
        float diameter, uint border, uint accent, float opacity)
    {
        Polyline(points, x, y, scale, border, diameter * scale * .921875f, opacity);
        Polyline(points, x, y, scale, accent, diameter * scale * .8125f, opacity * .7f);
    }
    void Circle(float x, float y, float radius, uint color, bool filled = true, float width = 1, float opacity = 1);
    float MeasureText(string text, float size, bool bold = false) => text.Length * size * 0.6f;
    void Text(string text, float x, float y, float size, uint color, float maxWidth = 10000, bool bold = false);
    float MeasureStyledText(string text, float size, bool bold, bool italic) => MeasureText(text, size, bold);
    void StyledText(string text, float x, float y, float size, uint color, float maxWidth, bool bold, bool italic)
        => Text(text, x, y, size, color, maxWidth, bold);
    void TextOpacity(string text, float x, float y, float size, uint color, float maxWidth, bool bold, float opacity)
    {
        if (opacity > 0) Text(text, x, y, size, color, maxWidth, bold);
    }
    bool Image(string filePath, Rect destination, uint tint = 0xFFFFFF, Rect? source = null, float opacity = 1);
    bool SpriteImage(string filePath, Rect destination, uint tint, Rect source, float opacity, float rotation, bool additive)
        => Image(filePath, destination, tint, source, opacity);
    bool AdditiveImage(string filePath, Rect destination, uint tint, float opacity)
        => Image(filePath, destination, tint, opacity: opacity);
    bool CatcherImage(string filePath, Rect destination, uint tint, float opacity, bool additive, bool flipHorizontal)
        => additive ? AdditiveImage(filePath, destination, tint, opacity) : Image(filePath, destination, tint, opacity: opacity);
    bool Thumbnail(string filePath, Rect destination) => false;
    bool BackgroundImage(string filePath, Rect destination) => Image(filePath, destination);
    void CachedPreview(object key, Rect bounds, Action<ICanvas, Rect> draw) => draw(this, bounds);
    void DimBackground(Rect bounds, uint color, float opacity) => Fill(bounds, color, opacity: opacity);
    void Clip(Rect r);
    void Unclip();
}

internal static class SliderTrackGradient
{
    internal const int Steps = 24;
    internal static uint Colour(uint accent, int step)
    {
        float amount = step / (float)(Steps - 1);
        uint Channel(int shift)
        {
            float value = (accent >> shift) & 255;
            float outer = value * .9f, inner = Math.Min(255, value * 1.125f + 63.75f);
            return (uint)Math.Clamp(MathF.Round(outer + (inner - outer) * amount), 0, 255);
        }
        return Channel(16) << 16 | Channel(8) << 8 | Channel(0);
    }
}
