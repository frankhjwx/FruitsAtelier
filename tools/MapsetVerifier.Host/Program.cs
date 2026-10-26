using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MapsetVerifier.Framework;
using MapsetVerifier.Framework.Objects;
using MapsetVerifier.Parser.Objects;
using MapsetVerifier.Checks.AllModes.General.Audio;
using MapsetVerifier.Checks.AllModes.General.Resources;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
if (args.Length != 2) { Console.Error.WriteLine("Usage: FruitsAtelier.MapsetVerifier request.json result.json"); return 2; }
try
{
    var request = JsonSerializer.Deserialize<Request>(File.ReadAllText(args[0]), json) ?? throw new InvalidDataException("Missing request.");
    var set = new BeatmapSet(request.Folder);
    set.ClearInterpretedDifficultyOverrides();
    var identities = request.Difficulties.ToDictionary(d => d.File, StringComparer.OrdinalIgnoreCase);
    foreach (var map in set.Beatmaps)
        if (identities.TryGetValue(Path.GetFileName(map.MapPath), out var entry) && entry.Interpretation is { } level)
            map.InterpretedDifficultyOverride = Enum.Parse<Beatmap.Difficulty>(level);
    set.Beatmaps.Sort((a, b) => a.GetDifficulty().CompareTo(b.GetDifficulty()));
    // Only built-in checks are loaded; this host does not discover user plugins or start a server.
    Checker.LoadDefaultChecks();
    if (request.Videos is not null)
    {
        var checks = CheckerRegistry.GetChecks();
        CheckerRegistry.ClearChecks();
        foreach (var check in checks.Where(c => c is not (CheckVideoResolution or CheckAudioInVideo)))
            CheckerRegistry.RegisterCheck(check);
    }
    var issues = Checker.GetBeatmapSetIssues(set);
    if (request.Videos is { } videos)
    {
        // Use the editor's bounded local-file probe for formats TagLib cannot inspect, including shared .osb videos.
        var resolution = new CheckVideoResolution();
        var audio = new CheckAudioInVideo();
        foreach (var video in videos)
        {
            if (video.Error is { } error)
            {
                issues.Add(new Issue(resolution.GetTemplate(error), null, video.File).WithOrigin(resolution));
                issues.Add(new Issue(audio.GetTemplate(error), null, video.File).WithOrigin(audio));
                continue;
            }
            if (video.Width > 1280 || video.Height > 720)
                issues.Add(new Issue(resolution.GetTemplate("Resolution"), null, video.File, video.Width, video.Height).WithOrigin(resolution));
            if (video.HasAudio)
                issues.Add(new Issue(audio.GetTemplate("Audio"), null, video.File).WithOrigin(audio));
        }
    }
    Guid? Identity(Beatmap? map) => map is not null && identities.TryGetValue(Path.GetFileName(map.MapPath), out var entry) ? entry.Id : null;
    object ProjectIssue(Issue issue) => new
    {
        Check = issue.CheckOrigin?.GetType().FullName ?? "unknown",
        Title = issue.CheckOrigin?.GetMetadata().Message ?? "Check failed.",
        Level = issue.level.ToString(), Message = issue.message,
        Cause = issue.Template.Cause, DifficultyId = Identity(issue.beatmap)
    };
    var report = new
    {
        Schema = 1,
        RuntimeDirectory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
        RuntimeVersion = Environment.Version.ToString(),
        General = issues.Where(i => i.CheckOrigin is GeneralCheck || i.beatmap is null).Select(ProjectIssue).ToArray(),
        Difficulties = set.Beatmaps.Select(map => new
        {
            Id = Identity(map), Name = map.MetadataSettings.version,
            Interpretation = map.GetDifficulty().ToString(), Stars = map.StarRating,
            Issues = issues.Where(i => i.beatmap == map && i.CheckOrigin is not GeneralCheck && i.AppliesToDifficulty(map.GetDifficulty())).Select(ProjectIssue).ToArray()
        }).ToArray()
    };
    File.WriteAllText(args[1], JsonSerializer.Serialize(report, json));
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }

internal sealed record Difficulty(Guid Id, string File, string? Interpretation);
internal sealed record VideoResource(string File, int Width, int Height, bool HasAudio, string? Error);
internal sealed record Request(string Folder, Difficulty[] Difficulties, VideoResource[]? Videos);
