using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private Guid? customBpmTarget;
    private bool customDefaultBpm;
    internal Rect BpmMultiplierBounds { get; private set; }

    private double BpmValue(Guid? target) => target is null ? Document.DefaultSliderBpmMultiplier
        : Document.Tracks.FirstOrDefault(t => t.Id == target)?.BpmMultiplier
            ?? Document.ImportedSliders.First(s => s.Id == target).BpmMultiplier;

    private void ApplyBpm(Guid? target, double value)
    {
        if (!double.IsFinite(value) || value <= 0) throw new ArgumentException(L.Get("aspire.invalidMultiplier"));
        if (target is null) Document.DefaultSliderBpmMultiplier = value;
        else if (Document.Tracks.FirstOrDefault(t => t.Id == target) is { } track) track.BpmMultiplier = value;
        else Document.ImportedSliders.First(s => s.Id == target).BpmMultiplier = value;
    }

    private void DrawBpmControl(ICanvas c, Rect box, Guid? target, bool enabled = true)
    {
        double value = BpmValue(target);
        bool custom = !SliderBpm.Presets.Contains(value) || (target is null ? customDefaultBpm : customBpmTarget == target);
        if (target is null)
        {
            BpmMultiplierBounds = box;
            if (custom)
            {
                Button(c, box with { Width = 122 }, L.Get("aspire.multiplier") + " ▾", () => OpenBpmMenu(target), enabled: enabled, fontSize: 11);
                float y = box.Y;
                Field(c, box.X + 126, ref y, box.Width - 126, L.Get("aspire.custom"), value, v => ApplyBpm(target, v), 0);
                return;
            }
        }
        Button(c, box, L.Get("aspire.value", value) + " ▾", () => OpenBpmMenu(target), enabled: enabled, fontSize: 11);
    }

    private void OpenBpmMenu(Guid? target)
    {
        if (editField >= 0 && !CommitField()) return;
        menu = -1; languageMenuOpen = false; contextItems.Clear();
        foreach (double preset in SliderBpm.Presets)
            contextItems.Add(new(preset.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "×", () =>
            {
                if (!ClipboardInteractionReady || target is not null && notesLocked) return;
                if (target is null) customDefaultBpm = false; else customBpmTarget = null;
                Edit(L.Get("aspire.multiplier"), () => ApplyBpm(target, preset));
            }, Active: BpmValue(target) == preset));
        contextItems.Add(new(L.Get("aspire.custom"), () =>
        {
            if (target is null) customDefaultBpm = true; else customBpmTarget = target;
        }));
        float w = Math.Min(240, width);
        contextBounds = new(Math.Clamp(mouseX, 0, width - w),
            Math.Clamp(mouseY + 4, 0, Math.Max(0, height - ContextMenuHeight)), w, ContextMenuHeight);
    }
}
