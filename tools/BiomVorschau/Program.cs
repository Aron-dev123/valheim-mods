using System.IO.Compression;
using Breitengrade;

// Zeichnet die Biom-Karte der Breitengrade-Mod als PNG, ohne Valheim zu starten.
// Aufruf: dotnet run -- [ausgabe.png] [seed] [grenzen] [welligkeit] [pixel]

// Planetos: dotnet run -- planetos <ausgabe.png> [weltgroesse]
if (args.Length > 0 && args[0] == "planetos")
{
    float scale = args.Length > 2 ? float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 1.5f;
    Planetos.Load(File.ReadAllBytes(Path.Combine("..", "..", "Breitengrade", "Planetos.biome.gz")));
    Latitude.Configure(12345, null, 400f, scale);
    Planetos.Configure(12345, scale);
    Planetos.Enabled = true;
    args = new[] { args.Length > 1 ? args[1] : "planetos-welt.png", "12345", Latitude.DefaultBounds, "400", "1024", "planetos" };
}
bool planetosMode = args.Length > 5 && args[5] == "planetos";
string outPath = args.Length > 0 ? args[0] : "biom-vorschau.png";
int seed = args.Length > 1 ? int.Parse(args[1]) : 12345;
string boundsText = args.Length > 2 ? args[2] : Latitude.DefaultBounds;
float wobble = args.Length > 3 ? float.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 400f;
int size = args.Length > 4 ? int.Parse(args[4]) : 1024;

if (!Latitude.TryParseBounds(boundsText, out var bounds))
{
    Console.Error.WriteLine("Ungültige Grenzen.");
    return 1;
}
if (!planetosMode) Latitude.Configure(seed, bounds, wobble);

var colors = new Dictionary<int, (byte r, byte g, byte b)>
{
    [Latitude.AshLands] = (150, 40, 25),
    [Latitude.Plains] = (222, 196, 90),
    [Latitude.Meadows] = (120, 190, 70),
    [Latitude.Swamp] = (95, 80, 55),
    [Latitude.Mistlands] = (110, 95, 140),
    [Latitude.BlackForest] = (35, 85, 40),
    [Latitude.Mountain] = (215, 215, 225),
    [Latitude.DeepNorth] = (170, 210, 245),
    [Latitude.Ocean] = (30, 60, 120),
};
(byte r, byte g, byte b) ocean = (30, 60, 120), outside = (12, 12, 16);

float S = planetosMode ? Latitude.Scale : 1f;
float worldRadius = 10500f * S, landRadius = 10250f * S;
var px = new byte[size * size * 3];
for (int py = 0; py < size; py++)
{
    // Bild oben = Norden (positives z)
    float z = worldRadius - (py + 0.5f) * (2 * worldRadius / size);
    for (int pxi = 0; pxi < size; pxi++)
    {
        float x = -worldRadius + (pxi + 0.5f) * (2 * worldRadius / size);
        float d = MathF.Sqrt(x * x + z * z);
        var c = d > worldRadius ? outside : d > landRadius ? ocean : colors[Latitude.GetBiome(x, z)];

        if (planetosMode && d <= landRadius && !float.IsNaN(Planetos.WallHeight(x, z, 260f))) c = (255, 255, 255);
        // Gitter alle 2000 m und Markierung der Weltmitte (Startpunkt)
        if (d <= landRadius && !planetosMode)
        {
            bool grid = MathF.Abs(x % 2000f) < 18f || MathF.Abs(z % 2000f) < 18f;
            if (grid) c = ((byte)(c.r * 0.8f), (byte)(c.g * 0.8f), (byte)(c.b * 0.8f));
            if (d < 120f) c = (255, 255, 255);
            if (d < 70f) c = (220, 30, 30);
        }

        int i = (py * size + pxi) * 3;
        px[i] = c.r; px[i + 1] = c.g; px[i + 2] = c.b;
    }
}

WritePng(outPath, size, size, px);

// Flächenanteile ausgeben
var counts = new Dictionary<int, int>();
int total = 0;
for (int iz = -100; iz <= 100; iz++)
for (int ix = -100; ix <= 100; ix++)
{
    float x = ix * 100f * S, z = iz * 100f * S;
    if (x * x + z * z > landRadius * landRadius) continue;
    int b = Latitude.GetBiome(x, z);
    counts[b] = counts.TryGetValue(b, out var n) ? n + 1 : 1;
    total++;
}
for (int i = 0; i < Latitude.BandBiomes.Length; i++)
{
    counts.TryGetValue(Latitude.BandBiomes[i], out var n);
    Console.WriteLine($"{Latitude.BandNames[i],-13} {100.0 * n / total,5:0.0} %");
}
counts.TryGetValue(Latitude.Ocean, out var oceanCount);
Console.WriteLine($"{"Meer",-13} {100.0 * oceanCount / total,5:0.0} %");
Console.WriteLine($"Startpunkt (0,0): {Latitude.BandNames[Latitude.BandIndex(0, 0)]}");
Console.WriteLine($"Gespeichert: {Path.GetFullPath(outPath)}");
return 0;

static void WritePng(string path, int w, int h, byte[] rgb)
{
    using var fs = File.Create(path);
    fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

    var ihdr = new byte[13];
    WriteBE(ihdr, 0, w); WriteBE(ihdr, 4, h);
    ihdr[8] = 8; ihdr[9] = 2; // 8 Bit, RGB
    Chunk(fs, "IHDR", ihdr);

    using var raw = new MemoryStream();
    using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
    {
        for (int y = 0; y < h; y++)
        {
            z.WriteByte(0);
            z.Write(rgb, y * w * 3, w * 3);
        }
    }
    Chunk(fs, "IDAT", raw.ToArray());
    Chunk(fs, "IEND", Array.Empty<byte>());
}

static void Chunk(Stream s, string type, byte[] data)
{
    var len = new byte[4]; WriteBE(len, 0, data.Length); s.Write(len);
    var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
    s.Write(typeBytes); s.Write(data);
    uint crc = Crc(Crc(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
    var c = new byte[4]; WriteBE(c, 0, (int)crc); s.Write(c);
}

static uint Crc(uint crc, byte[] data)
{
    foreach (var b in data)
    {
        crc ^= b;
        for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
    }
    return crc;
}

static void WriteBE(byte[] buf, int o, int v)
{
    buf[o] = (byte)(v >> 24); buf[o + 1] = (byte)(v >> 16); buf[o + 2] = (byte)(v >> 8); buf[o + 3] = (byte)v;
}
