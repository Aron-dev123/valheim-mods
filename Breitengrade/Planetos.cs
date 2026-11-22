using System;
using System.IO;
using System.IO.Compression;

namespace Breitengrade
{
    /// <summary>
    /// Planetos-Welt: Biome kommen aus einer Rasterkarte (abgeleitet aus einer Fan-Karte von Planetos).
    /// Enthält keinen Unity-Code.
    ///
    /// Die Kontinente werden einzeln aus der Karte ausgeschnitten und in die runde Welt gesetzt
    /// (eigene Position und Größe je Kontinent), damit sie möglichst groß werden:
    ///   Westeros links, oben direkt an der Nordkappe (Deep North),
    ///   Essos rechts daneben, Sothoryos darunter, unten direkt an der Südkappe (Ashlands).
    /// Daraus entsteht ein Welt-Raster, auf dem Küstenabstand, Tiefe in Ashlands/Deep North,
    /// Bergfaktor und Höhengrenze berechnet werden.
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

        /// <summary>Zeile der Mauer auf der Karte (wie im Umwandlungs-Werkzeug). Die Mauer ist gerade.</summary>
        public const float WallV = 455f;
        const float CastleBlackU = 398f;

        // ------------------------------------------------------------ Kontinente

        /// <summary>
        /// Ausschnitt der Fan-Karte (u0..u1, v0..v1 in Bildpixeln) und Ziel in der Welt
        /// (x0 = linker Rand, z0 = oberer Rand, in Anteilen des Landradius; xs/zs = Radius-Anteil je Bildpixel).
        /// </summary>
        struct Region
        {
            public string Name;
            public float U0, U1, V0, V1, X0, Z0, Xs, Zs;
        }

        static readonly Region[] Regions =
        {
            // Westeros: groß, Nordspitze (Lands of Always Winter) an der Nordkappe
            new Region { Name = "Westeros", U0 = 150f, U1 = 512f, V0 = 285f, V1 = 1078f, X0 = -0.80f, Z0 = 0.80f, Xs = 0.00133f, Zs = 0.00133f },
            // Essos: rechts daneben, Meerenge dazwischen (ca. 1 km)
            new Region { Name = "Essos", U0 = 520f, U1 = 1830f, V0 = 540f, V1 = 1252f, X0 = -0.255f, Z0 = 0.338f, Xs = 0.00092f, Zs = 0.00092f },
            // Sothoryos und Ulthos: darunter, unten an der Südkappe
            new Region { Name = "Sothoryos", U0 = 500f, U1 = 2000f, V0 = 1290f, V1 = 1666f, X0 = -0.45f, Z0 = -0.335f, Xs = 0.00093f, Zs = 0.00087f },
        };

        /// <summary>Polkappen: ab diesem Anteil des Radius (Nord/Süd) Deep North bzw. Ashlands.</summary>
        const float NorthCap = 0.72f, SouthCap = 0.66f;

        // ------------------------------------------------------------ Raster

        static int IW, IH;               // Fan-Karte (Bild)
        static byte[] s_image;
        const int N = 1024;              // Welt-Raster N x N
        static byte[] s_grid;            // Biom-Code je Weltzelle
        static byte[] s_biome;           // dto., Meer bekommt das Biom des nächsten Landes
        static float[] s_coast;          // Meter: + im Wasser, - an Land
        static float[] s_ash, s_dn;      // Meter: + innerhalb, - außerhalb
        static float[] s_mountain;       // 0..1 weich
        static float[] s_capSouthToNorth;// 0 = Südgrenze, 1 = Nordgrenze, 2 = frei

        /// <summary>Meter je Weltzelle.</summary>
        public static float PixelSize = 30f;
        static float s_radius = 10000f, s_extent = 10500f;
        static float s_scale = 1f;
        static Noise s_noise = new Noise(1);

        public static bool Loaded => s_image != null;

