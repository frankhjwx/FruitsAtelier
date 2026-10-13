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
    public Rect SamplesetBounds { get; private set; }
    public Rect AdditionsBounds { get; private set; }

    private static string SampleBankName(int bank) => L.Get(bank switch
    {
        1 => "timing.normal", 2 => "timing.soft", 3 => "timing.drum", _ => "samples.auto"
    });

    private void DrawSampleBankSelectors(ICanvas c, float top)
    {
        var ids = FlagTargets();
        bool placement = tool == Tool.Fruit || ids.Length == 0;
        if (placement)
        {
            sampleBankConversion = null;
            sampleBankSelection = [];
            sampleBankEdge = null;
        }
        if (!placement && (!ReferenceEquals(sampleBankConversion, conversion)
            || !sampleBankSelection.SequenceEqual(ids) || sampleBankEdge != soundEdge))
        {
            sampleBankConversion = conversion;
            sampleBankSelection = ids;
            sampleBankEdge = soundEdge;
            var targets = ids.Where(id => !Document.BananaShowers.Any(b => b.Id == id)).ToArray();
            sampleBankTargetsAvailable = targets.Length > 0;
            for (int i = 0; i < 2; i++)
            {
                var values = targets.SelectMany(id => ObjectFlags.SampleBanks(Document, id, i == 1, SoundEdge(id))).Distinct().Take(2).ToArray();
                selectedSampleBanks[i] = values.Length == 1 ? values[0] : 0;
            }
        }
        bool enabled = tool != Tool.Banana && draftTrack == Guid.Empty && draftBanana == Guid.Empty
            && (placement || sampleBankTargetsAvailable);
        for (int i = 0; i < 2; i++)
        {
            bool additions = i == 1;
            int selected = placement ? additions ? nextAdditionBank : nextNormalBank : selectedSampleBanks[i];
            var bounds = new Rect(leftPanel.Right + 6, top + i * 48 + 17, 96, 28);
            if (additions) AdditionsBounds = bounds; else SamplesetBounds = bounds;
            c.Text(L.Get(additions ? "samples.additions" : "samples.sampleset"), bounds.X, bounds.Y - 17, 11, Muted, bounds.Width);
            c.Fill(bounds, Panel, 4);
            c.Stroke(bounds, Grid, 1, 4);
            Button(c, bounds, SampleBankName(selected) + " ▾", () => OpenSampleBankMenu(additions, selected, bounds), enabled: enabled);
        }
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
        if (tool == Tool.Banana || draftTrack != Guid.Empty || draftBanana != Guid.Empty) return;
        var ids = FlagTargets();
        if (tool == Tool.Fruit || ids.Length == 0)
        {
            if (additions) nextAdditionBank = bank; else nextNormalBank = bank;
            return;
        }
        var targets = ids.Where(id => !Document.BananaShowers.Any(b => b.Id == id)).ToArray();
        if (targets.Length == 0) return;
        Edit(L.Get("samples.change"), () =>
        {
            foreach (var id in targets) ObjectFlags.SetSampleBank(Document, id, additions, bank, SoundEdge(id));
        });
    }
}
