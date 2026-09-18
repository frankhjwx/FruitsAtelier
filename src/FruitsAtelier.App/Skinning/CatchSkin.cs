using L = FruitsAtelier.Localization.Strings;
using System.Buffers.Binary;
using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Skinning;

public enum CatchSkinObject { Fruit, Droplet, TinyDroplet, Banana }

public sealed record SkinTexture(string FilePath, int PixelWidth, int PixelHeight, int Density)
{
    public float LogicalWidth => Math.Min(PixelWidth / (float)Density, 160);
    public float LogicalHeight => Math.Min(PixelHeight / (float)Density, 160);
    public Rect Source => new((PixelWidth - LogicalWidth * Density) / 2,
        (PixelHeight - LogicalHeight * Density) / 2, LogicalWidth * Density, LogicalHeight * Density);
}

public sealed record CatchSkinSprite(SkinTexture? Base, SkinTexture? Overlay);

public sealed class CatchSkin
{
    private static readonly string[] fruitNames = ["pear", "grapes", "apple", "orange"];
    private readonly Dictionary<string, SkinTexture> textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<uint> comboColours = [];
    public string FolderPath { get; }
    public string Name { get; private set; }
    public IReadOnlyList<uint> ComboColours => comboColours;
    public uint HyperDashFruitColour { get; private set; } = 0xFF0000;
    public uint HyperDashColour { get; private set; } = 0xFF0000;
    public uint HyperDashAfterImageColour { get; private set; } = 0xFF0000;
    public int TextureCount => textures.Count;

    private CatchSkin(string folder)
    {
        FolderPath = folder;
        Name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
    }