        public static void Load(byte[] gz)
        {
            using (var ms = new MemoryStream(gz))
            using (var z = new GZipStream(ms, CompressionMode.Decompress))
            using (var outMs = new MemoryStream())
            {
                z.CopyTo(outMs);
                var all = outMs.ToArray();
                IW = all[0] | (all[1] << 8);
                IH = all[2] | (all[3] << 8);
                s_image = new byte[IW * IH];
                Array.Copy(all, 4, s_image, 0, IW * IH);
            }
        }

        /// <summary>Baut das Welt-Raster und die Abstandsfelder für Weltgröße und Seed.</summary>
        public static void Configure(int seed, float scale)
        {
            s_scale = scale;
            s_noise = new Noise(seed ^ 0x51A3);
            s_radius = 10000f * scale;
            s_extent = 10500f * scale;
            PixelSize = 2f * s_extent / N;
            int n = N * N;

            // 1) Welt-Raster aus den Kontinenten und den Polkappen
            s_grid = new byte[n];
            for (int gy = 0; gy < N; gy++)
            for (int gx = 0; gx < N; gx++)
            {
                float x = (gx + 0.5f) * PixelSize - s_extent;
                float z = s_extent - (gy + 0.5f) * PixelSize;
                s_grid[gy * N + gx] = CellCode(x, z);
            }

            // Mauer: zusammenhängendes Land auf der Mauerzeile um die Schwarze Festung (von Küste zu Küste)
            int row = (int)WallV;
            int cb = (int)CastleBlackU;
            int a0 = cb, a1 = cb;
            while (a0 > 0 && s_image[row * IW + a0 - 1] != R_OCEAN) a0--;
            while (a1 < IW - 1 && s_image[row * IW + a1 + 1] != R_OCEAN) a1++;
            WallU0 = a0; WallU1 = a1;

            bool[] water = new bool[n];
            for (int i = 0; i < n; i++) water[i] = s_grid[i] == R_OCEAN;

            // 2) Meer: Biom des nächstgelegenen Landes (für flache Ufer)
            var nearestLand = NearestSeed(i => !water[i]);
            s_biome = new byte[n];
            for (int i = 0; i < n; i++) s_biome[i] = water[i] ? s_grid[nearestLand[i]] : s_grid[i];

            // 3) Abstandsfelder
            s_coast = Signed(i => water[i]);
            s_ash = Signed(i => s_grid[i] == R_ASHLANDS);
            s_dn = Signed(i => s_grid[i] == R_DEEPNORTH);

            var m = new float[n];
            var cap = new float[n];
            for (int i = 0; i < n; i++)
            {
                byte b = s_biome[i];
                m[i] = b == R_MOUNTAIN ? 1f : 0f;
                cap[i] = b == R_MOUNTAIN || b == R_DEEPNORTH ? 2f : b == R_BLACKFOREST ? 1f : 0f;
            }
            s_mountain = Blur(m, 6);
            s_capSouthToNorth = Blur(cap, 8);
        }

        /// <summary>Biom-Code einer Weltposition beim Aufbau des Rasters.</summary>
        static byte CellCode(float x, float z)
        {
            float nx = x / s_radius, nz = z / s_radius;
            float xs = x / s_scale;
            float wobN = 0.02f * s_noise.Perlin(xs * 0.0012f, 17.3f) + 0.008f * s_noise.Perlin(xs * 0.006f, 3.1f);
            float wobS = 0.02f * s_noise.Perlin(xs * 0.0012f, -17.3f) + 0.008f * s_noise.Perlin(xs * 0.006f, -3.1f);
            if (nz > NorthCap + wobN) return R_DEEPNORTH;
            if (nz < -SouthCap + wobS) return R_ASHLANDS;

            foreach (var r in Regions)
            {
                float u = r.U0 + (nx - r.X0) / r.Xs;
                float v = r.V0 + (r.Z0 - nz) / r.Zs;
                if (u < r.U0 || u >= r.U1 || v < r.V0 || v >= r.V1) continue;
                byte c = s_image[(int)v * IW + (int)u];
                if (c != R_OCEAN) return c;
            }
            return R_OCEAN;
        }

        // ------------------------------------------------------------ Koordinaten

