using System;

namespace Breitengrade
{
    /// <summary>
    /// Ordnet jedem Punkt der Welt ein Biom nach seiner Nord-Süd-Position zu.
    /// Enthält bewusst keinen Unity- oder Valheim-Code, damit das Vorschau-Werkzeug
    /// genau dieselbe Datei benutzen kann.
    /// </summary>
    public static class Latitude
    {
        // Werte entsprechen Heightmap.Biome in Valheim.
        public const int Meadows = 1, Swamp = 2, Mountain = 4, BlackForest = 8,
                         Plains = 16, AshLands = 32, DeepNorth = 64, Ocean = 256, Mistlands = 512;

        /// <summary>Biome von Süden nach Norden.</summary>
        public static readonly int[] BandBiomes =
            { AshLands, Plains, Meadows, Swamp, Mistlands, BlackForest, Mountain, DeepNorth };

        public static readonly string[] BandNames =
            { "Ashlands", "Plains", "Meadows", "Swamp", "Mistlands", "Black Forest", "Mountain", "Deep North" };

        /// <summary>
        /// Grenzen zwischen den Bändern in Metern (Süd = negativ, Nord = positiv).
        /// Die Welt reicht von -10000 bis +10000. Meadows liegt über der Mitte (0),
        /// damit der Startpunkt dort bleibt.
        /// </summary>
        public static float[] Bounds = { -6650f, -3200f, 1000f, 1900f, 4400f, 6250f, 7300f };

        public const string DefaultBounds = "-6650,-3200,1000,1900,4400,6250,7300";

        /// <summary>Wie stark die Grenzen wellig sind (Meter nach Nord/Süd).</summary>
        public static float WobbleAmplitude = 400f;

        static float s_ox, s_oy;
        static Noise s_noise = new Noise(0);

        /// <summary>Weltgrößen-Faktor. Alle Abmessungen (Bänder, Meer, Welligkeit) werden damit multipliziert.</summary>
        public static float Scale = 1f;

        public static void Configure(int seed, float[] bounds, float wobble, float scale = 1f)
        {
            Scale = scale;
            var rnd = new Random(seed);
            s_ox = rnd.Next(-50000, 50000);
            s_oy = rnd.Next(-50000, 50000);
            s_noise = new Noise(seed);
            if (bounds != null && bounds.Length == BandBiomes.Length - 1)
                Bounds = Array.ConvertAll(bounds, b => b * scale);
            WobbleAmplitude = wobble * scale;
            CoastWobble = 250f * scale;
        }

