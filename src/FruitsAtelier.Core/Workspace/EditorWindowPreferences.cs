namespace FruitsAtelier.Core;

public sealed class EditorWindowPreferences
{
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }

    public bool HasValidSize() => double.IsFinite(Width) && double.IsFinite(Height)
        && Width > 0 && Height > 0 && Width <= 100000 && Height <= 100000;
}
