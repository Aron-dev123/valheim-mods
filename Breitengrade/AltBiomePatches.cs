using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace Breitengrade
{
    /// <summary>
    /// Biom-Varianten (z. B. "Dark Meadows", "Bog Swamp") werden auf zusammenhängende Gebiete
    /// ("Sektoren") verteilt, die eine bestimmte Größe haben müssen. In normalem Valheim ist jede
    /// Insel ein Sektor. Bei uns wäre jedes Band ein einziger riesiger Sektor, der zu groß ist.
    /// Deshalb zerschneiden wir die Bänder in kleinere, natürlich geformte Teilgebiete.
    /// </summary>
    internal static class Sectors
    {
        public static readonly Dictionary<BiomeSector, long> RegionOf = new Dictionary<BiomeSector, long>();

        /// <summary>Teilgebiet eines Punkts: nächster Punkt in einem leicht verschobenen Raster (Voronoi).</summary>
        public static long Region(short gx, short gy)
        {
            float wx = AltBiomeWorldData.MapSpaceToWorldSpace((float)gx);
            float wz = AltBiomeWorldData.MapSpaceToWorldSpace((float)gy);
            float size = Math.Max(300f, Plugin.SectorSize.Value);

            int cx = (int)Math.Floor(wx / size), cz = (int)Math.Floor(wz / size);
            long best = 0;
            float bestD = float.MaxValue;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int ix = cx + dx, iz = cz + dz;
                float px = (ix + Jitter(ix, iz, 1)) * size;
                float pz = (iz + Jitter(ix, iz, 2)) * size;
                float d = (px - wx) * (px - wx) + (pz - wz) * (pz - wz);
                if (d < bestD) { bestD = d; best = ((long)ix << 32) ^ (uint)iz; }
            }
            return best;
        }

        static float Jitter(int x, int z, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)(salt * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return 0.1f + 0.8f * (h & 0xFFFF) / 65535f;
            }
        }
    }

    [HarmonyPatch(typeof(AltBiomeWorldData), nameof(AltBiomeWorldData.GenerateSectors))]
    static class Patch_GenerateSectors
    {
        static void Prefix() => Sectors.RegionOf.Clear();

        static void Postfix(AltBiomeWorldData __instance)
        {
            Sectors.RegionOf.Clear();
            try { Diagnostics.LogSectors(__instance); }
            catch (Exception e) { Plugin.Log.LogWarning("[Diagnose] Sektoren: " + e.Message); }
        }
    }

    /// <summary>Beim Füllen eines Sektors nicht über die Grenze des Teilgebiets hinaus füllen.</summary>
    [HarmonyPatch(typeof(AltBiomeWorldData), "tryFill")]
    static class Patch_TryFill
    {
        static bool Prefix(BiomeSector pSector, short x, short y)
        {
            if (!State.Active || pSector == null) return true;
            long region = Sectors.Region(x, y);
            if (!Sectors.RegionOf.TryGetValue(pSector, out long own))
            {
                Sectors.RegionOf[pSector] = region;
                return true;
            }
            // Punkt gehört zu einem anderen Teilgebiet: offen lassen, er bekommt später einen eigenen Sektor.
            return own == region;
        }
    }

    [HarmonyPatch(typeof(AltBiomeWorldData), nameof(AltBiomeWorldData.GenerateAltBiomes))]
    static class Patch_GenerateAltBiomes
    {
        static void Postfix()
        {
            try { Diagnostics.LogAltBiomes(); }
            catch (Exception e) { Plugin.Log.LogWarning("[Diagnose] Alt-Biome: " + e.Message); }
        }
    }

    internal static class Diagnostics
    {
        public static void LogSectors(AltBiomeWorldData data)
        {
            if (data?.Sectors == null) return;
            foreach (var g in data.Sectors.GroupBy(s => s.Biome).OrderBy(g => g.Key.ToString()))
            {
                var edges = g.Select(s => s.EdgeCount).OrderBy(e => e).ToList();
                Plugin.Log.LogInfo($"[Diagnose] Sektoren {g.Key}: {edges.Count} Stück, " +
                                   $"Randgröße min {edges.First()}, Median {edges[edges.Count / 2]}, max {edges.Last()}");
            }
        }

        public static void LogAltBiomes()
        {
            var list = Traverse.Create(typeof(AltBiomeList)).Field("m_altBiomes").GetValue<List<AltBiome>>();
            if (list == null) return;
            foreach (var a in list)
            {
                Plugin.Log.LogInfo($"[Diagnose] Variante '{a.m_name}' ({a.m_biome}): platziert {a.Sectors?.Count ?? 0} " +
                                   $"(soll {a.m_minAmountSpawned}-{a.m_maxAmountSpawned}), Rand {a.m_minEdgeSize}-{a.m_maxEdgeSize}, " +
                                   $"Höhe {a.m_minAvgHeight}-{a.m_maxAvgHeight}, MinAbstand {a.m_minDistanceFromCenter}, " +
                                   $"Nachbar nötig {a.m_requireNeighbor}, Nachbar verboten {a.m_notNeighbor}, " +
                                   $"gültige Sektoren {a.ValidPlacementSectors}");
            }
        }
    }
}
