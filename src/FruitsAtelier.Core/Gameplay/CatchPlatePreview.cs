namespace FruitsAtelier.Core;

public readonly record struct CatchPlateSprite(ConvertedCatchObject Object, double X, double Y, float Opacity);

public sealed class CatchPlatePreview
{
    private readonly CatchPlate plate;
    public CatchPlatePreview(IReadOnlyList<ConvertedCatchObject> objects, CatchAutoPreview autoplay,
        double circleSize, HashSet<(Guid SourceId, int EventIndex)> comboEnds)
    {
        plate = new(circleSize);
        foreach (var item in objects)
            plate.Judge(item, autoplay.At(item.TimeMs).X, true, comboEnds.Contains((item.SourceId, item.EventIndex)));
    }
    public IEnumerable<CatchPlateSprite> At(double time, double catcherX) => plate.At(time, catcherX);
}