        public static bool TryParseBounds(string text, out float[] bounds)
        {
            bounds = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = text.Split(',');
            if (parts.Length != BandBiomes.Length - 1) return false;
            var result = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!float.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out result[i])) return false;
                if (i > 0 && result[i] <= result[i - 1]) return false;
            }
            bounds = result;
            return true;
        }

        /// <summary>Nord-Süd-Position mit welliger Verschiebung, damit Grenzen nicht schnurgerade sind.</summary>
        public static float WobbledZ(float x, float z)
        {
            x /= Scale; z /= Scale;
            float n1 = s_noise.Perlin((x + s_ox) * 0.0006f, (z + s_oy) * 0.0006f);
            float n2 = s_noise.Perlin((x - s_oy) * 0.0025f, (z + s_ox) * 0.0025f);
            return z * Scale + (n1 * 0.8f + n2 * 0.2f) * WobbleAmplitude;
        }

        public static int BandIndex(float x, float z)
        {
            float zz = WobbledZ(x, z);
            int i = 0;
            while (i < Bounds.Length && zz >= Bounds[i]) i++;
            return i;
        }

        // ------------------------------------------------------------ Meer mit Insel

        /// <summary>Großes rundes Meer mit einer Insel in der Mitte (Meadows im Norden, Plains im Süden).</summary>
        public static bool SeaEnabled = true;
        public static float SeaX = 5350f, SeaZ = -1100f;
        /// <summary>Außenradius des Meeres und Radius der Insel. Wasser ≈ 1/8, Insel ≈ 1/20 der Karte.</summary>
        public static float SeaRadius = 4180f, IslandRadius = 2240f;
        public static float CoastWobble = 250f;

        /// <summary>Ab so viel Metern vom Ufer zählt ein Punkt als offenes Meer (Biom Ocean).</summary>
        public const float OpenSeaDistance = 80f; // echte Meter (Uferbreite bleibt gleich)

        /// <summary>Abstand zur Meeresmitte, mit welliger Küste.</summary>
        static float SeaDistance(float x, float z)
        {
            float dx = x - SeaX, dz = z - SeaZ;
            float xs = x / Scale, zs = z / Scale;
            float n = s_noise.Perlin((xs + s_oy) * 0.0012f, (zs - s_ox) * 0.0012f) * 0.75f +
                      s_noise.Perlin((xs - s_ox) * 0.004f, (zs + s_oy) * 0.004f) * 0.25f;
            return (float)Math.Sqrt(dx * dx + dz * dz) + n * CoastWobble;
        }

        /// <summary>
        /// Abstand zur nächsten Küste des Meeres in Metern: positiv = im Wasser,
        /// negativ = an Land (auf der Insel oder außerhalb des Meeres).
        /// </summary>
        public static float CoastDistance(float x, float z)
        {
            if (Planetos.Enabled) return Planetos.CoastDistance(x, z);
            if (!SeaEnabled) return -1e6f;
            float d = SeaDistance(x, z);
            if (d < IslandRadius) return d - IslandRadius;
            if (d < SeaRadius) return Math.Min(d - IslandRadius, SeaRadius - d);
            return SeaRadius - d;
        }

        public static bool IsIsland(float x, float z) => !Planetos.Enabled && SeaEnabled && SeaDistance(x, z) < IslandRadius;

        /// <summary>Biom an Land (ohne Meer): Insel oder Breitengrad-Band.</summary>
        public static int GetLandBiome(float x, float z)
        {
            if (Planetos.Enabled) return Planetos.GetLandBiome(x, z);
            // Auch das flache Ufer rund um die Insel gehört zur Insel (bis zur Mitte des Wasserrings).
            if (SeaEnabled && SeaDistance(x, z) < (IslandRadius + SeaRadius) / 2f)
            {
                // Insel: Norden Meadows, Süden Plains, mit welliger Grenze.
                float w = s_noise.Perlin((x / Scale + s_ox) * 0.002f, (z / Scale + s_oy) * 0.002f) * 300f * Scale;
                return z + w >= SeaZ ? Meadows : Plains;
            }
            return BandBiomes[BandIndex(x, z)];
        }

        /// <summary>Biom inklusive offenem Meer.</summary>
        public static int GetBiome(float x, float z) =>
            CoastDistance(x, z) > OpenSeaDistance ? Ocean : GetLandBiome(x, z);

        /// <summary>Wie weit (Meter) ein Punkt in den Ashlands liegt. Positiv = drin.</summary>
        public static float AshlandsDepth(float x, float z) =>
            Planetos.Enabled ? Planetos.AshlandsDepth(x, z) : Bounds[0] - WobbledZ(x, z);

        /// <summary>Wie weit (Meter) ein Punkt im Deep North liegt. Positiv = drin.</summary>
        public static float DeepNorthDepth(float x, float z) =>
            Planetos.Enabled ? Planetos.DeepNorthDepth(x, z) : WobbledZ(x, z) - Bounds[Bounds.Length - 1];

        /// <summary>0..1: wie sehr ein Punkt im Mountain-Band liegt (weicher Übergang am Rand).</summary>
        public static float MountainFactor(float x, float z)
        {
            if (Planetos.Enabled) return Planetos.MountainFactor(x, z);
            int mi = Array.IndexOf(BandBiomes, Mountain);
            float lo = Bounds[mi - 1], hi = mi < Bounds.Length ? Bounds[mi] : 1e9f;
            float zz = WobbledZ(x, z);
            float fade = 500f * Scale;
            float a = Clamp01((zz - lo) / fade + 0.5f);
            float b = Clamp01((hi - zz) / fade + 0.5f);
            float t = Math.Min(a, b);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Höchste Grundhöhe an dieser Stelle. Südlich des Black Forest niedrig (wie normales Land,
        /// kein Schnee), im Black Forest nach Norden ansteigend, ab Mountain unbegrenzt.
        /// </summary>
        public static float HeightCap(float x, float z, float southCap, float northCap)
        {
            if (Planetos.Enabled) return Planetos.HeightCap(x, z, southCap, northCap);
            int bf = Array.IndexOf(BandBiomes, BlackForest);
            float lo = Bounds[bf - 1], hi = Bounds[bf];
            float zz = WobbledZ(x, z);
            if (zz >= hi) return northCap + (zz - hi) / (300f * Scale); // nördlich davon: schnell keine Grenze mehr
            float t = Clamp01((zz - lo) / (hi - lo));
            t = t * t * (3f - 2f * t);
            return southCap + (northCap - southCap) * t;
        }

        static float Smooth(float a, float b, float v)
        {
            float t = Clamp01((v - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }

    /// <summary>Einfaches 2D-Perlin-Rauschen (Ergebnis etwa -1..1), unabhängig von Unity.</summary>
    public sealed class Noise
    {
        readonly int[] _p = new int[512];

        public Noise(int seed)
        {
            var perm = new int[256];
            for (int i = 0; i < 256; i++) perm[i] = i;
            var rnd = new Random(seed);
            for (int i = 255; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                (perm[i], perm[j]) = (perm[j], perm[i]);
            }
            for (int i = 0; i < 512; i++) _p[i] = perm[i & 255];
        }

        public float Perlin(float x, float y)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            float xf = x - xi, yf = y - yi;
            xi &= 255; yi &= 255;
            float u = Fade(xf), v = Fade(yf);
            int aa = _p[_p[xi] + yi], ab = _p[_p[xi] + yi + 1];
            int ba = _p[_p[xi + 1] + yi], bb = _p[_p[xi + 1] + yi + 1];
            float x1 = Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1, yf), u);
            float x2 = Lerp(Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1), u);
            return Lerp(x1, x2, v) * 1.41f;
        }

        static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        static float Grad(int hash, float x, float y)
        {
            switch (hash & 7)
            {
                case 0: return x + y;
                case 1: return -x + y;
                case 2: return x - y;
                case 3: return -x - y;
                case 4: return x;
                case 5: return -x;
                case 6: return y;
                default: return -y;
            }
        }
    }
}
