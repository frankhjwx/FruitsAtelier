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
        bool Fits(double time, int candidate) => Math.Abs(TimingMap.Snap(Document, time, candidate) - time) <= 1;
        int matched = SnapDivisors.FirstOrDefault(candidate => Fits(note.TimeMs, candidate));
        if (matched != 0)
        {
            foreach (var neighbor in Document.Fruits.Where(f => f.Id != note.SourceId
                         && Math.Abs(f.TimeMs - note.TimeMs) <= timing.BeatLengthMs / 2)
                     .OrderBy(f => Math.Abs(f.TimeMs - note.TimeMs)))
            {
                var neighborTiming = TimingMap.At(Document, neighbor.TimeMs);
                if (neighborTiming.OffsetMs != timing.OffsetMs || neighborTiming.BeatLengthMs != timing.BeatLengthMs) continue;
                int shared = SnapDivisors.FirstOrDefault(candidate => Fits(note.TimeMs, candidate) && Fits(neighbor.TimeMs, candidate));
                if (shared != 0) { matched = shared; break; }
            }
        }
        if (matched == 0)
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
        int step = (int)Math.Round((note.TimeMs - timing.OffsetMs) / timing.BeatLengthMs * matched);
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
