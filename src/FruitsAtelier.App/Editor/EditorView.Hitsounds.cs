using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Action<IReadOnlyList<MapDocument>>? RequestPreloadHitsounds { get; set; }
    public void PreloadProjectHitsounds() => RequestPreloadHitsounds?.Invoke(
        difficulties.Select(d => d.History.Document.DeepClone()).ToArray());
    public Action<Hitsound, double>? RequestScheduleHitsound { get; set; }
    private double? scheduledThrough;
    public Action<Hitsound>? RequestHitsound { get; set; }
    public Action<Hitsound>? RequestPrepareHitsound { get; set; }
    private double preparedThrough = double.NegativeInfinity;
    public Action? RequestStopHitsounds { get; set; }
    private double? hitsoundPosition;
    private CatchConversionResult? hitsoundConversion;
    private IReadOnlyList<Hitsound>[] resolvedHitsounds = [];
    private object? hitsoundDocument;
    private string? hitsoundFile;

    public void StartHitsounds(double position)
    {
        if (hitsoundPosition != position) hitsoundPosition = position - 0.001;
        hitsoundDocument = Document; hitsoundFile = Document.AudioPath;
        scheduledThrough = null;
        PrepareUpcoming(position);
    }

    public void ResetHitsounds()
    {
        hitsoundPosition = null;
        scheduledThrough = null;
        preparedThrough = double.NegativeInfinity;
        RequestStopHitsounds?.Invoke();
    }

    private void UpdateHitsounds(double position, bool playing, string? filename)
    {
        if (!ReferenceEquals(hitsoundDocument, Document) || hitsoundFile != filename)
        {
            ResetHitsounds(); hitsoundDocument = Document; hitsoundFile = filename;
        }
        if (!double.IsFinite(position) || !playing)
        {
            RequestStopHitsounds?.Invoke();
            scheduledThrough = null;
            if (!double.IsFinite(position) || hitsoundPosition != position) hitsoundPosition = null;
            return;
        }
        double start = hitsoundPosition ?? position - 0.001;
        hitsoundPosition = position;
        // Cancel queued future sounds when the transport jumps or content changes.
        bool discontinuity = position < start || position - start > 250;
        if (discontinuity)
        {
            RequestStopHitsounds?.Invoke(); scheduledThrough = null;
            if (RequestScheduleHitsound is null) return;
            start = position - .001;
        }
        if (RequestHitsound is null && RequestScheduleHitsound is null) return;
        var previousConversion = hitsoundConversion;
        var current = GetHitsoundConversion();
        if (previousConversion is not null && !ReferenceEquals(previousConversion, current))
        {
            RequestStopHitsounds?.Invoke(); scheduledThrough = null;
            start = position - .001;
        }
        var objects = current.Objects;
        bool scheduled = RequestScheduleHitsound is not null;
        double end = scheduled ? position + 100 : position;
        int low = FirstAfter(objects, scheduled ? scheduledThrough ?? start : start);
        for (int i = low; i < objects.Count && objects[i].TimeMs <= end; i++)
            foreach (var sound in resolvedHitsounds[i])
                if (scheduled) RequestScheduleHitsound!(sound, objects[i].TimeMs);
                else RequestHitsound!(sound);
        if (scheduled) scheduledThrough = end;
        if (position + 500 >= preparedThrough) PrepareUpcoming(position);
    }

    private CatchConversionResult GetHitsoundConversion()
    {
        var current = Conversion;
        if (!ReferenceEquals(current, hitsoundConversion))
        {
            hitsoundConversion = current;
            var resolver = new HitsoundResolver(Document, current.Objects);
            resolvedHitsounds = current.Objects.Select(resolver.Resolve).ToArray();
        }
        return current;
    }

    private void PrepareUpcoming(double position)
    {
        if (RequestPrepareHitsound is null) return;
        var objects = GetHitsoundConversion().Objects;
        int low = FirstAfter(objects, position - .001);
        var prepared = new HashSet<Hitsound>();
        for (int i = low; i < objects.Count && objects[i].TimeMs <= position + 1000 && prepared.Count < 128; i++)
            foreach (var sound in resolvedHitsounds[i])
                if (prepared.Add(sound)) RequestPrepareHitsound(sound);
        preparedThrough = position + 1000;
    }

    private static int FirstAfter(IReadOnlyList<ConvertedCatchObject> objects, double start)
    {
        int low = 0, high = objects.Count;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (objects[mid].TimeMs <= start) low = mid + 1; else high = mid;
        }
        return low;
    }
}
