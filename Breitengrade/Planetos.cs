using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Breitengrade
{
    /// <summary>
    /// Planetos-Welt: Biome kommen aus einer Rasterkarte (abgeleitet aus einer Fan-Karte von Planetos).
    /// Enthält keinen Unity-Code. Liefert dieselben Abfragen wie die Breitengrad-Bänder: Biom,
    /// Küstenabstand, Tiefe in Ashlands/Deep North, Bergfaktor, Höhengrenze und die Mauer.
    /// </summary>
    public static class Planetos
    {
        public static bool Enabled;

        // Biom-Codes in der Rasterdatei
        const byte R_OCEAN = 0, R_MEADOWS = 1, R_SWAMP = 2, R_MOUNTAIN = 3, R_BLACKFOREST = 4,
                   R_PLAINS = 5, R_ASHLANDS = 6, R_DEEPNORTH = 7, R_MISTLANDS = 8;

        static readonly int[] CodeToBiome =
        {
            Latitude.Ocean, Latitude.Meadows, Latitude.Swamp, Latitude.Mountain, Latitude.BlackForest,
            Latitude.Plains, Latitude.AshLands, Latitude.DeepNorth, Latitude.Mistlands,
        };

        /// <summary>Breitengrad der Mauer in Pixelzeilen (wie im Umwandlungs-Werkzeug).</summary>
        public const float WallV = 192f;

        static int W, H;
        static byte[] s_raw;
        static byte[] s_biome;           // Biom je Pixel; Meer bekommt das Biom des nächsten Landes
        static bool[] s_water;
        static float[] s_coast;          // Meter: + im Wasser, - an Land
        static float[] s_ash, s_dn;      // Meter: + innerhalb, - außerhalb
        static float[] s_mountain;       // 0..1 weich
        static float[] s_capSouthToNorth;// 0 = Südgrenze, 1 = Nordgrenze, 2 = frei

        /// <summary>
        /// Ausschnitt der Karte, der auf die runde Welt passt: Mitte (CU, CV) und Radius RPX in Bildpixeln.
        /// Westeros bis Sothoryos; die fernen Kontinente ganz links und rechts fallen weg.
        /// </summary>
        const float CU = 610f, CV = 340f, RPX = 380f;

        /// <summary>Polkappen: ab diesem Anteil des Radius (Nord/Süd) Deep North bzw. Ashlands.</summary>
        const float PolarCap = 0.83f;

        /// <summary>Meter je Pixel (Landradius 10000 m x Faktor geteilt durch RPX).</summary>
        public static float PixelSize = 26f;
        static float s_radius = 10000f;
        static float s_scale = 1f;
        static Noise s_noise = new Noise(1);

        public static bool Loaded => s_raw != null;

        public static void Load(byte[] gz)
        {
            using (var ms = new MemoryStream(gz))
            using (var z = new GZipStream(ms, CompressionMode.Decompress))
            using (var outMs = new MemoryStream())
            {
                z.CopyTo(outMs);
                var all = outMs.ToArray();
                W = all[0] | (all[1] << 8);
                H = all[2] | (all[3] << 8);
                s_raw = new byte[W * H];
                Array.Copy(all, 4, s_raw, 0, W * H);
            }
        }

        /// <summary>Bereitet die Abfragen für Weltgröße und Seed vor (Abstandsfelder usw.).</summary>
        public static void Configure(int seed, float scale)
        {
            s_scale = scale;
            s_noise = new Noise(seed ^ 0x51A3);
            s_radius = 10000f * scale;
            PixelSize = s_radius / RPX;
            int n = W * H;

            s_water = new bool[n];
            for (int i = 0; i < n; i++) s_water[i] = s_raw[i] == R_OCEAN;

            // Meer: Biom des nächstgelegenen Landes (für flache Ufer)
            var nearestLand = NearestSeed(i => !s_water[i]);
            s_biome = new byte[n];
            for (int i = 0; i < n; i++) s_biome[i] = s_water[i] ? s_raw[nearestLand[i]] : s_raw[i];

            s_coast = Signed(i => s_water[i]);
            s_ash = Signed(i => s_raw[i] == R_ASHLANDS);
            s_dn = Signed(i => s_raw[i] == R_DEEPNORTH);

            var m = new float[n];
            var cap = new float[n];
            for (int i = 0; i < n; i++)
            {
                byte b = s_biome[i];
                m[i] = b == R_MOUNTAIN ? 1f : 0f;
                cap[i] = b == R_MOUNTAIN || b == R_DEEPNORTH ? 2f : b == R_BLACKFOREST ? 1f : 0f;
            }
            s_mountain = Blur(m, 8);
            s_capSouthToNorth = Blur(cap, 10);
        }

        // ------------------------------------------------------------ Koordinaten

        static void ToPixel(float x, float z, out float u, out float v)
        {
            u = CU + x / PixelSize;
            v = CV - z / PixelSize;
        }

        public static void FromPixel(float u, float v, out float x, out float z)
        {
            x = (u - CU) * PixelSize;
            z = (CV - v) * PixelSize;
        }

        static bool InImage(float u, float v) => u >= 0f && v >= 0f && u < W && v < H;

        /// <summary>Wie weit (Meter) ein Punkt in der Polkappe liegt: + drin, - außerhalb. Nord: +1, Süd: -1.</summary>
        static float CapDepth(float x, float z, float side)
        {
            float xs = x / s_scale;
            float cap = PolarCap + 0.025f * s_noise.Perlin(xs * 0.0012f, side * 17.3f) + 0.01f * s_noise.Perlin(xs * 0.006f, side * 3.1f);
            return (side * z / s_radius - cap) * s_radius;
        }

        static int Index(float u, float v)
        {
            int iu = Clamp((int)u, 0, W - 1), iv = Clamp((int)v, 0, H - 1);
            return iv * W + iu;
        }

        static float Sample(float[] f, float u, float v)
        {
            u -= 0.5f; v -= 0.5f;
            int u0 = Clamp((int)Math.Floor(u), 0, W - 1), v0 = Clamp((int)Math.Floor(v), 0, H - 1);
            int u1 = Math.Min(W - 1, u0 + 1), v1 = Math.Min(H - 1, v0 + 1);
            float fu = Clamp01(u - u0), fv = Clamp01(v - v0);
            float a = f[v0 * W + u0] + (f[v0 * W + u1] - f[v0 * W + u0]) * fu;
            float b = f[v1 * W + u0] + (f[v1 * W + u1] - f[v1 * W + u0]) * fu;
            return a + (b - a) * fv;
        }

        // ------------------------------------------------------------ Abfragen

        public static int GetLandBiome(float x, float z)
        {
            if (z > 0f && CapDepth(x, z, 1f) > -40f) return Latitude.DeepNorth;
            if (z < 0f && CapDepth(x, z, -1f) > -40f) return Latitude.AshLands;
            ToPixel(x, z, out float u, out float v);
            if (!InImage(u, v)) return z > 0f ? Latitude.DeepNorth : Latitude.AshLands;
            // leicht verwackelt, damit Grenzen nicht pixelig wirken
            float xs = x / s_scale, zs = z / s_scale;
            u += s_noise.Perlin(xs * 0.01f, zs * 0.01f) * 1.3f;
            v += s_noise.Perlin(xs * 0.01f + 31.7f, zs * 0.01f - 12.3f) * 1.3f;
            return CodeToBiome[s_biome[Index(u, v)]];
        }

        /// <summary>Abstand zur Küste in Metern: + im Wasser, - an Land.</summary>
        public static float CoastDistance(float x, float z)
        {
            ToPixel(x, z, out float u, out float v);
            float c = InImage(u, v) ? Sample(s_coast, u, v) : 3000f;
            // Polkappen sind Land: Abstand zur Kappe zählt als Küste
            float cap = -CapDepth(x, z, z >= 0f ? 1f : -1f);
            c = Math.Min(c, cap);
            float xs = x / s_scale, zs = z / s_scale;
            float wobble = s_noise.Perlin(xs * 0.006f, zs * 0.006f) * 40f + s_noise.Perlin(xs * 0.02f, zs * 0.02f) * 12f;
            return c + wobble;
        }

        public static float AshlandsDepth(float x, float z)
        {
            float cap = CapDepth(x, z, -1f);
            ToPixel(x, z, out float u, out float v);
            return InImage(u, v) ? Math.Max(cap, Sample(s_ash, u, v)) : cap;
        }

        public static float DeepNorthDepth(float x, float z)
        {
            float cap = CapDepth(x, z, 1f);
            ToPixel(x, z, out float u, out float v);
            return InImage(u, v) ? Math.Max(cap, Sample(s_dn, u, v)) : cap;
        }

        public static float MountainFactor(float x, float z)
        {
            ToPixel(x, z, out float u, out float v);
            if (!InImage(u, v)) return 0f;
            return Sample(s_mountain, u, v);
        }

        public static float HeightCap(float x, float z, float southCap, float northCap)
        {
            if (CapDepth(x, z, 1f) > -300f) return 10f;
            ToPixel(x, z, out float u, out float v);
            if (!InImage(u, v)) return southCap;
            float t = Sample(s_capSouthToNorth, u, v);
            if (t <= 1f) return southCap + (northCap - southCap) * t;
            return northCap + (t - 1f) * 3f; // Mountain/Deep North: praktisch keine Grenze
        }

        // ------------------------------------------------------------ Die Mauer

        /// <summary>Wellige Linie der Mauer (gleich wie die Klimagrenze im Umwandlungs-Werkzeug).</summary>
        static float WallLineV(float u) =>
            WallV - (5f * (float)Math.Sin(u * 0.045f) + 3f * (float)Math.Sin(u * 0.13f + 1.7f) + 1.5f * (float)Math.Sin(u * 0.37f));

        /// <summary>Westeros liegt auf dem Bild etwa zwischen diesen Spalten.</summary>
        const float WesterosU0 = 255f, WesterosU1 = 360f;

        /// <summary>
        /// Höhe der Mauer an dieser Stelle (Meter) oder NaN, wenn hier keine Mauer ist.
        /// Oben ca. 24 m breit, Flanken fast senkrecht, quer über ganz Westeros von Küste zu Küste.
        /// </summary>
        public static float WallHeight(float x, float z, float top)
        {
            ToPixel(x, z, out float u, out float v);
            if (u < WesterosU0 || u > WesterosU1) return float.NaN;
            float dz = Math.Abs(v - WallLineV(u)) * PixelSize;
            const float half = 12f, flank = 18f;
            if (dz > half + flank) return float.NaN;
            // an der Küste auslaufen lassen
            float coast = Sample(s_coast, u, v);
            if (coast > 60f) return float.NaN;
            float edge = Clamp01((60f - coast) / 80f);
            float f = dz <= half ? 1f : 1f - Smooth((dz - half) / flank);
            return top * f * edge;
        }

        /// <summary>Weltposition auf der Mauer (Mitte) bei Bildspalte u.</summary>
        public static void WallPoint(float u, out float x, out float z) => FromPixel(u, WallLineV(u), out x, out z);

        // ------------------------------------------------------------ Orte

        /// <summary>Benannte Orte (Bildkoordinaten auf der Fan-Karte) mit dem Biom, in dem das Portal stehen soll.</summary>
        public static readonly (string name, float u, float v, int biome)[] Places =
        {
            // Westeros
            ("Land des ewigen Winters", 610f, 10f, Latitude.DeepNorth),
            ("Jenseits der Mauer", 300f, 160f, Latitude.DeepNorth),
            ("Hartheim", 328f, 178f, Latitude.DeepNorth),
            ("Schwarze Festung", 312f, 199f, Latitude.Mountain),
            ("Winterfell", 300f, 226f, Latitude.BlackForest),
            ("Die Eng", 306f, 258f, Latitude.Swamp),
            ("Hohenehr", 332f, 268f, Latitude.Meadows),
            ("Schnellwasser", 300f, 276f, Latitude.Meadows),
            ("Casterly Stein", 277f, 285f, Latitude.Meadows),
            ("Königsmund", 338f, 292f, Latitude.Meadows),
            ("Rosengarten", 297f, 310f, Latitude.Meadows),
            ("Altsass", 285f, 322f, Latitude.Meadows),
            ("Sonnspeer", 338f, 330f, Latitude.Meadows),
            // Essos
            ("Braavos", 372f, 250f, Latitude.Meadows),
            ("Pentos", 370f, 286f, Latitude.Meadows),
            ("Volantis", 398f, 335f, Latitude.Meadows),
            ("Sumpf von Essos", 468f, 300f, Latitude.Swamp),
            ("Vaes Dothrak", 530f, 275f, Latitude.Plains),
            ("Meereen", 575f, 340f, Latitude.Plains),
            ("Rote Wüste", 690f, 340f, Latitude.Plains),
            ("Asshai", 790f, 325f, Latitude.Plains),
            // Sothoryos und Süden
            ("Sothoryos", 450f, 450f, Latitude.Mistlands),
            ("Yeen", 660f, 470f, Latitude.Mistlands),
            ("Feuerland", 610f, 670f, Latitude.AshLands),
        };

        /// <summary>Startpunkt und Magierturm in der Mitte von Westeros.</summary>
        public const float SpawnU = 304f, SpawnV = 296f, MageU = 306f, MageV = 279f;

        // ------------------------------------------------------------ Abstandsfelder

        /// <summary>Für jeden Pixel der Index des nächsten Pixels, auf den <paramref name="seed"/> zutrifft.</summary>
        static int[] NearestSeed(Func<int, bool> seed)
        {
            int n = W * H;
            var near = new int[n];
            var dist = new float[n];
            for (int i = 0; i < n; i++)
            {
                bool s = seed(i);
                near[i] = s ? i : -1;
                dist[i] = s ? 0f : float.MaxValue;
            }
            Chamfer(dist, near);
            for (int i = 0; i < n; i++) if (near[i] < 0) near[i] = i;
            return near;
        }

        /// <summary>Vorzeichenbehafteter Abstand in Metern: + innerhalb der Maske, - außerhalb.</summary>
        static float[] Signed(Func<int, bool> inside)
        {
            int n = W * H;
            var dIn = new float[n];   // für Pixel außerhalb: Abstand zur Maske
            var dOut = new float[n];  // für Pixel innerhalb: Abstand zum Rand
            for (int i = 0; i < n; i++)
            {
                bool s = inside(i);
                dIn[i] = s ? 0f : float.MaxValue;
                dOut[i] = s ? float.MaxValue : 0f;
            }
            Chamfer(dIn, null);
            Chamfer(dOut, null);
            var r = new float[n];
            for (int i = 0; i < n; i++)
            {
                float a = dOut[i] >= float.MaxValue / 2 ? 9999f : dOut[i];
                float b = dIn[i] >= float.MaxValue / 2 ? 9999f : dIn[i];
                r[i] = (a > 0f ? a - 0.5f : -(b - 0.5f)) * PixelSize;
            }
            return r;
        }

        /// <summary>Zwei-Durchgangs-Abstandstransformation (8 Nachbarn).</summary>
        static void Chamfer(float[] d, int[] near)
        {
            const float A = 1f, B = 1.41421f;
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                Relax(d, near, i, x - 1, y, A); Relax(d, near, i, x - 1, y - 1, B);
                Relax(d, near, i, x, y - 1, A); Relax(d, near, i, x + 1, y - 1, B);
            }
            for (int y = H - 1; y >= 0; y--)
            for (int x = W - 1; x >= 0; x--)
            {
                int i = y * W + x;
                Relax(d, near, i, x + 1, y, A); Relax(d, near, i, x + 1, y + 1, B);
                Relax(d, near, i, x, y + 1, A); Relax(d, near, i, x - 1, y + 1, B);
            }
        }

        static void Relax(float[] d, int[] near, int i, int x, int y, float w)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            int j = y * W + x;
            float c = d[j] + w;
            if (c < d[i])
            {
                d[i] = c;
                if (near != null) near[i] = near[j];
            }
        }

        static float[] Blur(float[] src, int r)
        {
            var tmp = new float[src.Length];
            var dst = new float[src.Length];
            for (int y = 0; y < H; y++)
            {
                float sum = 0f; int cnt = 0;
                for (int x = -r; x < W; x++)
                {
                    int add = x + r, rem = x - r - 1;
                    if (add < W) { sum += src[y * W + add]; cnt++; }
                    if (rem >= 0) { sum -= src[y * W + rem]; cnt--; }
                    if (x >= 0) tmp[y * W + x] = sum / cnt;
                }
            }
            for (int x = 0; x < W; x++)
            {
                float sum = 0f; int cnt = 0;
                for (int y = -r; y < H; y++)
                {
                    int add = y + r, rem = y - r - 1;
                    if (add < H) { sum += tmp[add * W + x]; cnt++; }
                    if (rem >= 0) { sum -= tmp[rem * W + x]; cnt--; }
                    if (y >= 0) dst[y * W + x] = sum / cnt;
                }
            }
            return dst;
        }

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        static float Smooth(float t) { t = Clamp01(t); return t * t * (3f - 2f * t); }
    }
}
