using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Breitengrade
{
    /// <summary>
    /// Portal-Platz am Startpunkt einer Breitengrade-Welt (eingeebnete Lichtung):
    /// - innerer Ring: je ein Portal in jedes andere Biom und in beide Inselhälften,
    /// - äußerer Ring: Reisepunkte, je Band vier über die Ost-West-Breite verteilt.
    /// Die Gegenstücke stehen auf flachen, trockenen Stellen, weit weg von Strukturen,
    /// und werden auf der Karte markiert.
    /// </summary>
    internal static class TestPortals
    {
        const string PortalPrefab = "portal_wood";
        const string MarkerKey = "breitengrade_testportal";
        const string IslandMeadowsTag = "Insel Meadows", IslandPlainsTag = "Insel Plains";

        /// <summary>Lichtung um den Startpunkt; innerer Ring = Biome, äußerer Ring = Reisepunkte.</summary>
        public const float ClearingRadius = 62f;
        const float InnerRing = 22f, OuterRing = 46f;

        /// <summary>Reisepunkte je Band: Anteil der halben Bandbreite (Ost-West) und Namenszusatz.</summary>
        static readonly (float frac, string name)[] TravelSlots =
            { (-0.78f, "Fernwest"), (-0.42f, "West"), (0.42f, "Ost"), (0.78f, "Fernost") };

        public static readonly List<(Vector3 center, float radius)> ClearAreas = new List<(Vector3 center, float radius)>();
        static readonly List<(string name, Vector3 pos)> s_destinations = new List<(string name, Vector3 pos)>();
        static bool s_done, s_pinsDone;
        static float s_nextCheck;

        public static void OnWorldChanged()
        {
            s_done = false;
            s_pinsDone = false;
            ClearAreas.Clear();
            s_destinations.Clear();
        }

        public static void Tick()
        {
            if (!State.Active || Time.time < s_nextCheck) return;
            s_nextCheck = Time.time + 1f;
            if (s_done) { TryAddPins(); return; }

            if (ZNet.instance == null || ZoneSystem.instance == null || ZDOMan.instance == null ||
                ZNetScene.instance == null || WorldGenerator.instance == null) return;
            if (!ZoneSystem.instance.LocationsGenerated) return;
            s_done = true;

            try
            {
                Vector3 temple = FindStartTemple();
                ClearAreas.Add((temple, ClearingRadius));

                var existing = FindOurPortals();
                foreach (var z in existing)
                {
                    var p = z.GetPosition();
                    if (Flat(p - temple).magnitude <= ClearingRadius) continue;
                    if (Flat(p - MageTower.Center).magnitude <= MageTower.BaseRadius) continue;
                    ClearAreas.Add((p, 8f));
                    s_destinations.Add((z.GetString(ZDOVars.s_tag, "Portal"), p));
                }
                if (existing.Count > 0)
                {
                    Plugin.Log.LogInfo($"[Portale] {existing.Count} Portale vorhanden, nichts neu gebaut.");
                    if (Plugin.MageHouse.Value)
                    {
                        ClearAreas.Add((MageTower.Center, MageTower.TopRadius + 15f));
                        s_destinations.Add(("Magierhaus", MageTower.Center));
                    }
                    return;
                }

                if (!ZNet.instance.IsServer() || !Plugin.TestPortalsEnabled.Value) return;
                Build(temple, existing);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("[Portale] Fehler: " + e);
            }
        }

        static Vector3 FindStartTemple()
        {
            foreach (var li in ZoneSystem.instance.m_locationInstances.Values)
                if (li.m_location != null && li.m_location.m_prefabName == "StartTemple") return li.m_position;
            return Vector3.zero;
        }

        static List<ZDO> FindOurPortals()
        {
            var all = new List<ZDO>();
            int index = 0;
            for (int i = 0; i < 100000 && !ZDOMan.instance.GetAllZDOsWithPrefabIterative(PortalPrefab, all, ref index); i++) { }
            return all.Where(z => z.GetBool(MarkerKey)).ToList();
        }

        // ---------------------------------------------------------------- Aufstellen

        static void Build(Vector3 temple, List<ZDO> existing)
        {
            if (ZNetScene.instance.GetPrefab(PortalPrefab) == null)
            {
                Plugin.Log.LogWarning("[Portale] Portal-Bauteil nicht gefunden.");
                return;
            }

            // Nur Ziele, für die es noch kein Portal gibt.
            var haveTags = new HashSet<string>(existing.Select(z => z.GetString(ZDOVars.s_tag, "")));
            var inner = new List<(string name, Vector3 pos)>();
            var outer = new List<(string name, Vector3 pos)>();
            var t0 = DateTime.Now;

            if (Planetos.Enabled)
            {
                foreach (var place in Planetos.Places)
                {
                    if (haveTags.Contains(place.name)) continue;
                    Planetos.FromPixel(place.u, place.v, out float px, out float pz);
                    if (FindNear(px, pz, place.biome, 2500f * WorldScale.S, out var pos)) inner.Add((place.name, pos));
                    else Plugin.Log.LogWarning($"[Portale] Keine gute Stelle für {place.name} gefunden.");
                }
                // Oben auf der Mauer
                if (!haveTags.Contains("Die Mauer"))
                {
                    Planetos.WallPoint(312f, out float wx, out float wz);
                    float top = WorldGenerator.instance.GetHeight(wx, wz);
                    inner.Add(("Die Mauer", new Vector3(wx, top + 0.05f, wz)));
                }
            }
            // Innerer Ring: ein Portal je Biom (Mitte des Bands) plus die beiden Inselhälften.
            for (int i = 0; i < Latitude.BandBiomes.Length && !Planetos.Enabled; i++)
            {
                string tag = Latitude.BandNames[i];
                if (Latitude.BandBiomes[i] == Latitude.Meadows || haveTags.Contains(tag)) continue;
                if (FindSpot(i, 0f, 7000f * WorldScale.S, out var pos)) inner.Add((tag, pos));
                else Plugin.Log.LogWarning($"[Portale] Keine gute Stelle für {tag} gefunden.");
            }
            if (Latitude.SeaEnabled)
            {
                foreach (var (tag, biome) in new[] { (IslandMeadowsTag, Latitude.Meadows), (IslandPlainsTag, Latitude.Plains) })
                {
                    if (haveTags.Contains(tag)) continue;
                    if (FindIslandSpot(biome, out var pos)) inner.Add((tag, pos));
                    else Plugin.Log.LogWarning($"[Portale] Keine gute Stelle für {tag} gefunden.");
                }
            }

            // Äußerer Ring: Reisepunkte, je Band vier über die Breite verteilt.
            if (Plugin.TravelPortalsEnabled.Value && !Planetos.Enabled)
            {
                float edge = 9300f * WorldScale.S;
                for (int i = 0; i < Latitude.BandBiomes.Length; i++)
                {
                    float lo = i == 0 ? -edge : Math.Max(-edge, Latitude.Bounds[i - 1]);
                    float hi = i == Latitude.Bounds.Length ? edge : Math.Min(edge, Latitude.Bounds[i]);
                    float mid = (lo + hi) / 2f;
                    float halfWidth = (float)Math.Sqrt(Math.Max(0f, edge * edge - mid * mid));
                    foreach (var (frac, suffix) in TravelSlots)
                    {
                        string tag = $"{Latitude.BandNames[i]} {suffix}";
                        if (haveTags.Contains(tag)) continue;
                        if (FindSpot(i, frac * halfWidth, 0.2f * halfWidth, out var pos)) outer.Add((tag, pos));
                        else Plugin.Log.LogWarning($"[Portale] Keine gute Stelle für {tag} gefunden.");
                    }
                }
            }

            var all = inner.Concat(outer).ToList();
            if (Plugin.MageHouse.Value)
            {
                // Ein Portal am Start führt ins Magierhaus; dort stehen alle anderen im Portalturm.
                if (!FindRingSpot(temple, 90f, 14f, out Vector3 spawnPos, out float spawnYaw))
                    spawnPos = temple + new Vector3(0f, 0f, 14f);
                var hall = MageTower.Build(spawnPos, spawnYaw, all, SpawnPortalWithYaw);
                ClearAreas.Add((MageTower.Center, MageTower.TopRadius + 15f));
                s_destinations.Add(("Magierhaus", hall));
                foreach (var t in all)
                {
                    ClearAreas.Add((t.pos, 8f));
                    s_destinations.Add(t);
                }
            }
            else
            {
                PlaceRing(temple, existing, inner, InnerRing);
                PlaceRing(temple, existing, outer, OuterRing);
            }
            Plugin.Log.LogInfo($"[Portale] {all.Count} Ziele aufgestellt (Suche {(DateTime.Now - t0).TotalSeconds:0.0} s).");
        }

        static void SpawnPortalWithYaw(Vector3 pos, float yaw, string tag) => SpawnPortal(pos, yaw, tag);

        /// <summary>Stellt Portale gleichmäßig in einen Ring um den Start, alle mit Blick zur Mitte.</summary>
        static void PlaceRing(Vector3 temple, List<ZDO> existing, List<(string name, Vector3 pos)> targets, float radius)
        {
            if (targets.Count == 0) return;
            var usedAngles = existing
                .Select(z => Flat(z.GetPosition() - temple))
                .Where(v => Math.Abs(v.magnitude - radius) < 8f)
                .Select(v => Mathf.Atan2(v.z, v.x) * Mathf.Rad2Deg)
                .ToList();

            int n = targets.Count;
            for (int i = 0; i < n; i++)
            {
                float baseAngle = usedAngles.Count == 0 ? 90f - i * 360f / n : LargestGap(usedAngles);
                usedAngles.Add(baseAngle);
                if (!FindRingSpot(temple, baseAngle, radius, out Vector3 home, out float yaw))
                {
                    Plugin.Log.LogWarning($"[Portale] Kein Platz im Ring für {targets[i].name}.");
                    continue;
                }
                string tag = targets[i].name;
                SpawnPortal(home, yaw, tag);
                SpawnPortal(targets[i].pos, 0f, tag);
                ClearAreas.Add((targets[i].pos, 8f));
                s_destinations.Add((tag, targets[i].pos));
                Plugin.Log.LogInfo($"[Portale] {tag}: Ziel bei {Fmt(targets[i].pos)}");
            }
        }

        /// <summary>Winkel, der am weitesten von allen schon belegten Winkeln entfernt ist.</summary>
        static float LargestGap(List<float> used)
        {
            float best = 0f, bestDist = -1f;
            for (float a = 0f; a < 360f; a += 2f)
            {
                float d = used.Min(u => Math.Abs(Mathf.DeltaAngle(a, u)));
                if (d > bestDist) { bestDist = d; best = a; }
            }
            return best;
        }

        static void SpawnPortal(Vector3 pos, float yaw, string tag)
        {
            var prefab = ZNetScene.instance.GetPrefab(PortalPrefab);
            int hash = prefab.name.GetStableHashCode();
            var zdo = ZDOMan.instance.CreateNewZDO(pos, hash);
            zdo.SetPrefab(hash);
            zdo.SetRotation(Quaternion.Euler(0f, yaw, 0f));
            var view = prefab.GetComponent<ZNetView>();
            if (view != null)
            {
                zdo.Persistent = view.m_persistent;
                zdo.Type = view.m_type;
                zdo.Distant = view.m_distant;
            }
            zdo.Set(ZDOVars.s_tag, tag);
            zdo.Set(MarkerKey, true);
            zdo.Set(MageTower.BuildMarker, true);
            Game.instance?.ConnectPortals();
        }

        // ---------------------------------------------------------------- Karte

        /// <summary>Markiert alle Portal-Ziele auf der Karte (ohne doppelte Markierungen).</summary>
        static void TryAddPins()
        {
            if (s_pinsDone || !Plugin.PortalPins.Value || s_destinations.Count == 0) return;
            if (Player.m_localPlayer == null || Minimap.instance == null) return;
            s_pinsDone = true;
            try
            {
                var pins = Traverse.Create(Minimap.instance).Field("m_pins").GetValue<List<Minimap.PinData>>();
                int added = 0;
                foreach (var (name, pos) in s_destinations)
                {
                    if (pins != null && pins.Any(p => p.m_name == name && Flat(p.m_pos - pos).magnitude < 10f)) continue;
                    Minimap.instance.AddPin(pos, Minimap.PinType.Icon4, name, true, false, 0L, default);
                    added++;
                }
                if (added > 0) Plugin.Log.LogInfo($"[Portale] {added} Portal-Ziele auf der Karte markiert.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Portale] Kartenmarkierungen: " + e.Message);
            }
        }

        // ---------------------------------------------------------------- Plätze suchen

        /// <summary>Platz im Ring um den Start (der Platz ist eingeebnet). Weicht bei Bedarf etwas aus.</summary>
        static bool FindRingSpot(Vector3 temple, float baseAngle, float radius, out Vector3 pos, out float yaw)
        {
            var wg = WorldGenerator.instance;
            foreach (float off in new[] { 0f, 3f, -3f, 6f, -6f })
            {
                float ang = (baseAngle + off) * Mathf.Deg2Rad;
                float x = temple.x + Mathf.Cos(ang) * radius, z = temple.z + Mathf.Sin(ang) * radius;
                float min = float.MaxValue, max = float.MinValue;
                for (int a = 0; a < 9; a++)
                {
                    float sx = a == 0 ? x : x + Mathf.Cos(a * 45f * Mathf.Deg2Rad) * 2f;
                    float sz = a == 0 ? z : z + Mathf.Sin(a * 45f * Mathf.Deg2Rad) * 2f;
                    float h = wg.GetHeight(sx, sz);
                    min = Math.Min(min, h); max = Math.Max(max, h);
                }
                if (min < 31f || max - min > 0.6f) continue;
                pos = new Vector3(x, max + 0.02f, z);
                var toCenter = Flat(temple - pos);
                yaw = Mathf.Atan2(toCenter.x, toCenter.z) * Mathf.Rad2Deg;
                return true;
            }
            pos = Vector3.zero; yaw = 0f;
            return false;
        }

        /// <summary>Sucht im Band um die Ost-West-Position <paramref name="xStart"/> (bis ±maxDx) eine gute Stelle.</summary>
        static bool FindSpot(int band, float xStart, float maxDx, out Vector3 result)
        {
            int biome = Latitude.BandBiomes[band];
            float S = WorldScale.S, edge = 9300f * S;
            float lo = band == 0 ? -edge : Latitude.Bounds[band - 1];
            float hi = band == Latitude.Bounds.Length ? edge : Latitude.Bounds[band];
            lo = Math.Max(lo, -edge); hi = Math.Min(hi, edge);
            float mid = (lo + hi) / 2f, half = (hi - lo) / 2f;

            // Drei Durchgänge: erst streng, dann lockerer.
            for (int pass = 0; pass < 3; pass++)
            for (float dx = 0f; dx <= maxDx; dx += 60f * S)
            for (int s = 0; s < 2; s++)
            {
                if (s == 1 && dx == 0f) continue;
                float px = s == 0 ? xStart + dx : xStart - dx;
                for (float dz = 0f; dz <= half; dz += 150f * S)
                for (int t = 0; t < 2; t++)
                {
                    float pz = t == 0 ? mid + dz : mid - dz;
                    if (t == 1 && dz == 0f) continue;
                    if (Check(px, pz, band, biome, lo, hi, pass, out float y))
                    {
                        result = new Vector3(px, y, pz);
                        return true;
                    }
                }
            }
            result = Vector3.zero;
            return false;
        }

        static bool Check(float x, float z, int band, int biome, float lo, float hi, int pass, out float y)
        {
            y = 0f;
            float edge = 9300f * WorldScale.S;
            if (x * x + z * z > edge * edge) return false;
            if (Latitude.GetBiome(x, z) != biome) return false;

            // Genug Abstand zur Nachbar-Biomgrenze, damit man nicht im Übergang landet.
            float wz = Latitude.WobbledZ(x, z);
            bool south = band == 0, north = band == Latitude.Bounds.Length;
            if (!south && wz - lo < 250f) return false;
            if (!north && hi - wz < 250f) return false;
            if (Latitude.CoastDistance(x, z) > -200f) return false; // nicht am großen Meer / auf der Insel

            return GroundOk(x, z, biome, pass, out y);
        }

        /// <summary>Sucht spiralförmig um (x, z) eine flache, trockene Stelle im gewünschten Biom.</summary>
        static bool FindNear(float x0, float z0, int biome, float maxR, out Vector3 result)
        {
            float step = 40f * WorldScale.S;
            float edge = 9300f * WorldScale.S;
            for (int pass = 0; pass < 3; pass++)
            for (float r = 0f; r <= maxR; r += step)
            {
                int n = r == 0f ? 1 : Math.Max(8, (int)(2f * Mathf.PI * r / step));
                for (int a = 0; a < n; a++)
                {
                    float ang = a * 2f * Mathf.PI / n;
                    float x = x0 + Mathf.Cos(ang) * r, z = z0 + Mathf.Sin(ang) * r;
                    if (x * x + z * z > edge * edge) continue;
                    if (Latitude.GetBiome(x, z) != biome || Latitude.CoastDistance(x, z) > -60f) continue;
                    if (GroundOk(x, z, biome, pass, out float y))
                    {
                        result = new Vector3(x, y, z);
                        return true;
                    }
                }
            }
            result = Vector3.zero;
            return false;
        }

        /// <summary>Insel-Portal: Meadows im Norden bzw. Plains im Süden der Insel, weit weg von der Küste.</summary>
        static bool FindIslandSpot(int biome, out Vector3 result)
        {
            float cx = Latitude.SeaX;
            float cz = Latitude.SeaZ + Latitude.IslandRadius * (biome == Latitude.Meadows ? 0.45f : -0.45f);
            for (int pass = 0; pass < 3; pass++)
            for (float r = 0f; r <= Latitude.IslandRadius * 0.8f; r += 40f * WorldScale.S)
            for (int a = 0; a < 24; a++)
            {
                float ang = a * 15f * Mathf.Deg2Rad;
                float x = cx + Mathf.Cos(ang) * r, z = cz + Mathf.Sin(ang) * r;
                if (!Latitude.IsIsland(x, z) || Latitude.CoastDistance(x, z) > -200f) continue;
                if (Latitude.GetBiome(x, z) != biome) continue;
                if (GroundOk(x, z, biome, pass, out float y))
                {
                    result = new Vector3(x, y, z);
                    return true;
                }
                if (r == 0f) break;
            }
            result = Vector3.zero;
            return false;
        }

        /// <summary>Boden flach und trocken, keine Lava, keine Struktur in der Nähe.</summary>
        static bool GroundOk(float x, float z, int biome, int pass, out float y)
        {
            y = 0f;
            var wg = WorldGenerator.instance;
            if ((int)wg.GetBiome(new Vector3(x, 0f, z)) != biome) return false;

            // Der Sumpf liegt knapp über dem Wasser (30 m), alle anderen Biome deutlich höher.
            bool swamp = biome == Latitude.Swamp;
            float dry = swamp ? 30.4f : 32f;
            float tolInner = pass == 0 ? 0.5f : pass == 1 ? 1.0f : 1.6f;
            float tolOuter = pass == 0 ? 1.4f : pass == 1 ? 2.5f : 4f;
            float outerR = pass == 2 ? 6f : 9f;

            float center = wg.GetHeight(x, z, out Color mask);
            if (center < (swamp && pass == 2 ? 30.4f : dry + 0.3f)) return false;
            if (biome == Latitude.AshLands && mask.a > 0.02f) return false;

            // Im Sumpf ist fast überall Wasser in der Nähe. Im letzten Durchgang reicht dort eine
            // trockene kleine Insel (3 m um das Portal), drumherum darf Wasser sein.
            int rings = swamp && pass == 2 ? 1 : 2;
            if (swamp && pass == 2) dry = 30.2f;

            float minIn = center, maxIn = center, minAll = center, maxAll = center;
            for (int ring = 1; ring <= rings; ring++)
            {
                float r = ring == 1 ? 3f : outerR;
                for (int a = 0; a < 8; a++)
                {
                    float ang = a * 45f * Mathf.Deg2Rad;
                    float h = wg.GetHeight(x + Mathf.Cos(ang) * r, z + Mathf.Sin(ang) * r, out Color m);
                    if (h < dry) return false;                                     // Wasser in der Nähe
                    if (biome == Latitude.AshLands && m.a > 0.02f) return false;   // Lava
                    if (ring == 1) { minIn = Math.Min(minIn, h); maxIn = Math.Max(maxIn, h); }
                    minAll = Math.Min(minAll, h); maxAll = Math.Max(maxAll, h);
                }
            }
            if (maxIn - minIn > tolInner || maxAll - minAll > tolOuter) return false;
            if (NearLocation(new Vector3(x, 0f, z), 15f)) return false;
            if (Plugin.MageHouse.Value && Flat(new Vector3(x, 0f, z) - MageTower.Center).magnitude < MageTower.BaseRadius + 30f)
                return false; // nicht am Tafelberg des Magierhauses

            // Auf die höchste Stelle direkt unter dem Portal setzen, damit es nicht im Boden steckt.
            y = maxIn + 0.02f;
            return true;
        }

        static bool NearLocation(Vector3 p, float extra)
        {
            foreach (var li in ZoneSystem.instance.m_locationInstances.Values)
            {
                var loc = li.m_location;
                if (loc == null) continue;
                float r = Math.Max(20f, loc.m_exteriorRadius + extra);
                if (Flat(li.m_position - p).sqrMagnitude < r * r) return true;
            }
            return false;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
        static string Fmt(Vector3 v) => $"({v.x:0}, {v.y:0}, {v.z:0})";
    }

    /// <summary>Beim Erzeugen der Bäume/Steine die Lichtung und die Portalplätze freihalten.</summary>
    [HarmonyPatch(typeof(ZoneSystem), "PlaceVegetation")]
    static class Patch_PlaceVegetation
    {
        // ZoneSystem.ClearArea ist privat, deshalb per Reflection.
        static readonly Type ClearAreaType = AccessTools.Inner(typeof(ZoneSystem), "ClearArea");

        static void Prefix(Vector3 zoneCenterPos, object clearAreas)
        {
            if (!State.Active || !(clearAreas is System.Collections.IList list) || ClearAreaType == null) return;
            foreach (var (center, radius) in TestPortals.ClearAreas)
            {
                float reach = 46f + radius; // halbe Zonen-Diagonale + Radius
                var d = center - zoneCenterPos;
                if (d.x * d.x + d.z * d.z < reach * reach)
                    list.Add(Activator.CreateInstance(ClearAreaType, center, radius));
            }
        }
    }

    /// <summary>
    /// Ebnet den Platz um die Weltmitte (dort entsteht der Startpunkt) zu einer flachen Ebene ein,
    /// mit weichem Übergang ins umliegende Gelände.
    /// </summary>
    [HarmonyPatch(typeof(WorldGenerator), "GetBiomeHeight")]
    static class Patch_SpawnPlaza
    {
        const float FlatRadius = 100f, FadeRadius = 160f;
        static readonly object s_lock = new object();
        static float s_plazaHeight = float.NaN;
        [ThreadStatic] static bool t_inside;

        /// <summary>Mitte des eingeebneten Startplatzes (Weltmitte, bei Planetos in Westeros).</summary>
        public static Vector3 Center;

        public static void Reset()
        {
            lock (s_lock) s_plazaHeight = float.NaN;
            Center = Vector3.zero;
            if (Planetos.Enabled)
            {
                Planetos.FromPixel(Planetos.SpawnU, Planetos.SpawnV, out float x, out float z);
                Center = new Vector3(x, 0f, z);
            }
        }

        static void Postfix(WorldGenerator __instance, float wx, float wy, ref float __result)
        {
            if (!State.Active || t_inside) return;
            MageTower.Shape(wx, wy, ref __result);
            if (Planetos.Enabled)
            {
                float wall = Planetos.WallHeight(wx, wy, Plugin.WallTop.Value);
                if (!float.IsNaN(wall) && wall > __result) __result = wall;
            }
            if (!Plugin.FlattenSpawn.Value && !Planetos.Enabled) return;
            float d = (float)Math.Sqrt((wx - Center.x) * (wx - Center.x) + (wy - Center.z) * (wy - Center.z));
            if (d >= FadeRadius) return;

            float plaza = PlazaHeight(__instance);
            float t = Mathf.Clamp01((d - FlatRadius) / (FadeRadius - FlatRadius));
            t = t * t * (3f - 2f * t);
            __result = Mathf.Lerp(plaza, __result, t);
        }

        /// <summary>Höhe der Ebene: Gelände in der Weltmitte ohne Einebnung, mindestens 6 m über Wasser.</summary>
        static float PlazaHeight(WorldGenerator wg)
        {
            lock (s_lock)
            {
                if (!float.IsNaN(s_plazaHeight)) return s_plazaHeight;
                t_inside = true;
                try
                {
                    float sum = 0f;
                    int n = 0;
                    for (float x = -40f; x <= 40f; x += 20f)
                    for (float z = -40f; z <= 40f; z += 20f)
                    {
                        sum += wg.GetBiomeHeight(Heightmap.Biome.Meadows, Center.x + x, Center.z + z, out Color _, false, false);
                        n++;
                    }
                    s_plazaHeight = Math.Max(36f, sum / n);
                }
                finally { t_inside = false; }
                return s_plazaHeight;
            }
        }
    }
}
