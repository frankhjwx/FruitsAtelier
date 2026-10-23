namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool projectMemoryReclaimPending;
    private bool difficultyGraphicsReleasePending;
    private readonly List<Task> retiredDifficultyWork = [];

    private void ReclaimProjectMemory()
    {
        retiredDifficultyWork.RemoveAll(task => task.IsCompleted);
        if (!projectMemoryReclaimPending || AudioPlaying || IsTestplaying || WantsCapture || RatingEditInProgress
            || retiredDifficultyWork.Count != 0
            || LibraryOpening || SynchronizationBusy || resourceCheckTask is { IsCompleted: false }
            || difficulties.Any(d => d.RatingTask is not null) || !LibraryVisible && difficultySortPending) return;
        projectMemoryReclaimPending = false;
        // Large import/read-back arrays leave LOH holes even after ordinary collections.
        // Compact once after project preparation or retirement, never on an edit or playback frame.
        if (GC.GetGCMemoryInfo().TotalCommittedBytes < 256L * 1024 * 1024) return;
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }

    private void ReleaseDifficultyCaches()
    {
        ClearDiscussionRegions();
        difficultyGraphicsReleasePending = true;
        projectMemoryReclaimPending = true;
        // The library does not render a difficulty, so lazy invalidation cannot release its data.
        editorWriteCache = new(); editorConversionCache = new(); draftConversionCache = new();
        conversion = null; playableExport = null; playableObjects = [];
        renderedTiming = snapTiming = null;
        importedCurveSnapshot = null; importedCurveDistances.Clear(); importedCurveScreens.Clear();
        importedCurveSources.Clear(); importedCurvePaths.Clear();
        osuCanvasSource = null; osuCanvasObjects = []; osuCanvasMaximumEnds = []; osuCanvasVisible.Clear();
        osuDraftPathSource = osuDraftCandidate = null; osuDraftPath = []; osuDraftStrokePath = []; osuDraftInputs = default;
        timelineConversion = null; timelineSources = []; timelineMarkers = [];
        timelineStarts = []; timelineEnds = []; timelinePrefixEnd = [];
        timelineVisibleMarkers.Clear(); timelineObjects.Clear(); timelinePatternEvents.Clear(); timelineEventNumbers.Clear();
        previewSource = null; previewObjects = []; previewAutoplay = null; previewPlate = null;
        previewLighting = []; previewHyperdash.Clear(); previewComboEnds.Clear();
        hitsoundConversion = null; hitsoundDocument = null; resolvedHitsounds = [];
        distanceConversion = null; distanceReferences = [];
        objectSnapConversion = null; objectSnapFruits = []; objectSnapTarget = null; EndObjectDragSnap();
        lazerDistanceConversion = null; lazerDistanceReferences = []; lazerDistanceParentEnds.Clear();
        lazerVisualSource = null; lazerAutoplay = null; lazerLighting = []; lazerCaught.Clear();
        movementSnapshots.Clear();
        sampleBankConversion = null; sliderSnapLabelSource = null; sliderSnapLabels.Clear();
        inspectorExport = null; inspectorSnapshot = null; inspectorRows.Clear();
        songDockSnapshot = randomDockSnapshot = patternDockSnapshot = null;
        liveSongSnapshot = liveRandomSnapshot = livePatternSnapshot = null;
        placementWriteCache = new(); placementSource = null; placementOrderSource = null;
        placementMovementObjects = null; placementDraftEvents = []; placementDraftTrack = null;
        rotationConversionCache = new(); rotationValidation = null; bananaEditCache = new(); bananaEditSnapshot = null;
        metronomeSnapshot = null; cachedCurveSamples = null;
        hyperdashObjects.Clear(); skinIndices.Clear(); comboEntries = [];
    }
}
