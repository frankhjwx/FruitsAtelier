using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private Guid temporarySnapSource;
    private int savedSnapDivisor;
    private bool savedSnapEnabled;

    private void RestoreTemporarySnap()
    {
        if (temporarySnapSource == Guid.Empty) return;
        divisor = savedSnapDivisor;
        snap = savedSnapEnabled;
        temporarySnapSource = Guid.Empty;
    }

    private void ForgetTemporarySnap() => temporarySnapSource = Guid.Empty;

    private void ShowNoteBeatPosition(ConvertedCatchObject note)
    {
        var timing = TimingMap.At(Document, note.TimeMs);
        int matched = 0;
        double closestDistance = double.MaxValue;
        double currentDistance = double.MaxValue;
        foreach (int candidate in SnapDivisors)
        {
            double beatLength = timing.BeatLengthMs / candidate;
            double beats = Math.Round((Math.Max(note.TimeMs, 0) - timing.OffsetMs) / beatLength, MidpointRounding.AwayFromZero);
            double snappedTime = timing.OffsetMs + beats * beatLength;
            if (snappedTime < 0) snappedTime += beatLength;
            double distance = Math.Abs(note.TimeMs - snappedTime);
            if (candidate == divisor) currentDistance = distance;
            // Equal-distance grids retain the smaller divisor despite floating-point roundoff.
            if (closestDistance - 1e-7 > distance)
            {
                matched = candidate;
                closestDistance = distance;
            }
        }
        if (snap && currentDistance <= 2) return;
        if (closestDistance > 2)
        {
            RestoreTemporarySnap();
            StatusMessage = L.Get("editor.status.noteOffGrid", Time(note.TimeMs));
            return;
        }
        if (temporarySnapSource == Guid.Empty)
        {
            savedSnapDivisor = divisor;
            savedSnapEnabled = snap;
        }
        temporarySnapSource = note.SourceId;
        divisor = matched;
        snap = true;
        int step = (int)Math.Round((note.TimeMs - timing.OffsetMs) / (timing.BeatLengthMs / matched), MidpointRounding.AwayFromZero);
        int numerator = ((step % matched) + matched) % matched;
        int denominator = matched;
        int gcd = GreatestCommonDivisor(numerator, denominator);
        StatusMessage = L.Get("editor.status.noteBeatPosition", numerator / gcd, denominator / gcd, Time(note.TimeMs), matched);

        static int GreatestCommonDivisor(int a, int b)
        {
            while (b != 0) (a, b) = (b, a % b);
            return a;
        }
    }
}
