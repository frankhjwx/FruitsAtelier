using System.Globalization;

namespace FruitsAtelier.Core;

public readonly record struct BeatmapMedia(string? Background, string? Video, int OffsetMs)
{
    public static BeatmapMedia Read(MapDocument document)
    {
        string? video = null; int offset = 0;
        foreach (var line in document.OriginalSections.Where(s => s.Name == "Events").SelectMany(s => s.Lines))
        {
            var parts = WorkspaceProject.Csv(line);
            if (parts.Length < 3 || parts[0] is not ("1" or "Video")) continue;
            OsuBeatmapReader.ValidateOsuResourceReference(parts[2]);
            video = parts[2];
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out offset);
            break;
        }
        return new(OsuTimeline.BackgroundFilename(document), video, offset);
    }

    public void Apply(MapDocument document)
    {
        if (Background is not null) OsuBeatmapReader.ValidateOsuResourceReference(Background);
        if (Video is not null) OsuBeatmapReader.ValidateOsuResourceReference(Video);
        var previous = Read(document);
        bool backgroundChanged = Background != previous.Background;
        bool videoChanged = Video != previous.Video || OffsetMs != previous.OffsetMs;
        if (!backgroundChanged && !videoChanged) return;
        string videoPosition = "";
        foreach (var section in document.OriginalSections.Where(s => s.Name == "Events"))
            for (int i = section.Lines.Count - 1; i >= 0; i--)
            {
                var parts = WorkspaceProject.Csv(section.Lines[i]);
                if (parts.Length < 3) continue;
                if (videoChanged && parts[0] is "1" or "Video")
                {
                    if (Video == previous.Video && parts.Length > 3) videoPosition = "," + string.Join(',', parts.Skip(3));
                    section.Lines.RemoveAt(i);
                }
                if (backgroundChanged && parts[0] is "0" or "Background") section.Lines.RemoveAt(i);
            }
        var events = document.OriginalSections.FirstOrDefault(s => s.Name == "Events");
        if (events is null) { events = new OsuSection { Name = "Events" }; document.OriginalSections.Add(events); }
        if (videoChanged && Video is not null) events.Lines.Insert(0, $"Video,{OffsetMs.ToString(CultureInfo.InvariantCulture)},\"{Video}\"{videoPosition}");
        if (backgroundChanged && Background is not null) events.Lines.Insert(0, $"0,0,\"{Background}\",0,0");
    }
}
