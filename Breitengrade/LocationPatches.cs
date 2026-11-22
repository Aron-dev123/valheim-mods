using System;
using System.Collections.Generic;
using HarmonyLib;

namespace Breitengrade
{
    /// <summary>
    /// Flächen der Biome je 500-m-Ring um die Weltmitte.
    /// Werte = Anteil an der Weltscheibe (Radius 10500 m) mal 100000.
    /// </summary>
    internal static class RingAreas
    {
        public const int Rings = 21;
        public const float RingWidth = 500f;

        static readonly int[] BiomeOrder =
        {
            Latitude.Meadows, Latitude.Swamp, Latitude.Mountain, Latitude.BlackForest,
            Latitude.Plains, Latitude.AshLands, Latitude.DeepNorth, Latitude.Mistlands, Latitude.Ocean,
        };

        /// <summary>Gemessen aus dem Biom-Cache einer normalen Valheim-Welt (ohne Mods).</summary>
        static readonly int[][] Vanilla =
        {
            new[] { 222, 286, 175, 371, 476, 478, 225, 167, 223, 264, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },          // Meadows
            new[] { 0, 0, 0, 0, 117, 246, 360, 194, 396, 525, 724, 412, 0, 0, 0, 0, 0, 0, 0, 0, 0 },              // Swamp
            new[] { 0, 10, 71, 65, 212, 103, 46, 262, 133, 35, 176, 229, 200, 213, 298, 300, 157, 111, 92, 185, 27 }, // Mountain
            new[] { 0, 292, 628, 717, 515, 944, 602, 381, 766, 623, 825, 1111, 520, 292, 534, 388, 1172, 717, 675, 914, 470 }, // Black Forest
            new[] { 0, 0, 0, 0, 0, 56, 842, 1505, 1404, 1314, 2071, 1834, 662, 863, 1036, 1150, 0, 0, 0, 0, 0 },  // Plains
            new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 23, 956, 1882, 2559, 3193, 3811 },               // Ashlands
            new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 5, 720, 1256, 1875, 2276, 809 },                 // Deep North
            new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 174, 2582, 2703, 2672, 3162, 2419, 1920, 1443, 999, 0 },     // Mistlands
            new[] { 4, 92, 260, 435, 722, 668, 873, 890, 934, 1548, 945, 1456, 1702, 2048, 2039, 2003, 2056, 2053, 1745, 1276, 4181 }, // Ozean
        };

        static float[][] s_ours;

        /// <summary>Misst unsere Welt mit derselben Einheit wie die Vanilla-Tabelle.</summary>
        public static void MeasureOurs()
        {
            // In normierten Koordinaten messen (geteilt durch den Weltgrößen-Faktor), damit die
            // Werte direkt mit der Vanilla-Tabelle vergleichbar sind.
            float S = WorldScale.S;
            var counts = new int[BiomeOrder.Length][];
            for (int i = 0; i < counts.Length; i++) counts[i] = new int[Rings];
            int total = 0;
            const float step = 100f;
            for (float z = -10500f; z <= 10500f; z += step)
            for (float x = -10500f; x <= 10500f; x += step)
            {
                float d = (float)Math.Sqrt(x * x + z * z);
                if (d > 10500f) continue;
                total++;
                if (d > 10250f) continue; // Weltrand = Wasser
                int bi = Array.IndexOf(BiomeOrder, Latitude.GetBiome(x * S, z * S));
                if (bi < 0) continue;
                counts[bi][Math.Min(Rings - 1, (int)(d / RingWidth))]++;
            }
            s_ours = new float[BiomeOrder.Length][];
            for (int i = 0; i < counts.Length; i++)
            {
                s_ours[i] = new float[Rings];
                for (int r = 0; r < Rings; r++) s_ours[i][r] = counts[i][r] * 100000f / total;
            }
        }

        public static float VanillaArea(int mask, float minD, float maxD) => Sum(mask, minD, maxD, true);
        public static float OurArea(int mask, float minD, float maxD) => Sum(mask, minD, maxD, false);

        static float Sum(int mask, float minD, float maxD, bool vanilla)
        {
            if (!vanilla && s_ours == null) MeasureOurs();
            float sum = 0f;
            for (int bi = 0; bi < BiomeOrder.Length; bi++)
            {
                if ((mask & BiomeOrder[bi]) == 0) continue;
                for (int r = 0; r < Rings; r++)
                {
                    // Anteil des Rings, der im Bereich [minD, maxD] liegt
                    float r0 = r * RingWidth, r1 = r0 + RingWidth;
                    float lo = Math.Max(r0, minD), hi = Math.Min(r1, maxD);
                    if (hi <= lo) continue;
                    float frac = (hi - lo) / RingWidth;
                    sum += frac * (vanilla ? Vanilla[bi][r] : s_ours[bi][r]);
                }
            }
            return sum;
        }
    }

    /// <summary>
    /// Valheim platziert von jeder Struktur (Krypten, Dungeons, Dörfer, Türme …) eine feste Anzahl
    /// pro Welt, teils nur in einem bestimmten Abstand zur Weltmitte. Hier passen wir beides an
    /// unsere Welt an, damit die Dichte je Biom wie in normalem Valheim bleibt.
    /// </summary>
    internal static class LocationScaler
    {
        sealed class Original
        {
            public int Quantity;
            public float MinDistance, MaxDistance, MinFromCenter, MaxFromCenter;
        }

        static readonly AccessTools.FieldRef<ZoneSystem, List<ZoneSystem.ZoneLocation>> Locations =
            AccessTools.FieldRefAccess<ZoneSystem, List<ZoneSystem.ZoneLocation>>("m_locations");
        static readonly AccessTools.FieldRef<ZoneSystem.ZoneLocation, string> AltBiomeParent =
            AccessTools.FieldRefAccess<ZoneSystem.ZoneLocation, string>("m_altBiomeParent");

        static readonly Dictionary<ZoneSystem.ZoneLocation, Original> s_original =
            new Dictionary<ZoneSystem.ZoneLocation, Original>();
        static bool s_loggedForCurrentWorld;

        public static void OnWorldChanged()
        {
            s_loggedForCurrentWorld = false;
            RingAreas.MeasureOurs();
        }

        public static void Apply(ZoneSystem zs)
        {
            var list = Locations(zs);
            if (list == null) return;

            bool log = State.Active && !s_loggedForCurrentWorld;
            int changed = 0;

            foreach (var loc in list)
            {
                if (loc == null) continue;
                if (!s_original.TryGetValue(loc, out var o))
                {
                    s_original[loc] = o = new Original
                    {
                        Quantity = loc.m_quantity,
                        MinDistance = loc.m_minDistance,
                        MaxDistance = loc.m_maxDistance,
                        MinFromCenter = loc.m_minDistanceFromCenter,
                        MaxFromCenter = loc.m_maxDistanceFromCenter,
                    };
                }

                // Erst alles auf Original zurück, dann (nur in unserer Welt) anpassen.
                loc.m_quantity = o.Quantity;
                loc.m_minDistance = o.MinDistance;
                loc.m_maxDistance = o.MaxDistance;
                loc.m_minDistanceFromCenter = o.MinFromCenter;
                loc.m_maxDistanceFromCenter = o.MaxFromCenter;

                if (!State.Active || !loc.m_enable) continue;

                string info = Adjust(loc, o);
                if (info != null)
                {
                    changed++;
                    if (log) Plugin.Log.LogInfo($"[Strukturen] {loc.m_prefabName ?? loc.m_name} ({loc.m_biome}): {info}");
                }
            }

            if (log)
            {
                s_loggedForCurrentWorld = true;
                Plugin.Log.LogInfo($"[Strukturen] {changed} Strukturtypen an die Welt angepasst.");
            }
        }

        /// <summary>Passt Abstandsgrenzen und Anzahl an. Gibt eine Beschreibung zurück oder null.</summary>
        static string Adjust(ZoneSystem.ZoneLocation loc, Original o)
        {
            int mask = (int)loc.m_biome;
            if (!Latitude.SeaEnabled && !Planetos.Enabled) mask &= ~Latitude.Ocean; // ohne Meer gibt es keine Wasser-Orte
            if (mask == 0) return null;

            float minD = Math.Max(o.MinDistance, o.MinFromCenter);
            float maxD = Min0(o.MaxDistance, o.MaxFromCenter);
            if (maxD <= 0f) maxD = 10500f;

            float vanilla = RingAreas.VanillaArea(mask, minD, maxD);
            if (vanilla <= 0f) return null;

            // 1) Abstandsgrenzen: Valheim ist ringförmig aufgebaut, unsere Welt nicht.
            //    Liegt im erlaubten Ring zu wenig von dem Biom, erweitern wir den Ring.
            float newMin = minD, newMax = maxD;
            while (RingAreas.OurArea(mask, newMin, newMax) < vanilla && (newMin > 0f || newMax < 10500f))
            {
                newMin = Math.Max(0f, newMin - 500f);
                newMax = Math.Min(10500f, newMax + 500f);
            }
            bool ringChanged = newMin != minD || newMax != maxD;
            float S = WorldScale.S;
            if (ringChanged || S != 1f)
            {
                // Abstände gelten in der echten (vergrößerten) Welt.
                float max = newMax >= 10500f ? 0f : newMax * S;
                if (o.MinDistance != 0f) loc.m_minDistance = newMin * S;
                if (o.MinFromCenter != 0f) loc.m_minDistanceFromCenter = newMin * S;
                if (o.MaxDistance != 0f) loc.m_maxDistance = max;
                if (o.MaxFromCenter != 0f) loc.m_maxDistanceFromCenter = max;
            }

            // 2) Anzahl: so viele wie in normalem Valheim pro Fläche.
            int quantity = o.Quantity;
            bool scalable = !loc.m_unique && !loc.m_prioritized && o.Quantity > 1 &&
                            string.IsNullOrEmpty(AltBiomeParent(loc));
            if (scalable)
            {
                float factor = RingAreas.OurArea(mask, newMin, newMax) / vanilla;
                factor = Math.Max(0.2f, Math.Min(Plugin.MaxStructureFactor.Value, factor));
                // Größere Welt = mehr Fläche: Faktor zum Quadrat.
                factor *= S * S;
                quantity = Math.Max(1, (int)Math.Round(o.Quantity * factor));
                loc.m_quantity = quantity;
            }

            if (!ringChanged && quantity == o.Quantity) return null;
            string text = quantity != o.Quantity ? $"Anzahl {o.Quantity} -> {quantity}" : $"Anzahl {quantity}";
            if (ringChanged) text += $", Abstand {minD:0}-{maxD:0} -> {newMin:0}-{newMax:0}";
            return text;
        }

        static float Min0(float a, float b)
        {
            if (a <= 0f) return b;
            if (b <= 0f) return a;
            return Math.Min(a, b);
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), "GenerateLocationsIfNeeded")]
    static class Patch_GenerateLocationsIfNeeded
    {
        static void Prefix(ZoneSystem __instance) => LocationScaler.Apply(__instance);
    }

    [HarmonyPatch(typeof(ZoneSystem), "GenerateLocations")]
    static class Patch_GenerateLocations
    {
        static void Prefix(ZoneSystem __instance) => LocationScaler.Apply(__instance);
    }
}
