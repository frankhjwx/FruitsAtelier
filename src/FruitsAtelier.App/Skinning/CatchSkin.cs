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
    private CatchSkin? fallback;
    private string comboPrefix = "score";
    private float comboOverlap;
    private string hitCirclePrefix = "default";
    private float hitCircleOverlap;
    private bool? overlayAboveNumber;
    private bool OverlayAboveNumber => overlayAboveNumber ?? fallback?.OverlayAboveNumber ?? true;
    private uint? sliderTrackColour, sliderBorderColour;
    public uint? SliderTrackColour => sliderTrackColour ?? fallback?.SliderTrackColour;
    public uint SliderBorderColour => sliderBorderColour ?? fallback?.SliderBorderColour ?? 0xFFFFFF;
    public string FolderPath { get; }
    public string Name { get; private set; }
    private static readonly uint[] defaultComboColours = [0xFFC000, 0x00CA00, 0x127CFF, 0xF21839];
    public static IReadOnlyList<uint> DefaultComboColours => defaultComboColours;
    public IReadOnlyList<uint> ComboColours => comboColours.Count > 0 ? comboColours : fallback?.ComboColours ?? defaultComboColours;
    public uint HyperDashFruitColour { get; private set; } = 0xFF0000;
    public uint HyperDashColour { get; private set; } = 0xFF0000;
    public uint HyperDashAfterImageColour { get; private set; } = 0xFF0000;
    public int TextureCount => textures.Count;

    private CatchSkin(string folder)
    {
        FolderPath = folder;
        Name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
    }

    public static bool TryLoad(string folder, out CatchSkin? skin, out string message, CatchSkin? fallback = null, bool allowEmpty = false)
    {
        skin = null;
        try
        {
            var candidate = new CatchSkin(Path.GetFullPath(folder));
            candidate.fallback = fallback;
            if (!Directory.Exists(candidate.FolderPath)) { message = L.Get("skin.folderMissing"); return false; }
            var files = Directory.EnumerateFiles(candidate.FolderPath).ToDictionary(p => Path.GetFileName(p)!, p => p, StringComparer.OrdinalIgnoreCase);
            int invalid = 0;
            if (files.TryGetValue("skin.ini", out var configuration)) candidate.ReadConfiguration(configuration);
            for (int digit = 0; digit <= 9; digit++)
            {
                LoadTexture($"{candidate.comboPrefix}-{digit}");
                LoadTexture($"{candidate.hitCirclePrefix}-{digit}");
                if (candidate.comboPrefix != "score") LoadTexture($"score-{digit}");
            }
            foreach (string name in fruitNames.Concat(["drop", "bananas"]))
            {
                LoadTexture($"fruit-{name}");
                LoadTexture($"fruit-{name}-overlay");
            }
            LoadTexture("reversearrow");
            foreach (string component in new[] { "hitcircle", "hitcircleoverlay", "sliderstartcircle", "sliderstartcircleoverlay", "sliderendcircle", "sliderendcircleoverlay" }) LoadTexture(component);
            LoadTexture("fruit-catcher-idle");
            LoadTexture("fruit-catcher-idle-0");
            if (candidate.textures.Count == 0 && fallback is null && !allowEmpty && !files.Keys.Any(HitsoundResolver.IsSkinSample)) { message = L.Get("skin.noTextures"); return false; }
            skin = candidate;
            message = L.Get("skin.loaded", candidate.Name, candidate.TextureCount, invalid > 0 ? L.Get("skin.invalidImages", invalid) : "");
            return true;

            void LoadTexture(string component)
            {
                foreach (int density in new[] { 2, 1 })
                {
                    string filename = component + (density == 2 ? "@2x" : "") + ".png";
                    if (!files.TryGetValue(filename, out var path)) continue;
                    if (TryReadTexture(path, density, out var texture)) { candidate.textures[component] = texture!; return; }
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
        var texture = Candidates("fruit-catcher-idle-0", "fruit-catcher-idle").FirstOrDefault();
        if (texture is null) return null;
        return Math.Max(0, texture.PixelHeight / (float)texture.Density - 16) * .35f * CatchSize.Scale(circleSize) * 2 * fieldWidth / 512;
    }
    public bool DrawCatcher(ICanvas canvas, float centerX, float catchY, float fieldWidth, double circleSize, uint tint = 0xFFFFFF, float opacity = 1, bool additive = false, bool flipHorizontal = false)
    {
        foreach (var texture in Candidates("fruit-catcher-idle-0", "fruit-catcher-idle"))
        {
            float scale = .5f * .7f * CatchSize.Scale(circleSize) * 2 * fieldWidth / 512;
            float width = texture.PixelWidth / (float)texture.Density * scale;
            float height = texture.PixelHeight / (float)texture.Density * scale;
            var destination = new Rect(centerX - width / 2, catchY - 16 * scale, width, height);
            if (canvas.CatcherImage(texture.FilePath, destination, tint, opacity, additive, flipHorizontal)) return true;
        }
        return false;
    }

    public bool DrawReverseArrow(ICanvas canvas, float centerX, float centerY, float diameter)
    {
        if (!float.IsFinite(diameter) || diameter <= 0) return false;
        foreach (var texture in Candidates("reversearrow"))
        {
            float scale = Math.Min(diameter / (128 * texture.Density), diameter * 2 / Math.Max(texture.PixelWidth, texture.PixelHeight));
            float width = texture.PixelWidth * scale, height = texture.PixelHeight * scale;
            if (canvas.Image(texture.FilePath, new(centerX - width / 2, centerY - height / 2, width, height))) return true;
        }
        return false;
    }

    private IEnumerable<SkinTexture> Candidates(params string[] components)
    {
        foreach (string component in components)
            if (textures.TryGetValue(component, out var texture)) yield return texture;
        if (fallback is not null)
            foreach (var texture in fallback.Candidates(components)) yield return texture;
    }

    public static void DrawTimelineCircle(ICanvas canvas, CatchSkin? skin, float x, float y, float diameter,
        uint colour, int? number = null, string prefix = "hitcircle")
    {
        var provider = skin;
        while (provider != null && !provider.textures.ContainsKey("hitcircle")) provider = provider.fallback;
        provider ??= skin;
        if (provider == null || !provider.textures.ContainsKey(prefix)) prefix = "hitcircle";
        bool Texture(string name, uint tint)
        {
            if (skin == null) return false;
            foreach (var texture in skin.Candidates(name))
            {
                float scale = Math.Min(diameter / (128 * texture.Density), diameter * 2 / Math.Max(texture.PixelWidth, texture.PixelHeight));
                float w = texture.PixelWidth * scale, h = texture.PixelHeight * scale;
                if (canvas.Image(texture.FilePath, new(x - w / 2, y - h / 2, w, h), tint)) return true;
            }
            return false;
        }
        if (!Texture(prefix, colour))
        {
            canvas.Circle(x, y, diameter / 2, colour);
            canvas.Circle(x, y, diameter / 2, 0xFFFFFF, false, 1.5f);
        }
        bool above = skin?.OverlayAboveNumber ?? true;
        if (!above) Texture(prefix + "overlay", 0xFFFFFF);
        if (number is int value && !(skin?.DrawHitCircleNumber(canvas, value, x, y, diameter / 128) ?? false))
        {
            string label = value.ToString(CultureInfo.InvariantCulture);
            canvas.Text(label, x - canvas.MeasureText(label, 13) / 2, y - 8, 13, 0xFFFFFF, diameter);
        }
        if (above) Texture(prefix + "overlay", 0xFFFFFF);
    }

    private bool DrawHitCircleNumber(ICanvas canvas, int number, float x, float y, float scale)
    {
        var owner = this;
        string text = number.ToString(CultureInfo.InvariantCulture);
        while (owner != null)
        {
            var glyphs = text.Select(d => owner.textures.GetValueOrDefault($"{owner.hitCirclePrefix}-{d}")).ToArray();
            if (glyphs.All(g => g != null))
            {
                float width = glyphs.Sum(g => g!.PixelWidth / (float)g.Density) - owner.hitCircleOverlap * (glyphs.Length - 1);
                float left = x - width * scale / 2;
                foreach (var glyph in glyphs)
                {
                    float w = glyph!.PixelWidth / (float)glyph.Density * scale, h = glyph.PixelHeight / (float)glyph.Density * scale;
                    if (!canvas.Image(glyph.FilePath, new(left, y - h / 2, w, h))) return false;
                    left += w - owner.hitCircleOverlap * scale;
                }
                return true;
            }
            owner = owner.fallback;
        }
        return false;
    }

    public bool DrawCombo(ICanvas canvas, int combo, float x, float y, float scale, uint tint, float opacity, bool additive)
    {
        string text = combo.ToString(CultureInfo.InvariantCulture);
        var glyphs = text.Select(digit => ComboGlyph(digit)).ToArray();
        if (glyphs.Any(g => g is null)) return false;
        float width = glyphs.Sum(g => g!.PixelWidth / (float)g.Density) - comboOverlap * (glyphs.Length - 1);
        float height = glyphs.Max(g => g!.PixelHeight / (float)g.Density);
        float at = x - width * scale / 2;
        foreach (var glyph in glyphs)
        {
            float w = glyph!.PixelWidth / (float)glyph.Density;
            var destination = new Rect(at, y - height * scale / 2, w * scale, glyph.PixelHeight / (float)glyph.Density * scale);
            if (additive) canvas.AdditiveImage(glyph.FilePath, destination, tint, opacity);
            else canvas.Image(glyph.FilePath, destination, tint, opacity: opacity);
            at += (w - comboOverlap) * scale;
        }
        return true;
    }

    private SkinTexture? ComboGlyph(char digit) => textures.GetValueOrDefault($"{comboPrefix}-{digit}")
        ?? textures.GetValueOrDefault($"score-{digit}") ?? fallback?.ComboGlyph(digit);

    private static string Component(CatchSkinObject kind, int index) => kind switch
        {
            CatchSkinObject.Droplet or CatchSkinObject.TinyDroplet => "fruit-drop",
            CatchSkinObject.Banana => "fruit-bananas",
            _ => "fruit-" + fruitNames[((index % 4) + 4) % 4]
        };

    public CatchSkinSprite SpriteFor(CatchSkinObject kind, int index = 0)
    {
        string component = Component(kind, index);
        return new(Candidates(component).FirstOrDefault(), Candidates(component + "-overlay").FirstOrDefault());
    }

    public bool Draw(ICanvas canvas, CatchSkinObject kind, int index, float centerX, float centerY,
        float nominalFruitDiameter, uint tint = 0xFFFFFF, float opacity = 1, float rotation = 0, uint? hyperColour = null)
    {
        if (!float.IsFinite(nominalFruitDiameter) || nominalFruitDiameter <= 0) return false;
        string component = Component(kind, index);
        float scale = ObjectScale(kind, nominalFruitDiameter);
        if (hyperColour is uint glow)
            foreach (var texture in Candidates(component))
                if (canvas.SpriteImage(texture.FilePath, Destination(texture, centerX, centerY, scale * 1.2f),
                    glow, texture.Source, opacity * .7f, rotation, true)) break;
        bool drawn = DrawTexture(component, tint);
        drawn |= DrawTexture(component + "-overlay", 0xFFFFFF);
        return drawn;

        bool DrawTexture(string name, uint colour)
        {
            foreach (var texture in Candidates(name))
                if (canvas.SpriteImage(texture.FilePath, Destination(texture, centerX, centerY, scale), colour, texture.Source, opacity, rotation, false)) return true;
            return false;
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
            if (section.Equals("General", StringComparison.OrdinalIgnoreCase) && key.Equals("HitCircleOverlayAboveNumber", StringComparison.OrdinalIgnoreCase))
                overlayAboveNumber = value != "0";
            if (section.Equals("Fonts", StringComparison.OrdinalIgnoreCase))
            {
                if (key.Equals("HitCirclePrefix", StringComparison.OrdinalIgnoreCase) && value.Length > 0 &&
                    !value.Contains('/') && !value.Contains('\\') && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                    hitCirclePrefix = value;
                if (key.Equals("HitCircleOverlap", StringComparison.OrdinalIgnoreCase) &&
                    float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float hitOverlap) && float.IsFinite(hitOverlap))
                    hitCircleOverlap = Math.Clamp(hitOverlap, -128, 128);
                if (key.Equals("ComboPrefix", StringComparison.OrdinalIgnoreCase) && value.Length > 0 &&
                    !value.Contains('/') && !value.Contains('\\') && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                    comboPrefix = value;
                if (key.Equals("ComboOverlap", StringComparison.OrdinalIgnoreCase) &&
                    float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float overlap) && float.IsFinite(overlap))
                    comboOverlap = Math.Clamp(overlap, -100, 100);
                continue;
            }
            if (section.Equals("General", StringComparison.OrdinalIgnoreCase) && key.Equals("Name", StringComparison.OrdinalIgnoreCase))
            { if (value.Length > 0) Name = value[..Math.Min(value.Length, 120)]; continue; }
            if (!TryColour(value, out uint colour)) continue;
            if (section.Equals("Colours", StringComparison.OrdinalIgnoreCase))
            {
                if (key.Equals("SliderBorder", StringComparison.OrdinalIgnoreCase)) sliderBorderColour = colour;
                if (key.Equals("SliderTrackOverride", StringComparison.OrdinalIgnoreCase)) sliderTrackColour = colour;
            }
            if (section.Equals("CatchTheBeat", StringComparison.OrdinalIgnoreCase))
            {
                if (key.Equals("HyperDashFruit", StringComparison.OrdinalIgnoreCase)) hyperFruit = colour;
                else if (key.Equals("HyperDash", StringComparison.OrdinalIgnoreCase)) hyper = colour;
                else if (key.Equals("HyperDashAfterImage", StringComparison.OrdinalIgnoreCase)) hyperAfterImage = colour;
                continue;
            }
            if (section.Equals("Colours", StringComparison.OrdinalIgnoreCase) && key.StartsWith("Combo", StringComparison.OrdinalIgnoreCase)
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
