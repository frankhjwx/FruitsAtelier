using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private double lazerSeekFrom, lazerSeekTo, lazerSeekStarted = double.NegativeInfinity;
    private double lastLazerPresentationTime, lastLazerViewStart;
    private bool lazerPresentationInitialized;
    private double lazerTimelineFrom = 1d / 6000, lazerTimelineTarget = 1d / 6000;
    private double lazerTimelineStarted = double.NegativeInfinity;
    private double lazerZoomFrom = 1, lazerZoomTarget = 1, lazerZoomStarted = double.NegativeInfinity;
    private double lazerTimeZoomFrom = 1, lazerTimeZoomTarget = 1;
    private bool lazerUniformScale = true;
    private double lazerPanX;
    private double LazerTimelineMinimum => 1 / Math.Min(Math.Max(1, TimelineDurationMs), 10000);
    private double LazerTimelineMaximum => 1 / Math.Min(Math.Max(1, TimelineDurationMs), 500);
    private double LazerTimelineScale => Math.Max(1, objectTimeline.Width) * Math.Clamp(
        LazerEase(lazerTimelineFrom, lazerTimelineTarget, lazerTimelineStarted, 200), LazerTimelineMinimum, LazerTimelineMaximum);
    private double LazerEase(double from, double to, double started, double duration)
    {
        double progress = Math.Clamp((TestplayRealtime - started) / duration, 0, 1);
        return from + (to - from) * (1 - Math.Pow(1 - progress, 5));
    }

    internal double LazerPresentationTime => IsLazerEditing && !AudioPlaying && !IsTestplaying
        && playhead == lazerSeekTo
        ? LazerEase(lazerSeekFrom, lazerSeekTo, lazerSeekStarted, 300) : playhead;
    private double PresentationViewStart => IsLazerEditing ? viewStart + LazerPresentationTime - playhead : viewStart;
    internal float LazerCatchLineY => plot.Y + plot.Height * .9f;
    private float LazerBaseScale => Math.Min(plot.Width * .8f / 512, plot.Height * .9f / 384);
    internal float LazerCanvasScale => LazerBaseScale
        * (float)LazerEase(lazerZoomFrom, lazerZoomTarget, lazerZoomStarted, 200);
    private double LazerTimeScale => LazerBaseScale
        * LazerEase(lazerTimeZoomFrom, lazerTimeZoomTarget, lazerZoomStarted, 200);
    private double EffectivePlaybackLineFromBottom => IsLazerEditing
        ? (plot.Bottom - LazerCatchLineY) / Math.Max(1, plot.Height) : playbackLineFromBottom;
    private double LazerPreempt => Document.ApproachRate <= 5
        ? 1800 - Document.ApproachRate * 120 : 1200 - (Document.ApproachRate - 5) * 150;
    public bool LazerPresentationNeedsRedraw => !IsTestplaying && (CanvasScrollNeedsRedraw || IsLazerEditing &&
        ((!AudioPlaying && playhead == lazerSeekTo && TestplayRealtime < lazerSeekStarted + 300)
         || TestplayRealtime < lazerTimelineStarted + 200
         || TestplayRealtime < lazerZoomStarted + 200));

    private void ResetLazerPresentation()
    {
        lazerSeekStarted = double.NegativeInfinity;
        lazerSeekFrom = lazerSeekTo = playhead;
        lazerTimelineStarted = double.NegativeInfinity;
        lazerTimelineFrom = lazerTimelineTarget;
        lazerZoomStarted = double.NegativeInfinity;
        lazerZoomFrom = lazerZoomTarget;
        lazerTimeZoomFrom = lazerTimeZoomTarget;
        lazerPresentationInitialized = false;
        if (plot.Height > 0 && pixelsPerMs > 0) FollowPlayhead();
    }

    private void AdvanceLazerPresentationGesture()
    {
        double time = LazerPresentationTime, start = PresentationViewStart;
        double previousTime = lastLazerPresentationTime, previousStart = lastLazerViewStart;
        bool advance = lazerPresentationInitialized && IsLazerEditing && NavigationDuringDrag;
        lastLazerPresentationTime = time;
        lastLazerViewStart = start;
        lazerPresentationInitialized = true;
        if (advance)
            ContinueDragAfterNavigation(viewStart - (start - previousStart), playhead - (time - previousTime), shiftHeld, placementCtrl, requireStarted: true);
    }

    private void SeekLazerSmoothlyTo(double time)
    {
        double from = LazerPresentationTime;
        bool smooth = IsLazerEditing && !AudioPlaying && Math.Abs(time - from) <= 5000;
        SeekTo(time);
        if (!smooth) return;
        // The audio/edit target is committed once; rendering interpolates without issuing more seeks.
        lazerSeekFrom = from;
        lazerSeekTo = playhead;
        lazerSeekStarted = TestplayRealtime;
    }

    private void ZoomLazerTimeline(double notches)
    {
        double minimum = LazerTimelineMinimum, maximum = LazerTimelineMaximum;
        double target = Math.Clamp(lazerTimelineTarget, minimum, maximum);
        double next = Math.Clamp(target + notches * (maximum - minimum) * .02, minimum, maximum);
        if (next == target) return;
        lazerTimelineFrom = LazerTimelineScale / Math.Max(1, objectTimeline.Width);
        lazerTimelineTarget = next;
        lazerTimelineStarted = TestplayRealtime;
    }

    private void ZoomLazerCanvas(double notches)
        => SetLazerCanvasScales(lazerZoomTarget * Math.Pow(1.16, notches),
            lazerTimeZoomTarget * Math.Pow(1.16, notches));

    private void SetLazerCanvasZoom(double value)
        => SetLazerCanvasScales(value, value);

    private void SetLazerCanvasScales(double horizontal, double vertical)
    {
        double next = Math.Clamp(horizontal, MinimumCanvasZoom, MaximumCanvasZoom);
        double nextTime = UniformCanvasScale ? next : Math.Clamp(vertical, MinimumCanvasZoom, 2);
        if (next == lazerZoomTarget && nextTime == lazerTimeZoomTarget) return;
        lazerZoomFrom = LazerEase(lazerZoomFrom, lazerZoomTarget, lazerZoomStarted, 200);
        lazerTimeZoomFrom = LazerEase(lazerTimeZoomFrom, lazerTimeZoomTarget, lazerZoomStarted, 200);
        lazerZoomTarget = next;
        lazerTimeZoomTarget = nextTime;
        lazerZoomStarted = TestplayRealtime;
    }
}