        static void ToCell(float x, float z, out float u, out float v)
        {
            u = (x + s_extent) / PixelSize;
            v = (s_extent - z) / PixelSize;
        }

        /// <summary>Bildkoordinaten der Fan-Karte → Weltposition (über den passenden Kontinent).</summary>
        public static void FromPixel(float u, float v, out float x, out float z)
        {
            foreach (var r in Regions)
            {
                if (u < r.U0 || u >= r.U1 || v < r.V0 || v >= r.V1) continue;
                x = (r.X0 + (u - r.U0) * r.Xs) * s_radius;
                z = (r.Z0 - (v - r.V0) * r.Zs) * s_radius;
                return;
            }
            // außerhalb der Kontinente: Polkappen
            x = (u - 1100f) / 1100f * s_radius;
            z = (v < 800f ? 0.85f : -0.80f) * s_radius;
        }

        static bool InGrid(float u, float v) => u >= 0f && v >= 0f && u < N && v < N;

        static float Sample(float[] f, float u, float v)
        {
            u -= 0.5f; v -= 0.5f;
            int u0 = Clamp((int)Math.Floor(u), 0, N - 1), v0 = Clamp((int)Math.Floor(v), 0, N - 1);
            int u1 = Math.Min(N - 1, u0 + 1), v1 = Math.Min(N - 1, v0 + 1);
            float fu = Clamp01(u - u0), fv = Clamp01(v - v0);
            float a = f[v0 * N + u0] + (f[v0 * N + u1] - f[v0 * N + u0]) * fu;
            float b = f[v1 * N + u0] + (f[v1 * N + u1] - f[v1 * N + u0]) * fu;
            return a + (b - a) * fv;
        }

        // ------------------------------------------------------------ Gefrorenes Meer

        /// <summary>
        /// Valheims Wasser-Grafik blendet das Meer dort aus, wo nach der normalen Weltform der
        /// Deep North liegt (ab ca. 12 km um den Punkt 0/-4000). In normalen Welten liegt dort Eis.
        /// Diese Formel steckt in der Grafik und lässt sich nicht ändern, deshalb machen wir das Meer
        /// in diesem Bereich zu einer flachen Eisfläche (plus kleiner Rand, damit keine Kante sichtbar ist).
        /// </summary>
        public static bool IsIceShelf(float x, float z)
        {
            float d = (float)Math.Sqrt(x * x + (z + 4000f) * (z + 4000f));
            float ring = 12000f + (float)Math.Sin(Math.Atan2(x, z) * 20.0) * 100f - 120f;
            if (d <= ring) return false;
            ToCell(x, z, out float u, out float v);
            return InGrid(u, v) && Sample(s_coast, u, v) > 0f;
        }

        // ------------------------------------------------------------ Abfragen

        public static int GetLandBiome(float x, float z)
        {
            if (IsOnWall(x, z)) return Latitude.DeepNorth;
            ToCell(x, z, out float u, out float v);
            if (!InGrid(u, v)) return Latitude.Meadows;
            // leicht verwackelt, damit Grenzen nicht pixelig wirken
            float xs = x / s_scale, zs = z / s_scale;
            u += s_noise.Perlin(xs * 0.01f, zs * 0.01f) * 1.4f;
            v += s_noise.Perlin(xs * 0.01f + 31.7f, zs * 0.01f - 12.3f) * 1.4f;
            int iu = Clamp((int)u, 0, N - 1), iv = Clamp((int)v, 0, N - 1);
            return CodeToBiome[s_biome[iv * N + iu]];
        }

        /// <summary>Abstand zur Küste in Metern: + im Wasser, - an Land.</summary>
        public static float CoastDistance(float x, float z)
        {
            ToCell(x, z, out float u, out float v);
            if (!InGrid(u, v)) return 3000f;
            float xs = x / s_scale, zs = z / s_scale;
            float wobble = s_noise.Perlin(xs * 0.006f, zs * 0.006f) * 40f + s_noise.Perlin(xs * 0.02f, zs * 0.02f) * 12f;
            return Sample(s_coast, u, v) + wobble;
        }