    public static bool TryLoad(string folder, out CatchSkin? skin, out string message)
    {
        skin = null;
        try
        {
            var candidate = new CatchSkin(Path.GetFullPath(folder));
            if (!Directory.Exists(candidate.FolderPath)) { message = L.Get("skin.folderMissing"); return false; }
            var files = Directory.EnumerateFiles(candidate.FolderPath).ToDictionary(p => Path.GetFileName(p)!, p => p, StringComparer.OrdinalIgnoreCase);
            int invalid = 0;
            foreach (string name in fruitNames.Concat(["drop", "bananas"]))
            {
                LoadTexture($"fruit-{name}");
                LoadTexture($"fruit-{name}-overlay");
            }
            LoadTexture("reversearrow");
            LoadTexture("fruit-catcher-idle");
            LoadTexture("fruit-catcher-idle-0");
            if (files.TryGetValue("skin.ini", out var configuration)) candidate.ReadConfiguration(configuration);
            if (candidate.textures.Count == 0) { message = L.Get("skin.noTextures"); return false; }
            skin = candidate;
            message = L.Get("skin.loaded", candidate.Name, candidate.TextureCount, invalid > 0 ? L.Get("skin.invalidImages", invalid) : "");
            return true;

            void LoadTexture(string component)
            {
                foreach (int density in new[] { 2, 1 })
                {
                    string filename = component + (density == 2 ? "@2x" : "") + ".png";
                    if (!files.TryGetValue(filename, out var path)) continue;
                    if (TryReadTexture(path, density, out var texture)) { candidate.textures.Add(component, texture!); return; }
                    invalid++;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            message = L.Get("skin.loadFailed", L.Localized(ex.Message));
            return false;
        }
    }

    public float? CatcherHeightBelowPlate(float fieldWidth, double circleSize)
    {
        if (!textures.TryGetValue("fruit-catcher-idle-0", out var texture) && !textures.TryGetValue("fruit-catcher-idle", out texture)) return null;
        return Math.Max(0, texture.PixelHeight / (float)texture.Density - 16) * .35f * CatchSize.Scale(circleSize) * 2 * fieldWidth / 512;
    }
    public bool DrawCatcher(ICanvas canvas, float centerX, float catchY, float fieldWidth, double circleSize, uint tint = 0xFFFFFF, float opacity = 1, bool additive = false)
    {
        if (!textures.TryGetValue("fruit-catcher-idle-0", out var texture) && !textures.TryGetValue("fruit-catcher-idle", out texture)) return false;
        float scale = .5f * .7f * CatchSize.Scale(circleSize) * 2 * fieldWidth / 512;
        float width = texture.PixelWidth / (float)texture.Density * scale;
        float height = texture.PixelHeight / (float)texture.Density * scale;
        var destination = new Rect(centerX - width / 2, catchY - 16 * scale, width, height);
        return additive ? canvas.AdditiveImage(texture.FilePath, destination, tint, opacity)
            : canvas.Image(texture.FilePath, destination, tint, opacity: opacity);
    }

    public bool DrawReverseArrow(ICanvas canvas, float centerX, float centerY, float diameter)
    {
        if (!float.IsFinite(diameter) || diameter <= 0 || !textures.TryGetValue("reversearrow", out var texture)) return false;
        float scale = Math.Min(diameter / (128 * texture.Density), diameter * 2 / Math.Max(texture.PixelWidth, texture.PixelHeight));
        float width = texture.PixelWidth * scale, height = texture.PixelHeight * scale;
        return canvas.Image(texture.FilePath, new(centerX - width / 2, centerY - height / 2, width, height));
    }

    public CatchSkinSprite SpriteFor(CatchSkinObject kind, int index = 0)
    {
        string component = kind switch
        {
            CatchSkinObject.Droplet or CatchSkinObject.TinyDroplet => "fruit-drop",
            CatchSkinObject.Banana => "fruit-bananas",
            _ => "fruit-" + fruitNames[((index % 4) + 4) % 4]
        };
        textures.TryGetValue(component, out var baseTexture);
        textures.TryGetValue(component + "-overlay", out var overlay);
        return new(baseTexture, overlay);
    }

    public bool Draw(ICanvas canvas, CatchSkinObject kind, int index, float centerX, float centerY,
        float nominalFruitDiameter, uint tint = 0xFFFFFF, float opacity = 1)
    {
        if (!float.IsFinite(nominalFruitDiameter) || nominalFruitDiameter <= 0) return false;
        var sprite = SpriteFor(kind, index);
        float scale = ObjectScale(kind, nominalFruitDiameter);
        bool drawn = DrawTexture(sprite.Base, tint);
        drawn |= DrawTexture(sprite.Overlay, 0xFFFFFF);
        return drawn;

        bool DrawTexture(SkinTexture? texture, uint colour)
        {
            if (texture is null) return false;
            return canvas.Image(texture.FilePath, Destination(texture, centerX, centerY, scale), colour, texture.Source, opacity);
        }
    }

    public Rect? Bounds(CatchSkinObject kind, int index, float centerX, float centerY, float nominalFruitDiameter)
    {
        if (!float.IsFinite(nominalFruitDiameter) || nominalFruitDiameter <= 0) return null;
        var sprite = SpriteFor(kind, index);
        float scale = ObjectScale(kind, nominalFruitDiameter);
        Rect? bounds = sprite.Base is null ? null : Destination(sprite.Base, centerX, centerY, scale);
        if (sprite.Overlay is null) return bounds;
        Rect overlay = Destination(sprite.Overlay, centerX, centerY, scale);
        if (bounds is not Rect baseBounds) return overlay;
        float left = Math.Min(baseBounds.X, overlay.X), top = Math.Min(baseBounds.Y, overlay.Y);
        return new Rect(left, top, Math.Max(baseBounds.Right, overlay.Right) - left,
            Math.Max(baseBounds.Bottom, overlay.Bottom) - top);
    }

    private static float ObjectScale(CatchSkinObject kind, float nominalFruitDiameter)
        => nominalFruitDiameter / 128 * (kind switch
        {
            CatchSkinObject.Droplet => 0.8f,
            CatchSkinObject.TinyDroplet => 0.4f,
            CatchSkinObject.Banana => CatchSize.BananaScaleFactor,
            _ => 1
        });

    private static Rect Destination(SkinTexture texture, float centerX, float centerY, float scale)
    {
        float width = texture.LogicalWidth * scale, height = texture.LogicalHeight * scale;
        return new(centerX - width / 2, centerY - height / 2, width, height);
    }

    private void ReadConfiguration(string path)
    {
        if (new FileInfo(path).Length > 1024 * 1024) return;
        string section = "";
        uint? hyper = null, hyperFruit = null, hyperAfterImage = null;
        var combos = new SortedDictionary<int, uint>();
        foreach (string raw in File.ReadLines(path).Take(4096))
        {
            string line = raw.Split("//", 2, StringSplitOptions.None)[0].Trim();
            if (line.Length == 0 || line[0] == ';') continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1].Trim(); continue; }
            int split = line.IndexOf(':');
            if (split < 0) continue;
            string key = line[..split].Trim(), value = line[(split + 1)..].Trim();
            if (section.Equals("General", StringComparison.OrdinalIgnoreCase) && key.Equals("Name", StringComparison.OrdinalIgnoreCase))
            { if (value.Length > 0) Name = value[..Math.Min(value.Length, 120)]; continue; }
            if (!section.Equals("Colours", StringComparison.OrdinalIgnoreCase) || !TryColour(value, out uint colour)) continue;
            if (key.Equals("HyperDashFruit", StringComparison.OrdinalIgnoreCase)) hyperFruit = colour;
            else if (key.Equals("HyperDash", StringComparison.OrdinalIgnoreCase)) hyper = colour;
            else if (key.Equals("HyperDashAfterImage", StringComparison.OrdinalIgnoreCase)) hyperAfterImage = colour;
            else if (key.StartsWith("Combo", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(key.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index is >= 1 and <= 8)
                combos[index] = colour;
        }
        comboColours.AddRange(combos.Values);
        HyperDashFruitColour = hyperFruit ?? hyper ?? 0xFF0000;
        HyperDashColour = hyper ?? 0xFF0000;
        HyperDashAfterImageColour = hyperAfterImage ?? HyperDashColour;
    }

    private static bool TryColour(string text, out uint colour)
    {
        colour = 0;
        var components = text.Split(',');
        if (components.Length is < 3 or > 4) return false;
        for (int i = 0; i < 3; i++)
        {
            if (!byte.TryParse(components[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte component)) return false;
            colour = colour << 8 | component;
        }
        return true;
    }

    private static bool TryReadTexture(string path, int density, out SkinTexture? texture)
    {
        texture = null;
        try
        {
            var info = new FileInfo(path);
            if (info.Length is < 24 or > 32 * 1024 * 1024) return false;
            Span<byte> header = stackalloc byte[24];
            using var stream = File.OpenRead(path);
            stream.ReadExactly(header);
            ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
            if (!header[..8].SequenceEqual(signature) || !header[12..16].SequenceEqual("IHDR"u8)) return false;
            int width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
            int height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
            if (width is < 1 or > 4096 || height is < 1 or > 4096) return false;
            texture = new(path, width, height, density);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
