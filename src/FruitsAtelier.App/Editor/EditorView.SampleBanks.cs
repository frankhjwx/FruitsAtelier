using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private int nextNormalBank, nextAdditionBank;
    private CatchConversionResult? sampleBankConversion;
    private Guid[] sampleBankSelection = [];
    private (Guid Id, int Edge)? sampleBankEdge;
    private readonly int[] selectedSampleBanks = new int[2];
    private bool sampleBankTargetsAvailable;
    private CatchConversionResult? hitsoundingSampleBankConversion;
    private readonly HashSet<(Guid Id, int Event, int Lane)> hitsoundingSampleBankSelection = [];
    private readonly int[] hitsoundingSelectedSampleBanks = new int[2];
    private bool hitsoundingSampleBankTargetsAvailable;
    public Rect SamplesetBounds { get; private set; }
    public Rect AdditionsBounds { get; private set; }

    private static string SampleBankName(int bank) => L.Get(bank switch
    {
        1 => "timing.normal", 2 => "timing.soft", 3 => "timing.drum", _ => "samples.auto"
    });

    private void DrawSampleBankSelectors(ICanvas c, float top)
    {
        var ids = FlagTargets();
        bool placement = HitsoundingPageVisible ? !RefreshHitsoundingSampleBanks() : tool == Tool.Fruit || ids.Length == 0;
        if (placement)
        {
            sampleBankConversion = null;
            sampleBankSelection = [];
            sampleBankEdge = null;
        }
        if (!HitsoundingPageVisible && !placement && (!ReferenceEquals(sampleBankConversion, conversion)
            || !sampleBankSelection.SequenceEqual(ids) || sampleBankEdge != soundEdge))
        {
            sampleBankConversion = conversion;
            sampleBankSelection = ids;
            sampleBankEdge = soundEdge;
            var targets = ids;
            sampleBankTargetsAvailable = targets.Length > 0;
            for (int i = 0; i < 2; i++)
            {
                var values = targets.SelectMany(id => ObjectFlags.SampleBanks(Document, id, i == 1, SoundEdge(id))).Distinct().Take(2).ToArray();
                selectedSampleBanks[i] = values.Length == 1 ? values[0] : 0;
            }
        }
        bool enabled = HitsoundingPageVisible || tool != Tool.Banana && draftTrack == Guid.Empty && draftBanana == Guid.Empty
            && (placement || sampleBankTargetsAvailable);
        for (int i = 0; i < 2; i++)
        {
            bool additions = i == 1;
            int selected = placement ? additions ? nextAdditionBank : nextNormalBank
                : HitsoundingPageVisible ? hitsoundingSelectedSampleBanks[i] : selectedSampleBanks[i];
            var bounds = new Rect(leftPanel.Right + (HitsoundingPageVisible ? 16 : 6), top + i * 48 + 17,
                HitsoundingPageVisible ? Math.Max(20, hitsoundingGridBounds.X - leftPanel.Right - 20) : 96, 28);
            if (HitsoundingPageVisible)
            {
                float bankWidth = Math.Max(20, (hitsoundingGridBounds.X - leftPanel.Right - 24) / 2);
                bounds = new(leftPanel.Right + 16 + i * (bankWidth + 4), top + 17, bankWidth, 28);
            }
            if (additions) AdditionsBounds = bounds; else SamplesetBounds = bounds;
            c.Text(L.Get(additions ? "samples.additions" : "samples.sampleset"), bounds.X, bounds.Y - 17, 11, Muted, bounds.Width);
            c.Fill(bounds, Panel, 4);
            c.Stroke(bounds, Grid, 1, 4);
            Button(c, bounds, SampleBankName(selected) + " ▾", () => OpenSampleBankMenu(additions, selected, bounds), enabled: enabled);
        }
    }

    private bool RefreshHitsoundingSampleBanks()
    {
        if (ReferenceEquals(hitsoundingSampleBankConversion, hitsoundingConversion)
            && hitsoundingSampleBankSelection.SetEquals(hitsoundingMultiSelection)) return hitsoundingSampleBankTargetsAvailable;
        hitsoundingSampleBankConversion = hitsoundingConversion;
        hitsoundingSampleBankSelection.Clear(); hitsoundingSampleBankSelection.UnionWith(hitsoundingMultiSelection);
        hitsoundingSampleBankTargetsAvailable = false;
        Array.Fill(hitsoundingSelectedSampleBanks, -1);
        foreach (var key in hitsoundingMultiSelection)
        {
            if (key.Lane is < 1 or > 4 || !hitsoundingNoteLookup.TryGetValue((key.Id, key.Event), out var note)) continue;
            hitsoundingSampleBankTargetsAvailable = true;
            var setting = hitsoundingResolver!.EventSettings(note);
            for (int i = 0; i < 2; i++)
            {
                var field = i == 0 ? InheritedSampleFields.NormalSet : InheritedSampleFields.AdditionSet;
                int bank = setting.InheritedFields.HasFlag(field) ? 0 : i == 0 ? setting.Sample.NormalSet : setting.Sample.AdditionSet;
                hitsoundingSelectedSampleBanks[i] = hitsoundingSelectedSampleBanks[i] == -1 ? bank
                    : hitsoundingSelectedSampleBanks[i] == bank ? bank : 0;
            }
        }
        return hitsoundingSampleBankTargetsAvailable;
    }

    private bool SetHitsoundingSelectedBank(bool additions, int bank)
    {
        var notes = hitsoundingMultiSelection.Where(k => k.Lane is >= 1 and <= 4)
            .Select(k => (k.Id, k.Event)).Distinct()
            .Select(k => hitsoundingNoteLookup.GetValueOrDefault(k)).Where(n => n is not null).ToArray();
        if (notes.Length == 0) return false;
        var field = additions ? InheritedSampleFields.AdditionSet : InheritedSampleFields.NormalSet;
        var changes = notes.Select(note =>
        {
            var setting = hitsoundingResolver!.EventSettings(note!);
            var sample = setting.Sample;
            var inherited = bank == 0 ? setting.InheritedFields | field : setting.InheritedFields & ~field;
            if (!additions && sample.Additions != 0 && inherited.HasFlag(InheritedSampleFields.AdditionSet))
            {
                sample = sample with { AdditionSet = hitsoundingResolver.Describe(note!).AdditionSet };
                inherited &= ~InheritedSampleFields.AdditionSet;
            }
            return setting with { Sample = additions ? sample with { AdditionSet = bank } : sample with { NormalSet = bank },
                InheritedFields = inherited };
        }).ToArray();
        Edit(L.Get("samples.change"), () => { foreach (var change in changes) SetHitsoundingEvent(change); });
        return true;
    }

    private void OpenSampleBankMenu(bool additions, int selected, Rect bounds)
    {
        contextItems.Clear();
        for (int i = 0; i < 4; i++)
        {
            int bank = i;
            contextItems.Add(new((selected == bank ? "✓ " : "") + SampleBankName(bank), () => SetSampleBank(additions, bank)));
        }
        contextBounds = new(bounds.X, Math.Min(bounds.Bottom, height - ContextMenuHeight - 8), 120, ContextMenuHeight);
    }

    private void SetSampleBank(bool additions, int bank)
    {
        if (HitsoundingPageVisible)
        {
            if (SetHitsoundingSelectedBank(additions, bank)) return;
            if (additions) nextAdditionBank = bank; else nextNormalBank = bank;
            return;
        }
        if (tool == Tool.Banana || draftTrack != Guid.Empty || draftBanana != Guid.Empty) return;
        var ids = FlagTargets();
        if (tool == Tool.Fruit || ids.Length == 0)
        {
            if (additions) nextAdditionBank = bank; else nextNormalBank = bank;
            return;
        }
        var targets = ids;
        if (targets.Length == 0) return;
        Edit(L.Get("samples.change"), () =>
        {
            foreach (var id in targets) ObjectFlags.SetSampleBank(Document, id, additions, bank, SoundEdge(id));
        });
    }
}
