using System.Text;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private StoryboardScene? storyboardScene;
    private StoryboardScene? storyboardDisplayScene;
    private string? storyboardDisplayBackground;
    private bool storyboardTransition;
    private Task<StoryboardScene>? storyboardLoad;
    private CancellationTokenSource? storyboardCancellation;
    private readonly List<(string Name, object Identity, string[]? General)> storyboardSource = [];
    private string? storyboardDirectory;
    private readonly List<StoryboardSpriteState> storyboardFrame = [];
    private readonly List<StoryboardSpriteState> storyboardDisplayFrame = [];
    private readonly List<string> storyboardPreload = [];
    private readonly HashSet<string> storyboardPreloadSeen = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> storyboardRequiredImages = [];
    private readonly HashSet<string> storyboardRequiredSeen = new(StringComparer.OrdinalIgnoreCase);
    private bool storyboardPresented;
    private bool storyboardTargetReady;
    private readonly List<string> storyboardRetainedImages = [];
    private bool mediaOpening;
    private bool mediaPreparationInEditor;
    private bool mediaExitPending;
    private bool mediaInitialPreparationComplete;
    private bool storyboardRendererObserved;
    private long storyboardCanvasGeneration;
    private long storyboardPinnedGeneration = -1;
    private long storyboardRenderFrame;
    private long storyboardPreparedFrame = -1;
    private double storyboardFrameTime = double.NaN, storyboardPreloadTime = double.NaN;
    private bool storyboardFramePassing;
    private double? storyboardSamplePosition, storyboardSamplesThrough;
    private Hitsound[] storyboardSampleSounds = [];
    private Task<CatchStoryboardPlayback>? storyboardGameplayLoad;
    private CancellationTokenSource? storyboardGameplayCancellation;
    private object? storyboardGameplaySource;
    private StoryboardScene? storyboardGameplayScene;
    private long storyboardGameplayRevision = -1;
    private CatchStoryboardPlayback? storyboardTemplate, storyboardPreview, storyboardTestplay;
    private readonly Dictionary<(Guid Source, int Index, double Time), ConvertedCatchObject> storyboardPreviewPlayed = [];
    private readonly List<ConvertedCatchObject> storyboardPendingPreviewSounds = [];
    private double? storyboardPreviewConsumedPosition;
    private readonly List<StoryboardTrigger> storyboardTriggers = [];
    private long storyboardTriggerVersion = -1;
    private bool mediaPlayPending, mediaTestplayPending;
    private string? mediaReportedError;
    public bool MediaNeedsRedraw => difficultyPauseTask is not null || storyboardResourcesReleasePending || mediaOpening || mediaPlayPending || mediaTestplayPending
        || storyboardLoad is { IsCompleted: true } || storyboardGameplayLoad is { IsCompleted: true };

    private bool storyboardResourcesReleasePending;

    private void RetireDifficultyStoryboard(bool preservePresentation)
    {
        bool retain = preservePresentation && storyboardPresented && LibrarySettings.EnableVideoStoryboard;
        var scene = storyboardDisplayScene;
        var frame = retain ? storyboardDisplayFrame.ToArray() : [];
        var paths = retain ? storyboardRetainedImages.ToArray() : [];
        string? background = storyboardDisplayBackground;
        long generation = storyboardCanvasGeneration;
        RetireStoryboard();
        if (!retain) return;
        storyboardDisplayScene = scene; storyboardDisplayBackground = background;
        storyboardDisplayFrame.AddRange(frame); storyboardRetainedImages.AddRange(paths);
        storyboardPresented = storyboardTransition = true;
        storyboardRendererObserved = true; storyboardCanvasGeneration = generation;
        storyboardPinnedGeneration = generation;
    }

    private void ApplyMediaPreference()
    {
        bool prepared = mediaInitialPreparationComplete;
        var samples = storyboardScene?.WithoutVisuals();
        bool sampleHealth = samples?.Samples.Any(s => s.Layer is StoryboardLayer.Fail or StoryboardLayer.Pass) == true;
        var sampleTemplate = sampleHealth ? storyboardTemplate?.ForSamples() : null;
        var samplePreview = sampleHealth ? storyboardPreview?.ForSamples() : null;
        var sampleTestplay = sampleHealth ? storyboardTestplay?.ForSamples() : null;
        var sounds = storyboardSampleSounds;
        var position = storyboardSamplePosition;
        var through = storyboardSamplesThrough;
        RetireStoryboard(preserveHistory: true);
        storyboardResourcesReleasePending = true;
        mediaInitialPreparationComplete = prepared;
        RefreshStoryboard();
        storyboardScene = samples; storyboardSampleSounds = sounds;
        storyboardTemplate = sampleTemplate; storyboardPreview = samplePreview; storyboardTestplay = sampleTestplay;
        storyboardSamplePosition = position; storyboardSamplesThrough = through;
    }

    private void ToggleVideoStoryboard()
    {
        LibrarySettings.EnableVideoStoryboard = !LibrarySettings.EnableVideoStoryboard;
        draftEnableVideoStoryboard = LibrarySettings.EnableVideoStoryboard;
        ApplyMediaPreference();
        RequestViewPreference?.Invoke();
    }

    private void RetireStoryboard(bool preserveDisplay = false, bool preserveHistory = false)
    {
        storyboardCancellation?.Cancel(); storyboardCancellation?.Dispose(); storyboardCancellation = null;
        if (storyboardLoad is { } retired) { retiredDifficultyWork.Add(retired); _ = retired.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default); }
        storyboardLoad = null; storyboardSource.Clear(); storyboardDirectory = null;
        if (storyboardGameplayLoad is { } retiredGameplay) { retiredDifficultyWork.Add(retiredGameplay); _ = retiredGameplay.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default); }
        storyboardGameplayLoad = null; storyboardGameplaySource = null; storyboardGameplayScene = null;
        storyboardGameplayCancellation?.Cancel(); storyboardGameplayCancellation?.Dispose(); storyboardGameplayCancellation = null;
        storyboardGameplayRevision = -1;
        if (preserveDisplay) return;
        storyboardScene = storyboardDisplayScene = null;
        storyboardDisplayBackground = null; storyboardTransition = false;
        storyboardFrame.Clear(); storyboardPreload.Clear(); storyboardPreloadSeen.Clear();
        storyboardDisplayFrame.Clear(); storyboardRetainedImages.Clear(); storyboardTargetReady = false;
        storyboardRequiredImages.Clear(); storyboardRequiredSeen.Clear(); storyboardPresented = false;
        mediaOpening = false;
        mediaPreparationInEditor = false;
        mediaExitPending = false;
        mediaInitialPreparationComplete = false;
        storyboardRendererObserved = false; storyboardCanvasGeneration = 0;
        storyboardPinnedGeneration = -1;
        storyboardPreparedFrame = -1;
        storyboardFrameTime = storyboardPreloadTime = double.NaN;
        storyboardSamplePosition = storyboardSamplesThrough = null; storyboardSampleSounds = [];
        storyboardTemplate = storyboardPreview = storyboardTestplay = null;
        if (!preserveHistory) { storyboardPreviewPlayed.Clear(); storyboardPendingPreviewSounds.Clear(); storyboardPreviewConsumedPosition = null; }
        storyboardTriggers.Clear(); storyboardTriggerVersion = -1; mediaPlayPending = mediaTestplayPending = false; mediaReportedError = null;
    }

    private string? StoryboardMediaDirectory(MapDocument document)
    {
        string? origin = document.SourcePath ?? document.AudioPath;
        try { return origin is null ? WorkspaceSession?.Directory : Path.GetDirectoryName(Path.GetFullPath(origin)); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException) { return null; }
    }

    private void RefreshStoryboard()
    {
        string? directory = StoryboardMediaDirectory(Document);
        if (directory is null) { RetireStoryboard(); return; }
        bool same = directory == storyboardDirectory; int index = 0;
        foreach (var section in Document.OriginalSections)
        {
            if (section.Name is not ("Events" or "Variables" or "General")) continue;
            if (index >= storyboardSource.Count || storyboardSource[index].Name != section.Name) same = false;
            else if (!ReferenceEquals(storyboardSource[index].Identity, section.Lines.SnapshotIdentity))
            {
                if (section.Name == "General" && StoryboardGeneralSource(section.Lines).SequenceEqual(storyboardSource[index].General!))
                    storyboardSource[index] = (section.Name, section.Lines.SnapshotIdentity, storyboardSource[index].General);
                else same = false;
            }
            index++;
        }
        if (same && index == storyboardSource.Count) { QueueStoryboardGameplay(); return; }
        bool opening = !mediaInitialPreparationComplete;
        bool exiting = mediaExitPending;
        bool prepareInEditor = mediaPreparationInEditor;
        bool transition = storyboardTransition;
        RetireStoryboard(preserveDisplay: !opening || transition, preserveHistory: true); storyboardDirectory = directory; mediaOpening = opening; mediaExitPending = exiting;
        mediaPreparationInEditor = prepareInEditor;
        var sections = new List<(string Name, string[] Lines)>();
        foreach (var section in Document.OriginalSections)
        {
            if (section.Name is not ("Events" or "Variables" or "General")) continue;
            storyboardSource.Add((section.Name, section.Lines.SnapshotIdentity, section.Name == "General" ? StoryboardGeneralSource(section.Lines) : null));
            sections.Add((section.Name, section.Lines.ToArray()));
        }
        bool samplesOnly = !LibrarySettings.EnableVideoStoryboard;
        var cancellation = storyboardCancellation = new(); var token = cancellation.Token;
        storyboardLoad = Task.Run(() =>
        {
            var source = new StringBuilder();
            foreach (var section in sections) { source.Append('[').Append(section.Name).AppendLine("]"); foreach (string line in section.Lines) source.AppendLine(line); }
            string? sharedPath = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(p => string.Equals(Path.GetExtension(p), ".osb", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            token.ThrowIfCancellationRequested();
            return StoryboardReader.Read(source.ToString(), sharedPath is null ? null : StoryboardReader.ReadSharedFile(sharedPath, token), directory, token, samplesOnly: samplesOnly);
        }, token);
    }

    private static string[] StoryboardGeneralSource(IEnumerable<string> lines)
    {
        // StoryboardReader consumes only WidescreenStoryboard from ordinary General lines.
        // Keep line positions for parser diagnostics. Embedded section boundaries can change
        // the reader's grammar, so retain all content from such a boundary onward.
        bool retain = false;
        return lines.Select(line =>
        {
            string trimmed = line.Trim();
            retain |= trimmed.StartsWith('[') || line.Contains('\n');
            return retain || trimmed.StartsWith("WidescreenStoryboard:", StringComparison.Ordinal) ? line : "";
        }).ToArray();
    }

    private void PumpStoryboard()
    {
        if (storyboardLoad is not { IsCompleted: true } task) return;
        storyboardLoad = null;
        if (task.IsCompletedSuccessfully)
        {
            storyboardScene = task.Result;
            if (!storyboardScene.RequiresGameplay) { storyboardTemplate = storyboardPreview = null; storyboardTriggers.Clear(); storyboardTriggerVersion = -1; }
            storyboardSampleSounds = storyboardScene.Samples.Select(s => new Hitsound(CatchObjectKind.Fruit, s.Path, s.Volume / 100f, "storyboard")).ToArray();
            foreach (var sound in storyboardSampleSounds) RequestPrepareHitsound?.Invoke(sound);
            if (storyboardScene.Diagnostics.Count > 0) SetNotice(L.Get("media.storyboardDiagnostics", storyboardScene.Diagnostics.Count));
            QueueStoryboardGameplay();
        }
        else if (task.Exception is { } error) SetNotice(L.Get("media.storyboardFailed", error.GetBaseException().Message));
        storyboardFrameTime = storyboardPreloadTime = double.NaN;
    }

    private void QueueStoryboardGameplay()
    {
        if (storyboardLoad is not null) return;
        if (storyboardScene is not { RequiresGameplay: true } scene || ReferenceEquals(storyboardGameplayScene, scene)
            && ReferenceEquals(storyboardGameplaySource, playableObjects) && storyboardGameplayRevision == history.ContentRevision) return;
        storyboardGameplaySource = playableObjects;
        storyboardGameplayScene = scene;
        storyboardGameplayRevision = history.ContentRevision;
        var objects = playableObjects.ToArray();
        var snapshot = Document.DeepClone();
        storyboardGameplayCancellation?.Cancel(); storyboardGameplayCancellation?.Dispose();
        storyboardGameplayCancellation = new(); var token = storyboardGameplayCancellation.Token;
        var previous = storyboardGameplayLoad;
        if (previous is { } retired) { retiredDifficultyWork.Add(retired); _ = retired.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default); }
        storyboardGameplayLoad = Task.Run(async () =>
        {
            if (previous is not null) try { await previous.ConfigureAwait(false); } catch (Exception) { }
            token.ThrowIfCancellationRequested();
            double hp = double.TryParse(SongSetup.Get(snapshot, "Difficulty", "HPDrainRate", "5"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double parsed) ? parsed : 5;
            var health = new CatchStoryboardHealth(objects, OsuTimeline.Breaks(snapshot), hp, token);
            var resolver = new HitsoundResolver(snapshot, objects, inspectResources: false);
            var playback = new CatchStoryboardPlayback(health, scene.TriggerNames, objects, resolver, Math.Min(0, objects.FirstOrDefault()?.TimeMs ?? 0));
            return playback;
        }, token);
    }

    private void PumpStoryboardGameplay()
    {
        PumpStoryboard();
        if (storyboardGameplayLoad is { IsCompleted: true } task)
        {
            storyboardGameplayLoad = null;
            if (task.IsCompletedSuccessfully)
            {
                storyboardTemplate = task.Result; storyboardPreview = storyboardTemplate.Fork(0);
                storyboardPreview.RecordSounds(storyboardPreviewPlayed.Values);
                if (IsTestplaying && storyboardTestplay is null) storyboardTestplay = storyboardTemplate.Fork(testplayStart);
            }
            else if (task.Exception is { } error) SetNotice(L.Get("media.storyboardFailed", error.GetBaseException().Message));
            storyboardTriggerVersion = -1; storyboardFrameTime = double.NaN;
        }
        if (mediaOpening || storyboardLoad is not null || storyboardGameplayLoad is not null) return;
        if (mediaTestplayPending) { mediaTestplayPending = false; StartTestplay(); }
        else if (mediaPlayPending) { mediaPlayPending = false; if (!AudioPlaying) TogglePlayback(); }
    }

    private bool DeferMediaPlayback(bool testplay)
    {
        PumpStoryboardGameplay();
        if (!mediaOpening && storyboardLoad is null && storyboardGameplayLoad is null) return false;
        if (testplay) mediaTestplayPending = true; else mediaPlayPending = true;
        return true;
    }

    private bool StoryboardPassingAt(double time)
    {
        if (IsTestplaying) return storyboardTestplay?.CopyAt(time, storyboardTriggers, ref storyboardTriggerVersion) ?? true;
        storyboardPreview?.CopyTriggers(storyboardTriggers, ref storyboardTriggerVersion);
        return true;
    }

    private void PrepareStoryboardFrame(ICanvas canvas)
    {
        if (storyboardPreparedFrame == storyboardRenderFrame) return;
        storyboardPreparedFrame = storyboardRenderFrame;
        PumpStoryboardGameplay();
        if (!LibrarySettings.EnableVideoStoryboard || storyboardScene is null) return;
        if (!storyboardRendererObserved || storyboardCanvasGeneration != canvas.MediaGeneration)
        {
            storyboardRendererObserved = true; storyboardCanvasGeneration = canvas.MediaGeneration;
            storyboardPresented = false; storyboardPreloadTime = double.NaN;
        }
        long previousVersion = storyboardTriggerVersion;
        bool passing = StoryboardPassingAt(playhead);
        bool seeking = !AudioPlaying || !double.IsNaN(storyboardFrameTime)
            && (playhead < storyboardFrameTime || playhead - storyboardFrameTime > 250);
        bool frameChanged = storyboardFrameTime != playhead || storyboardFramePassing != passing || previousVersion != storyboardTriggerVersion;
        if (frameChanged)
        {
            storyboardScene.Evaluate(playhead, storyboardFrame, passing, storyboardTriggers);
            storyboardFrameTime = playhead; storyboardFramePassing = passing;
            storyboardRequiredImages.Clear(); storyboardRequiredSeen.Clear();
            foreach (var sprite in storyboardFrame)
                if (storyboardRequiredSeen.Add(sprite.Path)) storyboardRequiredImages.Add(sprite.Path);
        }
        if (double.IsNaN(storyboardPreloadTime) || Math.Abs(playhead - storyboardPreloadTime) > 500 || seeking && frameChanged)
        {
            // A scrub target needs one complete frame, not every animation image
            // in its lookahead. Superseded seeks must retire that speculative queue.
            if (seeking) { storyboardPreload.Clear(); storyboardPreload.AddRange(storyboardRequiredImages); }
            else storyboardScene.Preload(playhead, 2000, storyboardPreload, storyboardPreloadSeen);
            canvas.PrepareStoryboardImages(storyboardPreload);
            storyboardPreloadTime = playhead;
        }
        if (storyboardPinnedGeneration != canvas.MediaGeneration)
        { canvas.RetainStoryboardImages(storyboardRetainedImages); storyboardPinnedGeneration = canvas.MediaGeneration; }
        bool wasReady = storyboardTargetReady;
        storyboardTargetReady = (storyboardRequiredImages.Count == 0 || canvas.StoryboardImagesReady(storyboardRequiredImages))
            && (!storyboardTransition || storyboardGameplayLoad is null);
        if (storyboardTransition && storyboardTargetReady && storyboardScene.Videos.FirstOrDefault() is var video && video.Path is not null && playhead >= video.OffsetMs)
        {
            videoRequestedThisFrame = true;
            storyboardTargetReady = canvas.PrepareVideoImage(video.Path, playhead - video.OffsetMs, AudioPlaying, PlaybackSpeed);
        }
        if (storyboardTargetReady && (storyboardTransition || frameChanged || !wasReady || !storyboardPresented))
        {
            storyboardDisplayFrame.Clear(); storyboardDisplayFrame.AddRange(storyboardFrame);
            storyboardDisplayScene = storyboardScene;
            storyboardDisplayBackground = beatmapBackground ?? storyboardScene.BackgroundPath;
            storyboardTransition = false;
            bool same = storyboardRetainedImages.Count == storyboardRequiredImages.Count;
            for (int i = 0; same && i < storyboardRetainedImages.Count; i++) same = storyboardRetainedImages[i] == storyboardRequiredImages[i];
            if (!same)
            {
                storyboardRetainedImages.Clear(); storyboardRetainedImages.AddRange(storyboardRequiredImages);
            }
            canvas.RetainStoryboardImages(storyboardRetainedImages);
            storyboardPresented = true;
        }
    }

    private void DrawStoryboard(ICanvas canvas, Rect bounds, bool overlay, float brightness = 1)
    {
        if (!LibrarySettings.EnableVideoStoryboard || storyboardDisplayScene is null || !storyboardPresented) return;
        float scale = bounds.Height / 480;
        // Storyboards keep 480 logical pixels vertically and centre their legacy 640-wide coordinates.
        float x = bounds.X + bounds.Width / 2 - 320 * scale;
        float width = storyboardDisplayScene.Widescreen ? 480 * 16f / 9 : 640;
        var viewport = new Rect(bounds.X + (bounds.Width - width * scale) / 2, bounds.Y, width * scale, bounds.Height);
        float left = Math.Max(bounds.X, viewport.X), top = Math.Max(bounds.Y, viewport.Y);
        float right = Math.Min(bounds.Right, viewport.Right), bottom = Math.Min(bounds.Bottom, viewport.Bottom);
        canvas.Clip(new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top))); canvas.BeginStoryboard();
        try
        {
            foreach (var sprite in storyboardDisplayFrame)
            {
                if ((sprite.Layer == StoryboardLayer.Overlay) != overlay) continue;
                uint ColourChannel(int shift) => (uint)Math.Clamp(Math.Round(((sprite.Colour >> shift) & 255) * brightness), 0, 255);
                uint colour = ColourChannel(16) << 16 | ColourChannel(8) << 8 | ColourChannel(0);
                canvas.StoryboardSprite(sprite.Path, x + (float)sprite.X * scale, bounds.Y + (float)sprite.Y * scale,
                    (float)sprite.ScaleX * scale, (float)sprite.ScaleY * scale, colour,
                    (float)sprite.Opacity, (float)sprite.Rotation, sprite.Additive, sprite.FlipX, sprite.FlipY,
                    (float)sprite.OriginX, (float)sprite.OriginY);
            }
        }
        finally { canvas.EndStoryboard(); canvas.Unclip(); }
    }

    private void DrawStoryboardOverlay(ICanvas canvas, Rect bounds, float dim)
    {
        if (LibrarySettings.ForceBackgroundDim) return;
        PrepareStoryboardFrame(canvas);
        DrawStoryboard(canvas, bounds, true, 1 - dim);
    }

    private void UpdateStoryboardSamples(double position, bool playing)
    {
        PumpStoryboardGameplay();
        if (!playing || !double.IsFinite(position) || storyboardScene is null)
        { storyboardSamplePosition = storyboardSamplesThrough = null; return; }
        double start = storyboardSamplePosition ?? position - .001;
        bool discontinuity = position < start || position - start > 250;
        storyboardSamplePosition = position;
        if (discontinuity) { start = position - .001; storyboardSamplesThrough = null; }
        bool scheduled = AudioReady && RequestScheduleHitsound is not null;
        start = scheduled ? storyboardSamplesThrough ?? start : start;
        double end = scheduled ? position + HitsoundLookaheadMs : position;
        var samples = storyboardScene.Samples;
        bool passing = StoryboardPassingAt(position);
        int low = 0, high = samples.Count;
        while (low < high) { int mid = (low + high) / 2; if (samples[mid].TimeMs <= start) low = mid + 1; else high = mid; }
        for (int i = low; i < samples.Count && samples[i].TimeMs <= end; i++)
        {
            if (samples[i].Layer == StoryboardLayer.Fail && passing || samples[i].Layer == StoryboardLayer.Pass && !passing) continue;
            if (scheduled) RequestScheduleHitsound!(storyboardSampleSounds[i], samples[i].TimeMs);
            else RequestHitsound?.Invoke(storyboardSampleSounds[i]);
        }
        if (scheduled) storyboardSamplesThrough = end;
    }

    private void ReportMediaError(ICanvas canvas)
    {
        if (canvas.MediaError is not { } error || error == mediaReportedError) return;
        mediaReportedError = error; SetNotice(L.Get("media.storyboardFailed", error));
    }

    private void RecordStoryboardPreviewSound(ConvertedCatchObject item)
    {
        if (!storyboardPreviewPlayed.TryAdd((item.SourceId, item.EventIndex, item.TimeMs), item)) return;
        storyboardPreview?.RecordSound(item);
    }

    private void ConsumeStoryboardPreviewSounds(double position, bool playing)
    {
        var previous = storyboardPreviewConsumedPosition;
        storyboardPreviewConsumedPosition = playing && double.IsFinite(position) ? position : null;
        if (!double.IsFinite(position) || previous is null || position < previous || position - previous > 250)
        { storyboardPendingPreviewSounds.Clear(); return; }
        int consumed = 0;
        while (position > previous && consumed < storyboardPendingPreviewSounds.Count && storyboardPendingPreviewSounds[consumed].TimeMs <= position)
        {
            RecordStoryboardPreviewSound(storyboardPendingPreviewSounds[consumed]); consumed++;
        }
        if (consumed > 0) storyboardPendingPreviewSounds.RemoveRange(0, consumed);
        if (!playing) storyboardPendingPreviewSounds.Clear();
    }

    private void CancelStoryboardPreviewSounds()
    { storyboardPendingPreviewSounds.Clear(); storyboardPreviewConsumedPosition = null; }

    private bool DrawMediaOpening(ICanvas canvas)
    {
        PumpStoryboardGameplay();
        if (storyboardScene is not null) PrepareStoryboardFrame(canvas);
        else if (storyboardTransition && storyboardLoad is null && storyboardGameplayLoad is null)
        {
            storyboardDisplayScene = null; storyboardDisplayBackground = null;
            storyboardDisplayFrame.Clear(); storyboardRetainedImages.Clear(); canvas.RetainStoryboardImages(storyboardRetainedImages);
            storyboardPresented = storyboardTransition = false;
        }
        if (mediaOpening && storyboardLoad is null && storyboardGameplayLoad is null
            && (!LibrarySettings.EnableVideoStoryboard || storyboardScene is null || storyboardTargetReady)) mediaOpening = false;
        if (!mediaOpening && storyboardLoad is null && storyboardGameplayLoad is null)
        { mediaInitialPreparationComplete = true; mediaPreparationInEditor = false; }
        if (!mediaOpening || mediaPreparationInEditor) return false;
        canvas.Fill(new(0, 0, width, height), Background);
        DrawSynchronizationWait(canvas);
        if (mediaExitPending && DiscardConfirmationVisible) DrawDiscardConfirmation(canvas);
        return true;
    }

    private void DrawStoryboardLetterbox(ICanvas canvas, Rect bounds)
    {
        if (!LibrarySettings.EnableVideoStoryboard || storyboardDisplayScene is not { Widescreen: false, SpriteCount: > 0 }) return;
        float innerWidth = bounds.Height * 4 / 3;
        float side = Math.Max(0, (bounds.Width - innerWidth) / 2);
        if (side <= 0) return;
        canvas.Fill(new(bounds.X, bounds.Y, side, bounds.Height), 0);
        canvas.Fill(new(bounds.Right - side, bounds.Y, side, bounds.Height), 0);
    }
}
