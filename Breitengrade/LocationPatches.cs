using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace Breitengrade
{
    /// <summary>
    /// Valheim platziert von jeder Struktur eine feste Anzahl pro Welt, egal wie groß das Biom ist.
    /// Hier passen wir die Anzahl an die Fläche an, die das Biom in unserer Welt hat,
    /// damit die Dichte ungefähr wie in normalem Valheim bleibt.
    /// </summary>
    internal static class LocationScaler
    {
        /// <summary>
        /// Anteil der Weltscheibe (Radius 10000 m) je Biom in einer normalen Valheim-Welt.
        /// Gemessen aus dem Biom-Cache einer Vanilla-Welt.
        /// </summary>
        static readonly Dictionary<int, float> VanillaShare = new Dictionary<int, float>
        {
            [Latitude.Meadows] = 0.032f,
            [Latitude.Swamp] = 0.033f,
            [Latitude.Mountain] = 0.032f,
            [Latitude.BlackForest] = 0.139f,
            [Latitude.Plains] = 0.140f,
            [Latitude.AshLands] = 0.095f,
            [Latitude.DeepNorth] = 0.068f,
            [Latitude.Mistlands] = 0.199f,
        };

        static readonly AccessTools.FieldRef<ZoneSystem, List<ZoneSystem.ZoneLocation>> Locations =
            AccessTools.FieldRefAccess<ZoneSystem, List<ZoneSystem.ZoneLocation>>("m_locations");
        static readonly AccessTools.FieldRef<ZoneSystem.ZoneLocation, string> AltBiomeParent =
            AccessTools.FieldRefAccess<ZoneSystem.ZoneLocation, string>("m_altBiomeParent");

        static readonly Dictionary<ZoneSystem.ZoneLocation, int> s_original = new Dictionary<ZoneSystem.ZoneLocation, int>();
        static bool s_loggedForCurrentWorld;

        public static void OnWorldChanged() => s_loggedForCurrentWorld = false;

        public static void Apply(ZoneSystem zs)
        {
            var list = Locations(zs);
            if (list == null) return;

            Dictionary<int, float> ours = State.Active ? MeasureOurShares() : null;
            bool log = State.Active && !s_loggedForCurrentWorld;
            int changed = 0;

            foreach (var loc in list)
            {
                if (loc == null) continue;
                if (!s_original.TryGetValue(loc, out int original))
                    s_original[loc] = original = loc.m_quantity;

                int target = State.Active ? Scaled(loc, original, ours) : original;
                if (loc.m_quantity != target) loc.m_quantity = target;

                if (log && target != original)
                {
                    changed++;
                    Plugin.Log.LogInfo($"[Strukturen] {loc.m_prefabName ?? loc.m_name} ({loc.m_biome}): {original} -> {target}");
                }
            }

            if (log)
            {
                s_loggedForCurrentWorld = true;
                Plugin.Log.LogInfo($"[Strukturen] Anzahl bei {changed} Strukturtypen an die Biom-Flächen angepasst.");
            }
        }

        static int Scaled(ZoneSystem.ZoneLocation loc, int original, Dictionary<int, float> ours)
        {
            if (!loc.m_enable || loc.m_unique || loc.m_prioritized || original <= 1) return original;
            // Orte von Biom-Varianten hängen an der Zahl der Varianten, nicht an der Fläche.
            if (!string.IsNullOrEmpty(AltBiomeParent(loc))) return original;

            int mask = (int)loc.m_biome;
            float vanilla = 0f, mine = 0f;
            foreach (var kv in VanillaShare)
            {
                if ((mask & kv.Key) == 0) continue;
                vanilla += kv.Value;
                mine += ours.TryGetValue(kv.Key, out var s) ? s : 0f;
            }
            if (vanilla <= 0f) return original; // z. B. reine Ozean-Strukturen

            float factor = mine / vanilla;
            factor = Math.Max(0.3f, Math.Min(Plugin.MaxStructureFactor.Value, factor));
            return Math.Max(1, (int)Math.Round(original * factor));
        }

        /// <summary>Flächenanteile der Biome in unserer Welt (gleiche Rechnung wie das Vorschau-Werkzeug).</summary>
        public static Dictionary<int, float> MeasureOurShares()
        {
            var counts = new Dictionary<int, int>();
            int total = 0;
            for (int iz = -50; iz <= 50; iz++)
            for (int ix = -50; ix <= 50; ix++)
            {
                float x = ix * 200f, z = iz * 200f;
                if (x * x + z * z > 10000f * 10000f) continue;
                int b = Latitude.GetBiome(x, z);
                counts[b] = counts.TryGetValue(b, out var n) ? n + 1 : 1;
                total++;
            }
            return counts.ToDictionary(kv => kv.Key, kv => (float)kv.Value / total);
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
