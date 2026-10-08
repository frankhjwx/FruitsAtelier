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
    public Action? RequestExport { get; set; }
    public Action? RequestTogglePlayback { get; set; }
    public Action<double>? RequestSeek { get; set; }
    public bool AudioReady { get; private set; }
    public bool AudioPlaying { get; private set; }
    public bool AudioLoading { get; private set; }
    public string AudioNotice { get; private set; } = L.Get("editor.audio.notLoaded");
    public double AudioDurationMs { get; private set; }
    private bool initializeTransport;
    private int? pauseSnapDivisor;
    private double playbackLineFromBottom = 0.25;
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
        CloseVersionHistory();
        CloseAimod();
        CloseHitsoundCopier();
        CloseSongSetup();
        CloseTimingSetup(); TimingPageVisible = false;
        TimeJumpVisible = false;
        StreamDialogVisible = false;
        MergeDialogVisible = false;
        CloseVolumeDialog();
        CloseDistanceSnapDialog();
        HasEditorProject = true;
        var retiredCancellation = sliderBatchCancellation;
        retiredCancellation?.Cancel();
        if (sliderBatchTask is { } retiredTask)
            _ = retiredTask.ContinueWith(t => { _ = t.Exception; retiredCancellation?.Dispose(); }, TaskScheduler.Default);
        sliderBatchTask = null; sliderBatchCancellation = null;
        sliderImportTargets = []; sliderBatchErrors = []; sliderDialogHits.Clear();
        WorkspaceSession = null; resourceErrors = [];
        if (syncTask is { } retiredSync) _ = retiredSync.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
        syncTask = null; syncRetry = null; afterSynchronization = null; syncReviewRequested = false; syncSearching.Clear();
        syncStatuses.Clear(); syncMerges.Clear(); syncPage = null; nextSyncCheck = DateTime.MaxValue;
        syncDifficulty = Guid.Empty; syncPreserveHistory = false;
        fileSyncPending = fileSearchMissing = false; nextMonitorConfiguration = DateTime.MinValue;
        syncRetainedReview.Clear();
        unreadableSyncRetries = 0;
        syncComparisons.Clear(); syncVisualMerge = null; syncResultPane = null; syncPreviewRevision++;
        resourceSnapshot = null; resourceReferences = null;
        CancelInteraction();
        foreach (var difficulty in difficulties) difficulty.RatingCancellation.Cancel();
        editorConversionCache = new();
        difficulties.Clear();
        difficulties.AddRange(project.Difficulties.Select(d => new DifficultySession(d)));
        activeDifficulty = firstDifficultyTab = 0;
        difficultyTabOrder = []; difficultySortPending = false;
        RestoreMapEditingPreferences();
        tabPointer = false; tabRemainder = 0; revealDifficultyTabs = true;
        ProjectName = project.Name;
        projectStructureDirty = false;
        ResetDifficultyView();
        PreloadProjectHitsounds();
    }

    private void ResetDifficultyView()
    {
        ResetDeferredConversion();
        pauseSnapDivisor = null;
        nextFruitNewCombo = false;
        nextSounds = 0; soundEdge = null;
        var document = Document;
        convertedSnapshot = null;
        Select(Guid.Empty);
        tool = Tool.Select;
        menu = -1;
        ResetView();
        playhead = viewStart = 0;
        AudioReady = AudioPlaying = false;
        initializeTransport = AudioLoading = !string.IsNullOrWhiteSpace(document.AudioPath);
        AudioDurationMs = 0;
        AudioNotice = L.Get(AudioLoading ? "editor.audio.loading" : "editor.audio.notLoaded");
        pinPlayhead = true;
        StatusMessage = L.Get("editor.status.documentOpened", document.Name, document.TimingPoints.Count);
    }

    public BeatmapProject CaptureProject() => new()
    {
        Name = ProjectName,
        Difficulties = difficulties.Select(d => new ProjectDifficulty { Id = d.Id, Name = d.Name, Document = d.History.Document.DeepClone() }).ToList()
    };

    private MapDocument NewAuthoringDocument()
    {
        var document = new MapDocument
        {
            IsDemo = false, DerandomizeFSliderDroplets = LibrarySettings.DerandomizeNewProjects, RandomizeNewSliders = !LibrarySettings.DerandomizeNewProjects
        };
        SongSetup.Set(document, "General", "Mode", "2");
        foreach (var (key, value) in new[]
        {
            ("AudioLeadIn", "0"), ("PreviewTime", "-1"), ("Countdown", "1"), ("CountdownOffset", "0"),
            ("SampleSet", "Normal"), ("StackLeniency", "0.7"), ("LetterboxInBreaks", "0"),
            ("WidescreenStoryboard", "0"), ("EpilepsyWarning", "0")
        }) SongSetup.Set(document, "General", key, value);
        SongSetup.Set(document, "Metadata", "Title", document.Name);
        SongSetup.Set(document, "Metadata", "TitleUnicode", document.Name);
        SongSetup.Set(document, "Metadata", "Version", L.Get("project.defaultDifficulty", 1));
        SongSetup.Set(document, "Metadata", "BeatmapID", "0");
        SongSetup.Set(document, "Metadata", "BeatmapSetID", "-1");
        SongSetup.Set(document, "Difficulty", "HPDrainRate", "5");
        SongSetup.Set(document, "Difficulty", "OverallDifficulty", "5");
        document.TimingPoints.Add(new TimingPoint { TimeMs = document.TimingOffsetMs, BeatLengthMs = document.BeatLengthMs, SourceOrder = 0 });
        return document;
    }

    public void NewProject()
    {
        var document = NewAuthoringDocument();
        LoadProject(BeatmapProject.FromDocuments([document]));
    }

    public bool SwitchDifficulty(int index)
        => SwitchDifficultyCore(index, reloadAudio: true);

    private bool SwitchDifficultyCore(int index, bool reloadAudio)
    {
        if (workspaceSaveTask is not null || syncCommitTask is not null) { NotifySynchronizationBlocked(); return false; }
        if (index < 0 || index >= difficulties.Count) return false;
        if (!syncBypass && WorkspaceSession?.Manifest.Difficulties.FirstOrDefault(d => d.Id == difficulties[index].Id) is { } linked
            && WorkspaceSynchronization.Target(linked) is { } target && !File.Exists(target)
            && DifficultySyncState(index) is WorkspaceSyncState.Local or WorkspaceSyncState.Current)
        {
            if (!SynchronizationBusy) RefreshSynchronization(quiet: true);
        }
        if (!syncBypass && WorkspaceSession is not null && ShowSyncProblem(index)) return false;
        if (index == activeDifficulty) return true;
        if (!PrepareFileOperation()) return false;
        double currentPlayhead = playhead;
        double currentViewStart = viewStart;
        activeDifficulty = index;
        RevealDifficultyTab();
        ResetDifficultyView();
        playhead = currentPlayhead;
        viewStart = currentViewStart;
        if (reloadAudio) RequestDifficultyChanged?.Invoke();
        return true;
    }

    public bool AddDifficulty(MapDocument? imported = null)
    {
        if (!PrepareFileOperation()) return false;
        if (imported?.SourcePath is { } source)
        {
            int existing = difficulties.FindIndex(d =>
            {
                var entry = WorkspaceSession?.Manifest.Difficulties.FirstOrDefault(e => e.Id == d.Id);
                return WorkspaceSynchronization.Paths.Equals(entry is null ? d.History.Document.SourcePath : WorkspaceSynchronization.Target(entry), source);
            });
            if (existing >= 0) return SwitchDifficulty(existing);
            WorkspaceAssociations.EnsureImport(WorkspaceSession, LibrarySettings.Workspace, source);
        }
        if (difficulties.Count >= 256) { SetNotice(L.Get("project.invalid")); return false; }
        var document = imported?.DeepClone() ?? Document.DeepClone();
        string name = imported is null ? L.Get("project.defaultDifficulty", difficulties.Count + 1)
            : OsuBeatmapReader.Setting(document, "Metadata", "Version") ?? L.Get("project.defaultDifficulty", difficulties.Count + 1);
        if (imported is null)
        {
            document.HitsoundOverrides.Clear();
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
        var added = new ProjectDifficulty { Name = name, Document = document };
        difficulties.Add(new DifficultySession(added));
        if (imported is null && WorkspaceSession is { } workspace)
            workspace.Manifest.Difficulties.Add(new WorkspaceDifficulty { Id = added.Id, Name = name });
        projectStructureDirty = true;
        PreloadProjectHitsounds();
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
        if (previousSaveRestore is not null) return false;
        if (AudioProjectCreating) return false;
        if (workspaceSaveTask is not null || syncCommitTask is not null) { NotifySynchronizationBlocked(); return false; }
        if (VersionHistoryVisible || SynchronizationVisible) return false;
        if (librarySettingsOpen || HitsoundCopierVisible || SongSetupVisible || DistanceSnapDialogVisible || TimingModal || AimodVisible) return false;
        if (!CommitTimingField()) return false;
        if (SliderMultiplierValidationBusy)
        { StatusMessage = L.Get("timing.sliderMultiplierChecking"); return false; }
        if (SliderDialogVisible || ErrorVisible) return false;
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

    public void UpdateTransport(double positionMs, double durationMs, bool ready, bool playing, bool loading,
        string? error, string? filename, double? sampledAtMs = null, double outputBufferAheadMs = 0)
    {
        if (initializeTransport)
        {
            // A retiring audio session must not repaint the new difficulty's initial frame.
            if (!string.Equals(filename, Document.AudioPath, StringComparison.OrdinalIgnoreCase)) return;
            if (ready && !loading)
            {
                double initialPosition = Math.Clamp(playhead, 0, double.IsFinite(durationMs) ? Math.Max(0, durationMs) : 0);
                if (Math.Abs(positionMs - initialPosition) > .01) RequestSeek?.Invoke(initialPosition);
                positionMs = initialPosition;
                initializeTransport = false;
            }
            else if (!loading) initializeTransport = false;
        }
        transportSampleAt = sampledAtMs ?? TestplayRealtime;
        transportSamplePosition = positionMs;
        transportSampleLeadMs = outputBufferAheadMs;
        UpdateHitsounds(positionMs, ready && playing && !loading, filename);
        if (playing && drag == DragKind.PlaybackLine) CancelPlaybackLineDrag();
        bool wasReady = AudioReady;
        AudioReady = ready; AudioPlaying = playing; AudioLoading = loading;
        AudioDurationMs = double.IsFinite(durationMs) ? Math.Max(0, durationMs) : 0;
        AudioNotice = error ?? (loading ? L.Get("editor.audio.loading") : ready ? Path.GetFileName(filename) ?? L.Get("editor.audio.loaded") : L.Get("editor.audio.notLoaded"));
        if (ready && drag is not (DragKind.Timeline or DragKind.DifficultySeek) && !IsTestplaying)
            playhead = Math.Clamp(positionMs, 0, TimelineDurationMs);
        if (playing || ready && !wasReady) FollowPlayhead();
        if (testplay is not null && testplayWithAudio)
        {
            if (testplayDriver is null)
            {
                testplay!.UpdateAudio(positionMs, transportSampleAt, AudioDurationMs, ready, playing, loading,
                    error is not null, outputBufferAheadMs);
                AdvanceTestplay();
            }
        }
        if (!ready || loading || error is not null || IsTestplaying) pauseSnapDivisor = null;
        if (!playing && pauseSnapDivisor is { } pauseDivisor)
        {
            pauseSnapDivisor = null;
            if (snap && !TimingPageVisible && !LibraryVisible && !librarySettingsOpen && !SongSetupVisible && !TimingModal)
                SeekTo(Math.Clamp(TimingMap.Snap(Document, positionMs, pauseDivisor), 0, AudioDurationMs));
        }
    }

    private void SeekTo(double time)
    {
        pauseSnapDivisor = null;
        wheelPlayhead = double.NaN;
        playhead = Math.Clamp(time, 0, TimelineDurationMs);
        FollowPlayhead();
        ResetHitsounds();
        RequestSeek?.Invoke(playhead);
        StatusMessage = L.Get("editor.status.seek", Time(playhead), AudioReady ? "" : L.Get("editor.audio.notLoadedSuffix"));
    }

    private void FollowPlayhead()
    {
        if (ViewportFrozenByDrag) return;
        pinPlayhead = true;
        viewStart = playhead - plot.Height * playbackLineFromBottom / pixelsPerMs;
    }

    private void TogglePlayback()
    {
        if (AudioReady)
        {
            // Arm before the callback: hosts may publish the confirmed pause synchronously.
            pauseSnapDivisor = AudioPlaying && snap && !TimingPageVisible ? divisor : null;
            RequestTogglePlayback?.Invoke();
        }
        else StatusMessage = AudioLoading ? L.Get("editor.audio.stillLoading") : L.Get("editor.audio.loadFromFileMenu");
    }
}
