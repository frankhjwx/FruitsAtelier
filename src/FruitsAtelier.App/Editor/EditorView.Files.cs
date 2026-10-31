using L = FruitsAtelier.Localization.Strings;
using FruitsAtelier.Core;
using FruitsAtelier.App.Platform;

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
        if (PatternEditingActive) throw new InvalidOperationException(L.Get("gallery.finishEditing"));
        project.Validate();
        if (SliderCanvasChoiceVisible) AnswerDiscard(2);
        timingAlignmentChecksEnabled = false; timingAlignmentChecked.Clear();
        timingAlignmentPlan = null; timingAlignmentSource = null;
        StopDiscussions();
        patternRestoringTransport = null;
        if (MapTemplatesVisible) CloseMapTemplates(false);
        ClearFadReview();
        copiedPatternText = null; pendingPatternPaste = null; pendingPatternImport = null;
        objectClipboard = null; clipboardDifficulty = null; ObjectClipboardText = "";
        foreach (var difficulty in project.Difficulties) WorkspaceTimingSynchronization.NormalizeDocument(difficulty.Document);
        CancelLegacyDropletRefresh();
        if (libraryOpenTask is { } retiredOpen)
            _ = retiredOpen.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
        libraryOpenTask = null; libraryOpenCompleted = null; libraryOpenDifficultyPath = null; pendingLibraryDifficultyPath = null;
        CloseVersionHistory();
        CloseAimod();
        CloseMapsetVerifier();
        CloseHitsoundCopier();
        CloseSongSetup();
        CloseTimingSetup(); TimingPageVisible = false;
        collaborationField = collaborationError = "";
        collaborationSyncObjectSounds = true;
        CloseCollaborationReport();
        if (collaborationTask is { } retiredCollaboration) _ = retiredCollaboration.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
        collaborationTask = null; collaborationSnapshot = null; collaborationOwner = null;
        TimeJumpVisible = false;
        RotationVisible = rotationDragging = false;
        CloseBananaEdit(); bananaSegments.Clear();
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
        if (sliderImportTask is { } retiredImport) _ = retiredImport.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
        sliderImportTask = null; sliderSourceTargets = []; sliderSourceIndex = 0; SliderImportSourcePromptVisible = false;
        sliderSourceQuestions = null; sliderSourceCompensated = null; sliderImportWizard = false; rememberSliderImportChoice = false;
        WorkspaceSession = null; resourceErrors = [];
        var retiredSyncCancellation = syncCancellation;
        retiredSyncCancellation?.Cancel(); syncCancellation = null;
        if (syncTask is { } retiredSync) _ = retiredSync.ContinueWith(t => { _ = t.Exception; retiredSyncCancellation?.Dispose(); }, TaskScheduler.Default);
        else retiredSyncCancellation?.Dispose();
        syncTask = null; syncRetry = null; afterSynchronization = null; syncReviewRequested = false; syncSearching.Clear();
        syncStatuses.Clear(); syncMerges.Clear(); syncPage = null; nextSyncCheck = DateTime.MaxValue;
        syncDifficulty = Guid.Empty; syncPreserveHistory = false;
        fileSyncPending = fileSearchMissing = false; acceptedFileCheck = null; nextMonitorConfiguration = DateTime.MinValue;
        syncRetainedReview.Clear();
        unreadableSyncRetries = 0;
        syncComparisons.Clear(); syncVisualMerge = null; syncResultPane = null; syncPreviewRevision++;
        resourceSnapshot = null; resourceReferences = null;
        if (resourceCheckTask is { } retiredResources)
        {
            retiredDifficultyWork.Add(retiredResources);
            _ = retiredResources.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
        }
        resourceCheckTask = null;
        CancelInteraction();
        foreach (var difficulty in difficulties)
        {
            difficulty.RatingCancellation.Cancel();
            if (difficulty.RatingTask is { } rating) retiredDifficultyWork.Add(rating);
        }
        difficulties.Clear();
        difficulties.AddRange(project.Difficulties.Select(d => new DifficultySession(d)));
        activeDifficulty = firstDifficultyTab = 0;
        difficultyTabOrder = []; difficultySortPending = true;
        difficultyTabsManuallyOrdered = false; pinnedDifficulties.Clear();
        RestoreMapEditingPreferences();
        tabPointer = false; tabRemainder = 0; revealDifficultyTabs = true;
        ProjectName = project.Name;
        projectNotes = savedProjectNotes = project.Notes; notesFocused = false; notesScroll = 0;
        notesUndo.Clear(); notesRedo.Clear(); ResetPatternDock();
        projectStructureDirty = false;
        ResetDifficultyView();
        PreloadProjectHitsounds();
    }

    private void ResetDifficultyView()
    {
        timingAlignmentPlan = null; timingAlignmentSource = null;
        RestoreTemporarySnap();
        RestoreSnapPreferences();
        CloseAimod();
        ResetDeferredConversion();
        pauseSnapDivisor = null;
        nextFruitNewCombo = false;
        nextSounds = 0; soundEdge = null; timelineFruit = null;
        var document = Document;
        convertedSnapshot = null;
        ReleaseDifficultyCaches();
        Select(Guid.Empty);
        tool = Tool.Select;
        menu = -1;
        ResetView();
        playhead = viewStart = 0;
        ResetLazerPresentation();
        AudioReady = AudioPlaying = false;
        initializeTransport = AudioLoading = !string.IsNullOrWhiteSpace(document.AudioPath);
        AudioDurationMs = 0;
        AudioNotice = L.Get(AudioLoading ? "editor.audio.loading" : "editor.audio.notLoaded");
        pinPlayhead = true;
        StatusMessage = L.Get("editor.status.documentOpened", document.Name, document.TimingPoints.Count);
        RefreshLoadedAimod();
        EnsureMapsetVerifierReport();
    }

    public BeatmapProject CaptureProject() => new()
    {
        Name = ProjectName, Notes = projectNotes,
        Difficulties = difficulties.Select(d => new ProjectDifficulty { Id = d.Id, Name = d.Name, FaOnly = d.FaOnly, Document = d.History.Document.DeepClone() }).ToList()
    };

    private MapDocument NewAuthoringDocument()
    {
        var document = new MapDocument
        {
            IsDemo = false, DerandomizeFSliderDroplets = LibrarySettings.DerandomizeNewProjects, RandomizeNewSliders = !LibrarySettings.DerandomizeNewProjects,
            DerandomizeDropletsForHardRock = LibrarySettings.DefaultDropletHardRock,
            RandomizeDropletStrength = LibrarySettings.DefaultDropletStrength, RandomizeDropletSeed = LibrarySettings.DefaultDropletSeed
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
        if (!CommitCollaborationField()) return false;
        if (!PrepareFileOperation(allowDifficultySave: true)) return false;
        double currentPlayhead = playhead;
        double currentViewStart = viewStart;
        activeDifficulty = index;
        collaborationField = collaborationError = ""; collaborationPanelScroll = 0;
        RevealDifficultyTab();
        ResetDifficultyView();
        playhead = currentPlayhead;
        viewStart = currentViewStart;
        if (reloadAudio) RequestDifficultyChanged?.Invoke();
        return true;
    }

    public bool AddDifficulty(MapDocument? imported = null, bool faOnly = false,
        int? copyFrom = null, MapTemplate? template = null, string? difficultyName = null)
    {
        long started = Performance.Start();
        try { return AddDifficultyCore(imported, faOnly, copyFrom, template, difficultyName); }
        finally { Performance.End(EditorPerformanceStage.DifficultyAdd, started); }
    }

    private bool AddDifficultyCore(MapDocument? imported, bool faOnly, int? copyFrom, MapTemplate? template, string? difficultyName)
    {
        if (RejectPatternProjectOperation()) return false;
        if (!PrepareFileOperation()) return false;
        if (copyFrom is { } sourceIndex && (sourceIndex < 0 || sourceIndex >= difficulties.Count || imported is not null || template is not null)) return false;
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
        var document = (imported ?? (copyFrom is { } index ? difficulties[index].History.Document : Document)).DeepClone();
        if (imported is not null) LibraryOperations.ApplyImportDropletDefaults(document, LibrarySettings);
        WorkspaceTimingSynchronization.NormalizeDocument(document);
        string name = imported is null ? L.Get("project.defaultDifficulty", difficulties.Count + 1)
            : OsuBeatmapReader.Setting(document, "Metadata", "Version") ?? L.Get("project.defaultDifficulty", difficulties.Count + 1);
        if (imported is null)
        {
            name = string.IsNullOrWhiteSpace(difficultyName) ? name : difficultyName.Trim();
            if (copyFrom is null)
            {
                document.HitsoundOverrides.Clear();
                document.CollaborationReferences = null;
                document.Fruits.Clear(); document.Tracks.Clear(); document.ImportedSliders.Clear(); document.BananaShowers.Clear();
                document.IsDemo = false;
                foreach (var section in document.OriginalSections.Where(s => s.Name == "HitObjects")) section.Lines.Clear();
            }
            template?.Apply(document);
            var metadata = document.OriginalSections.FirstOrDefault(s => s.Name == "Metadata");
            if (metadata is null) { metadata = new OsuSection { Name = "Metadata" }; document.OriginalSections.Add(metadata); }
            metadata.Lines.RemoveAll(line => line.Split(':', 2)[0].Trim() is "Version" or "BeatmapID");
            metadata.Lines.Add("Version:" + name);
            metadata.Lines.Add("BeatmapID:0");
        }
        OsuBeatmapReader.Validate(document);
        var added = new ProjectDifficulty { Name = name, Document = document, FaOnly = faOnly };
        difficulties.Add(new DifficultySession(added));
        if (imported is null && WorkspaceSession is { } workspace)
            workspace.Manifest.Difficulties.Add(new WorkspaceDifficulty { Id = added.Id, Name = name, FaOnly = faOnly });
        projectStructureDirty = true;
        PreloadProjectHitsounds();
        bool switched = SwitchDifficulty(difficulties.Count - 1);
        if (switched && imported is not null) PrepareImportedSliders(false);
        return switched;
    }

    public void MarkSaved()
    {
        if (RejectPatternProjectOperation()) return;
        foreach (var difficulty in difficulties) difficulty.History.MarkSaved();
        projectStructureDirty = false; savedProjectNotes = projectNotes;
    }

    public void ChangeAudioPath(string path) => Edit(L.Get("editor.command.changeAudio"), () => Document.AudioPath = path);

    public bool PrepareFileOperation(bool allowDifficultySave = false)
    {
        if (ErrorVisible) return false;
        if (MapTemplatesVisible || addDifficultyConfirmation) return false;
        if (CollaborationBusy) return false;
        if (!CommitCollaborationField()) return false;
        PumpLegacyDropletRefresh();
        if (LegacyDropletRefreshBusy) return false;
        if (AcknowledgementsVisible) return false;
        if (previousSaveRestore is not null) return false;
        if (FazVisible || LibraryOpening || AudioProjectCreating) return false;
        if (difficultySaveTask is not null && !allowDifficultySave) { NotifySynchronizationBlocked(); return false; }
        if (workspaceSaveTask is not null || syncCommitTask is not null) { NotifySynchronizationBlocked(); return false; }
        if (VersionHistoryVisible || SynchronizationVisible) return false;
        if (librarySettingsOpen || HitsoundCopierVisible || SongSetupVisible || DistanceSnapDialogVisible || TimingModal || RotationVisible) return false;
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
        if (PatternEditingActive && !string.Equals(filename, Document.AudioPath, StringComparison.OrdinalIgnoreCase)) return;
        PumpLegacyDropletRefresh();
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
        if (playing && !AudioPlaying) lazerSeekStarted = double.NegativeInfinity;
        double previousPlayhead = playhead;
        AudioReady = ready; AudioPlaying = playing; AudioLoading = loading;
        AudioDurationMs = double.IsFinite(durationMs) ? Math.Max(0, durationMs) : 0;
        RefreshAimodAudio();
        AudioNotice = error ?? (loading ? L.Get("editor.audio.loading") : ready ? Path.GetFileName(filename) ?? L.Get("editor.audio.loaded") : L.Get("editor.audio.notLoaded"));
        if (ready && drag is not (DragKind.Timeline or DragKind.DifficultySeek) && !IsTestplaying)
            playhead = Math.Clamp(positionMs, 0, TimelineDurationMs);
        double previousViewStart = viewStart;
        if (playing || ready && !wasReady) FollowPlayhead();
        if (playing && !IsLazerEditing) ContinueDragAfterNavigation(previousViewStart, previousPlayhead, shiftHeld, placementCtrl, requireStarted: true);
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
        RestorePatternTransportAfterLoad(ready, loading);
    }

    private void SeekTo(double time)
    {
        lazerSeekStarted = double.NegativeInfinity;
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
        if (ViewportFrozenByDrag && !IsLazerEditing) return;
        pinPlayhead = true;
        viewStart = playhead - plot.Height * EffectivePlaybackLineFromBottom / pixelsPerMs;
    }

    private void TogglePlayback()
    {
        if (!AudioPlaying && DeferMediaPlayback(false)) return;
        if (AudioReady)
        {
            if (IsLazerEditing && !AudioPlaying && TestplayRealtime < lazerSeekStarted + 300)
                SeekTo(LazerPresentationTime);
            // Arm before the callback: hosts may publish the confirmed pause synchronously.
            pauseSnapDivisor = !IsLazerEditing && AudioPlaying && snap && !TimingPageVisible ? divisor : null;
            RequestTogglePlayback?.Invoke();
        }
        else StatusMessage = AudioLoading ? L.Get("editor.audio.stillLoading") : L.Get("editor.audio.loadFromFileMenu");
    }
}
