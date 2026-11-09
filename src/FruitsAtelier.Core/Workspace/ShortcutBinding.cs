namespace FruitsAtelier.Core;

/// <summary>A simultaneous chord of platform-neutral virtual keys.</summary>
public sealed record ShortcutBinding
{
    public int[] Keys { get; init; } = [];
    public static int Normalize(int key) => key switch { 160 or 161 => 16, 162 or 163 => 17, 164 or 165 => 18, _ => key };
    public static bool Modifier(int key) => key is 16 or 17 or 18 or 91 or 92;
    public static ShortcutBinding From(params int[] keys) => new() { Keys = keys.Select(Normalize).Where(k => k is > 0 and < 255).Distinct().Order().ToArray() };
    public bool Same(ShortcutBinding other) => Keys.SequenceEqual(other.Keys);
    public bool Overlaps(ShortcutBinding other) => Keys.Length > 0 && other.Keys.Length > 0
        && Keys.Where(Modifier).SequenceEqual(other.Keys.Where(Modifier))
        && (Keys.All(other.Keys.Contains) || other.Keys.All(Keys.Contains));
    private static readonly int[] Modifiers = [16, 17, 18, 91, 92];
    public bool Held(IReadOnlySet<int> held, bool exactModifiers = true)
    {
        if (Keys.Length == 0) return false;
        foreach (int key in Keys) if (!held.Contains(key)) return false;
        if (exactModifiers)
            foreach (int key in Modifiers) if (held.Contains(key) != Keys.Contains(key)) return false;
        return true;
    }
}
