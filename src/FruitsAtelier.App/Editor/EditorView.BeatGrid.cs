using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private static (uint Color, float Width, float Height) GridStyle(BeatGridLine line)
    {
        uint color = line.Subdivision switch
        {
            1 => 0xEEEEEE,
            2 => 0xFF6688,
            3 or 6 => 0xBB66EE,
            4 => 0x66AAFF,
            5 or 7 or 8 or 9 => 0xEEDD66,
            _ => 0xAAAAAA
        };
        return (color, line.IsMeasure ? 2.5f : line.IsBeat ? 2 : line.Subdivision is 2 or 3 ? 1.5f : 1,
            line.IsMeasure ? 20 : line.IsBeat ? 14 : line.Subdivision is 2 or 3 ? 9 : 5);
    }
}
