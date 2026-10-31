using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private WeakReference<IReadOnlyList<string>>? resourceErrorLayoutSource;
    private (int Start, float Width, float Height, string Language, Type Canvas)? resourceErrorLayoutKey;
    private readonly List<(string Text, float Y)> resourceErrorLines = [];

    private void DrawResourceErrors(ICanvas c)
    {
        if (width <= 48 || height <= 160) return;
        int first = Math.Clamp((int)libraryScroll, 0, resourceErrors.Count);
        var key = (first, width, height, L.Language, c.GetType());
        bool sameSource = resourceErrorLayoutSource is { } cached && cached.TryGetTarget(out var source) && ReferenceEquals(source, resourceErrors);
        if (resourceErrorLayoutKey != key || !sameSource)
        {
            resourceErrorLines.Clear();
            float y = 144;
            for (int i = first; i < resourceErrors.Count && y < height - 20 && resourceErrorLines.Count < 256; i++)
            {
                int remainingLines = Math.Min(256 - resourceErrorLines.Count, (int)Math.Floor((height - 20 - y - 13) / 22) + 1);
                if (remainingLines <= 0) break;
                var lines = WrapDiagnosticPreview(c, [resourceErrors[i]], 13, width - 80, maximumLines: remainingLines);
                foreach (string line in lines)
                {
                    resourceErrorLines.Add((line, y));
                    y += 22;
                }
                y += 18;
            }
            resourceErrorLayoutKey = key;
            resourceErrorLayoutSource = new(resourceErrors);
        }
        c.Clip(new(24, 140, width - 48, height - 160));
        foreach (var line in resourceErrorLines) c.Text(line.Text, 32, line.Y, 13, Error, width - 64);
        c.Unclip();
    }
}
