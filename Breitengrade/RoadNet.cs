using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Breitengrade
{
    /// <summary>
    /// Planetos: Straßen zwischen den Städten und Gassen in den Städten, direkt in die Geländetextur
    /// gemalt (keine Bauteile, kein Lag): Feldwege als Erde, die großen Königsstraßen nach King's Landing
    /// als Pflaster. In den Städten: Hauptstraßen, Platz, Gassen zwischen den Häusern, Gärten.
    /// An den Stadtausgängen und alle paar Kilometer stehen Wegweiser mit Richtung und Entfernung.
    /// </summary>
    public static class RoadNet
    {
        // ---------------------------------------------------------------- Malen (Gelände-Threads)

        /// <summary>Wegstück; Half = halbe Breite (Meter).</summary>
        public struct Seg { public float Ax, Az, Bx, Bz, Half; public bool Paved; }

        /// <summary>Fläche in einer Stadt: Kind 0 = Garten (Rechteck), 1 = Platz (Kreis, Radius HX), 2 = Pflasterfläche (Rechteck).</summary>
        public struct Area { public float X, Z, HX, HZ, Yaw; public int Kind; }

        const float GridCell = 64f;
        static volatile Dictionary<long, Seg[]> s_grid = new Dictionary<long, Seg[]>();
        static volatile Dictionary<long, Area[]> s_areas = new Dictionary<long, Area[]>();
        static readonly List<Seg> s_citySegs = new List<Seg>();
        static readonly List<Area> s_cityAreas = new List<Area>();

        static readonly Color Dirt = new Color(1f, 0f, 0f, 0f);
        static readonly Color Paved = new Color(0f, 0f, 1f, 0f);
        static readonly Color Cultivated = new Color(0f, 1f, 0f, 0f);

        public static void OnWorldChanged()
        {
            s_grid = new Dictionary<long, Seg[]>();
            s_areas = new Dictionary<long, Area[]>();
            s_citySegs.Clear();
            s_cityAreas.Clear();
        }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        /// <summary>Straßen und Flächen der Städte (vom Stadtbau-Modul) übernehmen.</summary>
        public static void SetCityLayout(List<Seg> segs, List<Area> areas)
        {
            s_citySegs.Clear(); s_citySegs.AddRange(segs);
            s_cityAreas.Clear(); s_cityAreas.AddRange(areas);
            BuildGrid();
        }

        /// <summary>Färbt die Geländemaske an dieser Stelle (Straße, Gasse, Platz, Garten).</summary>
        public static void Paint(float x, float z, ref Color mask)
        {
            int gx = (int)Math.Floor(x / GridCell), gz = (int)Math.Floor(z / GridCell);
            long key = Key(gx, gz);
            var areas = s_areas;
            if (areas.TryGetValue(key, out var al))
            {
                foreach (var a in al)
                {
                    float dx = x - a.X, dz = z - a.Z;
                    if (a.Kind == 1)
                    {
                        float d = (float)Math.Sqrt(dx * dx + dz * dz);
                        if (d < a.HX) { Blend(ref mask, Paved, Edge(a.HX - d)); return; }
                        continue;
                    }
                    float rad = -a.Yaw * Mathf.Deg2Rad;
                    float lx = dx * (float)Math.Cos(rad) - dz * (float)Math.Sin(rad);
                    float lz = dx * (float)Math.Sin(rad) + dz * (float)Math.Cos(rad);
                    float inside = Math.Min(a.HX - Math.Abs(lx), a.HZ - Math.Abs(lz));
                    if (inside > 0f) { Blend(ref mask, a.Kind == 0 ? Cultivated : Paved, Edge(inside)); return; }
                }
            }
            var grid = s_grid;
            if (!grid.TryGetValue(key, out var segs)) return;
            float best = float.MaxValue;
            bool paved = false;
            foreach (var s in segs)
            {
                float dd = DistSeg(x, z, s) - s.Half;
                if (dd < best) { best = dd; paved = s.Paved; }
            }
            if (best < 0.8f) Blend(ref mask, paved ? Paved : Dirt, Edge(-best + 0.8f));
        }

        static float Edge(float inside) => Mathf.Clamp01(inside / 1.2f);

        static void Blend(ref Color mask, Color target, float t)
        {
            if (t <= 0f) return;
            mask = Color.Lerp(mask, target, t);
        }

        static float DistSeg(float x, float z, Seg s)
        {
            float vx = s.Bx - s.Ax, vz = s.Bz - s.Az;
            float wx = x - s.Ax, wz = z - s.Az;
            float l2 = vx * vx + vz * vz;
            float t = l2 > 0f ? Mathf.Clamp01((wx * vx + wz * vz) / l2) : 0f;
            float px = s.Ax + vx * t - x, pz = s.Az + vz * t - z;
            return (float)Math.Sqrt(px * px + pz * pz);
        }

        /// <summary>Art einer Häuserzelle: 0 = Haus, 1 = Garten, 2 = frei. Gleich beim Malen und Bauen.</summary>
        public static int CellKind(int seed, int ix, int iz)
        {
            unchecked
            {
                uint h = (uint)(seed * 73856093) ^ (uint)(ix * 19349663) ^ (uint)(iz * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                float r = (h & 0xFFFF) / 65535f;
                return r < 0.14f ? 1 : r < 0.2f ? 2 : 0;
            }
        }

        // ---------------------------------------------------------------- Planen

        public class Road
        {
            public string A, B;
            public bool Paved;
            public List<Vector3> Points = new List<Vector3>();
            public float Length;
        }

        public static readonly List<Road> Roads = new List<Road>();

        /// <summary>Die großen Straßen nach King's Landing (gepflastert).</summary>
        static readonly (string, string)[] KingsRoads =
        {
            ("King's Landing", "The Twins"), ("The Twins", "The Neck"), ("The Neck", "Winterfell"),
            ("Winterfell", "Castle Black"), ("King's Landing", "Highgarden"), ("Highgarden", "Oldtown"),
            ("King's Landing", "Lannisport"), ("King's Landing", "Storm's End"), ("King's Landing", "Harrenhal"),
        };

        /// <summary>Weitere bekannte Straßen (Feldwege).</summary>
        static readonly (string, string)[] OtherRoads =
        {
            ("Lannisport", "Riverrun"), ("Riverrun", "The Twins"), ("Riverrun", "The Eyrie"), ("The Eyrie", "Harrenhal"),
            ("Winterfell", "White Harbor"), ("Winterfell", "Deepwood Motte"), ("Winterfell", "The Dreadfort"),
            ("The Dreadfort", "Karhold"), ("Highgarden", "Sunspear"), ("Storm's End", "Sunspear"),
            ("Castle Black", "Hardhome"), ("Castle Black", "Thenn"),
        };

        public static void Plan(IReadOnlyList<CityBuilder.Plan> plans, string saveFile)
        {
            Roads.Clear();
            var t0 = DateTime.Now;
            bool loaded = Load(saveFile);
            var byName = plans.ToDictionary(p => p.Name);
            if (!loaded) Compute(plans, byName);
            if (!loaded) Save(saveFile);
            BuildGrid();
            Plugin.Log.LogInfo($"[Straßen] {Roads.Count} Straßen, {Roads.Sum(r => r.Length) / 1000f:0.0} km" +
                               $"{(loaded ? " (gespeichert)" : "")} ({(DateTime.Now - t0).TotalSeconds:0.0} s).");
        }

        static void Compute(IReadOnlyList<CityBuilder.Plan> plans, Dictionary<string, CityBuilder.Plan> byName)
        {
            var pairs = new List<(string a, string b, bool paved)>();
            foreach (var (a, b) in KingsRoads) pairs.Add((a, b, true));
            foreach (var (a, b) in OtherRoads) pairs.Add((a, b, false));
            // Jede Stadt mit ihren zwei nächsten Nachbarn verbinden (falls über Land erreichbar)
            foreach (var p in plans)
            {
                var near = plans.Where(q => q != p)
                    .OrderBy(q => Dist(p, q)).Take(2)
                    .Where(q => Dist(p, q) < 5200f * WorldScale.S);
                foreach (var q in near) pairs.Add((p.Name, q.Name, false));
            }
            var done = new HashSet<string>();
            foreach (var (a, b, paved) in pairs)
            {
                if (!byName.TryGetValue(a, out var pa) || !byName.TryGetValue(b, out var pb)) continue;
                string key = string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;
                if (!done.Add(key)) continue;
                var road = Route(pa, pb, plans);
                if (road == null) continue;
                road.Paved = paved;
                Roads.Add(road);
            }

        }

        static void BuildGrid()
        {
            var grid = new Dictionary<long, List<Seg>>();
            foreach (var r in Roads)
                for (int i = 1; i < r.Points.Count; i++)
                {
                    var a = r.Points[i - 1]; var b = r.Points[i];
                    var s = new Seg { Ax = a.x, Az = a.z, Bx = b.x, Bz = b.z, Paved = r.Paved, Half = r.Paved ? 3f : 2f };
                    float pad = 5f;
                    int x0 = (int)Math.Floor((Math.Min(a.x, b.x) - pad) / GridCell), x1 = (int)Math.Floor((Math.Max(a.x, b.x) + pad) / GridCell);
                    int z0 = (int)Math.Floor((Math.Min(a.z, b.z) - pad) / GridCell), z1 = (int)Math.Floor((Math.Max(a.z, b.z) + pad) / GridCell);
                    for (int gx = x0; gx <= x1; gx++)
                    for (int gz = z0; gz <= z1; gz++)
                    {
                        if (!grid.TryGetValue(Key(gx, gz), out var list)) grid[Key(gx, gz)] = list = new List<Seg>();
                        list.Add(s);
                    }
                }
            foreach (var s in s_citySegs)
            {
                float pad = s.Half + 2f;
                int x0 = (int)Math.Floor((Math.Min(s.Ax, s.Bx) - pad) / GridCell), x1 = (int)Math.Floor((Math.Max(s.Ax, s.Bx) + pad) / GridCell);
                int z0 = (int)Math.Floor((Math.Min(s.Az, s.Bz) - pad) / GridCell), z1 = (int)Math.Floor((Math.Max(s.Az, s.Bz) + pad) / GridCell);
                for (int gx = x0; gx <= x1; gx++)
                for (int gz = z0; gz <= z1; gz++)
                {
                    if (!grid.TryGetValue(Key(gx, gz), out var list)) grid[Key(gx, gz)] = list = new List<Seg>();
                    list.Add(s);
                }
            }
            var areas = new Dictionary<long, List<Area>>();
            foreach (var a in s_cityAreas)
            {
                float pad = Math.Max(a.HX, a.HZ) * 1.5f + 2f;
                for (int gx = (int)Math.Floor((a.X - pad) / GridCell); gx <= (int)Math.Floor((a.X + pad) / GridCell); gx++)
                for (int gz = (int)Math.Floor((a.Z - pad) / GridCell); gz <= (int)Math.Floor((a.Z + pad) / GridCell); gz++)
                {
                    if (!areas.TryGetValue(Key(gx, gz), out var list)) areas[Key(gx, gz)] = list = new List<Area>();
                    list.Add(a);
                }
            }
            s_areas = areas.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
            s_grid = grid.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
        }

        static void Save(string file)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            System.IO.File.WriteAllLines(file, Roads.Select(r =>
                $"{r.A};{r.B};{(r.Paved ? 1 : 0)};" + string.Join(" ", r.Points.Select(p => string.Format(inv, "{0:0.0},{1:0.0}", p.x, p.z)))));
        }

        static bool Load(string file)
        {
            if (!System.IO.File.Exists(file)) return false;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var ns = System.Globalization.NumberStyles.Float;
            foreach (var line in System.IO.File.ReadAllLines(file))
            {
                var p = line.Split(';');
                if (p.Length < 4) continue;
                var road = new Road { A = p[0], B = p[1], Paved = p[2] == "1" };
                foreach (var xy in p[3].Split(' '))
                {
                    var c = xy.Split(',');
                    if (c.Length == 2 && float.TryParse(c[0], ns, inv, out float x) && float.TryParse(c[1], ns, inv, out float z))
                        road.Points.Add(new Vector3(x, 0f, z));
                }
                for (int i = 1; i < road.Points.Count; i++) road.Length += Flat(road.Points[i] - road.Points[i - 1]).magnitude;
                if (road.Points.Count > 1) Roads.Add(road);
            }
            return true;
        }

        static float Dist(CityBuilder.Plan a, CityBuilder.Plan b) => Mathf.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

        /// <summary>Stadtausgang Richtung Ziel: das passende Tor (Hauptstraßen gehen in die vier Himmelsrichtungen).</summary>
        public static Vector3 Gate(CityBuilder.Plan p, Vector3 toward)
        {
            var d = new Vector3(toward.x - p.X, 0f, toward.z - p.Z);
            Vector3 dir = Math.Abs(d.x) > Math.Abs(d.z) ? new Vector3(Math.Sign(d.x), 0f, 0f) : new Vector3(0f, 0f, Math.Sign(d.z));
            return new Vector3(p.X, p.H, p.Z) + dir * (p.R + 8f);
        }

        // ---------------------------------------------------------------- Wegsuche

        const float Step = 40f;

        static Road Route(CityBuilder.Plan pa, CityBuilder.Plan pb, IReadOnlyList<CityBuilder.Plan> plans)
        {
            var ga = Gate(pa, new Vector3(pb.X, 0f, pb.Z));
            var gb = Gate(pb, new Vector3(pa.X, 0f, pa.Z));
            float margin = 1800f;
            float minX = Math.Min(ga.x, gb.x) - margin, maxX = Math.Max(ga.x, gb.x) + margin;
            float minZ = Math.Min(ga.z, gb.z) - margin, maxZ = Math.Max(ga.z, gb.z) + margin;
            int w = (int)((maxX - minX) / Step) + 1, h = (int)((maxZ - minZ) / Step) + 1;
            if ((long)w * h > 900000) return null;

            var castle = plans.FirstOrDefault(p => p.Name == "Castle Black");
            var others = plans.Where(p => p != pa && p != pb).ToList();
            float edge = 9300f * WorldScale.S;

            float Cost(int ix, int iz)
            {
                float x = minX + ix * Step, z = minZ + iz * Step;
                if (x * x + z * z > edge * edge) return -1f;
                if (Latitude.CoastDistance(x, z) > -12f) return -1f;
                if (Planetos.IsOnWall(x, z, 20f) && !(castle != null && Math.Abs(x - castle.X) < 30f)) return -1f;
                float c = 1f + 5f * Latitude.MountainFactor(x, z);
                foreach (var o in others)
                    if ((x - o.X) * (x - o.X) + (z - o.Z) * (z - o.Z) < (o.R + 10f) * (o.R + 10f)) { c += 20f; break; }
                return c;
            }

            int sx = Mathf.Clamp((int)((ga.x - minX) / Step), 0, w - 1), sz = Mathf.Clamp((int)((ga.z - minZ) / Step), 0, h - 1);
            int tx = Mathf.Clamp((int)((gb.x - minX) / Step), 0, w - 1), tz = Mathf.Clamp((int)((gb.z - minZ) / Step), 0, h - 1);
            var gCost = new float[w * h];
            var from = new int[w * h];
            var costCache = new float[w * h];
            for (int i = 0; i < gCost.Length; i++) { gCost[i] = float.MaxValue; from[i] = -1; costCache[i] = float.NaN; }
            var heap = new MinHeap();
            int start = sz * w + sx, goal = tz * w + tx;
            gCost[start] = 0f;
            heap.Push(start, 0f);
            int expanded = 0;
            while (heap.Count > 0)
            {
                int cur = heap.Pop();
                if (cur == goal) break;
                if (++expanded > 400000) return null;
                int cx = cur % w, cz = cur / w;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = cx + dx, nz = cz + dz;
                    if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                    int ni = nz * w + nx;
                    if (float.IsNaN(costCache[ni])) costCache[ni] = (ni == goal) ? 1f : Cost(nx, nz);
                    float c = costCache[ni];
                    if (c < 0f) continue;
                    float ng = gCost[cur] + c * Step * (dx != 0 && dz != 0 ? 1.4142f : 1f);
                    if (ng >= gCost[ni]) continue;
                    gCost[ni] = ng;
                    from[ni] = cur;
                    float hx = (tx - nx) * Step, hz = (tz - nz) * Step;
                    heap.Push(ni, ng + (float)Math.Sqrt(hx * hx + hz * hz));
                }
            }
            if (from[goal] < 0 && goal != start) return null;

            var pts = new List<Vector3>();
            for (int i = goal; i >= 0; i = from[i])
            {
                pts.Add(new Vector3(minX + (i % w) * Step, 0f, minZ + (i / w) * Step));
                if (i == start) break;
            }
            pts.Reverse();
            pts[0] = ga; pts[pts.Count - 1] = gb;
            pts.Insert(0, new Vector3(pa.X, 0f, pa.Z) + (ga - new Vector3(pa.X, ga.y, pa.Z)).normalized * (pa.R - 2f));
            pts.Add(new Vector3(pb.X, 0f, pb.Z) + (gb - new Vector3(pb.X, gb.y, pb.Z)).normalized * (pb.R - 2f));
            // Glätten (Chaikin), danach leicht schlängeln
            for (int it = 0; it < 3; it++) pts = Chaikin(pts);
            var road = new Road { A = pa.Name, B = pb.Name, Points = pts };
            for (int i = 1; i < pts.Count; i++) road.Length += Flat(pts[i] - pts[i - 1]).magnitude;
            return road;
        }

        static List<Vector3> Chaikin(List<Vector3> p)
        {
            if (p.Count < 3) return p;
            var r = new List<Vector3> { p[0] };
            for (int i = 0; i < p.Count - 1; i++)
            {
                r.Add(Vector3.Lerp(p[i], p[i + 1], 0.25f));
                r.Add(Vector3.Lerp(p[i], p[i + 1], 0.75f));
            }
            r.Add(p[p.Count - 1]);
            return r;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        sealed class MinHeap
        {
            readonly List<(int id, float p)> a = new List<(int, float)>();
            public int Count => a.Count;
            public void Push(int id, float p)
            {
                a.Add((id, p));
                int i = a.Count - 1;
                while (i > 0)
                {
                    int par = (i - 1) / 2;
                    if (a[par].p <= a[i].p) break;
                    (a[par], a[i]) = (a[i], a[par]);
                    i = par;
                }
            }
            public int Pop()
            {
                int top = a[0].id;
                a[0] = a[a.Count - 1];
                a.RemoveAt(a.Count - 1);
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < a.Count && a[l].p < a[m].p) m = l;
                    if (r < a.Count && a[r].p < a[m].p) m = r;
                    if (m == i) break;
                    (a[m], a[i]) = (a[i], a[m]);
                    i = m;
                }
                return top;
            }
        }

        // ---------------------------------------------------------------- Wegweiser

        /// <summary>Wegweiser an den Stadtausgängen und alle ~1,5 km entlang der Straßen.</summary>
        public static int BuildSigns()
        {
            int n = 0;
            foreach (var r in Roads)
            {
                if (r.Points.Count < 2) continue;
                // Positionen entlang des Weges (Abstand vom Anfang)
                var marks = new List<float> { 18f };
                for (float s = 1500f; s < r.Length - 600f; s += 1500f) marks.Add(s);
                marks.Add(r.Length - 18f);
                foreach (float s in marks)
                {
                    if (!PointAt(r, s, out Vector3 p, out Vector3 dir)) continue;
                    var side = new Vector3(dir.z, 0f, -dir.x);
                    var post = p + side * 3.6f;
                    if (Latitude.CoastDistance(post.x, post.z) > -3f) continue;
                    float toB = r.Length - s, toA = s;
                    var boards = new List<(Vector3 dir, string text)>();
                    if (toB > 40f) boards.Add((dir, $"{r.B}  {Km(toB)}"));
                    if (toA > 40f) boards.Add((-dir, $"{r.A}  {Km(toA)}"));
                    if (boards.Count == 0) continue;
                    if (SignPost(post, boards)) n++;
                }
            }
            return n;
        }

        static string Km(float m) => m >= 1000f ? $"{m / 1000f:0.0} km".Replace('.', ',') : $"{m:0} m";

        static bool PointAt(Road r, float s, out Vector3 p, out Vector3 dir)
        {
            float acc = 0f;
            for (int i = 1; i < r.Points.Count; i++)
            {
                var a = Flat(r.Points[i - 1]); var b = Flat(r.Points[i]);
                float l = (b - a).magnitude;
                if (acc + l >= s && l > 0.01f)
                {
                    p = Vector3.Lerp(a, b, (s - acc) / l);
                    dir = (b - a) / l;
                    return true;
                }
                acc += l;
            }
            p = Vector3.zero; dir = Vector3.forward;
            return false;
        }

        static Vector3 s_textFwd = Vector3.zero;

        /// <summary>Pfosten mit Schildern; jedes Schild zeigt mit der Pfeilseite in seine Richtung.</summary>
        static bool SignPost(Vector3 p, List<(Vector3 dir, string text)> boards)
        {
            var wg = WorldGenerator.instance;
            float y = wg.GetHeight(p.x, p.z);
            if (y < 30.3f) return false;
            var pole = ZNetScene.instance.GetPrefab("wood_pole2") != null ? "wood_pole2" : "wood_pole";
            var signPrefab = ZNetScene.instance.GetPrefab("sign");
            if (signPrefab == null) return false;
            if (s_textFwd == Vector3.zero)
            {
                var sg = signPrefab.GetComponent<Sign>();
                var tf = sg != null && sg.m_textWidget != null ? sg.m_textWidget.transform : null;
                s_textFwd = tf != null ? signPrefab.transform.InverseTransformDirection(tf.forward) : Vector3.forward;
            }
            for (float h = 0f; h < 3.2f; h += 1f) MageTower.Spawn(pole, new Vector3(p.x, y + h - 0.3f, p.z), Quaternion.identity);
            float hy = y + 2.6f;
            foreach (var (dir, text) in boards)
            {
                // Schild liegt in der Ebene von dir; Pfeil zeigt zur Seite, die für den Leser rechts ist
                float yaw = Mathf.Atan2(-dir.z, dir.x) * Mathf.Rad2Deg;
                var rot = Quaternion.Euler(0f, yaw, 0f);
                var view = rot * s_textFwd;
                var right = new Vector3(view.z, 0f, -view.x);
                bool arrowRight = Vector3.Dot(right, dir) > 0f;
                string label = arrowRight ? $"{text} >" : $"< {text}";
                var zdo = MageTower.Spawn("sign", new Vector3(p.x, hy, p.z) + dir * 0.75f, rot);
                if (zdo != null) zdo.Set(ZDOVars.s_text, label);
                hy -= 0.55f;
            }
            return true;
        }
    }
}