        public static float AshlandsDepth(float x, float z)
        {
            ToCell(x, z, out float u, out float v);
            return InGrid(u, v) ? Sample(s_ash, u, v) : -1000f;
        }

        public static float DeepNorthDepth(float x, float z)
        {
            ToCell(x, z, out float u, out float v);
            return InGrid(u, v) ? Sample(s_dn, u, v) : -1000f;
        }

        public static float MountainFactor(float x, float z)
        {
            ToCell(x, z, out float u, out float v);
            return InGrid(u, v) ? Sample(s_mountain, u, v) : 0f;
        }

        public static float HeightCap(float x, float z, float southCap, float northCap)
        {
            ToCell(x, z, out float u, out float v);
            if (!InGrid(u, v)) return southCap;
            float t = Sample(s_capSouthToNorth, u, v);
            if (t <= 1f) return southCap + (northCap - southCap) * t;
            return northCap + (t - 1f) * 3f; // Mountain/Deep North: praktisch keine Grenze
        }

        // ------------------------------------------------------------ Die Mauer

        /// <summary>Anfang und Ende der Mauer auf der Karte (Westküste bis Ostküste des Festlands).</summary>
        public static float WallU0 = 240f, WallU1 = 430f;

        static float WallLineV(float u) => WallV;

        /// <summary>Halbe Breite der Mauerkrone und Breite der Flanken in Metern.</summary>
        const float WallHalf = 12f, WallFlank = 18f;

        /// <summary>Abstand (Meter) eines Punkts zur Mittellinie der Mauer, oder unendlich außerhalb ihrer Länge.</summary>
        static float WallDistance(float x, float z)
        {
            var r = Regions[0]; // Westeros
            float u = r.U0 + (x / s_radius - r.X0) / r.Xs;
            float v = r.V0 + (r.Z0 - z / s_radius) / r.Zs;
            if (u < WallU0 - 1.5f || u > WallU1 + 1.5f) return float.MaxValue;
            return Math.Abs(v - WallV) * r.Zs * s_radius;
        }

        /// <summary>
        /// Höhe der Mauer an dieser Stelle (Meter) oder NaN, wenn hier keine Mauer ist.
        /// Oben ca. 24 m breit, Flanken fast senkrecht, durchgehend von Küste zu Küste.
        /// </summary>
        public static float WallHeight(float x, float z, float top)
        {
            float dz = WallDistance(x, z);
            if (dz > WallHalf + WallFlank) return float.NaN;
            float f = dz <= WallHalf ? 1f : 1f - Smooth((dz - WallHalf) / WallFlank);
            return top * f;
        }

        /// <summary>Liegt der Punkt auf der Mauer (Krone oder Flanke, plus Rand)?</summary>
        public static bool IsOnWall(float x, float z, float margin = 0f) =>
            WallDistance(x, z) <= WallHalf + WallFlank + margin;

        /// <summary>Punkte entlang der Mauer (alle ca. <paramref name="step"/> Meter), für freigehaltene Flächen.</summary>
        public static System.Collections.Generic.List<(float x, float z)> WallPoints(float step)
        {
            var list = new System.Collections.Generic.List<(float x, float z)>();
            var r = Regions[0];
            float du = step / (r.Xs * s_radius);
            for (float u = WallU0 - 1f; u <= WallU1 + 1f; u += du)
            {
                FromPixel(u, WallV, out float x, out float z);
                list.Add((x, z));
            }
            return list;
        }

        /// <summary>Weltposition auf der Mauer (Mitte) bei Bildspalte u.</summary>
        public static void WallPoint(float u, out float x, out float z) => FromPixel(u, WallLineV(u), out x, out z);

        // ------------------------------------------------------------ Orte

