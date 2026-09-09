using L = FruitsAtelier.Localization.Strings;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Action? RequestOpen { get; set; }
    public Action? RequestNewProject { get; set; }
    public Action? RequestImportDifficulty { get; set; }
    public Action? RequestDifficultyChanged { get; set; }
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
        LoadProject(BeatmapProject.FromDocuments([document]));
    }

    public void LoadProject(BeatmapProject project)
    {
        project.Validate();
        var retiredCancellation = sliderBatchCancellation;
        retiredCancellation?.Cancel();
        if (sliderBatchTask is { } retiredTask)
            _ = retiredTask.ContinueWith(t => { _ = t.Exception; retiredCancellation?.Dispose(); }, TaskScheduler.Default);
        sliderBatchTask = null; sliderBatchCancellation = null;
        sliderImportTargets = []; sliderBatchErrors = []; sliderDialogHits.Clear();
        WorkspaceSession = null; resourceErrors = [];
        CancelInteraction();
        foreach (var difficulty in difficulties) difficulty.RatingCancellation.Cancel();
        editorConversionCache = new();
        difficulties.Clear();
        difficulties.AddRange(project.Difficulties.Select(d => new DifficultySession(d)));
        activeDifficulty = firstDifficultyTab = 0;
        ProjectName = project.Name;
        projectStructureDirty = false;
        ResetDifficultyView();
    }

    private void ResetDifficultyView()
    {
        var document = Document;
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

    public BeatmapProject CaptureProject() => new()
    {
        Name = ProjectName,
        Difficulties = difficulties.Select(d => new ProjectDifficulty { Id = d.Id, Name = d.Name, Document = d.History.Document.DeepClone() }).ToList()
    };

    public void NewProject()
    {
        var document = new MapDocument { IsDemo = false };
        var metadata = new OsuSection { Name = "Metadata" };
        metadata.Lines.Add("Version:" + L.Get("project.defaultDifficulty", 1));
        document.OriginalSections.Add(metadata);
        LoadProject(BeatmapProject.FromDocuments([document]));
    }

    public bool SwitchDifficulty(int index)
    {
        if (index < 0 || index >= difficulties.Count) return false;
        if (index == activeDifficulty) return true;
        if (!PrepareFileOperation()) return false;
        difficulties[activeDifficulty].Playhead = playhead;
        difficulties[activeDifficulty].ViewStart = viewStart;
        activeDifficulty = index;
        RevealDifficultyTab();
        ResetDifficultyView();
        playhead = difficulties[index].Playhead;
        viewStart = difficulties[index].ViewStart;
        RequestDifficultyChanged?.Invoke();
        return true;
    }

    public bool AddDifficulty(MapDocument? imported = null)
    {
        if (!PrepareFileOperation()) return false;
        if (difficulties.Count >= 256) { SetNotice(L.Get("project.invalid")); return false; }
        var document = imported?.DeepClone() ?? Document.DeepClone();
        string name = imported is null ? L.Get("project.defaultDifficulty", difficulties.Count + 1)
            : OsuBeatmapReader.Setting(document, "Metadata", "Version") ?? L.Get("project.defaultDifficulty", difficulties.Count + 1);
        if (imported is null)
        {
            document.Fruits.Clear(); document.Tracks.Clear(); document.ImportedSliders.Clear(); document.BananaShowers.Clear();
            document.IsDemo = false;
            foreach (var section in document.OriginalSections.Where(s => s.Name == "HitObjects")) section.Lines.Clear();
            var metadata = document.OriginalSections.FirstOrDefault(s => s.Name == "Metadata");
            if (metadata is null) { metadata = new OsuSection { Name = "Metadata" }; document.OriginalSections.Add(metadata); }
            metadata.Lines.RemoveAll(line => line.Split(':', 2)[0].Trim() is "Version" or "BeatmapID");
            metadata.Lines.Add("Version:" + name);
            metadata.Lines.Add("BeatmapID:0");
        }
        OsuBeatmapReader.Validate(document);
        difficulties.Add(new DifficultySession(new ProjectDifficulty { Name = name, Document = document }));
        projectStructureDirty = true;
        bool switched = SwitchDifficulty(difficulties.Count - 1);
        if (switched && imported is not null) OfferSliderConversion(false);
        return switched;
    }

    public void MarkSaved()
    {
        foreach (var difficulty in difficulties) difficulty.History.MarkSaved();
        projectStructureDirty = false;
    }

    public void ChangeAudioPath(string path) => Edit(L.Get("editor.command.changeAudio"), () => Document.AudioPath = path);

    public bool PrepareFileOperation()
    {
        if (SliderDialogVisible) return false;
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
        UpdateHitsounds(positionMs, ready && playing && !loading, filename);
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
        ResetHitsounds();
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
