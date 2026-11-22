using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Breitengrade
{
    /// <summary>
    /// Planetos: Städte an den benannten Orten, gebaut aus Valheim-Locations, die zum Biom passen
    ///   Meadows → Dörfer, Plains → Fuling-Lager, Black Forest → Turmruinen, Mistlands → Dvergr-Türme,
    ///   Deep North → Dörfer des Nordens, Swamp → Hütten und Krypten, Mountain → Ruinen, Steinkreise,
    ///   Hütten, Ashlands → Festungen und Ruinen.
    ///
    /// Valheim erlaubt nur eine Location je 64-m-Zone. Für dichte Städte setzen wir die Gebäude
    /// deshalb selbst: Wenn eine Zone zum ersten Mal entsteht, werden alle unsere Gebäude in ihr
    /// gebaut (beliebig viele, Turm an Turm). Jede Stadt bekommt eine eingeebnete Fläche und
    /// darauf wachsen keine Bäume oder Steine.
    ///
    /// Alle Städte haben ungefähr die Fläche der Meadows-Städte: kleine Gebäude (Türme) gibt es
    /// entsprechend öfter. Die Gebäude einer Welt stehen in BepInEx/config/Breitengrade/orte_&lt;id&gt;.txt.
    /// </summary>
    public static class PointsOfInterest
    {
        static readonly MethodInfo ZoneGenerated = AccessTools.Method(typeof(ZoneSystem), "IsZoneGenerated");
        static readonly MethodInfo SpawnLocationM = AccessTools.Method(typeof(ZoneSystem), "SpawnLocation");
        static readonly MethodInfo GroundData = AccessTools.Method(typeof(ZoneSystem), "GetGroundData");
        static readonly MethodInfo PokeM = AccessTools.Method(typeof(ZoneSystem), "PokeCanSpawnLocation");

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
            // Berge: Ruinen mit Skeletten, Steinkreise mit Treppe nach unten, verlassene Hütten
            [Latitude.Mountain] = new[]
            {
                new[] { "StoneTowerRuins04", "StoneTowerRuins05" }, new[] { "MountainWell" },
                new[] { "StoneTowerRuins05", "StoneTowerRuins04" }, new[] { "AbandonedLogCabin" },
            },
            // Ashlands: Festungen, verfallene Gebäude mit hohen Mauern, Dvergr-Türme
            [Latitude.AshLands] = new[]
            {
                new[] { "FortressRuins" }, new[] { "CharredRuins" }, new[] { "CharredTowerRuins1_dvergr" },
                new[] { "CharredRuins" }, new[] { "CharredTowerRuins" },
            },
        };

        /// <summary>Höchstens so viele Gebäude je Stadt.</summary>
        const int MaxPerCity = 30;

        /// <summary>Markierung je Ort (Mitte der Stadt) für die Karte.</summary>
        public static readonly Dictionary<string, Vector3> PinPositions = new Dictionary<string, Vector3>();

        /// <summary>Unsere Gebäude je Zone (werden gebaut, wenn die Zone entsteht).</summary>
        static readonly Dictionary<Vector2s, List<(ZoneSystem.ZoneLocation loc, Vector3 pos)>> s_extras =
            new Dictionary<Vector2s, List<(ZoneSystem.ZoneLocation, Vector3)>>();

        /// <summary>Eingeebnete Stadtflächen (Mitte, Radius, Höhe). Wird als Ganzes ersetzt (Gelände-Threads).</summary>
        /// <summary>Eingeebnete Fläche; Fade = Breite des Übergangs (0 = Standard 70 m).</summary>
        public struct Plateau { public float X, Z, R, H, Fade; }
        public static volatile Plateau[] Plateaus = new Plateau[0];
        public const float PlateauFade = 70f;

        public static void OnWorldChanged()
        {
            lock (s_extras) s_extras.Clear();
            Plateaus = new Plateau[0];
            PinPositions.Clear();
        }

        // ---------------------------------------------------------------- Speichern

        struct Saved { public string Place, Prefab; public Vector3 Pos; }

        static string SaveFile()
        {
            long uid = ZNet.instance != null ? ZNet.instance.GetWorldUID() : 0L;
            string dir = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "Breitengrade");
            System.IO.Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, $"orte_{uid}.txt");
        }

        static List<Saved> LoadSaved()
        {
            var list = new List<Saved>();
            string f = SaveFile();
            if (!System.IO.File.Exists(f)) return list;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var ns = System.Globalization.NumberStyles.Float;
            foreach (var line in System.IO.File.ReadAllLines(f))
            {
                var p = line.Split(';');
                if (p.Length < 5) continue;
                if (!float.TryParse(p[1], ns, inv, out float x) || !float.TryParse(p[2], ns, inv, out float y) ||
                    !float.TryParse(p[3], ns, inv, out float z)) continue;
                list.Add(new Saved { Place = p[0], Pos = new Vector3(x, y, z), Prefab = p[4] });
            }
            return list;
        }

        static void AppendSaved(string name, Vector3 p, string prefab)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            System.IO.File.AppendAllText(SaveFile(),
                string.Format(inv, "{0};{1:0.0};{2:0.0};{3:0.0};{4}", name, p.x, p.y, p.z, prefab) + Environment.NewLine);
        }

        // ---------------------------------------------------------------- Städte bauen

        class City
        {
            public string Name;
            public readonly List<(Vector3 pos, ZoneSystem.ZoneLocation loc)> Members = new List<(Vector3, ZoneSystem.ZoneLocation)>();
            public bool OldStyle; // in älteren Versionen als normale Valheim-Location eingetragen
        }

        public static void Place(List<Vector3> portals)
        {
            var zs = ZoneSystem.instance;
            if (zs == null || SpawnLocationM == null) return;
            float S = WorldScale.S;
            int added = 0;
            PinPositions.Clear();
            lock (s_extras) s_extras.Clear();

            var byName = new Dictionary<string, ZoneSystem.ZoneLocation>();
            foreach (var l in zs.m_locations)
                if (l != null && l.m_prefabName != null && !byName.ContainsKey(l.m_prefabName)) byName[l.m_prefabName] = l;

            // Gespeicherte Gebäude
            var saved = LoadSaved();
            var cities = new Dictionary<string, City>();
            foreach (var sv in saved)
            {
                if (!byName.TryGetValue(sv.Prefab, out var loc)) continue;
                if (!cities.TryGetValue(sv.Place, out var city)) cities[sv.Place] = city = new City { Name = sv.Place };
                city.Members.Add((sv.Pos, loc));
                // ältere Version: als Valheim-Location eingetragen, baut Valheim selbst
                if (zs.m_locationInstances.TryGetValue(ZoneSystem.GetZone(sv.Pos), out var li) &&
                    li.m_location == loc && Flat(li.m_position - sv.Pos).magnitude < 0.5f)
                    city.OldStyle = true;
                else
                    AddExtra(loc, sv.Pos);
            }

            // Größe der Meadows-Städte (Maßstab für alle)
            var village = FindLocations(zs, new[] { "WoodVillage" }).FirstOrDefault();
            float meadowsSpacing = village != null ? LooseSpacing(village, village) : 50f;
            var plateaus = new List<Plateau>();

            foreach (var place in Planetos.Places)
            {
                Planetos.FromPixel(place.u, place.v, out float cx, out float cz);
                if (!cities.TryGetValue(place.name, out var city)) cities[place.name] = city = new City { Name = place.name };

                int baseWant = Planetos.PoiCount.TryGetValue(place.name, out int c) ? c : Planetos.DefaultPoiCount;
                var groups = Kinds.TryGetValue(place.biome, out var kinds)
                    ? kinds.Select(g => FindLocations(zs, g)).Where(g => g.Count > 0).ToList()
                    : new List<List<ZoneSystem.ZoneLocation>>();
                if (false && baseWant > 0 && groups.Count > 0 && !city.OldStyle)
                {
                    bool meadows = place.biome == Latitude.Meadows;
                    // gleiche Fläche wie eine Meadows-Stadt mit baseWant Dörfern
                    float kindSpacing = (float)groups.SelectMany(g => g).Average(l => Spacing(l, l, meadows));
                    int want = meadows ? baseWant
                        : Math.Min(MaxPerCity, (int)Math.Round(baseWant * (meadowsSpacing / kindSpacing) * (meadowsSpacing / kindSpacing)));
                    int made = Grow(zs, city, place, groups, want, meadows, cx, cz, 900f * S, meadowsSpacing * 0.55f * (float)Math.Sqrt(baseWant) + 80f, portals);
                    added += made;
                    if (city.Members.Count < want)
                        Plugin.Log.LogWarning($"[Orte] {place.name}: nur {city.Members.Count} von {want} Gebäuden gefunden.");
                    else if (made > 0)
                        Plugin.Log.LogInfo($"[Orte] {place.name}: {made} Gebäude, Abstand {kindSpacing:0} m ({string.Join(", ", groups.SelectMany(g => g).Select(l => $"{l.m_prefabName} {l.m_interiorRadius:0}/{l.m_exteriorRadius:0}").Distinct())}).");
                }

                // Karte, freie Fläche, Einebnung
                if (city.Members.Count == 0) continue; // wird vom Stadtbauer gebaut
                Vector3 mid = Vector3.zero;
                foreach (var m in city.Members) mid += Flat(m.pos);
                mid /= city.Members.Count;
                float r = 0f;
                foreach (var m in city.Members)
                    r = Math.Max(r, Flat(m.pos - mid).magnitude + Size(m.loc));
                PinPositions[place.name] = mid;
                foreach (var m in city.Members)
                    TestPortals.ClearAreas.Add((m.pos, Math.Max(m.loc.m_exteriorRadius, 15f) + 12f));
                TestPortals.ClearAreas.Add((mid, r + 10f));
                if (!city.OldStyle)
                {
                    var hs = city.Members.Select(m => m.pos.y).OrderBy(h => h).ToList();
                    plateaus.Add(new Plateau { X = mid.x, Z = mid.z, R = r + 8f, H = Math.Max(hs[hs.Count / 2], 33f) });
                }
                Plateaus = plateaus.ToArray();
            }
            // Neue Städte aus Bauteilen (Orte ohne Gebäude aus älteren Versionen)
            var old = new HashSet<string>(cities.Where(kv => kv.Value.Members.Count > 0).Select(kv => kv.Key));
            plateaus.AddRange(CityBuilder.PlanAll(old, portals));
            Plateaus = plateaus.ToArray();
            if (added > 0) Plugin.Log.LogInfo($"[Orte] {added} Gebäude eingetragen.");
        }

        /// <summary>
        /// Lässt eine Stadt wachsen: erster Bauplatz nahe der Kartenstelle, danach immer direkt neben
        /// den schon gesetzten Gebäuden, bevorzugt nahe der Stadtmitte (kompakt, rund).
        /// </summary>
        static int Grow(ZoneSystem zs, City city, (string name, float u, float v, int biome) place,
                        List<List<ZoneSystem.ZoneLocation>> groups, int want, bool loose,
                        float cx, float cz, float searchR, float cityR, List<Vector3> portals)
        {
            ZoneSystem.ZoneLocation Next(int k)
            {
                var grp = groups[k % groups.Count];
                return grp[(k / groups.Count) % grp.Count];
            }
            int made = 0;
            if (city.Members.Count == 0)
            {
                var loc = Next(0);
                if (!FindAnchor(zs, loc, place.biome, cx, cz, searchR, portals, out Vector3 anchor)) return 0;
                ClearMinorLocations(zs, anchor, cityR);
                Add(city, loc, anchor);
                made++;
            }
            while (city.Members.Count < want)
            {
                var loc = Next(city.Members.Count);
                if (!FindAdjacent(zs, loc, place.biome, city.Members, loose, portals, out Vector3 pos)) break;
                Add(city, loc, pos);
                made++;
            }
            return made;
        }

        static void Add(City city, ZoneSystem.ZoneLocation loc, Vector3 pos)
        {
            city.Members.Add((pos, loc));
            AddExtra(loc, pos);
            AppendSaved(city.Name, pos, loc.m_prefabName);
        }

        static void AddExtra(ZoneSystem.ZoneLocation loc, Vector3 pos)
        {
            var zone = ZoneSystem.GetZone(pos);
            lock (s_extras)
            {
                if (!s_extras.TryGetValue(zone, out var list)) s_extras[zone] = list = new List<(ZoneSystem.ZoneLocation, Vector3)>();
                list.Add((loc, pos));
            }
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

        /// <summary>Erster Bauplatz: nächstgelegene gute Stelle zur Kartenposition.</summary>
        static bool FindAnchor(ZoneSystem zs, ZoneSystem.ZoneLocation loc, int biome, float cx, float cz, float searchR,
                               List<Vector3> portals, out Vector3 pos)
        {
            for (int pass = 0; pass < 3; pass++)
            for (float r = 0f; r <= searchR; r += 40f)
            {
                int n = r == 0f ? 1 : Math.Max(6, (int)(2f * Mathf.PI * r / 40f));
                for (int a = 0; a < n; a++)
                {
                    float ang = a * 2f * Mathf.PI / n;
                    if (SpotOk(zs, loc, biome, cx + Mathf.Cos(ang) * r, cz + Mathf.Sin(ang) * r, pass, null, true, portals, out pos)) return true;
                }
            }
            pos = Vector3.zero;
            return false;
        }

        /// <summary>Nächster Bauplatz direkt neben einem schon gesetzten Gebäude, möglichst nahe der Stadtmitte.</summary>
        static bool FindAdjacent(ZoneSystem zs, ZoneSystem.ZoneLocation loc, int biome, List<(Vector3 pos, ZoneSystem.ZoneLocation loc)> members,
                                 bool loose, List<Vector3> portals, out Vector3 pos)
        {
            Vector3 mid = Vector3.zero;
            foreach (var m in members) mid += m.pos;
            mid /= members.Count;

            var cands = new List<Vector3>();
            foreach (var m in members)
            {
                float baseD = Spacing(m.loc, loc, loose);
                foreach (float d in new[] { baseD, baseD + 5f, baseD + 12f })
                for (int a = 0; a < 18; a++)
                {
                    float ang = (a + (d > baseD ? 0.5f : 0f)) * 2f * Mathf.PI / 18f;
                    cands.Add(new Vector3(m.pos.x + Mathf.Cos(ang) * d, 0f, m.pos.z + Mathf.Sin(ang) * d));
                }
            }
            cands.Sort((p, q) => Flat(p - mid).sqrMagnitude.CompareTo(Flat(q - mid).sqrMagnitude));
            for (int pass = 0; pass < 3; pass++)
                foreach (var c in cands)
                    if (SpotOk(zs, loc, biome, c.x, c.z, pass, members, loose, portals, out pos)) return true;
            pos = Vector3.zero;
            return false;
        }

        /// <summary>Abstand der Mittelpunkte zweier Gebäude einer Stadt.</summary>
        static float Spacing(ZoneSystem.ZoneLocation a, ZoneSystem.ZoneLocation b, bool loose) =>
            loose ? LooseSpacing(a, b) : TightSpacing(a, b);

        /// <summary>Meadows-Dörfer: Innenbereiche berühren sich fast, Außenbereiche überlappen.</summary>
        static float LooseSpacing(ZoneSystem.ZoneLocation a, ZoneSystem.ZoneLocation b)
        {
            float ia = a != null ? a.m_interiorRadius : 15f, ib = b != null ? b.m_interiorRadius : 15f;
            float ea = a != null ? a.m_exteriorRadius : 25f, eb = b != null ? b.m_exteriorRadius : 25f;
            return Math.Max(Math.Max(ia, 8f) + Math.Max(ib, 8f) + 4f, (ea + eb) * 0.6f);
        }

        /// <summary>
        /// Alle anderen: Dorf-Dichte. Die Außenbereiche der Gebäude berühren sich, dazwischen bleibt eine
        /// Gasse (wie zwischen den Hütten eines Fuling-Dorfs), nichts steht ineinander.
        /// </summary>
        static float TightSpacing(ZoneSystem.ZoneLocation a, ZoneSystem.ZoneLocation b) => Size(a) + Size(b) + StreetWidth;

        const float StreetWidth = 8f;

        /// <summary>Halbe Breite eines Gebäudes: Außenradius (bei manchen ist der Innenradius 0).</summary>
        static float Size(ZoneSystem.ZoneLocation l) =>
            l == null ? 20f : Math.Max(10f, l.m_exteriorRadius > 0f ? l.m_exteriorRadius : l.m_interiorRadius);

        /// <summary>
        /// Macht Platz für eine Stadt: noch nicht gebaute, häufige Valheim-Locations im Stadtgebiet werden
        /// entfernt (keine Bosse, Händler oder einzigartigen Orte, nichts schon Gebautes).
        /// </summary>
        static void ClearMinorLocations(ZoneSystem zs, Vector3 center, float radius)
        {
            var remove = new List<Vector2s>();
            foreach (var kv in zs.m_locationInstances)
            {
                var li = kv.Value;
                var l = li.m_location;
                if (li.m_placed || l == null) continue;
                if (Flat(li.m_position - center).magnitude > radius + l.m_exteriorRadius + PlateauFade) continue;
                if (l.m_prioritized || l.m_unique || l.m_quantity < 15) continue;
                string n = l.m_prefabName ?? "";
                if (n.Contains("Vendor") || n.Contains("Hildir") || n.Contains("BogWitch") || n.Contains("StartTemple")) continue;
                remove.Add(kv.Key);
            }
            foreach (var key in remove)
            {
                var li = zs.m_locationInstances[key];
                zs.m_locationInstances.Remove(key);
                foreach (var list in zs.m_locationIDCache.Values) list.RemoveAll(x => x.m_position == li.m_position && x.m_location == li.m_location);
                foreach (var list in zs.m_locationGroupCache.Values) list.RemoveAll(x => x.m_position == li.m_position && x.m_location == li.m_location);
                foreach (var list in zs.m_locationMaxGroupCache.Values) list.RemoveAll(x => x.m_position == li.m_position && x.m_location == li.m_location);
            }
            if (remove.Count > 0) Plugin.Log.LogInfo($"[Orte] {remove.Count} andere Locations im Stadtgebiet entfernt.");
        }

        static bool SpotOk(ZoneSystem zs, ZoneSystem.ZoneLocation loc, int biome, float x, float z, int pass,
                           List<(Vector3 pos, ZoneSystem.ZoneLocation loc)> members, bool loose,
                           List<Vector3> portals, out Vector3 pos)
        {
            pos = Vector3.zero;
            float ext = Math.Max(loc.m_exteriorRadius, 10f);
            float inner = Size(loc) * 0.7f;
            float edge = 9400f * WorldScale.S;
            if (x * x + z * z > edge * edge) return false;
            if (Latitude.GetBiome(x, z) != biome || Latitude.CoastDistance(x, z) > -(inner + 25f)) return false;
            if (Planetos.IsOnWall(x, z, ext + 15f)) return false;

            var p = new Vector3(x, 0f, z);
            if (Flat(p - MageTower.Center).magnitude < MageTower.BaseRadius + ext + PlateauFade + 30f) return false;
            if (Flat(p - Patch_SpawnPlaza.Center).magnitude < 260f + ext + PlateauFade) return false;
            foreach (var portal in portals)
                if (Flat(p - portal).magnitude < ext + 20f) return false;

            // Zone noch nicht gebaut (auch die Nachbarn: Einebnung und Gebäude reichen hinüber)
            var zone = ZoneSystem.GetZone(p);
            if (ZoneGenerated != null)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if ((bool)ZoneGenerated.Invoke(zs, new object[] { new Vector2s(zone.x + dx, zone.y + dy) })) return false;

            // Abstand zu Valheim-Locations und zu unseren Gebäuden
            for (int dy = -3; dy <= 3; dy++)
            for (int dx = -3; dx <= 3; dx++)
            {
                var zz = new Vector2s(zone.x + dx, zone.y + dy);
                if (zs.m_locationInstances.TryGetValue(zz, out var li) &&
                    Flat(li.m_position - p).magnitude < LooseSpacing(li.m_location, loc) - 0.5f) return false;
                lock (s_extras)
                {
                    if (!s_extras.TryGetValue(zz, out var list)) continue;
                    foreach (var e in list)
                        if (Flat(e.pos - p).magnitude < Spacing(e.loc, loc, loose) - 0.5f) return false;
                }
            }

            // Boden: trocken, im richtigen Biom (wird danach eingeebnet)
            var wg = WorldGenerator.instance;
            if ((int)wg.GetBiome(p) != biome) return false;
            bool swamp = biome == Latitude.Swamp;
            float dry = swamp ? 30.3f : 31.5f;
            float h = wg.GetHeight(x, z);
            if (h < dry + 0.5f) return false;
            float lo = h, hi = h;
            float rr = inner;
            for (int a = 0; a < 8; a++)
            {
                float ang = a * 45f * Mathf.Deg2Rad;
                float hh = wg.GetHeight(x + Mathf.Cos(ang) * rr, z + Mathf.Sin(ang) * rr);
                if (hh < dry && !swamp) return false;
                lo = Math.Min(lo, hh); hi = Math.Max(hi, hh);
            }
            float maxDelta = pass == 0 ? 8f : pass == 1 ? 20f : 45f;
            if (hi - lo > maxDelta) return false;
            // nicht zu weit über/unter dem Rest der Stadt (sonst wird die Einebnung zur Klippe)
            if (members != null && members.Count > 0)
            {
                float avg = members.Average(m => m.pos.y);
                if (Math.Abs(h - avg) > (pass == 0 ? 10f : pass == 1 ? 25f : 50f)) return false;
            }

            pos = new Vector3(x, h, z);
            return true;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // ---------------------------------------------------------------- Gelände

        /// <summary>Ebnet die Stadtflächen ein (weicher Übergang zum Umland).</summary>
        public static void ShapeTerrain(float x, float z, ref float height)
        {
            var arr = Plateaus;
            for (int i = 0; i < arr.Length; i++)
            {
                var pl = arr[i];
                float dx = x - pl.X, dz = z - pl.Z;
                float fade = pl.Fade > 0f ? pl.Fade : PlateauFade;
                float outer = pl.R + fade;
                float d2 = dx * dx + dz * dz;
                if (d2 >= outer * outer) continue;
                float d = (float)Math.Sqrt(d2);
                float t = Mathf.Clamp01((d - pl.R) / fade);
                t = t * t * (3f - 2f * t);
                // Küste bleibt Küste: unter Wasser wird kaum angehoben (keine flache Scheibe ins Meer)
                float keep = Mathf.Clamp01((height - 22f) / 8f);
                t = 1f - (1f - t) * keep;
                height = pl.H + (height - pl.H) * t;
            }
        }

        // ---------------------------------------------------------------- Bauen, wenn die Zone entsteht

        /// <summary>Vor dem Entstehen einer Zone: alle unsere Gebäude darin müssen geladen sein.</summary>
        public static bool ReadyToSpawn(ZoneSystem zs, Vector2s zone)
        {
            List<(ZoneSystem.ZoneLocation loc, Vector3 pos)> list;
            lock (s_extras) if (!s_extras.TryGetValue(zone, out list)) return true;
            if (PokeM == null) return true;
            bool ok = true;
            foreach (var e in list)
                if (!(bool)PokeM.Invoke(zs, new object[] { e.loc, true })) ok = false;
            return ok;
        }

        public static void SpawnIn(ZoneSystem zs, Vector2s zone, object mode, List<GameObject> spawned)
        {
            List<(ZoneSystem.ZoneLocation loc, Vector3 pos)> list;
            lock (s_extras)
            {
                if (!s_extras.TryGetValue(zone, out list)) return;
                list = list.ToList();
            }
            int seedBase = WorldGenerator.instance.GetSeed();
            foreach (var (loc, p0) in list)
            {
                try
                {
                    var args = new object[] { p0, null, null, null, null };
                    GroundData.Invoke(zs, args);
                    var pos = (Vector3)args[0];
                    if (loc.m_snapToWater) pos.y = 30f;
                    int seed = seedBase + (int)(p0.x * 13.7f) + (int)(p0.z * 31.3f);
                    var rng = new System.Random(seed);
                    var rot = Quaternion.Euler(0f, rng.Next(16) * 22.5f, 0f);
                    SpawnLocationM.Invoke(zs, new object[] { loc, seed, pos, rot, mode, spawned, false });
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[Orte] {loc.m_prefabName} bei {p0}: {e.InnerException?.Message ?? e.Message}");
                }
            }
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), "SpawnZone")]
    static class Patch_SpawnZoneExtras
    {
        static readonly MethodInfo ZoneGenerated = AccessTools.Method(typeof(ZoneSystem), "IsZoneGenerated");

        static bool Prefix(ZoneSystem __instance, Vector2s zoneID, ref bool __result)
        {
            if (!State.Active || !Planetos.Enabled) return true;
            if (ZoneGenerated != null && (bool)ZoneGenerated.Invoke(__instance, new object[] { zoneID })) return true;
            if (PointsOfInterest.ReadyToSpawn(__instance, zoneID)) return true;
            __result = false; // Gebäude noch nicht geladen: später noch einmal versuchen
            return false;
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), "PlaceLocations")]
    static class Patch_PlaceExtras
    {
        static void Postfix(ZoneSystem __instance, object[] __args)
        {
            if (!State.Active || !Planetos.Enabled) return;
            try
            {
                PointsOfInterest.SpawnIn(__instance, (Vector2s)__args[0], __args[5], (List<GameObject>)__args[6]);
            }
            catch (Exception e) { Plugin.Log.LogWarning("[Orte] Bauen: " + e.Message); }
        }
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
