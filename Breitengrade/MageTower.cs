using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Breitengrade
{
    /// <summary>
    /// Magierhaus auf einem hohen Tafelberg in den Meadows, mit Portalturm.
    /// Vorbild: "The Wizard's Rook" (Valheimians) – Portale nach Etagen, wichtige unten,
    /// jede Etage mit eigenem Licht; schwarzer Marmor und dunkles Holz.
    ///
    /// Aufbau auf dem Gipfel (eingeebnet):
    /// - Haupthalle 16 x 12 m, 6 m hoch, Flachdach mit Zinnen. Darin: Galdr-Tisch, Thron,
    ///   Feuerschalen, Teppich und das Portal "Magierhaus" zurück zum Startpunkt.
    /// - Portalturm 16 x 16 m daneben, mehrere Etagen à 6 m. Je Etage 12 Portalplätze an den Wänden.
    ///   Die Etagen sind per Portal verbunden ("Turm Ebene 1-2" …), es gibt keine Treppen.
    /// </summary>
    internal static class MageTower
    {
        public const string BuildMarker = "breitengrade_bau";

        // ------------------------------------------------------------ Lage und Podest

        /// <summary>Gipfelmitte (wird beim Aktivieren der Welt gesetzt).</summary>
        public static Vector3 Center;
        public const float TopRadius = 48f, CliffWidth = 28f, BaseRadius = 135f;

        public static void Configure()
        {
            float S = WorldScale.S;
            int mi = Array.IndexOf(Latitude.BandBiomes, Latitude.Meadows);
            float mid = (Latitude.Bounds[mi - 1] + Latitude.Bounds[mi]) / 2f;
            Center = new Vector3(-1800f * S, Plugin.MageHeight.Value, mid);
            if (Planetos.Enabled)
            {
                Planetos.FromPixel(Planetos.MageU, Planetos.MageV, out float x, out float z);
                Center = new Vector3(x, Plugin.MageHeight.Value, z);
            }
        }

        /// <summary>
        /// Gelände am Podest: flacher Gipfel, fast senkrechte Felswand (ca. 28 m breit),
        /// darunter ein kurzer, flacher Fuß. Wirkt wie ein riesiges Podest.
        /// </summary>
        public static bool Shape(float wx, float wz, ref float height)
        {
            if (!Plugin.MageHouse.Value) return false;
            float dx = wx - Center.x, dz = wz - Center.z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d >= BaseRadius) return false;

            // Unregelmäßiger Felsrand
            float ang = Mathf.Atan2(dz, dx);
            float rim = TopRadius + 5f * Mathf.Sin(ang * 5f + 1.3f) + 3f * Mathf.Sin(ang * 11f + 0.4f);
            float top = Center.y;
            if (d <= rim) { height = Math.Max(height, top); return true; }

            float f;
            if (d < rim + CliffWidth)
            {
                // Steilwand: von 100 % auf 12 % der Podesthöhe
                float t = (d - rim) / CliffWidth;
                f = 1f - 0.88f * (t * t * (3f - 2f * t));
            }
            else
            {
                // Fuß: läuft flach ins Gelände aus
                float t = Mathf.Clamp01((d - rim - CliffWidth) / (BaseRadius - rim - CliffWidth));
                f = 0.12f * (1f - t) * (1f - t);
            }
            height = Mathf.Lerp(height, Math.Max(height, top), f);
            return true;
        }

        // ------------------------------------------------------------ Bauen

        /// <summary>Baut Haus und Turm, verteilt die Portale. Gibt die Hallenmitte zurück.</summary>
        public static Vector3 Build(Vector3 spawnPos, float spawnYaw, List<(string name, Vector3 pos)> targets,
            Action<Vector3, float, string> spawnPortal)
        {
            float floorTop = Center.y + 0.05f;
            var hall = new Vector3(Center.x - 11f, floorTop, Center.z);
            var tower = new Vector3(Center.x + 12f, floorTop, Center.z);

            var floor = Pick(new[] { "blackmarble_floor", "stone_floor_2x2", "wood_floor" }, true);
            var wall = Pick(new[] { "blackmarble_tile_wall_2x2", "stone_wall_2x1", "stone_wall_4x2", "woodwall" }, true);
            var pillar = Pick(new[] { "blackmarble_column_1", "stone_pillar", "wood_pole2" }, false);
            var merlon = Pick(new[] { "blackmarble_tile_wall_1x1", "stone_wall_1x1" }, true);
            var stair = Pick(new[] { "blackmarble_stair", "stone_stair", "darkwood_stair", "wood_stair" }, true);
            if (floor == null || wall == null)
            {
                Plugin.Log.LogWarning("[Magierhaus] Bauteile für Boden/Wände nicht gefunden, nichts gebaut.");
                return hall;
            }
            // Oberfläche des Bodens über der Andock-Ebene (sonst stehen Dinge halb im Marmor).
            float surface = floor.SurfaceAbove;
            Plugin.Log.LogInfo($"[Magierhaus] Boden {floor.Prefab} ({floor.Width:0.#} m, Oberfläche +{surface:0.00} m), " +
                               $"Wand {wall.Prefab} ({wall.Width:0.#} x {wall.Height:0.#} m), Säule {pillar?.Prefab ?? "-"}, " +
                               $"Zinne {merlon?.Prefab ?? "-"}, Treppe {stair?.Prefab ?? "-"} " +
                               (stair != null ? $"({stair.Rise:0.##} hoch, {stair.Run:0.##} lang)" : ""));

            int rows = Math.Max(1, (int)Math.Round(6f / wall.Height));
            float levelH = rows * wall.Height;

            // ---- Haupthalle
            Box(hall, 16f, 12f, floorTop, 1, rows, floor, wall, pillar, merlon, Side.East, null, 0f);
            Decorate(hall, floorTop + surface);
            spawnPortal(new Vector3(hall.x, floorTop + surface, hall.z - 6f + 1.6f), 0f, "Magierhaus");

            // ---- Portalturm: Etagen nach Bedarf, verbunden durch Treppen an der Südwand
            int levels = 1;
            while (levels < 12 && TowerCapacity(levels) < targets.Count) levels++;

            var stairPlan = stair != null ? new StairPlan(stair, levelH) : null;
            Box(tower, 16f, 16f, floorTop, levels, rows, floor, wall, pillar, merlon, Side.West, stairPlan, surface);

            string[] torches = { "piece_groundtorch_blue", "piece_groundtorch_green", "piece_groundtorch", "piece_groundtorch_mist" };
            int next = 0;
            for (int lv = 0; lv < levels; lv++)
            {
                float y = floorTop + lv * levelH;
                float walk = y + surface;

                // Licht: jede Etage eine eigene Farbe (Ecken im Norden, im Süden neben der Treppe)
                string torch = torches[lv % torches.Length];
                foreach (var (cx, cz) in new[] { (-6.6f, 6.6f), (6.6f, 6.6f), (-7.2f, -6.2f), (7.2f, -6.2f) })
                    SpawnDecor(new[] { torch, "piece_groundtorch" }, new Vector3(tower.x + cx, walk, tower.z + cz), 0f);

                if (stairPlan != null && lv < levels - 1) stairPlan.Place(tower, y, lv);

                foreach (var slot in LevelSlots(tower, walk, lv))
                {
                    if (next >= targets.Count) break;
                    var target = targets[next++];
                    spawnPortal(slot.pos, slot.yaw, target.name);
                    spawnPortal(target.pos, 0f, target.name);
                }
            }

            // ---- Portal am Startpunkt
            spawnPortal(spawnPos, spawnYaw, "Magierhaus");

            Plugin.Log.LogInfo($"[Magierhaus] Gebaut bei {Fmt(Center)}: Halle + Turm mit {levels} Etagen, " +
                               $"{next} Ziel-Portale, {s_spawned} Bauteile.");
            return hall;
        }

        enum Side { North, East, South, West }

        static int TowerCapacity(int levels)
        {
            int cap = 0;
            for (int lv = 0; lv < levels; lv++) cap += LevelSlots(Vector3.zero, 0f, lv).Count();
            return cap;
        }

        /// <summary>
        /// Portalplätze je Etage: 3 an der Nordwand, 3 an der Ostwand, 3 an der Westwand
        /// (im Erdgeschoss ohne den mittleren, dort ist die Tür). Die Südwand gehört der Treppe.
        /// </summary>
        static IEnumerable<(Vector3 pos, float yaw)> LevelSlots(Vector3 c, float y, int lv)
        {
            const float half = 8f, inset = 1.5f;
            foreach (var o in new[] { -4.5f, 0f, 4.5f })
                yield return (new Vector3(c.x + o, y, c.z + half - inset), 180f);           // Nord
            foreach (var o in new[] { 4.6f, 1.2f, -2.2f })
                yield return (new Vector3(c.x + half - inset, y, c.z + o), 270f);           // Ost
            foreach (var o in new[] { 4.6f, 1.2f, -2.2f })
            {
                if (lv == 0 && Math.Abs(o) < 2.2f) continue;                                // Tür
                yield return (new Vector3(c.x - half + inset, y, c.z + o), 90f);            // West
            }
        }

        /// <summary>
        /// Gerade Treppen an der Südseite, auf zwei Bahnen: gerade Etagen direkt an der Wand nach Osten,
        /// ungerade eine Bahn weiter innen nach Westen. So liegt nie eine Treppe über dem Ausgang der
        /// Treppe darunter, und über dem oberen Ende jeder Treppe ist die Decke offen.
        /// </summary>
        sealed class StairPlan
        {
            readonly PieceBox _box;
            readonly int _steps;
            public readonly float Length;
            const float WallZ = -8f, Lane = 2f, Headroom = 6f;

            public StairPlan(PieceBox box, float levelH)
            {
                _box = box;
                _steps = Math.Max(1, (int)Math.Round(levelH / Math.Max(0.2f, box.Rise)));
                Length = Math.Min(14f, _steps * box.Run);
            }

            float Dir(int lv) => lv % 2 == 0 ? 1f : -1f;

            /// <summary>Mitte der Bahn (z relativ zur Turmmitte).</summary>
            public float LaneZ(int lv) => WallZ + Lane * (lv % 2 == 0 ? 0.5f : 1.5f);

            /// <summary>Offene Deckenfläche über dem oberen Ende der Treppe von Etage lv.</summary>
            public (float laneZ, float from, float to) Hole(int lv)
            {
                float dir = Dir(lv), end = dir * Length / 2f;
                float a = end - dir * Math.Min(Length, Headroom);
                return (LaneZ(lv), Math.Min(a, end), Math.Max(a, end));
            }

            public void Place(Vector3 c, float y, int lv)
            {
                float dir = Dir(lv), start = -dir * Length / 2f;
                float run = Length / _steps;
                for (int k = 0; k < _steps; k++)
                {
                    float x = c.x + start + dir * run * (k + 0.5f);
                    _box.PlaceStair(new Vector3(x, y + k * _box.Rise, c.z + LaneZ(lv)), dir);
                }
            }
        }

        /// <summary>Rechteckiger Bau mit Böden, Wänden, Ecksäulen, Decken je Etage und Zinnen auf dem Dach.</summary>
        static void Box(Vector3 c, float w, float d, float floorTop, int levels, int rows,
            PieceBox floor, PieceBox wall, PieceBox pillar, PieceBox merlon, Side doorSide, StairPlan stairs, float surface)
        {
            float levelH = rows * wall.Height;
            for (int lv = 0; lv <= levels; lv++)
            {
                float y = floorTop + lv * levelH;
                // Boden bzw. Decke; über der Treppe der Etage darunter bleibt die Decke offen.
                (float laneZ, float from, float to)? hole = stairs != null && lv > 0 && lv < levels ? stairs.Hole(lv - 1) : ((float, float, float)?)null;
                Tiles(c, w, d, y, floor, hole);
                if (lv == levels) break;

                for (int r = 0; r < rows; r++)
                {
                    float wy = y + r * wall.Height;
                    bool doorRow = lv == 0 && r * wall.Height < 3.9f;
                    WallLine(c, w, d, wy, Side.North, wall, doorRow && doorSide == Side.North);
                    WallLine(c, w, d, wy, Side.South, wall, doorRow && doorSide == Side.South);
                    WallLine(c, w, d, wy, Side.East, wall, doorRow && doorSide == Side.East);
                    WallLine(c, w, d, wy, Side.West, wall, doorRow && doorSide == Side.West);
                }
            }

            float total = levels * levelH;
            if (pillar != null && pillar.Height > 0.1f)
            {
                foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                    for (float y = 0f; y < total + 0.01f; y += pillar.Height)
                        pillar.Place(new Vector3(c.x + sx * (w / 2f + 0.3f), floorTop + y, c.z + sz * (d / 2f + 0.3f)), 0f);
            }

            if (merlon != null)
            {
                float y = floorTop + total;
                float step = Math.Max(merlon.Width, 1f) * 2f;
                for (float x = -w / 2f + merlon.Width / 2f; x < w / 2f; x += step)
                {
                    merlon.Place(new Vector3(c.x + x, y, c.z + d / 2f), 0f);
                    merlon.Place(new Vector3(c.x + x, y, c.z - d / 2f), 0f);
                }
                for (float z = -d / 2f + merlon.Width / 2f; z < d / 2f; z += step)
                {
                    merlon.Place(new Vector3(c.x + w / 2f, y, c.z + z), 90f);
                    merlon.Place(new Vector3(c.x - w / 2f, y, c.z + z), 90f);
                }
                // Feuerschalen auf dem Dach
                foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                    SpawnDecor(new[] { "piece_brazierfloor02", "piece_brazierfloor01" },
                        new Vector3(c.x + sx * (w / 2f - 1.5f), y + surface, c.z + sz * (d / 2f - 1.5f)), 0f);
            }
        }

        static void Tiles(Vector3 c, float w, float d, float y, PieceBox floor, (float laneZ, float from, float to)? hole)
        {
            float t = floor.Width;
            int nx = Math.Max(1, (int)Math.Round(w / t)), nz = Math.Max(1, (int)Math.Round(d / t));
            for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
            {
                float lx = -w / 2f + t * (ix + 0.5f), lz = -d / 2f + t * (iz + 0.5f);
                // Treppenloch über dem oberen Ende der Treppe darunter (nur in deren Bahn)
                if (hole.HasValue && Math.Abs(lz - hole.Value.laneZ) < t / 2f &&
                    lx + t / 2f > hole.Value.from + 0.01f && lx - t / 2f < hole.Value.to - 0.01f) continue;
                floor.Place(new Vector3(c.x + lx, y, c.z + lz), 0f);
            }
        }

        static void WallLine(Vector3 c, float w, float d, float y, Side side, PieceBox wall, bool door)
        {
            bool alongX = side == Side.North || side == Side.South;
            float len = alongX ? w : d;
            int n = Math.Max(1, (int)Math.Round(len / wall.Width));
            float seg = len / n;
            for (int i = 0; i < n; i++)
            {
                float o = -len / 2f + seg * (i + 0.5f);
                if (door && Math.Abs(o) < 2.1f) continue; // Durchgang in der Mitte
                Vector3 p;
                switch (side)
                {
                    case Side.North: p = new Vector3(c.x + o, y, c.z + d / 2f); break;
                    case Side.South: p = new Vector3(c.x + o, y, c.z - d / 2f); break;
                    case Side.East: p = new Vector3(c.x + w / 2f, y, c.z + o); break;
                    default: p = new Vector3(c.x - w / 2f, y, c.z + o); break;
                }
                wall.Place(p, alongX ? 0f : 90f);
            }
        }

        /// <summary>Einrichtung der Halle.</summary>
        static void Decorate(Vector3 hall, float y)
        {
            SpawnDecor(new[] { "jute_carpet_blue", "jute_carpet", "rug_fur", "rug_wolf", "rug_deer" }, new Vector3(hall.x, y, hall.z), 0f);
            SpawnDecor(new[] { "piece_magetable" }, new Vector3(hall.x, y, hall.z + 3.5f), 180f);
            SpawnDecor(new[] { "piece_throne01", "piece_throne02", "piece_chair03", "piece_chair" }, new Vector3(hall.x - 6.4f, y, hall.z), 90f);
            foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                SpawnDecor(new[] { "piece_brazierfloor02", "piece_brazierfloor01", "piece_groundtorch_blue" },
                    new Vector3(hall.x + sx * 6.3f, y, hall.z + sz * 4.3f), 0f);
            foreach (var sz in new[] { -2.6f, 2.6f })
                SpawnDecor(new[] { "piece_groundtorch_blue", "piece_groundtorch_green", "piece_groundtorch" },
                    new Vector3(hall.x + 7f, y, hall.z + sz), 0f);
        }

        // ------------------------------------------------------------ Hilfen

        static readonly Dictionary<string, PieceBox> s_boxes = new Dictionary<string, PieceBox>();
        static int s_spawned;

        static PieceBox Pick(string[] names, bool needWidth)
        {
            foreach (var n in names)
            {
                if (!s_boxes.TryGetValue(n, out var box)) s_boxes[n] = box = PieceBox.Of(n);
                if (box != null && (!needWidth || box.Width >= 0.5f)) return box;
            }
            return null;
        }

        static void SpawnDecor(string[] names, Vector3 pos, float yaw)
        {
            foreach (var n in names)
            {
                if (ZNetScene.instance.GetPrefab(n) == null) continue;
                Spawn(n, pos, Quaternion.Euler(0f, yaw, 0f));
                return;
            }
            Plugin.Log.LogInfo($"[Magierhaus] Kein Bauteil gefunden aus: {string.Join(", ", names)}");
        }

        /// <summary>Legt ein Bauteil als Weltobjekt an; markiert es als geschützt.</summary>
        public static ZDO Spawn(string prefabName, Vector3 pos, Quaternion rot)
        {
            var prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null) return null;
            int hash = prefab.name.GetStableHashCode();
            var zdo = ZDOMan.instance.CreateNewZDO(pos, hash);
            zdo.SetPrefab(hash);
            zdo.SetRotation(rot);
            var view = prefab.GetComponent<ZNetView>();
            if (view != null)
            {
                zdo.Persistent = view.m_persistent;
                zdo.Type = view.m_type;
                zdo.Distant = view.m_distant;
            }
            zdo.Set(BuildMarker, true);
            var fire = prefab.GetComponent<Fireplace>();
            if (fire != null) zdo.Set(ZDOVars.s_fuel, fire.m_maxFuel);
            s_spawned++;
            return zdo;
        }

        public static bool IsProtected(Component c)
        {
            var view = c != null ? c.GetComponent<ZNetView>() : null;
            var zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            return zdo != null && zdo.GetBool(BuildMarker);
        }

        static string Fmt(Vector3 v) => $"({v.x:0}, {v.y:0}, {v.z:0})";
    }

    /// <summary>Maße eines Bauteils aus seinen Andockpunkten, damit Teile sauber aneinander passen.</summary>
    internal sealed class PieceBox
    {
        public string Prefab;
        public Vector3 Min, Max;
        public bool AlongX;
        public float Width => AlongX ? Max.x - Min.x : Max.z - Min.z;
        public float Height => Max.y - Min.y;

        /// <summary>Höhe der begehbaren Oberfläche über der untersten Andock-Ebene (aus den Kollidern).</summary>
        public float SurfaceAbove;
        /// <summary>Treppe: Stufenhöhe, Länge und Breite; Richtung des Anstiegs in lokaler z (+1/-1).</summary>
        public float Rise => Height;
        public float Run, StairWidth;
        public float RiseDirZ = 1f;

        public static PieceBox Of(string prefabName)
        {
            var prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null) return null;
            var pts = prefab.GetComponentsInChildren<Transform>(true)
                .Where(t => t.CompareTag("snappoint"))
                .Select(t => prefab.transform.InverseTransformPoint(t.position))
                .ToList();
            if (pts.Count == 0) return null;
            var box = new PieceBox
            {
                Prefab = prefabName,
                Min = new Vector3(pts.Min(p => p.x), pts.Min(p => p.y), pts.Min(p => p.z)),
                Max = new Vector3(pts.Max(p => p.x), pts.Max(p => p.y), pts.Max(p => p.z)),
            };
            box.AlongX = box.Max.x - box.Min.x >= box.Max.z - box.Min.z;

            // Oberfläche: höchster Punkt der Kollider (z. B. Dicke einer Bodenplatte).
            float top = float.MinValue;
            foreach (var col in prefab.GetComponentsInChildren<Collider>(true))
            {
                Bounds b;
                if (col is BoxCollider bc) b = new Bounds(bc.center, bc.size);
                else if (col is MeshCollider mc && mc.sharedMesh != null) b = mc.sharedMesh.bounds;
                else continue;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    top = Math.Max(top, prefab.transform.InverseTransformPoint(col.transform.TransformPoint(corner)).y);
                }
            }
            box.SurfaceAbove = top > float.MinValue ? Math.Max(0f, top - box.Min.y) : 0f;

            // Treppenmaße: Breite entlang x, Länge entlang z; wo liegen die oberen Andockpunkte?
            box.StairWidth = Math.Max(0.5f, box.Max.x - box.Min.x);
            box.Run = box.Max.z - box.Min.z;
            if (box.Run < 0.2f) box.Run = Math.Max(0.5f, box.Height);
            var high = pts.Where(p => p.y > box.Max.y - 0.05f).ToList();
            var low = pts.Where(p => p.y < box.Min.y + 0.05f).ToList();
            if (high.Count > 0 && low.Count > 0)
                box.RiseDirZ = high.Average(p => p.z) >= low.Average(p => p.z) ? 1f : -1f;
            return box;
        }

        /// <summary>Setzt das Teil so, dass die Mitte seiner Andockpunkte über <paramref name="center"/> liegt
        /// und die unterste Andockebene auf Höhe center.y. yaw = 0 heißt: Breite entlang der x-Achse.</summary>
        /// <summary>Setzt ein Treppenstück so, dass es in Richtung <paramref name="dirX"/> (+1 Osten, -1 Westen)
        /// ansteigt; <paramref name="center"/> ist die Mitte des Stücks auf Höhe seiner Unterkante.</summary>
        public void PlaceStair(Vector3 center, float dirX)
        {
            // lokale +z (mal RiseDirZ) soll in Welt-Richtung (dirX, 0, 0) zeigen
            float yaw = dirX * RiseDirZ > 0f ? 90f : 270f;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var localMid = new Vector3((Min.x + Max.x) / 2f, Min.y, (Min.z + Max.z) / 2f);
            MageTower.Spawn(Prefab, center - rot * localMid, rot);
        }

        public void Place(Vector3 center, float yaw)
        {
            if (!AlongX) yaw += 90f;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var localMid = new Vector3((Min.x + Max.x) / 2f, Min.y, (Min.z + Max.z) / 2f);
            MageTower.Spawn(Prefab, center - rot * localMid, rot);
        }
    }

    // ------------------------------------------------------------ Schutz der Bauten

    /// <summary>Unsere Bauten brauchen keine Stütze (sonst stürzen hohe Türme ein).</summary>
    [HarmonyPatch(typeof(WearNTear), "HaveSupport")]
    static class Patch_WntSupport
    {
        static bool Prefix(WearNTear __instance, ref bool __result)
        {
            PerfMonitor.SupportCalls++;
            if (!State.Active || !MageTower.IsProtected(__instance)) return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(WearNTear), "UpdateSupport")]
    static class Patch_WntUpdateSupport
    {
        static bool Prefix(WearNTear __instance) => !(State.Active && MageTower.IsProtected(__instance));
    }

    /// <summary>Unsere Bauten nehmen keinen Schaden (Monster, Wetter).</summary>
    [HarmonyPatch(typeof(WearNTear), "RPC_Damage")]
    static class Patch_WntDamage
    {
        static bool Prefix(WearNTear __instance) => !(State.Active && MageTower.IsProtected(__instance));
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.ApplyDamage))]
    static class Patch_WntApplyDamage
    {
        static bool Prefix(WearNTear __instance, ref bool __result)
        {
            if (!State.Active || !MageTower.IsProtected(__instance)) return true;
            __result = false;
            return false;
        }
    }

    /// <summary>Fackeln und Feuerschalen des Magierhauses brennen ewig.</summary>
    [HarmonyPatch(typeof(Fireplace), "Awake")]
    static class Patch_FireplaceAwake
    {
        static void Postfix(Fireplace __instance)
        {
            if (State.Active && MageTower.IsProtected(__instance)) __instance.m_infiniteFuel = true;
        }
    }

    /// <summary>Keine Strukturen (Ruinen, Lager …) auf dem Tafelberg.</summary>
    [HarmonyPatch(typeof(ZoneSystem), "RegisterLocation")]
    static class Patch_BlockLocationsOnMesa
    {
        static bool Prefix(ZoneSystem.ZoneLocation location, ref Vector3 pos)
        {
            if (!State.Active || location == null) return true;
            if (location.m_prefabName == "StartTemple")
            {
                // Planetos: Startpunkt auf den eingeebneten Platz in Westeros
                if (Planetos.Enabled)
                {
                    var c = Patch_SpawnPlaza.Center;
                    pos = new Vector3(c.x, WorldGenerator.instance.GetHeight(c.x, c.z), c.z);
                    Plugin.Log.LogInfo($"[Planetos] Startpunkt nach Westeros verlegt: ({pos.x:0}, {pos.y:0}, {pos.z:0})");
                }
                return true;
            }
            // Planetos: auf der Mauer entsteht nichts
            if (Planetos.Enabled && Planetos.IsOnWall(pos.x, pos.z, location.m_exteriorRadius + 10f)) return false;
            if (!Plugin.MageHouse.Value) return true;
            float dx = pos.x - MageTower.Center.x, dz = pos.z - MageTower.Center.z;
            float r = MageTower.BaseRadius + 20f + location.m_exteriorRadius;
            return dx * dx + dz * dz > r * r;
        }
    }
}
