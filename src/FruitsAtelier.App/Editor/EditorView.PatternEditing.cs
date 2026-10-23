using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using FruitsAtelier.Core.Patterns;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private sealed record PatternReturnSession(
        DifficultySession[] Difficulties, int Active, WorkspaceSession? Workspace, string Name,
        string Notes, string SavedNotes, string[] NotesUndo, string[] NotesRedo, bool StructureDirty,
        bool HasProject, double Playhead, double ViewStart, double Zoom, double PixelsPerMs, bool PinPlayhead,
        Guid[] Objects, Guid[] Anchors, Guid Selection, Guid SelectedTrack, DragKind SelectedPart, Tool Tool,
        int FirstTab, int[] TabOrder, bool ManualTabOrder, Guid[] Pinned, string Status,
        double TimeZoom, double PanX, double OsuZoom, double OsuPanX, double OsuPanY, double LazerPanX,
        MapDocument? Clipboard, Guid? ClipboardDifficulty, string ClipboardText,
        double LazerZoom, double LazerTimeZoom, double LazerTimelineZoom);
    private PatternReturnSession? patternReturnSession, retainedPatternProject;
    private PatternSnapshot? editingPattern;
    private Task<PatternSnapshot>? patternEditingSaveTask;
    private Action? patternAfterExit, patternExitCancelled;
    private bool patternRetainOriginal;
    private Rect patternEditingBannerBounds;
    private (double Playhead, double ViewStart, bool Pin)? patternRestoringTransport;
    public bool PatternEditingActive => editingPattern is not null;
    public bool PatternEditingBusy => patternEditingSaveTask is not null;
    public bool PatternEditingNeedsRedraw => PatternEditingBusy;
    public PatternSnapshot? EditingPattern => editingPattern;
    public bool HasRetainedPatternProject => retainedPatternProject is not null;
    private float PatternEditingBannerHeight => (PatternEditingActive || HasRetainedPatternProject) && !TimingPageVisible ? 88 : 0;

    private bool RejectPatternProjectOperation()
    {
        if (!PatternEditingActive) return false;
        SetNotice(L.Get("gallery.finishEditing"));
        return true;
    }

    public bool BeginPatternEditing(PatternSnapshot snapshot)
    {
        if (HasRetainedPatternProject && !PatternEditingActive)
            return RestoreRetainedPatternProject(() => BeginPatternEditing(snapshot));
        if (PatternEditingActive || PatternEditingBusy || galleryIoTask is not null || DiscardConfirmationVisible
            || SynchronizationBusy || !PrepareFileOperation()) return false;
        var draft = new DifficultySession(new ProjectDifficulty { Name = snapshot.Name, Document = snapshot.CreateDocument(), FaOnly = true });
        patternReturnSession = CapturePatternReturnSession();
        if (AudioPlaying) RequestTogglePlayback?.Invoke();
        RetirePatternContextWork();
        editingPattern = snapshot;
        CancelPatternGalleryInteraction();
        galleryFocused = false;
        CloseAimod(); CloseSongSetup(); CloseTimingSetup(); ResetPatternDock();
        TimingPageVisible = false; LibraryVisible = false;
        WorkspaceSession = null;
        difficulties.Clear(); difficulties.Add(draft); activeDifficulty = firstDifficultyTab = 0;
        difficultyTabOrder = []; difficultyTabsManuallyOrdered = false; pinnedDifficulties.Clear();
        ProjectName = snapshot.Name; projectNotes = savedProjectNotes = ""; notesUndo.Clear(); notesRedo.Clear();
        projectStructureDirty = false; HasEditorProject = true;
        ResetDifficultyView();
        playhead = snapshot.StartTimeMs;
        viewStart = playhead - Math.Max(0, plot.Height * EffectivePlaybackLineFromBottom / pixelsPerMs);
        RequestDifficultyChanged?.Invoke();
        StatusMessage = L.Get("gallery.editing", snapshot.Name);
        return true;
    }

    public bool SavePatternEditing()
    {
        if (editingPattern is not { } snapshot || galleryStore is not { } store || PatternEditingBusy
            || galleryIoTask is not null || !PrepareFileOperation()) return false;
        var document = Document.DeepClone();
        patternEditingSaveTask = Task.Run(() =>
        {
            var updated = snapshot.WithDocument(document);
            store.Replace(updated);
            return updated;
        });
        return true;
    }

    public void DiscardPatternEditing()
    {
        if (!PatternEditingActive || PatternEditingBusy) return;
        ExitPatternEditing();
    }

    public void RequestExitPatternEditing(Action? afterExit = null, bool retainOriginalForNavigation = false, Action? cancelled = null)
    {
        if (!PatternEditingActive) { afterExit?.Invoke(); return; }
        if (PatternEditingBusy || DiscardConfirmationVisible || !PrepareFileOperation()) { cancelled?.Invoke(); return; }
        patternAfterExit = afterExit;
        patternExitCancelled = cancelled;
        patternRetainOriginal = retainOriginalForNavigation;
        if (!history.IsDirty && !retainOriginalForNavigation) { ExitPatternEditing(); return; }
        ShowDiscardConfirmation(answer =>
        {
            if (answer == 6)
            {
                if (!SavePatternEditing()) CancelPatternExit();
            }
            else if (answer == 7) ExitPatternEditing();
            else CancelPatternExit();
        });
    }

    internal void PumpPatternEditing()
    {
        if (patternEditingSaveTask is not { IsCompleted: true } task) return;
        patternEditingSaveTask = null;
        try
        {
            var updated = task.GetAwaiter().GetResult();
            galleryPatterns = galleryPatterns.Select(p => p.Id == updated.Id ? updated : p).ToArray();
            galleryFullPreview = null; galleryFullPreviewId = Guid.Empty;
            galleryPreviews.Remove(updated.Id); galleryPreviewErrors.Remove(updated.Id);
            galleryPreviewGeneration++;
            galleryPreviewTask = null;
            ExitPatternEditing();
            SetNotice(L.Get("gallery.saved"));
        }
        catch (Exception ex)
        {
            CancelPatternExit();
            ShowError(ex.Message);
        }
    }

    public bool RestoreRetainedPatternProject(Action? restored = null, Action? cancelled = null)
    {
        if (retainedPatternProject is not { } retained || PatternEditingActive || !PrepareFileOperation()) { cancelled?.Invoke(); return false; }
        bool sameProject = difficulties.Count == retained.Difficulties.Length
            && difficulties.Zip(retained.Difficulties).All(pair => ReferenceEquals(pair.First, pair.Second));
        if (IsDirty && !sameProject)
        {
            ShowDiscardConfirmation(answer =>
            {
                if (answer == 7) RestoreRetained();
                else if (answer == 6)
                {
                    if (!BeginWorkspaceSave(saved => { if (saved) RestoreRetained(); else cancelled?.Invoke(); })) cancelled?.Invoke();
                }
                else cancelled?.Invoke();
            });
            return false;
        }
        RestoreRetained();
        return true;

        void RestoreRetained()
        {
            retainedPatternProject = null;
            RestorePatternReturnSession(retained);
            restored?.Invoke();
        }
    }

    public bool ResolvePatternSessionsBeforeLeaving(Action continuation, Action? cancelled = null)
    {
        if (PatternEditingActive)
        {
            RequestExitPatternEditing(continuation, cancelled: cancelled);
            return true;
        }
        if (HasRetainedPatternProject)
        {
            RestoreRetainedPatternProject(continuation, cancelled);
            return true;
        }
        return false;
    }

    public Task<bool> ResolvePatternSessionsBeforeLeavingAsync()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!ResolvePatternSessionsBeforeLeaving(() => completion.TrySetResult(true), () => completion.TrySetResult(false)))
            completion.TrySetResult(true);
        return completion.Task;
    }

    private void CancelPatternExit()
    {
        var cancelled = patternExitCancelled;
        patternAfterExit = patternExitCancelled = null; patternRetainOriginal = false;
        cancelled?.Invoke();
    }

    private PatternReturnSession CapturePatternReturnSession() => new(difficulties.ToArray(), activeDifficulty,
        WorkspaceSession, ProjectName, projectNotes, savedProjectNotes, notesUndo.ToArray(), notesRedo.ToArray(),
        projectStructureDirty, HasEditorProject, playhead, viewStart, canvasZoom, pixelsPerMs, pinPlayhead,
        objectSelection.ToArray(), anchorSelection.ToArray(), selection, selectedTrack, selectedPart, tool,
        firstDifficultyTab, difficultyTabOrder.ToArray(), difficultyTabsManuallyOrdered, pinnedDifficulties.ToArray(), StatusMessage,
        canvasTimeZoom, canvasPanX, osuCanvasZoom, osuCanvasPanX, osuCanvasPanY, lazerPanX,
        objectClipboard, clipboardDifficulty, ObjectClipboardText, lazerZoomTarget, lazerTimeZoomTarget, lazerTimelineTarget);

    private void ExitPatternEditing()
    {
        if (patternReturnSession is not { } original) return;
        if (patternRetainOriginal) retainedPatternProject = original;
        patternReturnSession = null; editingPattern = null; patternRetainOriginal = false;
        var after = patternAfterExit; patternAfterExit = patternExitCancelled = null;
        RestorePatternReturnSession(original);
        after?.Invoke();
    }

    private void RestorePatternReturnSession(PatternReturnSession saved)
    {
        CancelInteraction(); CancelLegacyDropletRefresh(); CancelPatternGalleryInteraction();
        RetirePatternContextWork();
        CloseSongSetup(); CloseTimingSetup(); ResetPatternDock(); TimingPageVisible = false; LibraryVisible = false;
        foreach (var session in difficulties.Where(s => !saved.Difficulties.Contains(s)))
        {
            session.RatingCancellation.Cancel();
            if (session.RatingTask is { } task) retiredDifficultyWork.Add(task);
        }
        difficulties.Clear(); difficulties.AddRange(saved.Difficulties); activeDifficulty = saved.Active;
        foreach (var session in difficulties)
            if (session.RatingCancellation.IsCancellationRequested)
            {
                session.RatingCancellation = new(); session.RatingTask = null; session.RatingSnapshot = null;
            }
        WorkspaceSession = saved.Workspace; ProjectName = saved.Name;
        projectNotes = saved.Notes; savedProjectNotes = saved.SavedNotes;
        notesUndo.Clear(); foreach (string value in saved.NotesUndo.Reverse()) notesUndo.Push(value);
        notesRedo.Clear(); foreach (string value in saved.NotesRedo.Reverse()) notesRedo.Push(value);
        projectStructureDirty = saved.StructureDirty; HasEditorProject = saved.HasProject;
        firstDifficultyTab = saved.FirstTab; difficultyTabOrder = saved.TabOrder;
        difficultyTabsManuallyOrdered = saved.ManualTabOrder; pinnedDifficulties.Clear(); pinnedDifficulties.UnionWith(saved.Pinned);
        difficultySortPending = true; revealDifficultyTabs = true;
        ResetDifficultyView();
        canvasZoom = saved.Zoom; pixelsPerMs = saved.PixelsPerMs; playhead = saved.Playhead; viewStart = saved.ViewStart; pinPlayhead = saved.PinPlayhead;
        canvasTimeZoom = saved.TimeZoom; canvasPanX = saved.PanX;
        osuCanvasZoom = saved.OsuZoom; osuCanvasPanX = saved.OsuPanX; osuCanvasPanY = saved.OsuPanY; lazerPanX = saved.LazerPanX;
        lazerZoomFrom = lazerZoomTarget = saved.LazerZoom; lazerTimeZoomFrom = lazerTimeZoomTarget = saved.LazerTimeZoom;
        lazerTimelineFrom = lazerTimelineTarget = saved.LazerTimelineZoom;
        objectClipboard = saved.Clipboard; clipboardDifficulty = saved.ClipboardDifficulty; ObjectClipboardText = saved.ClipboardText;
        objectSelection.Clear(); objectSelection.UnionWith(saved.Objects);
        anchorSelection.Clear(); anchorSelection.UnionWith(saved.Anchors);
        selection = saved.Selection; selectedTrack = saved.SelectedTrack; selectedPart = saved.SelectedPart; tool = saved.Tool;
        patternRestoringTransport = AudioLoading ? (saved.Playhead, saved.ViewStart, saved.PinPlayhead) : null;
        RequestDifficultyChanged?.Invoke();
        if (!AudioLoading) RequestSeek?.Invoke(playhead);
        StatusMessage = saved.Status;
        nextMonitorConfiguration = DateTime.MinValue;
        if (WorkspaceSession is not null) CheckWorkspaceResources();
    }

    private void RetirePatternContextWork()
    {
        var cancellation = syncCancellation;
        cancellation?.Cancel(); syncCancellation = null;
        if (syncTask is { } oldSync)
        {
            retiredDifficultyWork.Add(oldSync);
            _ = oldSync.ContinueWith(t => { _ = t.Exception; cancellation?.Dispose(); }, TaskScheduler.Default);
        }
        else cancellation?.Dispose();
        syncTask = null; syncRetry = null; afterSynchronization = null; syncReviewRequested = false; syncSearching.Clear();
        syncStatuses.Clear(); syncMerges.Clear(); syncPage = null; nextSyncCheck = DateTime.MaxValue;
        syncDifficulty = Guid.Empty; syncPreserveHistory = false;
        fileSyncPending = fileSearchMissing = false; acceptedFileCheck = null; nextMonitorConfiguration = DateTime.MinValue;
        syncRetainedReview.Clear(); unreadableSyncRetries = 0;
        syncComparisons.Clear(); syncVisualMerge = null; syncResultPane = null; syncPreviewRevision++;
        resourceSnapshot = null; resourceReferences = null; resourceErrors = [];
        if (resourceCheckTask is { } oldResources)
        {
            retiredDifficultyWork.Add(oldResources);
            _ = oldResources.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
        }
        resourceCheckTask = null;
    }

    private void DrawPatternEditingBanner(ICanvas c)
    {
        patternEditingBannerBounds = default;
        if (PatternEditingBannerHeight <= 0) return;
        var bounds = new Rect(canvas.X + 10, plot.Y - PatternEditingBannerHeight, Math.Max(200, canvas.Width - 20), 66);
        patternEditingBannerBounds = bounds;
        c.Fill(bounds, Panel, 5); c.Stroke(bounds, Accent, 1, 5);
        string title = PatternEditingActive ? L.Get("gallery.editing", editingPattern!.Name) : L.Get("gallery.originalRetained");
        c.Text(title, bounds.X + 12, bounds.Y + 8, 13, Accent, bounds.Width - 24);
        if (PatternEditingActive)
        {
            float buttonWidth = (bounds.Width - 32) / 3;
            Button(c, new(bounds.X + 8, bounds.Y + 29, buttonWidth, 30), L.Get("gallery.jumpSource"), () => RequestPatternSource?.Invoke(editingPattern!), enabled: !PatternEditingBusy);
            Button(c, new(bounds.X + 16 + buttonWidth, bounds.Y + 29, buttonWidth, 30), L.Get("gallery.saveReturn"), () => SavePatternEditing(), enabled: !PatternEditingBusy);
            Button(c, new(bounds.X + 24 + buttonWidth * 2, bounds.Y + 29, buttonWidth, 30), L.Get("gallery.discardReturn"), DiscardPatternEditing, enabled: !PatternEditingBusy);
        }
        else Button(c, new(bounds.Right - Math.Min(230, bounds.Width - 8), bounds.Y + 29, Math.Min(220, bounds.Width - 16), 30), L.Get("gallery.returnOriginal"), () => RestoreRetainedPatternProject());
    }

    private bool PatternEditingPointerDown(float x, float y, int button)
    {
        if (ErrorVisible || DiscardConfirmationVisible) return false;
        if (PatternEditingBusy) return true;
        if ((!PatternEditingActive && !HasRetainedPatternProject) || !patternEditingBannerBounds.Contains(x, y)) return false;
        if (button == 0)
            for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); break; }
        return true;
    }

    private bool PatternEditingKeyDown(int virtualKey, bool ctrl)
    {
        if (ErrorVisible || DiscardConfirmationVisible) return false;
        if (PatternEditingBusy) return true;
        return false;
    }

    private void RestorePatternTransportAfterLoad(bool ready, bool loading)
    {
        if (patternRestoringTransport is not { } saved || !ready || loading) return;
        patternRestoringTransport = null;
        playhead = saved.Playhead; viewStart = saved.ViewStart; pinPlayhead = saved.Pin;
        RequestSeek?.Invoke(playhead);
    }
}
