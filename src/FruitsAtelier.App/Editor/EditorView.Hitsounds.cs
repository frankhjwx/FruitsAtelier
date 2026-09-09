using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Action<Hitsound>? RequestHitsound { get; set; }
    public Action<Hitsound>? RequestPrepareHitsound { get; set; }
    private double preparedThrough = double.NegativeInfinity;
    public Action? RequestStopHitsounds { get; set; }
    private double? hitsoundPosition;
    private CatchConversionResult? hitsoundConversion;
    private HitsoundResolver? hitsoundResolver;
    private object? hitsoundDocument;
    private string? hitsoundFile;

    public void StartHitsounds(double position)
    {
        if (hitsoundPosition != position) hitsoundPosition = position - 0.001;
        hitsoundDocument = Document; hitsoundFile = Document.AudioPath;
        PrepareUpcoming(position);
    }

    public void ResetHitsounds()
    {
        hitsoundPosition = null;
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
            if (!double.IsFinite(position) || hitsoundPosition != position) hitsoundPosition = null;
            return;
        }
        double start = hitsoundPosition ?? position - 0.001;
        hitsoundPosition = position;
        // A discontinuous clock (seek, replay, or suspended UI) must not burst old sounds.
        if (position < start || position - start > 250) { RequestStopHitsounds?.Invoke(); return; }
        if (RequestHitsound is null) return;
        var current = GetHitsoundConversion();
        var objects = current.Objects;
        int low = FirstAfter(objects, start);
        for (int i = low; i < objects.Count && objects[i].TimeMs <= position; i++)
            foreach (var sound in hitsoundResolver!.Resolve(objects[i])) RequestHitsound(sound);
        if (position + 500 >= preparedThrough) PrepareUpcoming(position);
    }

    private CatchConversionResult GetHitsoundConversion()
    {
        var current = Conversion;
        if (!ReferenceEquals(current, hitsoundConversion))
        {
            hitsoundConversion = current;
            hitsoundResolver = new HitsoundResolver(Document, current.Objects);
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
            foreach (var sound in hitsoundResolver!.Resolve(objects[i]))
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
