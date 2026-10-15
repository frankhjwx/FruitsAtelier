using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private IReadOnlyList<double>? lazerDistanceSpeedSource;
    private IReadOnlyList<double> lazerDistanceSpeeds = Array.AsReadOnly(new[] { .5, 1d });
    internal IReadOnlyList<double> LazerDistanceSpeeds
    {
        get
        {
            var source = LibrarySettings.LazerDistanceCustomSpeeds;
            if (!ReferenceEquals(lazerDistanceSpeedSource, source))
            {
                lazerDistanceSpeedSource = source;
                lazerDistanceSpeeds = Array.AsReadOnly(source.Concat([.5, 1d]).Order().ToArray());
            }
            return lazerDistanceSpeeds;
        }
    }

    internal bool UsesSpeedDistanceSnap => IsLazerEditing ? LibrarySettings.LazerSpeedDistanceSnap : LibrarySettings.MappingSpeedDistanceSnap;

    private void SetDistanceSnapPresetMode(bool speed)
    {
        CancelDistanceSnapDrag();
        dsSpeedMode = speed;
        dsDraft = speed ? dsSpeedDraft : dsRatioDraft;
    }

    private int DistanceSnapPresetLimit => DistanceSnap.MaximumPresets + (dsSpeedMode ? 2 : 0);
    private bool DistanceSnapPresetFixed(int index) => dsSpeedMode && index < 2;
    private string DistanceSnapPresetText(double value) => L.Get(dsSpeedMode ? "ds.lazerValue" : "ds.ratio", value);
    private double DistanceSnapPreviewVelocity => dsSpeedMode ? 60000 / dsBpm : dsBaseDraft;

    private double[] LazerDistanceSnapLimits()
    {
        double duration = 60000 / dsBpm / dsSnap;
        var movement = HyperDashCalculator.Evaluate(dsReferenceTime, 0, dsReferenceTime + duration, 512,
            HyperDashCalculator.CreateContext(Document.CircleSize)).State.Movement!.Value;
        double stand = movement.StandLimit / duration;
        double walk = Math.Max(stand, movement.WalkLimit / duration);
        double dash = Math.Max(walk, movement.DashLimit / duration);
        return [0, stand, walk, dash, Math.Max(3, dash + .5)];
    }
}