        /// <summary>Benannte Orte (Bildkoordinaten auf der Fan-Karte) mit dem Biom, in dem das Portal stehen soll.</summary>
        public static readonly (string name, float u, float v, int biome)[] Places =
        {
            // Westeros (Koordinaten auf der Karte mit Ortsnamen)
            ("Lands of Always Winter", 315f, 300f, Latitude.DeepNorth),
            ("Thenn", 343f, 360f, Latitude.DeepNorth),
            ("Hardhome", 437f, 417f, Latitude.DeepNorth),
            ("Castle Black", 398f, 468f, Latitude.Mountain),
            ("Deepwood Motte", 297f, 530f, Latitude.BlackForest),
            ("Karhold", 470f, 531f, Latitude.BlackForest),
            ("The Dreadfort", 422f, 557f, Latitude.BlackForest),
            ("Winterfell", 347f, 567f, Latitude.BlackForest),
            ("White Harbor", 380f, 637f, Latitude.BlackForest),
            ("The Neck", 305f, 690f, Latitude.Swamp),
            ("The Twins", 329f, 733f, Latitude.Meadows),
            ("Pyke", 235f, 768f, Latitude.Meadows),
            ("The Eyrie", 410f, 762f, Latitude.Meadows),
            ("Riverrun", 320f, 790f, Latitude.Meadows),
            ("Harrenhal", 373f, 812f, Latitude.Meadows),
            ("Lannisport", 233f, 846f, Latitude.Meadows),
            ("Dragonstone", 470f, 832f, Latitude.Meadows),
            ("King's Landing", 395f, 870f, Latitude.Meadows),
            ("Storm's End", 446f, 931f, Latitude.Meadows),
            ("Highgarden", 272f, 950f, Latitude.Meadows),
            ("Oldtown", 230f, 1001f, Latitude.Meadows),
            ("Sunspear", 476f, 1043f, Latitude.Plains),
            // Essos
            ("Braavos", 564f, 712f, Latitude.Meadows),
            ("Lorath", 630f, 731f, Latitude.Meadows),
            ("Norvos", 662f, 806f, Latitude.Meadows),
            ("Pentos", 566f, 867f, Latitude.Meadows),
            ("Qohor", 752f, 866f, Latitude.Meadows),
            ("Myr", 600f, 953f, Latitude.Meadows),
            ("The Sorrows", 700f, 955f, Latitude.Swamp),
            ("Lys", 570f, 1044f, Latitude.Meadows),
            ("Volantis", 728f, 1051f, Latitude.Meadows),
            ("Valyria", 817f, 1185f, Latitude.Meadows),
            ("Vaes Dothrak", 1182f, 820f, Latitude.Plains),
            ("Meereen", 1007f, 1000f, Latitude.Plains),
            ("Astapor", 972f, 1080f, Latitude.Plains),
            ("Old Ghis", 988f, 1146f, Latitude.Plains),
            ("The Red Waste", 1230f, 1040f, Latitude.Plains),
            ("Qarth", 1268f, 1143f, Latitude.Plains),
            ("Samyriana", 1322f, 963f, Latitude.Plains),
            ("Port of Ibben", 1193f, 612f, Latitude.BlackForest),
            ("Yin", 1438f, 1217f, Latitude.Meadows),
            // Sothoryos, Ulthos und Süden
            ("Asshai", 1628f, 1362f, Latitude.Plains),
            ("Sothoryos", 1036f, 1386f, Latitude.Mistlands),
            ("Yeen", 930f, 1500f, Latitude.Mistlands),
            ("Ulthos", 1736f, 1460f, Latitude.Mistlands),
            ("Land of Fire", 1250f, 1760f, Latitude.AshLands),
        };

        /// <summary>
        /// Wie viele Orte (Points of Interest) um einen benannten Ort herum entstehen.
        /// Ohne Eintrag: DefaultPoiCount. Die Art richtet sich nach dem Biom (Dorf, Fuling-Lager, Turmruinen, Dvergr …).
        /// </summary>
        public const int DefaultPoiCount = 5;

