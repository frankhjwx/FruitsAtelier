namespace FruitsAtelier.App.Rendering;

public readonly record struct Rect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public bool Contains(float x, float y) => x >= X && y >= Y && x < Right && y < Bottom;
}

public interface ICanvas
{
    void Fill(Rect r, uint color, float radius = 0);
    void Stroke(Rect r, uint color, float width = 1, float radius = 0);
    void Line(float x1, float y1, float x2, float y2, uint color, float width = 1, float opacity = 1);
    void Circle(float x, float y, float radius, uint color, bool filled = true, float width = 1);
    float MeasureText(string text, float size, bool bold = false) => text.Length * size * 0.6f;
    void Text(string text, float x, float y, float size, uint color, float maxWidth = 10000, bool bold = false);
    bool Image(string filePath, Rect destination, uint tint = 0xFFFFFF, Rect? source = null);
    void Clip(Rect r);
    void Unclip();
}
