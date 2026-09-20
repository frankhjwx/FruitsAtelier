using FruitsAtelier.App.Rendering;
using System.Reflection;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private static readonly string DisplayVersion = "v" + (typeof(EditorView).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(EditorView).Assembly.GetName().Version!.ToString(3));
    public string WindowTitle
    {
        get
        {
            if (!HasEditorProject) return FruitsAtelier.Localization.Strings.Get("window.initialTitle") + " " + DisplayVersion;
            string Metadata(string key, string fallback) => FruitsAtelier.Core.OsuBeatmapReader.Setting(Document, "Metadata", key) is { Length: > 0 } value ? value : fallback;
            string artist = DisplayMetadata(Metadata("Artist", ""), Metadata("ArtistUnicode", ""));
            string title = DisplayMetadata(Metadata("Title", ""), Metadata("TitleUnicode", ""));
            if (string.IsNullOrWhiteSpace(title)) title = Document.Name;
            string mapper = Metadata("Creator", "");
            string name = (artist.Length > 0 ? artist + " - " : "") + title
                + (mapper.Length > 0 ? " (" + mapper + ")" : "") + " [" + CurrentDifficultyName + "]";
            return FruitsAtelier.Localization.Strings.Get("window.title", name, IsDirty ? " *" : "", DisplayVersion);
        }
    }
    private const float HeaderHeight = 40;
    private string DisplayMetadata(string romanised, string unicode)
        => LibrarySettings.RomanisedMetadata
            ? (string.IsNullOrWhiteSpace(romanised) ? unicode : romanised)
            : (string.IsNullOrWhiteSpace(unicode) ? romanised : unicode);
    private Rect HeaderLanguageBounds => new(width - 298, 6, 198, 28);
    private Rect HeaderNavigationBounds => new(width - 94, 6, 82, 28);

    private void DrawHeader(ICanvas c)
    {
        c.Fill(new(0, 0, width, HeaderHeight), 0x1B2028);
        c.Image(Path.Combine(AppContext.BaseDirectory, "assets", "branding", "mark.png"), new(26, 2, 52, 36));
        c.Line(0, HeaderHeight - 1, width, HeaderHeight - 1, Grid);
    }
}
