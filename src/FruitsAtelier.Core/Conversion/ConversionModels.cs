namespace FruitsAtelier.Core;

public enum CatchObjectKind { Fruit, Droplet, TinyDroplet, Banana }

public sealed record ConvertedCatchObject(
    Guid SourceId,
    int EventIndex,
    CatchObjectKind Kind,
    double TimeMs,
    double X,
    double TargetX,
    double PathX,
    double RandomOffset,
    bool IsStandalone = false);

public readonly record struct SliderPathPoint(double X, double GeometryY);

public sealed class GeneratedSlider
{
    internal ImportedSliderGeometry? ImportedGeometry { get; init; }
    internal IReadOnlyList<int>? ImportedOffsets { get; init; }
    public bool IsImported { get; init; }
    public int SpanCount { get; init; } = 1;
    public required Guid SourceId { get; init; }
    public required double StartTimeMs { get; init; }
    public required double DurationMs { get; init; }
    public required double Velocity { get; init; }
    public required double SliderVelocityMultiplier { get; init; }
    public required double TickDistance { get; init; }
    public required double Length { get; init; }
    public required IReadOnlyList<SliderPathPoint> Path { get; init; }
    public required bool TinyCompensationApplied { get; init; }
    public bool TinyCompensationSucceeded { get; init; }
    public double MaxTickError { get; init; }
    public double MaxTinyError { get; init; }
}

public sealed class CatchConversionResult
{
    public required IReadOnlyList<GeneratedSlider> Sliders { get; init; }
    public required IReadOnlyList<ConvertedCatchObject> Objects { get; init; }
    public required IReadOnlyList<string> Diagnostics { get; init; }
    public required bool Success { get; init; }
    public double MaxTickError { get; init; }
    public double MaxTinyError { get; init; }
}
