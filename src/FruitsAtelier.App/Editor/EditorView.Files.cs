using L = FruitsAtelier.Localization.Strings;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Action? RequestOpen { get; set; }
    public Action? RequestSave { get; set; }
    public Action? RequestSaveAs { get; set; }
    public Action? RequestExport { get; set; }
    public Action? RequestAudio { get; set; }
    public Action? RequestTogglePlayback { get; set; }
    public Action<double>? RequestSeek { get; set; }
    public bool AudioReady { get; private set; }
    public bool AudioPlaying { get; private set; }
    public bool AudioLoading { get; private set; }
    public string AudioNotice { get; private set; } = L.Get("editor.audio.notLoaded");
    public double AudioDurationMs { get; private set; }
    private const double playbackLineFromBottom = 0.25;
    private bool pinPlayhead = true;
    public double TimelineDurationMs => Math.Max(Document.DurationMs, AudioDurationMs);
    private double EditableDurationMs => Math.Min(int.MaxValue, AudioReady ? TimelineDurationMs : Document.DurationMs);
    public bool CompensateTinyDroplets => compensateTinyDroplets;
    public CatchConversionResult Conversion { get { EnsureConversion(); return conversion!; } }
    private ImportedSlider? SelectedImportedSlider => Document.ImportedSliders.FirstOrDefault(s => s.Id == selection);
    private BananaShower? SelectedBananaShower => Document.BananaShowers.FirstOrDefault(s => s.Id == selection);

    public void LoadDocument(MapDocument document)
    {
        CancelInteraction();
        history.Reset(document);
        convertedSnapshot = null;
        Select(Guid.Empty);
        tool = Tool.Select;
        listScroll = 0;
        menu = -1;
        ResetView();
        playhead = Math.Clamp(document.Fruits.Select(f => f.TimeMs)
            .Concat(document.ImportedSliders.Select(s => s.TimeMs)).DefaultIfEmpty(0).Min() - 1000, 0, document.DurationMs);
        viewStart = Math.Max(0, playhead - 500);
        AudioReady = AudioPlaying = AudioLoading = false;
        AudioDurationMs = 0;
        AudioNotice = L.Get("editor.audio.notLoaded");
        pinPlayhead = true;
        StatusMessage = L.Get("editor.status.documentOpened", document.Name, document.TimingPoints.Count);
    }

    public void MarkSaved() => history.MarkSaved();

    public void ChangeAudioPath(string path) => Edit(L.Get("editor.command.changeAudio"), () => Document.AudioPath = path);

    public bool PrepareFileOperation()
    {
        if (draftBanana != Guid.Empty)
        {
            StatusMessage = L.Get("editor.status.bananaNeedsEnd");
            return false;
        }
        if (draftTrack != Guid.Empty) FinishCurve();
        if (draftTrack != Guid.Empty) return false;
        if (editField >= 0 && !CommitField()) return false;
        CancelInteraction();
        return true;
    }

    public void UpdateTransport(double positionMs, double durationMs, bool ready, bool playing, bool loading, string? error, string? filename)
    {
        bool wasReady = AudioReady;
        AudioReady = ready; AudioPlaying = playing; AudioLoading = loading;
        AudioDurationMs = double.IsFinite(durationMs) ? Math.Max(0, durationMs) : 0;
        AudioNotice = error ?? (loading ? L.Get("editor.audio.loading") : ready ? Path.GetFileName(filename) ?? L.Get("editor.audio.loaded") : L.Get("editor.audio.notLoaded"));
        if (ready && drag != DragKind.Timeline)
            playhead = Math.Clamp(positionMs, 0, TimelineDurationMs);
        if (playing || ready && !wasReady) FollowPlayhead();
    }

    private void SeekTo(double time)
    {
        playhead = Math.Clamp(time, 0, TimelineDurationMs);
        FollowPlayhead();
        RequestSeek?.Invoke(playhead);
        StatusMessage = L.Get("editor.status.seek", Time(playhead), AudioReady ? "" : L.Get("editor.audio.notLoadedSuffix"));
    }

    private void FollowPlayhead()
    {
        if (drag is DragKind.Marquee or DragKind.Objects or DragKind.BananaStart or DragKind.BananaEnd) return;
        pinPlayhead = true;
        viewStart = playhead - plot.Height * playbackLineFromBottom / pixelsPerMs;
    }

    private void TogglePlayback()
    {
        if (AudioReady) RequestTogglePlayback?.Invoke();
        else StatusMessage = AudioLoading ? L.Get("editor.audio.stillLoading") : L.Get("editor.audio.loadFromFileMenu");
    }
}
