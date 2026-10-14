using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private MapDocument? liveSongSnapshot, liveRandomSnapshot, livePatternSnapshot;
    private uint[] liveSongColours = [];
    private bool liveSongCustom, committingLiveSong, committingLiveDocks, livePatternConvert;
    private int livePatternSnap;
    private bool livePatternStack;
    private StackEnvelope? liveStackBaseline;
    private BananaShowerPattern? liveBananaBaseline;

    // A local edit keeps field focus when its converted snapshot arrives. External edits reload the form.
    private void AdoptLiveDock(ref MapDocument? snapshot, ref MapDocument? pending)
    {
        if (pending is null || ReferenceEquals(snapshot, convertedSnapshot)) return;
        if (convertedSnapshot is not null && pending.ContentEquals(convertedSnapshot)) snapshot = convertedSnapshot;
        pending = null;
    }
    private void InvalidateLiveDocks()
    {
        songDockSnapshot = randomDockSnapshot = patternDockSnapshot = null;
        liveSongSnapshot = liveRandomSnapshot = livePatternSnapshot = null;
    }
    private void CommitLiveDockEdits()
    {
        if (committingLiveDocks || !DockContentInputAvailable || history.HasActiveTransaction) return;
        committingLiveDocks = true;
        try
        {
            if (SongDockEditing && (songInitial.Any(p => songValues.GetValueOrDefault(p.Key) != p.Value)
                || liveSongCustom != songCustomColours || songCustomColours && !liveSongColours.SequenceEqual(songColours)))
            {
                committingLiveSong = true;
                try { ApplySongSetup(); } finally { committingLiveSong = false; }
            }
            CommitRandomDock();
            CommitLivePattern();
        }
        finally { committingLiveDocks = false; }
    }
    private void InitializePatternDock()
    {
        if (patternDockBananas)
        { patternDockKind = 2; OpenBananaEdit(); BananaEditVisible = false; liveBananaBaseline = bananaEditPattern; }
        else
        {
            InitializeStreamEditor(); StreamDialogVisible = false; patternDockKind = 1;
            livePatternSnap = StreamSnapDivisor; livePatternStack = stackMode; liveStackBaseline = stackDraft.DeepClone();
        }
    }
    private void CommitLivePattern()
    {
        if (!PatternDockEditing || !ReferenceEquals(patternDockSnapshot, convertedSnapshot)
            || streamSnapDragging || stackPointDragging >= 0 || stackFruitDragging >= 0) return;
        if (patternDockKind == 2)
        {
            if (!int.TryParse(bananaEditNumbers[0], out int length) || length < 1
                || !int.TryParse(bananaEditNumbers[1], out int gap) || gap < 0) return;
            var pattern = bananaEditPattern with { LengthNumerator = length, GapNumerator = gap };
            if (!livePatternConvert && pattern == liveBananaBaseline) return;
            try
            {
                var timing = renderedTiming ?? new TimingMap.Lookup(Document);
                foreach (var source in Document.BananaShowers.Where(s => patternDockTargets.Contains(s.Id)))
                {
                    var candidate = new BananaShower { TimeMs = source.TimeMs, EndTimeMs = source.EndTimeMs, Pattern = pattern };
                    if (BananaShowerPattern.Segments(candidate, timing).Count == 0) throw new InvalidDataException(L.Get("bananaEdit.tooShort"));
                }
            }
            catch (InvalidDataException error) { bananaNumericError = error.Message; return; }
            if (!Edit(L.Get("bananaEdit.title"), () =>
            {
                foreach (var shower in Document.BananaShowers.Where(s => patternDockTargets.Contains(s.Id))) shower.Pattern = pattern;
            })) return;
            bananaEditPattern = liveBananaBaseline = pattern; bananaNumericError = "";
            bananaEditReady = null;
        }
        else
        {
            if (stackNumericField >= 0 && stackNumericText != stackNumericOpeningText)
            {
                int field = stackNumericField;
                if (!CommitStackNumeric()) return;
                stackNumericField = field; stackNumericOpeningText = stackNumericText;
            }
            if (!livePatternConvert && livePatternSnap == StreamSnapDivisor && livePatternStack == stackMode
                && (!stackMode || StackEnvelope.Equal(liveStackBaseline, stackDraft))) return;
            if (stackMode && !stackDraft.IsValid) return;
            if (!Edit(L.Get("conversion.editTitle"), () =>
            {
                foreach (var id in patternDockTargets)
                {
                    var track = Document.Tracks.FirstOrDefault(t => t.Id == id) ?? ConvertImportedSlider(id).Track;
                    track.StreamSnapDivisor = StreamSnapDivisor;
                    track.Stack = stackMode ? stackDraft.DeepClone() : null;
                }
            })) { streamError = StatusMessage; return; }
            livePatternSnap = StreamSnapDivisor; livePatternStack = stackMode; liveStackBaseline = stackDraft.DeepClone(); streamError = "";
        }
        livePatternConvert = false; patternDockExisting = true;
        livePatternSnapshot = Document.DeepClone();
    }
    private void BreakLiveBananas()
    {
        var ids = patternDockTargets;
        if (Edit(L.Get("bananaEdit.split"), () => ids = ObjectStructureEditing.BreakBananaShowers(Document, ids)))
        { ResetPatternDock(); SelectObjects(ids); }
    }
}
