using FruitsAtelier.App.Rendering;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private const float HeaderHeight = 40;
    private Rect HeaderLanguageBounds => new(width - 298, 6, 198, 28);
    private Rect HeaderNavigationBounds => new(width - 94, 6, 82, 28);

    private void DrawHeader(ICanvas c)
    {
        c.Fill(new(0, 0, width, HeaderHeight), 0x1B2028);
        c.Image(Path.Combine(AppContext.BaseDirectory, "assets", "branding", "mark.png"), new(26, 2, 52, 36));
        c.Line(0, HeaderHeight - 1, width, HeaderHeight - 1, Grid);
    }
}
