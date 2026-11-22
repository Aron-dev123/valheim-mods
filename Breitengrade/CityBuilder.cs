using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Breitengrade
{
    /// <summary>
    /// Planetos: baut an jedem benannten Ort eine eigene Stadt aus Bauteilen (wie das Magierhaus):
    /// eingeebnete Fläche, Stadtmauer mit Türmen und Tor, Straßenkreuz, Häuser in Blöcken und ein
    /// Wahrzeichen nach der Lore (Hightower, Red Keep, Große Pyramide, Horse Gate …).
    ///
    /// Die Städte werden einmal je Welt geplant (Fläche und Höhe stehen in
    /// BepInEx/config/Breitengrade/staedte_&lt;id&gt;.txt) und danach Stadt für Stadt gebaut, eine pro Sekunde.
    /// Alle Bauteile sind geschützt (kein Schaden, keine Stütze nötig).
    /// </summary>
    public static class CityBuilder
    {
        // ---------------------------------------------------------------- Planung

        public class Plan
        {
            public string Name;
            public float X, Z, R, H;
            public bool Built;
        }

        static readonly List<Plan> s_plans = new List<Plan>();
        static readonly Queue<Plan> s_queue = new Queue<Plan>();
        static bool s_signsChecked;
        public static IReadOnlyList<Plan> Plans => s_plans;

        public static void OnWorldChanged()
        {
            s_plans.Clear();
            s_queue.Clear();
            s_signsChecked = false;
            RoadNet.OnWorldChanged();
        }

        static string SaveFile()
        {
            long uid = ZNet.instance != null ? ZNet.instance.GetWorldUID() : 0L;
            string dir = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "Breitengrade");
            System.IO.Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, $"staedte_{uid}.txt");
        }

        static void SavePlans()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            System.IO.File.WriteAllLines(SaveFile(), s_plans.Select(p =>
                string.Format(inv, "{0};{1:0.0};{2:0.0};{3:0.0};{4:0.00};{5}", p.Name, p.X, p.Z, p.R, p.H, p.Built ? 1 : 0)));
        }

        static void LoadPlans()
        {
            s_plans.Clear();
            string f = SaveFile();
            if (!System.IO.File.Exists(f)) return;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var ns = System.Globalization.NumberStyles.Float;
            foreach (var line in System.IO.File.ReadAllLines(f))
            {
                var p = line.Split(';');
                if (p.Length < 6) continue;
                if (!float.TryParse(p[1], ns, inv, out float x) || !float.TryParse(p[2], ns, inv, out float z) ||
                    !float.TryParse(p[3], ns, inv, out float r) || !float.TryParse(p[4], ns, inv, out float h)) continue;
                s_plans.Add(new Plan { Name = p[0], X = x, Z = z, R = r, H = h, Built = p[5] == "1" });
            }
        }

        /// <summary>Stadtradius aus der gewünschten Größe (wie die Meadows-Städte mit n Dörfern).</summary>
        public static float RadiusFor(int size) => 31f * (float)Math.Sqrt(Math.Max(size, 3)) + 48f;

        /// <summary>
        /// Plant fehlende Städte (Fläche, Höhe), setzt die Einebnungen und stellt ungebaute Städte in die
        /// Bau-Warteschlange. <paramref name="skip"/>: Orte mit Städten aus älteren Versionen.
        /// </summary>
        public static List<PointsOfInterest.Plateau> PlanAll(HashSet<string> skip, List<Vector3> portals)
        {
            var zs = ZoneSystem.instance;
            LoadPlans();
            bool changed = false;
            foreach (var place in Planetos.Places)
            {
                if (skip.Contains(place.name) || s_plans.Any(p => p.Name == place.name)) continue;
                int size = Planetos.PoiCount.TryGetValue(place.name, out int c) ? c : Planetos.DefaultPoiCount;
                if (size <= 0) continue;
                float R = RadiusFor(size);
                Planetos.FromPixel(place.u, place.v, out float cx, out float cz);
                if (!FindSite(zs, place.biome, cx, cz, R, portals, out Vector3 site, out float h))
                {
                    Plugin.Log.LogWarning($"[Städte] {place.name}: kein Platz für die Stadt gefunden.");
                    continue;
                }
                s_plans.Add(new Plan { Name = place.name, X = site.x, Z = site.z, R = R, H = h });
                changed = true;
            }
            if (changed) SavePlans();
            ClearCityLocations(zs);

            RoadNet.Plan(s_plans, SaveFile().Replace("staedte_", "strassen_"));
            RefreshCityLayout();
            var plateaus = new List<PointsOfInterest.Plateau>();
            foreach (var p in s_plans)
            {
                plateaus.Add(new PointsOfInterest.Plateau { X = p.X, Z = p.Z, R = p.R + 6f, H = p.H });
                BauLoader.Hills(p, plateaus);
                TestPortals.ClearAreas.Add((new Vector3(p.X, 0f, p.Z), p.R + 14f));
                PointsOfInterest.PinPositions[p.Name] = new Vector3(p.X, 0f, p.Z);
                if (!p.Built) s_queue.Enqueue(p);
            }
            return plateaus;
        }

        /// <summary>Platz für eine Stadt: möglichst viel Land im richtigen Biom, noch nicht erzeugte Zonen.</summary>
        static bool FindSite(ZoneSystem zs, int biome, float cx, float cz, float R, List<Vector3> portals, out Vector3 site, out float height)
        {
            site = Vector3.zero; height = 0f;
            var wg = WorldGenerator.instance;
            float S = WorldScale.S;
            float edge = 9400f * S;
            float best = -1f;
            var gen = AccessTools_IsZoneGenerated();
            for (float r = 0f; r <= 900f * S; r += 60f)
            {
                int n = r == 0f ? 1 : Math.Max(8, (int)(2f * Mathf.PI * r / 60f));
                for (int a = 0; a < n; a++)
                {
                    float ang = a * 2f * Mathf.PI / n;
                    float x = cx + Mathf.Cos(ang) * r, z = cz + Mathf.Sin(ang) * r;
                    if ((x * x + z * z) > (edge - R) * (edge - R)) continue;
                    var p = new Vector3(x, 0f, z);
                    float reach = R + PointsOfInterest.PlateauFade;
                    if (Planetos.IsOnWall(x, z, reach + 20f)) continue;
                    if (Flat(p - MageTower.Center).magnitude < MageTower.BaseRadius + reach + 40f) continue;
                    if (Flat(p - Patch_SpawnPlaza.Center).magnitude < 200f + reach) continue;
                    if (portals.Any(q => Flat(q - p).magnitude < reach)) continue;
                    if (s_plans.Any(o => Flat(new Vector3(o.X, 0f, o.Z) - p).magnitude < o.R + R + 2f * PointsOfInterest.PlateauFade)) continue;
                    if (Latitude.GetBiome(x, z) != biome) continue;

                    // Anteil Land im richtigen Biom
                    int good = 0, total = 0;
                    foreach (float fr in new[] { 0f, 0.5f, 0.95f })
                    {
                        int m = fr == 0f ? 1 : 12;
                        for (int k = 0; k < m; k++)
                        {
                            float aa = k * 2f * Mathf.PI / m;
                            float sx = x + Mathf.Cos(aa) * R * fr, sz = z + Mathf.Sin(aa) * R * fr;
                            total++;
                            if (Latitude.CoastDistance(sx, sz) < -10f && Latitude.GetBiome(sx, sz) == biome) good++;
                        }
                    }
                    float score = good / (float)total - r / (900f * S) * 0.25f;
                    if (score <= best) continue;
                    // Zonen dürfen noch nicht erzeugt sein
                    if (gen != null && AnyGenerated(zs, gen, p, reach)) continue;
                    best = score; site = p;
                }
                if (best >= 0.97f) break;
            }
            if (best < 0.55f) return false;

            // Höhe: Mittel des Landes (ohne Wasser), mindestens 2 m über dem Meer
            var hs = new List<float>();
            for (int k = 0; k < 24; k++)
            {
                float aa = k * 2f * Mathf.PI / 24f;
                foreach (float fr in new[] { 0.3f, 0.7f })
                {
                    float hh = wg.GetHeight(site.x + Mathf.Cos(aa) * R * fr, site.z + Mathf.Sin(aa) * R * fr);
                    if (hh > 30.5f) hs.Add(hh);
                }
            }
            hs.Sort();
            height = hs.Count > 0 ? hs[hs.Count / 2] : 33f;
            height = Math.Max(height, biome == Latitude.Swamp ? 31.2f : 32.5f);
            return true;
        }

        static System.Reflection.MethodInfo AccessTools_IsZoneGenerated() =>
            HarmonyLib.AccessTools.Method(typeof(ZoneSystem), "IsZoneGenerated");

        static bool AnyGenerated(ZoneSystem zs, System.Reflection.MethodInfo gen, Vector3 c, float reach)
        {
            var z0 = ZoneSystem.GetZone(c);
            int k = (int)Math.Ceiling(reach / 64f) + 1;
            for (int dy = -k; dy <= k; dy++)
            for (int dx = -k; dx <= k; dx++)
                if ((bool)gen.Invoke(zs, new object[] { new Vector2s(z0.x + dx, z0.y + dy) })) return true;
            return false;
        }

        /// <summary>Noch nicht gebaute, häufige Valheim-Locations im Stadtgebiet entfernen.</summary>
        static readonly System.Reflection.MethodInfo s_register = HarmonyLib.AccessTools.Method(typeof(ZoneSystem), "RegisterLocation");

        /// <summary>Liegt der Punkt in einer Stadt (Radius + Rand)?</summary>
        public static bool InCity(float x, float z, float margin = 0f)
        {
            foreach (var p in s_plans)
            {
                float r = p.R + margin, dx = x - p.X, dz = z - p.Z;
                if (dx * dx + dz * dz < r * r) return true;
            }
            return false;
        }

        static bool Important(ZoneSystem.ZoneLocation l)
        {
            string n = l.m_prefabName ?? "";
            return l.m_prioritized || l.m_unique || l.m_quantity < 15 ||
                   n.Contains("Vendor") || n.Contains("Hildir") || n.Contains("BogWitch") || n.Contains("Crypt") ||
                   n.Contains("Cave") || n.Contains("Dungeon") || n.Contains("DvergrTown") || n.Contains("Morgen");
        }

        /// <summary>
        /// Stadtgebiete frei machen: Händler, Bosse, Dungeons und andere wichtige Orte werden vor die Stadt
        /// verlegt, alles andere (Ruinen, Steinkreise …) entfernt. Nur noch nicht gebaute Orte.
        /// </summary>
        static void ClearCityLocations(ZoneSystem zs)
        {
            var gen = HarmonyLib.AccessTools.Method(typeof(ZoneSystem), "IsZoneGenerated");
            var hits = zs.m_locationInstances.Where(kv =>
            {
                var l = kv.Value.m_location;
                if (kv.Value.m_placed || l == null || l.m_prefabName == "StartTemple") return false;
                var pos = kv.Value.m_position;
                return InCity(pos.x, pos.z, l.m_exteriorRadius + PointsOfInterest.PlateauFade);
            }).ToList();
            int moved = 0, removed = 0;
            foreach (var kv in hits)
            {
                var li = kv.Value;
                RemoveInstance(zs, kv.Key, li);
                if (!Important(li.m_location)) { removed++; continue; }
                // neuen Platz außerhalb der Stadt suchen
                var plan = s_plans.OrderBy(p => (p.X - li.m_position.x) * (p.X - li.m_position.x) + (p.Z - li.m_position.z) * (p.Z - li.m_position.z)).First();
                bool ok = false;
                for (float d = plan.R + PointsOfInterest.PlateauFade + li.m_location.m_exteriorRadius + 20f; d < plan.R + 900f && !ok; d += 40f)
                    for (int a = 0; a < 24 && !ok; a++)
                    {
                        float ang = a * Mathf.PI / 12f;
                        var q = new Vector3(plan.X + Mathf.Cos(ang) * d, 0f, plan.Z + Mathf.Sin(ang) * d);
                        if (InCity(q.x, q.z, li.m_location.m_exteriorRadius + PointsOfInterest.PlateauFade)) continue;
                        if (Latitude.CoastDistance(q.x, q.z) > -30f || Planetos.IsOnWall(q.x, q.z, 40f)) continue;
                        if (li.m_location.m_biome != Heightmap.Biome.None && (WorldGenerator.instance.GetBiome(q) & li.m_location.m_biome) == 0) continue;
                        var zone = ZoneSystem.GetZone(q);
                        if (zs.m_locationInstances.ContainsKey(zone)) continue;
                        if (gen != null && (bool)gen.Invoke(zs, new object[] { zone })) continue;
                        q.y = WorldGenerator.instance.GetHeight(q.x, q.z);
                        s_register.Invoke(zs, new object[] { li.m_location, q, false });
                        ok = zs.m_locationInstances.TryGetValue(zone, out var nl) && nl.m_location == li.m_location;
                    }
                if (ok) moved++;
                else Plugin.Log.LogWarning($"[Städte] {li.m_location.m_prefabName}: kein Platz außerhalb der Stadt gefunden.");
            }
            if (hits.Count > 0) Plugin.Log.LogInfo($"[Städte] Stadtgebiete frei gemacht: {moved} Orte verlegt (Händler, Bosse, Dungeons …), {removed} entfernt.");
        }

        static void RemoveInstance(ZoneSystem zs, Vector2s key, ZoneSystem.LocationInstance li)
        {
            zs.m_locationInstances.Remove(key);
            foreach (var list in zs.m_locationIDCache.Values) list.RemoveAll(x => x.m_position == li.m_position && x.m_location == li.m_location);
            foreach (var list in zs.m_locationGroupCache.Values) list.RemoveAll(x => x.m_position == li.m_position && x.m_location == li.m_location);
            foreach (var list in zs.m_locationMaxGroupCache.Values) list.RemoveAll(x => x.m_position == li.m_position && x.m_location == li.m_location);
        }

        /// <summary>Baut die nächste Stadt aus der Warteschlange (eine je Aufruf).</summary>
        public static void Tick()
        {
            if (ZNetScene.instance == null || ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (s_queue.Count == 0)
            {
                // Wegweiser einmal je Welt, wenn alle Städte stehen
                string flag = SaveFile() + ".schilder";
                if (s_plans.Count > 0 && !s_signsChecked)
                {
                    s_signsChecked = true;
                    if (!System.IO.File.Exists(flag))
                    {
                        int n = RoadNet.BuildSigns();
                        System.IO.File.WriteAllText(flag, n.ToString());
                        Plugin.Log.LogInfo($"[Straßen] {n} Wegweiser aufgestellt.");
                    }
                }
                return;
            }
            var plan = s_queue.Dequeue();
            try
            {
                var t0 = DateTime.Now;
                int count = BauLoader.Build(plan);
                plan.Built = true;
                SavePlans();
                Plugin.Log.LogInfo($"[Städte] {plan.Name}: {count} Bauteile, Radius {plan.R:0} m ({(DateTime.Now - t0).TotalSeconds:0.0} s).");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Städte] {plan.Name}: {e}");
            }
        }

        public static int Seed(Plan p) => p.Name.GetStableHashCode();

        /// <summary>Gassen, Plätze und Gärten aller Städte (aus dem Stadtbau-Modul) zum Malen übernehmen.</summary>
        public static void RefreshCityLayout()
        {
            var segs = new List<RoadNet.Seg>();
            var areas = new List<RoadNet.Area>();
            foreach (var p in s_plans) BauLoader.Streets(p, segs, areas);
            RoadNet.SetCityLayout(segs, areas);
        }

        /// <summary>Einebnungen neu berechnen (nach Neuladen des Moduls).</summary>
        public static void RefreshPlateaus()
        {
            var old = PointsOfInterest.Plateaus.ToList();
            var names = new HashSet<string>(s_plans.Select(p => p.Name));
            var list = new List<PointsOfInterest.Plateau>();
            foreach (var p in s_plans)
            {
                list.Add(new PointsOfInterest.Plateau { X = p.X, Z = p.Z, R = p.R + 6f, H = p.H });
                BauLoader.Hills(p, list);
            }
            // Einebnungen alter Ortsgruppen (ältere Versionen) behalten
            foreach (var pl in old)
                if (!s_plans.Any(p => Math.Abs(p.X - pl.X) < p.R && Math.Abs(p.Z - pl.Z) < p.R)) list.Add(pl);
            PointsOfInterest.Plateaus = list.ToArray();
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>Innenbereich (Häuser), Zellgröße und Pflaster einer Stadt (kommt aus dem Stadtbau-Modul).</summary>
        public static (float inner, float cell, bool paved) Layout(Plan p) => BauLoader.Layout(p);
    }
}