        public static readonly System.Collections.Generic.Dictionary<string, int> PoiCount =
            new System.Collections.Generic.Dictionary<string, int>
        {
            ["Hardhome"] = 3,
            ["Winterfell"] = 12,
            ["King's Landing"] = 8,
            ["Oldtown"] = 6,
            ["Volantis"] = 7,
            ["Vaes Dothrak"] = 10,
            ["Meereen"] = 6,
            ["Qarth"] = 6,
            ["Asshai"] = 6,
            ["Yeen"] = 10,
            ["Sothoryos"] = 6,
            ["Ulthos"] = 6,
            ["Castle Black"] = 8,
            ["The Neck"] = 7,
            ["The Sorrows"] = 7,
            ["Land of Fire"] = 12,
        };

        /// <summary>Frühere deutsche Portalnamen → englische Namen (für schon erstellte Welten).</summary>
        public static readonly System.Collections.Generic.Dictionary<string, string> OldNames =
            new System.Collections.Generic.Dictionary<string, string>
        {
            ["Land des ewigen Winters"] = "Lands of Always Winter",
            ["Jenseits der Mauer"] = "Beyond the Wall",
            ["Hartheim"] = "Hardhome",
            ["Schwarze Festung"] = "Castle Black",
            ["Die Eng"] = "The Neck",
            ["Hohenehr"] = "The Eyrie",
            ["Schnellwasser"] = "Riverrun",
            ["Casterly Stein"] = "Casterly Rock",
            ["Königsmund"] = "King's Landing",
            ["Rosengarten"] = "Highgarden",
            ["Altsass"] = "Oldtown",
            ["Sonnspeer"] = "Sunspear",
            ["Sumpf von Essos"] = "The Sorrows",
            ["Rote Wüste"] = "The Red Waste",
            ["Feuerland"] = "Land of Fire",
            ["Die Mauer"] = "The Wall",
        };

        /// <summary>Startpunkt und Magierturm in der Mitte von Westeros.</summary>
        public const float SpawnU = 335f, SpawnV = 862f, MageU = 300f, MageV = 828f;

        // ------------------------------------------------------------ Abstandsfelder

        /// <summary>Für jede Zelle der Index der nächsten Zelle, auf die <paramref name="seed"/> zutrifft.</summary>
        static int[] NearestSeed(Func<int, bool> seed)
        {
            int n = N * N;
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
            int n = N * N;
            var dIn = new float[n];
            var dOut = new float[n];
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
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;
                Relax(d, near, i, x - 1, y, A); Relax(d, near, i, x - 1, y - 1, B);
                Relax(d, near, i, x, y - 1, A); Relax(d, near, i, x + 1, y - 1, B);
            }
            for (int y = N - 1; y >= 0; y--)
            for (int x = N - 1; x >= 0; x--)
            {
                int i = y * N + x;
                Relax(d, near, i, x + 1, y, A); Relax(d, near, i, x + 1, y + 1, B);
                Relax(d, near, i, x, y + 1, A); Relax(d, near, i, x - 1, y + 1, B);
            }
        }

        static void Relax(float[] d, int[] near, int i, int x, int y, float w)
        {
            if (x < 0 || y < 0 || x >= N || y >= N) return;
            int j = y * N + x;
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
            for (int y = 0; y < N; y++)
            {
                float sum = 0f; int cnt = 0;
                for (int x = -r; x < N; x++)
                {
                    int add = x + r, rem = x - r - 1;
                    if (add < N) { sum += src[y * N + add]; cnt++; }
                    if (rem >= 0) { sum -= src[y * N + rem]; cnt--; }
                    if (x >= 0) tmp[y * N + x] = sum / cnt;
                }
            }
            for (int x = 0; x < N; x++)
            {
                float sum = 0f; int cnt = 0;
                for (int y = -r; y < N; y++)
                {
                    int add = y + r, rem = y - r - 1;
                    if (add < N) { sum += tmp[add * N + x]; cnt++; }
                    if (rem >= 0) { sum -= tmp[rem * N + x]; cnt--; }
                    if (y >= 0) dst[y * N + x] = sum / cnt;
                }
            }
            return dst;
        }

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        static float Smooth(float t) { t = Clamp01(t); return t * t * (3f - 2f * t); }
    }
}
