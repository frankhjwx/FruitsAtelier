using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private CatchConversionResult? sliderSpeedSnapshot;
    private Guid sliderSpeedSource;
    private bool sliderSpeedIncludesTiny;
    private (double? Sv, double? MaxDs)? cachedSliderSpeed;
    public (double? Sv, double? MaxDs)? SliderSpeedReadout { get; private set; }
    public Rect? SliderSpeedReadoutBounds { get; private set; }

    private void DrawSliderSpeedReadout(ICanvas c)
    {
        Guid source = draftTrack != Guid.Empty ? draftTrack : FlagTargets() is [var id] ? id : Guid.Empty;
        if (source == Guid.Empty || conversion is null || draftBanana != Guid.Empty || tool == Tool.Fruit)
        {
            sliderSpeedSnapshot = null; cachedSliderSpeed = null;
            return;
        }
        if (!ReferenceEquals(sliderSpeedSnapshot, conversion) || sliderSpeedSource != source
            || sliderSpeedIncludesTiny != movementIncludeTinyDroplets)
        {
            sliderSpeedSnapshot = conversion;
            sliderSpeedSource = source;
            sliderSpeedIncludesTiny = movementIncludeTinyDroplets;
            cachedSliderSpeed = null;
            var slider = conversion.Sliders.FirstOrDefault(s => s.SourceId == source);
            if (slider is not null)
            {
                double? maximum = null;
                ConvertedCatchObject? previous = null;
                // Restrict the scan to this parent's interval; selection and repaint reuse the result.
                var objects = conversion.Objects;
                int low = 0, high = objects.Count;
                while (low < high)
                {
                    int middle = low + (high - low) / 2;
                    if (objects[middle].TimeMs < slider.StartTimeMs - 1) low = middle + 1;
                    else high = middle;
                }
                for (int i = low; i < objects.Count && objects[i].TimeMs <= slider.StartTimeMs + slider.DurationMs + 1; i++)
                {
                    var item = objects[i];
                    if (item.SourceId != source || item.Kind is not (CatchObjectKind.Fruit or CatchObjectKind.Droplet)
                        && !(movementIncludeTinyDroplets && item.Kind == CatchObjectKind.TinyDroplet)) continue;
                    if (previous is not null && BaseDistanceRatio(previous, item) is { } ratio)
                        maximum = maximum is { } value ? Math.Max(value, ratio) : ratio;
                    previous = item;
                }
                cachedSliderSpeed = (slider.SliderVelocityMultiplier, maximum);
            }
            else if (Document.Tracks.Any(t => t.Id == source && t.StreamSnapDivisor is null)
                || Document.ImportedSliders.Any(s => s.Id == source)) cachedSliderSpeed = (null, null);
        }
        if (cachedSliderSpeed is not { } speed) return;
        float panelWidth = Math.Min(MovementPanelWidth, plot.Width - 12);
        if (panelWidth < 180 || plot.Height < 44) return;
        var bounds = MovementOverlayBounds is { } movement
            ? new Rect(movement.X, movement.Y >= plot.Y + 38 ? movement.Y - 34 : movement.Bottom + 4, movement.Width, 30)
            : new Rect(Math.Clamp(Playfield.X + Playfield.Width / 2 - panelWidth / 2, plot.X + 6, plot.Right - panelWidth - 6),
                plot.Bottom - 38, panelWidth, 30);
        if (bounds.Bottom > plot.Bottom) return;
        SliderSpeedReadout = speed;
        SliderSpeedReadoutBounds = bounds;
        c.Fill(bounds, 0x171C24, 6, .82f);
        c.Stroke(bounds, 0x424D5C, 1, 6);
        string sv = L.Get("sliderSpeed.sv", Ratio(speed.Sv));
        string ds = L.Get(movementIncludeTinyDroplets ? "sliderSpeed.maxDsTiny" : "sliderSpeed.maxDs", Ratio(speed.MaxDs));
        float cell = (bounds.Width - 28) / 2;
        Draw(sv, bounds.X + 10);
        Draw(ds, bounds.X + 18 + cell);

        void Draw(string text, float x)
        {
            float size = MovementPanelFontSize;
            while (size > 8 && c.MeasureText(text, size) > cell) size -= .5f;
            c.Text(text, x, bounds.Y + (bounds.Height - size) / 2 - 1, size, Foreground, cell);
        }
        static string Ratio(double? value) => value is { } number ? L.Get("assist.ratio", number) : "—";
    }
}
