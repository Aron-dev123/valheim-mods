using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Breitengrade
{
    /// <summary>
    /// Planetos: Points of Interest an den benannten Orten, passend zum Biom.
    ///   Meadows → Dörfer, Plains → Fuling-Lager, Black Forest → zerstörte Steintürme,
    ///   Mistlands → Dvergr-Festungen, Deep North → Dörfer des Nordens, Swamp → Sumpfhütten.
    /// Große Orte bekommen mehrere nebeneinander (Hardhome 3 Dörfer, Winterfell viele Türme,
    /// Vaes Dothrak viele Fuling-Lager, Yeen viele Dvergr-Festungen).
    ///
    /// Die Orte werden als normale Valheim-Locations eingetragen und entstehen, sobald jemand
    /// in die Nähe kommt. Bereits vorhandene passende Locations in der Nähe zählen mit, deshalb
    /// kann das bei jedem Laden der Welt laufen, ohne doppelt zu bauen.
    /// </summary>
    internal static class PointsOfInterest
    {
        static readonly MethodInfo Register = AccessTools.Method(typeof(ZoneSystem), "RegisterLocation");
        static readonly MethodInfo ZoneGenerated = AccessTools.Method(typeof(ZoneSystem), "IsZoneGenerated");

        /// <summary>
        /// Arten je Biom. Jede Gruppe ist eine Art (Anfänge von Prefab-Namen, Varianten werden
        /// abwechselnd genommen). Die Gruppen kommen reihum dran; eine Gruppe, die öfter in der Liste
        /// steht, kommt öfter vor.
        /// </summary>
        static readonly Dictionary<int, string[][]> Kinds = new Dictionary<int, string[][]>
        {
            [Latitude.Meadows] = new[] { new[] { "WoodVillage" } },
            [Latitude.Plains] = new[] { new[] { "GoblinCamp" } },
            [Latitude.BlackForest] = new[] { new[] { "StoneTowerRuins" } },
            [Latitude.Mistlands] = new[] { new[] { "Mistlands_DvergrTownEntrance" } },
            [Latitude.DeepNorth] = new[] { new[] { "NorthVillage", "DeepNorth_Village", "DeepNorthVillage" } },
            // Sumpf: viele kaputte Holzhäuser, dazwischen ein paar Krypten
            [Latitude.Swamp] = new[]
            {
                new[] { "SwampHut" }, new[] { "SwampHut" }, new[] { "SunkenCrypt" },
            },
            // Berge: Steintürme, Steinkreise mit Treppe nach unten, verlassene Hütten
            [Latitude.Mountain] = new[]
            {
                new[] { "StoneTower1", "StoneTower3" }, new[] { "MountainWell" }, new[] { "AbandonedLogCabin" },
            },
            // Ashlands: Festungen, verfallene Gebäude mit hohen Mauern, Dvergr-Türme
            [Latitude.AshLands] = new[]
            {
                new[] { "FortressRuins" }, new[] { "CharredRuins" }, new[] { "CharredTowerRuins1_dvergr" },
                new[] { "CharredRuins" }, new[] { "CharredTowerRuins" },
            },
        };

        /// <summary>Markierung je Ort (Mitte der Gruppe) für die Karte.</summary>
        public static readonly Dictionary<string, Vector3> PinPositions = new Dictionary<string, Vector3>();

        /// <summary>Eigene Orte dieser Welt: Ortsname → Positionen. Wird je Welt in einer Datei gemerkt.</summary>
        static string SaveFile()
        {
            long uid = ZNet.instance != null ? ZNet.instance.GetWorldUID() : 0L;
            string dir = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "Breitengrade");
            System.IO.Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, $"orte_{uid}.txt");
        }

        static Dictionary<string, List<Vector3>> LoadSaved()
        {
            var d = new Dictionary<string, List<Vector3>>();
            string f = SaveFile();
            if (!System.IO.File.Exists(f)) return d;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var line in System.IO.File.ReadAllLines(f))
            {
                var parts = line.Split(';');
                if (parts.Length < 4) continue;
                if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float, inv, out float x) ||
                    !float.TryParse(parts[2], System.Globalization.NumberStyles.Float, inv, out float y) ||
                    !float.TryParse(parts[3], System.Globalization.NumberStyles.Float, inv, out float z)) continue;
                if (!d.TryGetValue(parts[0], out var list)) d[parts[0]] = list = new List<Vector3>();
                list.Add(new Vector3(x, y, z));
            }
            return d;
        }

        static void AppendSaved(string name, Vector3 p, string prefab)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            System.IO.File.AppendAllText(SaveFile(),
                string.Format(inv, "{0};{1:0.0};{2:0.0};{3:0.0};{4}", name, p.x, p.y, p.z, prefab) + Environment.NewLine);
        }

        public static void Place(List<Vector3> portals)
        {
            var zs = ZoneSystem.instance;
            if (zs == null || Register == null) return;
            float S = WorldScale.S;
            int added = 0;
            PinPositions.Clear();
            var saved = LoadSaved();

            foreach (var place in Planetos.Places)
            {
                Planetos.FromPixel(place.u, place.v, out float cx, out float cz);
                var center = new Vector3(cx, 0f, cz);
                if (!saved.TryGetValue(place.name, out var mine)) mine = new List<Vector3>();
                SetPin(place.name, center, mine);

                int want = Planetos.PoiCount.TryGetValue(place.name, out int c) ? c : Planetos.DefaultPoiCount;
                if (want <= 0 || !Kinds.TryGetValue(place.biome, out var kinds)) continue;

                var groups = kinds.Select(g => FindLocations(zs, g)).Where(g => g.Count > 0).ToList();
                if (groups.Count == 0) continue;

                float searchR = (want > 6 ? 1300f : 900f) * S;
                int have = mine.Count;
                int missing = want - have;
                if (missing <= 0) continue;

                int made = 0;
                for (int pass = 0; pass < 2 && made < missing; pass++)
                {
                    float step = 60f;
                    for (float r = 0f; r <= searchR && made < missing; r += step)
                    {
                        int n = r == 0f ? 1 : Math.Max(6, (int)(2f * Mathf.PI * r / step));
                        float off = (place.u * 0.37f + r) % 1f;
                        for (int a = 0; a < n && made < missing; a++)
                        {
                            int k = have + made;
                            var grp = groups[k % groups.Count];
                            var loc = grp[(k / groups.Count) % grp.Count];
                            float ang = (a + off) * 2f * Mathf.PI / n;
                            float x = cx + Mathf.Cos(ang) * r, z = cz + Mathf.Sin(ang) * r;
                            if (!SpotOk(zs, loc, place.biome, x, z, pass, portals, out Vector3 pos)) continue;
                            Register.Invoke(zs, new object[] { loc, pos, false });
                            if (!zs.m_locationInstances.TryGetValue(ZoneSystem.GetZone(pos), out var li) || li.m_location != loc) continue;
                            AppendSaved(place.name, pos, loc.m_prefabName);
                            mine.Add(pos);
                            made++;
                        }
                    }
                }
                added += made;
                SetPin(place.name, center, mine);
                if (made < missing)
                    Plugin.Log.LogWarning($"[Orte] {place.name}: nur {have + made} von {want} Orten gefunden.");
                else
                    Plugin.Log.LogInfo($"[Orte] {place.name}: {made} neue Orte ({string.Join(", ", groups.SelectMany(g => g).Select(l => l.m_prefabName).Distinct())}).");
            }
            if (added > 0) Plugin.Log.LogInfo($"[Orte] {added} Points of Interest eingetragen.");
        }

        /// <summary>Kartenmarkierung: Mitte der eigenen Orte, sonst die Stelle auf der Karte.</summary>
        static void SetPin(string name, Vector3 center, List<Vector3> mine)
        {
            Vector3 p = center;
            if (mine.Count > 0)
            {
                p = Vector3.zero;
                foreach (var m in mine) p += m;
                p /= mine.Count;
            }
            p.y = 0f;
            PinPositions[name] = p;
        }

        static readonly HashSet<string> s_reported = new HashSet<string>();

        /// <summary>Name passt zum Anfang; Sonder-Varianten (…_sunk, …_1) nur, wenn ausdrücklich genannt.</summary>
        static bool Matches(string name, string prefix) =>
            name == prefix || (name.StartsWith(prefix, StringComparison.Ordinal) && name.IndexOf('_', prefix.Length) < 0);

        static List<ZoneSystem.ZoneLocation> FindLocations(ZoneSystem zs, string[] prefixes)
        {
            foreach (var p in prefixes)
            {
                var found = zs.m_locations
                    .Where(l => l != null && l.m_prefabName != null && Matches(l.m_prefabName, p))
                    .OrderBy(l => l.m_enable ? 0 : 1).ThenBy(l => l.m_prefabName)
                    .ToList();
                var enabled = found.Where(l => l.m_enable).ToList();
                if (enabled.Count > 0) return enabled;
                if (found.Count > 0) return found;
            }
            string key = string.Join("/", prefixes);
            if (s_reported.Add(key))
            {
                var all = zs.m_locations.Where(l => l != null).Select(l => l.m_prefabName).Distinct().OrderBy(n => n);
                Plugin.Log.LogWarning($"[Orte] Keine Location für {key}. Vorhanden: {string.Join(", ", all)}");
            }
            return new List<ZoneSystem.ZoneLocation>();
        }

        static bool SpotOk(ZoneSystem zs, ZoneSystem.ZoneLocation loc, int biome, float x, float z, int pass,
                           List<Vector3> portals, out Vector3 pos)
        {
            pos = Vector3.zero;
            float ext = Math.Max(loc.m_exteriorRadius, 10f);
            float edge = 9400f * WorldScale.S;
            if (x * x + z * z > edge * edge) return false;
            if (Latitude.GetBiome(x, z) != biome || Latitude.CoastDistance(x, z) > -(ext + 25f)) return false;
            if (Planetos.IsOnWall(x, z, ext + 15f)) return false;

            var p = new Vector3(x, 0f, z);
            if (Flat(p - MageTower.Center).magnitude < MageTower.BaseRadius + ext + 30f) return false;
            if (Flat(p - Patch_SpawnPlaza.Center).magnitude < 200f + ext) return false;
            foreach (var portal in portals)
                if (Flat(p - portal).magnitude < ext + 20f) return false;

            var zone = ZoneSystem.GetZone(p);
            if (zs.m_locationInstances.ContainsKey(zone)) return false;
            if (ZoneGenerated != null && (bool)ZoneGenerated.Invoke(zs, new object[] { zone })) return false;
            // Nachbarzonen (64 m): Abstand zu vorhandenen Locations
            for (int dy = -3; dy <= 3; dy++)
            for (int dx = -3; dx <= 3; dx++)
            {
                if (!zs.m_locationInstances.TryGetValue(new Vector2s(zone.x + dx, zone.y + dy), out var li)) continue;
                float other = li.m_location != null ? li.m_location.m_exteriorRadius : 20f;
                if (Flat(li.m_position - p).magnitude < ext + other + 15f) return false;
            }

            // Boden: trocken, im richtigen Biom und nicht zu steil
            var wg = WorldGenerator.instance;
            if ((int)wg.GetBiome(p) != biome) return false;
            bool swamp = biome == Latitude.Swamp;
            float dry = swamp ? 30.3f : 31.5f;
            float h = wg.GetHeight(x, z);
            if (h < dry + 0.5f) return false;
            float lo = h, hi = h;
            float rr = ext * 0.75f;
            for (int a = 0; a < 8; a++)
            {
                float ang = a * 45f * Mathf.Deg2Rad;
                float hh = wg.GetHeight(x + Mathf.Cos(ang) * rr, z + Mathf.Sin(ang) * rr);
                if (hh < dry && !swamp) return false;
                lo = Math.Min(lo, hh); hi = Math.Max(hi, hh);
            }
            float maxDelta = (pass == 0 ? 6f : 12f) + (swamp ? 2f : 0f);
            if (hi - lo > maxDelta) return false;

            pos = new Vector3(x, h, z);
            return true;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }

    /// <summary>Planetos: Auf der Mauer erscheinen keine Kreaturen.</summary>
    [HarmonyPatch(typeof(SpawnSystem), "IsSpawnPointGood")]
    static class Patch_NoSpawnsOnWall
    {
        static bool Prefix(ref Vector3 spawnPoint, ref bool __result)
        {
            if (!State.Active || !Planetos.Enabled || !Planetos.IsOnWall(spawnPoint.x, spawnPoint.z, 8f)) return true;
            __result = false;
            return false;
        }
    }
}
