using System.Buffers.Binary;
using System.IO.Compression;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.App.Skinning;

string root = FindRoot();
string runDirectory = Path.Combine(root, "artifacts", "tests", "skin-layout", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(runDirectory);
string defaultSkinPackage = Path.Combine(root, "assets", "skins", "default.osk");
var tests = new List<(string Name, Action Run)>
{
    ("A doubled texture has the same logical size and is preferred over 1x", HighDensity),
    ("Reverse arrows use skin density and fall back for missing or undecodable images", ReverseArrows),
    ("Oversized artwork is centre cropped without distorting the other axis", CentreCrop),
    ("Base tint and independently sized white overlay compose at one centre", Overlay),
    ("Droplets keep their aspect ratio; tiny droplets are half their size", Droplets),
    ("Banana base and overlay use the arrival scale at both viewport sizes", Bananas),
    ("Sprite bounds union cropped base and overlay with each object's scale", SpriteBounds),
    ("Skin colours and white defaults are explicit", Colours),
    ("Invalid metadata is rejected and a valid 1x file can replace bad 2x", InvalidMetadata),
    ("Missing sprites and drawing failures request a geometric fallback", MissingSprites)
};
if (File.Exists(defaultSkinPackage))
    tests.Add(("The supplied default osk retains its actual fruit and droplet dimensions", DefaultPackage));
else
    Console.WriteLine("SKIP Local default skin dimensions: optional assets/skins/default.osk is not present.");
int passed = 0;
foreach (var (name, run) in tests)
{
    try { run(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"{passed}/{tests.Count} skin layout tests passed; PNG decoding is verified separately by the renderer.");
return passed == tests.Count ? 0 : 1;

void ReverseArrows()
{
    string folder = Fixture("reverse-arrows");
    Header(folder, "reversearrow.png", 128, 64);
    Header(folder, "reversearrow@2x.png", 256, 128);
    var canvas = new RecordingCanvas();
    True(Load(folder).DrawReverseArrow(canvas, 100, 200, 38));
    var image = canvas.Calls.Single();
    True(image.Path.EndsWith("reversearrow@2x.png"));
    Rectangle(image.Destination, 81, 190.5f, 38, 19);
    Equal(0xFFFFFF, image.Tint);
    canvas.AcceptImages = false;
    True(!Load(folder).DrawReverseArrow(canvas, 100, 200, 38));
    File.WriteAllBytes(Path.Combine(folder, "reversearrow@2x.png"), new byte[24]);
    canvas.Calls.Clear(); canvas.AcceptImages = true;
    True(Load(folder).DrawReverseArrow(canvas, 100, 200, 38));
    True(canvas.Calls.Single().Path.EndsWith("reversearrow.png"));
    string missing = Fixture("no-reverse-arrow");
    Header(missing, "fruit-pear.png", 128, 128);
    True(!Load(missing).DrawReverseArrow(canvas, 100, 200, 38));
}

void HighDensity()
{
    string folder = Fixture("density");
    Header(folder, "fruit-pear.png", 128, 128);
    Header(folder, "fruit-pear@2x.png", 256, 256);
    var skin = Load(folder);
    var sprite = skin.SpriteFor(CatchSkinObject.Fruit).Base!;
    Equal(2, sprite.Density);
    Equal(128, sprite.LogicalWidth);
    var canvas = new RecordingCanvas();
    True(skin.Draw(canvas, CatchSkinObject.Fruit, 0, 100, 200, 64));
    Rectangle(canvas.Calls.Single().Destination, 68, 168, 64, 64);
    Rectangle(canvas.Calls.Single().Source!.Value, 0, 0, 256, 256);
    True(canvas.Calls.Single().Path.EndsWith("@2x.png", StringComparison.Ordinal));
}

void CentreCrop()
{
    string folder = Fixture("crop");
    Header(folder, "fruit-pear@2x.png", 400, 200);
    var skin = Load(folder);
    var canvas = new RecordingCanvas();
    True(skin.Draw(canvas, CatchSkinObject.Fruit, 0, 100, 100, 64));
    Rectangle(canvas.Calls.Single().Source!.Value, 40, 0, 320, 200);
    Rectangle(canvas.Calls.Single().Destination, 60, 75, 80, 50);
}

void Overlay()
{
    string folder = Fixture("overlay");
    Header(folder, "fruit-pear.png", 128, 128);
    Header(folder, "fruit-pear-overlay@2x.png", 200, 120);
    var skin = Load(folder);
    var canvas = new RecordingCanvas();
    True(skin.Draw(canvas, CatchSkinObject.Fruit, 0, 200, 100, 64, 0xFF0000));
    Equal(2, canvas.Calls.Count);
    Equal(0xFF0000, canvas.Calls[0].Tint);
    Equal(0xFFFFFF, canvas.Calls[1].Tint);
    Rectangle(canvas.Calls[0].Destination, 168, 68, 64, 64);
    Rectangle(canvas.Calls[1].Destination, 175, 85, 50, 30);
}

void Droplets()
{
    string folder = Fixture("droplets");
    Header(folder, "fruit-drop@2x.png", 164, 206);
    var skin = Load(folder);
    var canvas = new RecordingCanvas();
    True(skin.Draw(canvas, CatchSkinObject.Droplet, 0, 100, 100, 64, 0x804020));
    True(skin.Draw(canvas, CatchSkinObject.TinyDroplet, 0, 100, 100, 64, 0x804020));
    Rectangle(canvas.Calls[0].Destination, 83.6f, 79.4f, 32.8f, 41.2f);
    Rectangle(canvas.Calls[1].Destination, 91.8f, 89.7f, 16.4f, 20.6f);
    Equal(0x804020, canvas.Calls[0].Tint);
    Equal(0x804020, canvas.Calls[1].Tint);
    True(canvas.Calls[0].Path == canvas.Calls[1].Path);
}

void Bananas()
{
    string folder = Fixture("bananas");
    Header(folder, "fruit-pear@2x.png", 256, 256);
    Header(folder, "fruit-bananas@2x.png", 256, 256);
    Header(folder, "fruit-bananas-overlay@2x.png", 200, 120);
    var skin = Load(folder);
    var canvas = new RecordingCanvas();
    True(skin.Draw(canvas, CatchSkinObject.Fruit, 0, 100, 200, 64));
    True(skin.Draw(canvas, CatchSkinObject.Banana, 0, 100, 200, 64, 0xFFE000));
    Rectangle(canvas.Calls[0].Destination, 68, 168, 64, 64);
    Rectangle(canvas.Calls[1].Destination, 80.8f, 180.8f, 38.4f, 38.4f);
    Rectangle(canvas.Calls[2].Destination, 85, 191, 30, 18);
    Rectangle(canvas.Calls[1].Source!.Value, 0, 0, 256, 256);
    Equal(0xFFE000, canvas.Calls[1].Tint);
    Equal(0xFFFFFF, canvas.Calls[2].Tint);
    True(canvas.Calls[1].Path.EndsWith("fruit-bananas@2x.png", StringComparison.Ordinal));

    True(skin.Draw(canvas, CatchSkinObject.Banana, 0, 100, 200, 32));
    Rectangle(canvas.Calls[3].Destination, 90.4f, 190.4f, 19.2f, 19.2f);
    Rectangle(canvas.Calls[4].Destination, 92.5f, 195.5f, 15, 9);
}

void SpriteBounds()
{
    string folder = Fixture("sprite-bounds");
    Header(folder, "fruit-pear@2x.png", 256, 256);
    Header(folder, "fruit-pear-overlay@2x.png", 400, 120);
    Header(folder, "fruit-drop@2x.png", 164, 206);
    Header(folder, "fruit-bananas@2x.png", 256, 256);
    Header(folder, "fruit-bananas-overlay@2x.png", 400, 120);
    var skin = Load(folder);
    Rectangle(skin.Bounds(CatchSkinObject.Fruit, 0, 100, 200, 64)!.Value, 60, 168, 80, 64);
    Rectangle(skin.Bounds(CatchSkinObject.Droplet, 0, 100, 200, 64)!.Value, 83.6f, 179.4f, 32.8f, 41.2f);
    Rectangle(skin.Bounds(CatchSkinObject.TinyDroplet, 0, 100, 200, 64)!.Value, 91.8f, 189.7f, 16.4f, 20.6f);
    Rectangle(skin.Bounds(CatchSkinObject.Banana, 0, 100, 200, 64)!.Value, 76, 180.8f, 48, 38.4f);
    True(skin.Bounds(CatchSkinObject.Fruit, 1, 100, 200, 64) is null);
    True(skin.Bounds(CatchSkinObject.Fruit, 0, 100, 200, float.NaN) is null);

    string overlayFolder = Fixture("overlay-only-bounds");
    Header(overlayFolder, "fruit-bananas-overlay@2x.png", 200, 120);
    Rectangle(Load(overlayFolder).Bounds(CatchSkinObject.Banana, 0, 100, 200, 64)!.Value, 85, 191, 30, 18);
}

void Colours()
{
    string folder = Fixture("colours");
    Header(folder, "fruit-pear.png", 128, 128);
    File.WriteAllText(Path.Combine(folder, "skin.ini"), "[General]\nName: Fixture skin\n[Colours]\nCombo2: 1,2,3\nCombo1: 255,255,255\nCombo3: 256,0,0\nHyperDash: 1,2,3\nHyperDashFruit: 210,20,40\n");
    var skin = Load(folder);
    True(skin.Name == "Fixture skin");
    Equal(2, skin.ComboColours.Count);
    Equal(0xFFFFFF, skin.ComboColours[0]);
    Equal(0x010203, skin.ComboColours[1]);
    Equal(0xD21428, skin.HyperDashFruitColour);
    var canvas = new RecordingCanvas();
    True(skin.Draw(canvas, CatchSkinObject.Fruit, 0, 0, 0, 64));
    Equal(0xFFFFFF, canvas.Calls.Single().Tint);
    File.WriteAllText(Path.Combine(folder, "skin.ini"), "[Colours]\nHyperDash: 100,50,25\n");
    Equal(0x643219, Load(folder).HyperDashFruitColour);
    File.WriteAllText(Path.Combine(folder, "skin.ini"), "[Colours]\n");
    Equal(0xFF0000, Load(folder).HyperDashFruitColour);
}

void InvalidMetadata()
{
    string broken = Fixture("broken");
    File.WriteAllBytes(Path.Combine(broken, "fruit-pear.png"), new byte[24]);
    True(!CatchSkin.TryLoad(broken, out var rejected, out _));
    True(rejected is null);
    string fallback = Fixture("metadata-fallback");
    Header(fallback, "fruit-pear@2x.png", 5000, 128);
    Header(fallback, "fruit-pear.png", 128, 128);
    Equal(1, Load(fallback).SpriteFor(CatchSkinObject.Fruit).Base!.Density);
    string empty = Fixture("empty");
    True(!CatchSkin.TryLoad(empty, out _, out _));
}

void MissingSprites()
{
    string folder = Fixture("fallback");
    Header(folder, "fruit-pear.png", 128, 128);
    var skin = Load(folder);
    var canvas = new RecordingCanvas();
    True(!skin.Draw(canvas, CatchSkinObject.Droplet, 0, 0, 0, 64));
    Equal(0, canvas.Calls.Count);
    canvas.AcceptImages = false;
    True(!skin.Draw(canvas, CatchSkinObject.Fruit, 0, 0, 0, 64));
    Equal(1, canvas.Calls.Count);
    True(!skin.Draw(canvas, CatchSkinObject.Fruit, 0, 0, 0, float.NaN));
    Equal(1, canvas.Calls.Count);
}

void DefaultPackage()
{
    string folder = Fixture("default-package");
    string[] required = ["skin.ini", "fruit-pear@2x.png", "fruit-pear-overlay@2x.png", "fruit-drop@2x.png"];
    using var archive = ZipFile.OpenRead(defaultSkinPackage);
    foreach (string name in required)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidOperationException($"Missing default skin fixture: {name}");
        using var input = entry.Open();
        using var output = File.Create(Path.Combine(folder, name));
        input.CopyTo(output);
    }
    var skin = Load(folder);
    var pear = skin.SpriteFor(CatchSkinObject.Fruit).Base!;
    var drop = skin.SpriteFor(CatchSkinObject.Droplet).Base!;
    Equal(256, pear.PixelWidth);
    Equal(128, pear.LogicalWidth);
    Equal(164, drop.PixelWidth);
    Equal(206, drop.PixelHeight);
    Equal(82, drop.LogicalWidth);
    Equal(103, drop.LogicalHeight);
    Equal(4, skin.ComboColours.Count);
    Equal(0xFF0000, skin.HyperDashFruitColour);
    True(skin.SpriteFor(CatchSkinObject.Droplet).Overlay is null);
}

string Fixture(string name)
{
    string folder = Path.Combine(runDirectory, name);
    Directory.CreateDirectory(folder);
    return folder;
}

static CatchSkin Load(string folder)
{
    if (!CatchSkin.TryLoad(folder, out var skin, out string message)) throw new InvalidOperationException(message);
    return skin!;
}

static void Header(string folder, string filename, int width, int height)
{
    // Header-only fixtures exercise metadata and layout; the recording canvas never decodes their pixels.
    Span<byte> header = stackalloc byte[24];
    ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
    signature.CopyTo(header);
    BinaryPrimitives.WriteInt32BigEndian(header[8..12], 13);
    "IHDR"u8.CopyTo(header[12..16]);
    BinaryPrimitives.WriteInt32BigEndian(header[16..20], width);
    BinaryPrimitives.WriteInt32BigEndian(header[20..24], height);
    File.WriteAllBytes(Path.Combine(folder, filename), header.ToArray());
}

static void Rectangle(Rect actual, float x, float y, float width, float height)
{
    Equal(x, actual.X); Equal(y, actual.Y); Equal(width, actual.Width); Equal(height, actual.Height);
}
static void Equal(double expected, double actual)
{
    if (Math.Abs(expected - actual) > 0.0001) throw new InvalidOperationException($"Expected {expected}; got {actual}");
}
static void True(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed"); }
static string FindRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json"))) directory = directory.Parent;
    return directory?.FullName ?? throw new InvalidOperationException("Cannot locate project root");
}

sealed class RecordingCanvas : ICanvas
{
    public bool AcceptImages { get; set; } = true;
    public List<(string Path, Rect Destination, uint Tint, Rect? Source)> Calls { get; } = [];
    public bool Image(string filePath, Rect destination, uint tint = 0xFFFFFF, Rect? source = null, float opacity = 1)
    { Calls.Add((filePath, destination, tint, source)); return AcceptImages; }
    public void Fill(Rect r, uint color, float radius = 0) { }
    public void Stroke(Rect r, uint color, float width = 1, float radius = 0) { }
    public void Line(float x1, float y1, float x2, float y2, uint color, float width = 1, float opacity = 1) { }
    public void Circle(float x, float y, float radius, uint color, bool filled = true, float width = 1, float opacity = 1) { }
    public void Text(string text, float x, float y, float size, uint color, float maxWidth = 10000, bool bold = false) { }
    public void Clip(Rect r) { }
    public void Unclip() { }
}
