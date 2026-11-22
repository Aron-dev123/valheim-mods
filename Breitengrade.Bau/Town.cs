using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Breitengrade.Bau
{
    /// <summary>
    /// Stadtgrundriss und Häuser:
    /// - Hauptstraßen von den Toren zum Platz, leicht geschwungen (nicht schnurgerade),
    ///   eine unregelmäßige Ringstraße und Seitengassen, die abzweigen.
    /// - Häuser stehen an den Straßen, mit der Tür zur Straße, dicht an dicht wie in einer
    ///   mittelalterlichen Stadt; dazwischen Hinterhöfe und Gärten.
    /// - Häuser: Eckpfosten (keine Lücken an den Ecken), große Türen, Fensterläden, je nach Ort
    ///   Steinsockel mit Holz-Obergeschoss, Zwischenboden, einfache Einrichtung.
    /// Der Grundriss ist für eine Stadt immer gleich (fester Zufall), damit Malen und Bauen passen.
    /// </summary>
    public static partial class Builder
    {
        public sealed class TownPlan
        {
            public readonly List<(Vector3 a, Vector3 b, float half, bool paved)> Streets = new List<(Vector3, Vector3, float, bool)>();
            public readonly List<Plot> Plots = new List<Plot>();
            public readonly List<RoadNet.Area> Areas = new List<RoadNet.Area>();
            public float Plaza = 12f;
        }

        public struct Plot
        {
            public Vector3 C;      // Mitte (Höhe = Stadt)
            public float Yaw;      // Drehung: lokale +z zeigt von der Straße weg, Tür auf lokal Süd
            public float W, D;     // Breite (entlang der Straße), Tiefe
            public int Kind;       // 0 Haus, 1 Garten, 2 Markt
        }

        static readonly Dictionary<string, TownPlan> s_towns = new Dictionary<string, TownPlan>();

        /// <summary>Breite eines Wandstücks je Material (ohne Spiel-Daten, für den Grundriss).</summary>
        static float WallWidthOf(Mat m) => m == Mat.Stone || m == Mat.Grausten ? 4f : 2f;

        // ---------------------------------------------------------------- Öffentliche Schnittstelle

        /// <summary>Gassen, Platz und Gärten einer Stadt zum Malen in die Geländetextur.</summary>
        public static void Streets(CityBuilder.Plan p, List<RoadNet.Seg> segs, List<RoadNet.Area> areas)
        {
            var t = Town(p);
            foreach (var (a, b, half, paved) in t.Streets)
                segs.Add(new RoadNet.Seg { Ax = a.x, Az = a.z, Bx = b.x, Bz = b.z, Half = half, Paved = paved });
            areas.AddRange(t.Areas);
        }

        /// <summary>Hügel in einer Stadt (z. B. die drei Hügel von King's Landing).</summary>
        public static void Hills(CityBuilder.Plan p, List<PointsOfInterest.Plateau> list)
        {
            foreach (var (pos, r, dh) in HillsOf(p))
                list.Add(new PointsOfInterest.Plateau { X = pos.x, Z = pos.z, R = r, H = p.H + dh, Fade = 26f });
        }

        static IEnumerable<(Vector3 pos, float r, float dh)> HillsOf(CityBuilder.Plan p)
        {
            var c = new Vector3(p.X, p.H, p.Z);
            switch (StyleOf(p).Mark)
            {
                case Mark.KingsLanding:
                    var (rk, sept, pit) = KingsLandingSpots(c, p.R);
                    yield return (rk, 34f, 12f);   // Aegon's High Hill (Red Keep)
                    yield return (sept, 24f, 7f);  // Visenya's Hill (Great Sept)
                    yield return (pit, 26f, 8f);   // Rhaenys's Hill (Dragonpit)
                    break;
                case Mark.Eyrie:
                    yield return (c, 26f, 22f);    // die Ehr auf dem Berg
                    break;
            }
        }

        public static float HillHeight(CityBuilder.Plan p, Vector3 at)
        {
            float best = 0f;
            foreach (var (pos, r, dh) in HillsOf(p))
                if (Flat(at - pos).magnitude < r) best = Math.Max(best, dh);
            return best;
        }

        public static (Vector3 redKeep, Vector3 sept, Vector3 pit) KingsLandingSpots(Vector3 c, float R) =>
            (c + new Vector3(R * 0.42f, 0f, -R * 0.40f), c + new Vector3(-R * 0.45f, 0f, R * 0.05f), c + new Vector3(R * 0.05f, 0f, R * 0.47f));

        static Style StyleOf(CityBuilder.Plan p)
        {
            var place = Planetos.Places.FirstOrDefault(q => q.name == p.Name);
            return Styles.TryGetValue(p.Name, out var st) ? st : DefaultStyle(place.biome);
        }

        // ---------------------------------------------------------------- Grundriss

        static TownPlan Town(CityBuilder.Plan p)
        {
            if (s_towns.TryGetValue(p.Name, out var cached)) return cached;
            var t = new TownPlan();
            s_towns[p.Name] = t;
            var s = StyleOf(p);
            var c = new Vector3(p.X, p.H, p.Z);
            float inner = Layout(p).inner;
            bool paved = Layout(p).paved;
            var rng = new System.Random(CityBuilder.Seed(p) ^ 0x2F6E);

            // Wahrzeichen trocken durchspielen: wo ist kein Platz für Häuser?
            var reserved = DryLandmarks(p, s, c);

            // Platz, Höfe der Wahrzeichen (gepflastert, ohne Bewuchs)
            t.Areas.Add(new RoadNet.Area { X = c.x, Z = c.z, HX = t.Plaza, Kind = 1 });
            foreach (var (rc, rr) in reserved)
                t.Areas.Add(new RoadNet.Area { X = rc.x, Z = rc.z, HX = rr * 0.85f, Kind = 1 });

            // Hauptstraßen (zu den vier Toren, auch ohne Mauer), leicht geschwungen
            var mains = new List<List<Vector3>>();
            for (int g = 0; g < 4; g++)
            {
                float a = -Mathf.PI / 2f + g * Mathf.PI / 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var side = new Vector3(-dir.z, 0f, dir.x);
                float len = p.R + 8f - t.Plaza;
                float amp = 5f + (float)rng.NextDouble() * 7f, f1 = 1f + (float)rng.NextDouble() * 1.5f, ph = (float)rng.NextDouble() * 6f;
                var line = new List<Vector3>();
                for (float d = 0f; d <= len + 0.1f; d += 8f)
                {
                    float u = d / len;
                    float off = amp * Mathf.Sin(Mathf.PI * u) * Mathf.Sin(u * Mathf.PI * f1 + ph);
                    line.Add(c + dir * (t.Plaza + d) + side * off);
                }
                mains.Add(line);
                AddLine(t, line, 2.8f, paved, reserved);
            }
            // Ringstraße
            if (s.Mark != Mark.Huts && s.Mark != Mark.Stilts)
            {
                float rr = inner * (0.5f + (float)rng.NextDouble() * 0.12f);
                var ring = new List<Vector3>();
                float ph1 = (float)rng.NextDouble() * 6f, ph2 = (float)rng.NextDouble() * 6f;
                for (int i = 0; i <= 28; i++)
                {
                    float a = i * 2f * Mathf.PI / 28f;
                    float r = rr * (1f + 0.12f * Mathf.Sin(a * 3f + ph1) + 0.06f * Mathf.Sin(a * 5f + ph2));
                    ring.Add(c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
                }
                mains.Add(ring);
                AddLine(t, ring, 2.2f, paved, reserved);
            }
            // Seitengassen
            var lanes = new List<List<Vector3>>();
            foreach (var line in mains.ToList())
                for (int i = 2; i < line.Count - 2; i += 3)
                {
                    if (rng.NextDouble() < 0.35) continue;
                    var a = line[i];
                    var dir = Flat(line[i + 1] - line[i - 1]).normalized;
                    float sgn = rng.NextDouble() < 0.5 ? -1f : 1f;
                    float turn = (80f + (float)rng.NextDouble() * 20f) * sgn;
                    var ldir = Quaternion.Euler(0f, turn, 0f) * dir;
                    float len = 18f + (float)rng.NextDouble() * 26f;
                    var lane = new List<Vector3> { a };
                    var q = a;
                    for (float d = 6f; d <= len; d += 6f)
                    {
                        ldir = Quaternion.Euler(0f, ((float)rng.NextDouble() - 0.5f) * 18f, 0f) * ldir;
                        q += ldir * 6f;
                        if (Flat(q - c).magnitude > inner - 2f) break;
                        lane.Add(q);
                    }
                    if (lane.Count < 2) continue;
                    lanes.Add(lane);
                    AddLine(t, lane, 1.6f, paved && rng.NextDouble() < 0.5, reserved);
                }

            // Häuser entlang aller Straßen
            float ww = WallWidthOf(s.HouseMat);
            if (s.Mark != Mark.Tents)
                foreach (var (line, half) in mains.Select((l, i) => (l, i < 4 ? 2.8f : 2.2f)).Concat(lanes.Select(l => (l, 1.6f))))
                    PlotsAlong(t, s, line, half, c, inner, ww, rng, reserved);
            // Hinterhöfe auffüllen: kleine Häuser, zur nächsten Straße gedreht
            if (s.Mark != Mark.Tents)
                for (int k = 0; k < 1200; k++)
                {
                    float a = (float)rng.NextDouble() * 2f * Mathf.PI, r = Mathf.Sqrt((float)rng.NextDouble()) * inner;
                    var pt = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    var (dist, near, ndir) = NearestStreet(t, pt);
                    if (dist > 34f) continue;
                    var sideV = Flat(pt - near).normalized;
                    float w = ww * (ww > 3f ? 2 : 3), d = ww * (ww > 3f ? 2 : 3);
                    TryPlot(t, s, pt, sideV, w, d, c, inner, rng, reserved, rng.NextDouble() < 0.25 ? 1 : 0);
                }
            return t;
        }

        static void AddLine(TownPlan t, List<Vector3> line, float half, bool paved, List<(Vector3 c, float r)> reserved)
        {
            for (int i = 1; i < line.Count; i++)
            {
                var mid = (line[i - 1] + line[i]) / 2f;
                if (reserved.Any(q => Flat(q.c - mid).magnitude < q.r - 2f)) continue;
                t.Streets.Add((line[i - 1], line[i], half, paved));
            }
        }

        static void PlotsAlong(TownPlan t, Style s, List<Vector3> line, float half, Vector3 c, float inner, float ww, System.Random rng, List<(Vector3 c, float r)> reserved)
        {
            for (int i = 1; i < line.Count; i++)
            {
                var a = line[i - 1]; var b = line[i];
                var dir = Flat(b - a); float len = dir.magnitude;
                if (len < 1f) continue;
                dir /= len;
                foreach (float sgn in new[] { -1f, 1f })
                {
                    var side = new Vector3(-dir.z, 0f, dir.x) * sgn;
                    for (float u = 0f; u < len; )
                    {
                        int nw = ww > 3f ? 2 + rng.Next(2) : 2 + rng.Next(3);  // Breite in Wandstücken
                        int nd = ww > 3f ? 2 : 3 + rng.Next(2);
                        float w = nw * ww, d = nd * ww;
                        var front = a + dir * (u + w / 2f);
                        var pc = front + side * (half + 1.0f + d / 2f);
                        int kind = rng.NextDouble() < 0.07 ? 1 : 0;
                        TryPlot(t, s, pc, -side, w, d, c, inner, rng, reserved, kind, dir);
                        u += w + 0.6f + (float)rng.NextDouble() * 1.2f;
                    }
                }
            }
        }

        /// <summary>Bauplatz prüfen und eintragen. toStreet: Richtung von der Hausmitte zur Straße.</summary>
        public static readonly int[] s_why = new int[8];

        static bool TryPlot(TownPlan t, Style s, Vector3 pc, Vector3 toStreet, float w, float d, Vector3 c, float inner,
                            System.Random rng, List<(Vector3 c, float r)> reserved, int kind, Vector3? along = null)
        {
            bool ok = TryPlotInner(t, s, pc, toStreet, w, d, c, inner, rng, reserved, kind, out int why);
            s_why[why]++;
            return ok;
        }

        static bool TryPlotInner(TownPlan t, Style s, Vector3 pc, Vector3 toStreet, float w, float d, Vector3 c, float inner,
                            System.Random rng, List<(Vector3 c, float r)> reserved, int kind, out int why)
        {
            why = 0;
            var fwd = -toStreet; // lokale +z zeigt von der Straße weg
            float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            var q = Quaternion.Euler(0f, yaw, 0f);
            float rad = Mathf.Sqrt(w * w + d * d) / 2f;
            // Ecken innerhalb der Stadt, nicht auf Straßen, nicht im Wasser
            foreach (var (sx, sz) in Corners)
            {
                var corner = pc + q * new Vector3(sx * w / 2f, 0f, sz * d / 2f);
                if (Flat(corner - c).magnitude > inner) { why = 1; return false; }
                if (Latitude.CoastDistance(corner.x, corner.z) > -4f) { why = 2; return false; }
                foreach (var st in t.Streets)
                    if (DistSeg(corner, st.a, st.b) < st.half + 0.6f) { why = 3; return false; }
            }
            if (Flat(pc - c).magnitude < t.Plaza + rad) { why = 4; return false; }
            foreach (var st in t.Streets)
                if (DistSeg(pc, st.a, st.b) < st.half + 0.6f) { why = 3; return false; }
            if (reserved.Any(r => Flat(r.c - pc).magnitude < r.r + rad * 0.7f)) { why = 5; return false; }
            foreach (var o in t.Plots)
            {
                float orad = Mathf.Sqrt(o.W * o.W + o.D * o.D) / 2f;
                if (Flat(o.C - pc).magnitude < (orad + rad) * 0.72f && Overlap(o, pc, yaw, w, d)) { why = 6; return false; }
            }
            why = 7;
            t.Plots.Add(new Plot { C = pc, Yaw = yaw, W = w, D = d, Kind = kind });
            // Boden im Haus pflastern: kein Gras/Farn, das durch den Boden wächst
            if (kind == 0) t.Areas.Add(new RoadNet.Area { X = pc.x, Z = pc.z, HX = w / 2f + 0.3f, HZ = d / 2f + 0.3f, Yaw = yaw, Kind = 2 });
            if (kind == 1) t.Areas.Add(new RoadNet.Area { X = pc.x, Z = pc.z, HX = w / 2f - 0.6f, HZ = d / 2f - 0.6f, Yaw = yaw, Kind = 0 });
            return true;
        }

        /// <summary>Überlappen sich zwei gedrehte Rechtecke? (Trennachsen-Test, mit 0,3 m Luft)</summary>
        static bool Overlap(Plot o, Vector3 pc, float yaw, float w, float d)
        {
            var a = Corners4(o.C, o.Yaw, o.W + 0.3f, o.D + 0.3f);
            var b = Corners4(pc, yaw, w + 0.3f, d + 0.3f);
            foreach (var poly in new[] { a, b })
                for (int i = 0; i < 4; i++)
                {
                    var e = poly[(i + 1) % 4] - poly[i];
                    var n = new Vector3(-e.z, 0f, e.x);
                    float aMin = float.MaxValue, aMax = float.MinValue, bMin = float.MaxValue, bMax = float.MinValue;
                    foreach (var v in a) { float pr = Vector3.Dot(n, v); aMin = Math.Min(aMin, pr); aMax = Math.Max(aMax, pr); }
                    foreach (var v in b) { float pr = Vector3.Dot(n, v); bMin = Math.Min(bMin, pr); bMax = Math.Max(bMax, pr); }
                    if (aMax < bMin || bMax < aMin) return false;
                }
            return true;
        }

        static Vector3[] Corners4(Vector3 c, float yaw, float w, float d)
        {
            var q = Quaternion.Euler(0f, yaw, 0f);
            return new[]
            {
                Flat(c + q * new Vector3(-w / 2f, 0f, -d / 2f)), Flat(c + q * new Vector3(w / 2f, 0f, -d / 2f)),
                Flat(c + q * new Vector3(w / 2f, 0f, d / 2f)), Flat(c + q * new Vector3(-w / 2f, 0f, d / 2f)),
            };
        }

        static (float dist, Vector3 point, Vector3 dir) NearestStreet(TownPlan t, Vector3 p)
        {
            float best = float.MaxValue; Vector3 bp = p, bd = Vector3.forward;
            foreach (var st in t.Streets)
            {
                var ab = Flat(st.b - st.a); float l2 = ab.sqrMagnitude;
                float u = l2 > 0f ? Mathf.Clamp01(Vector3.Dot(Flat(p - st.a), ab) / l2) : 0f;
                var q = st.a + ab * u;
                float dd = Flat(p - q).magnitude - st.half;
                if (dd < best) { best = dd; bp = q; bd = ab.normalized; }
            }
            return (best, bp, bd);
        }

        static float DistSeg(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = Flat(b - a); float l2 = ab.sqrMagnitude;
            float u = l2 > 0f ? Mathf.Clamp01(Vector3.Dot(Flat(p - a), ab) / l2) : 0f;
            return Flat(p - (a + ab * u)).magnitude;
        }

        /// <summary>Wahrzeichen ohne zu bauen durchspielen und die freizuhaltenden Flächen sammeln.</summary>
        static List<(Vector3 c, float r)> DryLandmarks(CityBuilder.Plan p, Style s, Vector3 c)
        {
            bool oldDry = s_dry;
            var oldRes = s_reserved.ToList();
            var oldPlan = s_plan;
            s_dry = true;
            s_plan = p;
            s_reserved.Clear();
            try
            {
                var wallKit = KitOf(s.WallMat); var houseKit = KitOf(s.HouseMat); var keepKit = KitOf(s.KeepMat);
                if (wallKit.Wall != null && houseKit.Wall != null && keepKit.Wall != null)
                    Landmark(s, c, p.R, wallKit, houseKit, keepKit);
                return s_reserved.ToList();
            }
            finally
            {
                s_dry = oldDry;
                s_plan = oldPlan;
                s_reserved.Clear(); s_reserved.AddRange(oldRes);
            }
        }

        // ---------------------------------------------------------------- Ankunftspunkt

        /// <summary>
        /// Freie Stelle für die Kartenmarkierung (Teleport): auf einer Straße, möglichst nah an der Mitte,
        /// aber nicht in einem Wahrzeichen oder Haus.
        /// </summary>
        static Vector3 SafeSpot(CityBuilder.Plan p)
        {
            var t = Town(p);
            var c = new Vector3(p.X, p.H, p.Z);
            var reserved = DryLandmarks(p, StyleOf(p), c);
            Vector3 best = c; float bestD = float.MaxValue;
            foreach (var st in t.Streets)
            {
                if (st.half < 2f) continue; // Hauptstraßen und Ring
                for (float u = 0f; u <= 1f; u += 0.25f)
                {
                    var q = Vector3.Lerp(st.a, st.b, u);
                    float d = Flat(q - c).magnitude;
                    if (d < 6f || d >= bestD) continue;
                    if (reserved.Any(r => Flat(r.c - q).magnitude < r.r + 4f)) continue;
                    if (t.Plots.Any(o => Flat(o.C - q).magnitude < Math.Max(o.W, o.D) * 0.7f + 1f)) continue;
                    if (Occupied(q, 3f)) continue; // tatsächlich gebaute Teile (Burgen, Mauern, Bäume …)
                    if (Latitude.CoastDistance(q.x, q.z) > -6f) continue;
                    best = q; bestD = d;
                }
            }
            if (bestD == float.MaxValue)
            {
                // keine passende Straße: freie Stelle auf Ringen um die Mitte suchen
                for (float r = 8f; r < p.R && bestD == float.MaxValue; r += 4f)
                    for (int i = 0; i < 24; i++)
                    {
                        float a = i * Mathf.PI / 12f;
                        var q = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                        if (Occupied(q, 3f) || Latitude.CoastDistance(q.x, q.z) > -6f) continue;
                        best = q; bestD = r; break;
                    }
            }
            return new Vector3(best.x, 0f, best.z);
        }

        /// <summary>Kartenmarkierung der Stadt auf den Ankunftspunkt setzen (auch eine schon gesetzte).</summary>
        static void MovePin(CityBuilder.Plan p)
        {
            var spot = SafeSpot(p);
            PointsOfInterest.PinPositions[p.Name] = spot;
            Plugin.Log.LogInfo($"[Bau] Ankunft {p.Name}: {spot.x:0} {spot.z:0} ({Flat(spot - new Vector3(p.X, 0f, p.Z)).magnitude:0} m von der Mitte)");
            var map = Minimap.instance;
            if (map == null) return;
            var pins = HarmonyLib.Traverse.Create(map).Field("m_pins").GetValue<List<Minimap.PinData>>();
            if (pins == null) return;
            foreach (var pin in pins)
                if (pin.m_name == p.Name && pin.m_type == Minimap.PinType.Icon1) pin.m_pos = spot;
        }

        // ---------------------------------------------------------------- Häuser bauen

        static void BuildTown(Style s, CityBuilder.Plan p, Vector3 c, Kit k)
        {
            s_towns.Remove(p.Name); // frisch berechnen (Modul kann neu sein)
            var t = Town(p);
            var upper = s.UpperMat.HasValue ? KitOf(s.UpperMat.Value) : null;
            var wg = WorldGenerator.instance;
            int built = 0, slope = 0;
            foreach (var plot in t.Plots)
            {
                // Boden unter dem Haus muss eben sein (Hügel in der Stadt): sonst auslassen
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var v in Corners4(plot.C, plot.Yaw, plot.W, plot.D).Concat(new[] { plot.C }))
                {
                    float h = wg.GetHeight(v.x, v.z);
                    lo = Math.Min(lo, h); hi = Math.Max(hi, h);
                }
                if (hi - lo > 2.5f) { slope++; continue; }
                built++;
                var pc = new Vector3(plot.C.x, lo, plot.C.z);
                WithFrame(pc, plot.Yaw, () =>
                {
                    if (plot.Kind == 1) { Garden(pc, Math.Min(plot.W, plot.D) - 1.2f, k); return; }
                    if (s.Mark == Mark.Huts || s.Mark == Mark.Hardhome || s.Mark == Mark.Stilts)
                    {
                        Hut(pc, plot.W, plot.D, k, s.Mark == Mark.Stilts, 0);
                        return;
                    }
                    House2(s, pc, plot.W, plot.D, k, upper);
                });
            }
            Plugin.Log.LogInfo($"[Bau] {p.Name}: {t.Streets.Count} Straßenstücke, {t.Plots.Count} Bauplätze, {built} gebaut, {slope} am Hang. " +
                               $"Abgelehnt: außen {s_why[1]}, Wasser {s_why[2]}, Straße {s_why[3]}, Platz {s_why[4]}, Wahrzeichen {s_why[5]}, Nachbar {s_why[6]}");
            Array.Clear(s_why, 0, s_why.Length);
            StreetLife(s, c, Layout(p).inner);
            // Fackeln am Rand der Hauptstraßen
            if (s.Mark != Mark.Ruins)
            {
                string[] torch = { "piece_groundtorch_wood", "piece_groundtorch" };
                float acc = 0f; int side = 1;
                foreach (var st in t.Streets)
                {
                    if (st.half < 2.5f) continue;
                    var seg = Flat(st.b - st.a); float len = seg.magnitude;
                    acc += len;
                    if (acc < 26f || len < 0.5f) continue;
                    acc = 0f; side = -side;
                    var dir = seg / len; var n = new Vector3(-dir.z, 0f, dir.x) * side;
                    var q = (st.a + st.b) / 2f + n * (st.half + 0.6f);
                    Decor(torch, new Vector3(q.x, c.y, q.z), 0f);
                }
            }
        }

        /// <summary>
        /// Ein Haus (lokal: Tür auf Süd/−z zur Straße). Eckpfosten gegen Lücken, große Tür,
        /// Fensterläden, Zwischenboden bei zwei Stockwerken, einfache Einrichtung.
        /// </summary>
        static void House2(Style s, Vector3 p, float w, float d, Kit k, Kit upper)
        {
            bool pitched = s.Pitched && (upper ?? k).Roof != null;
            // Maße auf ganze Wandstücke und (bei Satteldach) ganze Dachreihen abrunden
            float unit = pitched ? Math.Max(4f, k.Wall.Width) : k.Wall.Width;
            w = Math.Max(k.Wall.Width * 2f, (float)Math.Floor(w / k.Wall.Width + 0.01f) * k.Wall.Width);
            d = Math.Max(unit, (float)Math.Floor(d / unit + 0.01f) * unit);
            if (w < d && pitched) w = Math.Max(unit, (float)Math.Floor(w / unit + 0.01f) * unit);
            int levels = 1 + (s.MaxLevels > 1 && s_rng.NextDouble() < 0.55 ? 1 : 0) + (s.MaxLevels > 2 && s_rng.NextDouble() < 0.3 ? 1 : 0);
            // Erdgeschoss
            bool roofHere = pitched && levels == 1 && upper == null;
            bool flatTop = !pitched && levels == 1;
            bool parapet = flatTop && s_rng.NextDouble() < 0.25; // nur wenige Häuser mit Zinnen
            Box(p, w, d, p.y, 1, k, door: true, pitched: roofHere, merlons: parapet, doorSide: 0, top: flatTop || roofHere);
            float lh = LevelHeight(k);
            // Obergeschosse (ggf. aus anderem Material, z. B. Fachwerk über Steinsockel)
            var uk = upper ?? k;
            if (levels > 1)
            {
                Box(new Vector3(p.x, p.y + lh, p.z), w, d, p.y + lh, levels - 1, uk, door: false, pitched: pitched, merlons: !pitched && s_rng.NextDouble() < 0.25);
            }
            else if (upper != null && pitched)
                Roof(p, Snap(w, k.Wall.Width), Snap(d, k.Wall.Width), p.y + lh, uk);
            Windows(p, w, d, p.y, levels, k, uk);
            // Flachdachhäuser (Essos, Dorne): manche mit kleiner Kuppel
            if (!pitched && s_ruin <= 0.25f && s_rng.NextDouble() < 0.18 && Math.Min(w, d) >= 6f)
                Dome(new Vector3(p.x, p.y + levels * lh, p.z), Math.Min(w, d) - 2f, uk);
            // Kleinigkeiten vor dem Haus
            double r = s_rng.NextDouble();
            var front = new Vector3(p.x, p.y, p.z - d / 2f - 1.1f);
            if (r < 0.18) Decor(new[] { "piece_chest_barrel" }, front + new Vector3(w / 2f - 1f, 0f, 0f), 0f);
            else if (r < 0.32) Decor(new[] { "wood_stack", "wood_fine_stack" }, new Vector3(p.x + w / 2f + 1.3f, p.y, p.z), 90f);
            else if (r < 0.40) Decor(new[] { "piece_bench01" }, front + new Vector3(-w / 2f + 1.6f, 0f, 0f), 180f);
        }

        static float Snap(float v, float unit) => Math.Max(1, (int)Math.Round(v / unit)) * unit;

        /// <summary>Fensterläden an den Außenwänden (in jedem Stockwerk, nicht über der Tür).</summary>
        static void Windows(Vector3 p, float w, float d, float y0, int levels, Kit k, Kit uk)
        {
            // Nur auf der Straßenseite (lokal Süd): dort sieht man sie, hinten und seitlich nicht
            var shutter = Pick("wood_window");
            if (shutter == null || s_ruin > 0.25f) return;
            float lh = LevelHeight(k);
            for (int lv = 0; lv < levels; lv++)
            {
                var kit = lv == 0 ? k : uk;
                float ww = kit.Wall.Width, sw = Snap(w, ww), sd = Snap(d, ww);
                float y = y0 + lv * lh + 1.6f;
                int nx = Math.Max(1, (int)Math.Round(sw / ww));
                for (int i = 0; i < nx; i++)
                {
                    if (lv == 0 && i == nx / 2) continue; // Tür
                    if (nx > 2 && i % 2 == 1) continue;
                    float o = -sw / 2f + ww * (i + 0.5f);
                    Put(shutter, new Vector3(p.x + o, y, p.z - sd / 2f - 0.28f), 0f, 0.5f);
                }
            }
        }

        /// <summary>Einrichtung: unten Tisch mit Bank oder Stühlen, Fass, Teppich, ab und zu Feuerstelle; oben Bett und Truhe.</summary>
        static void Furnish2(Vector3 p, float w, float d, float y0, int levels, float lh, Kit k)
        {
            if (s_ruin > 0.2f || w < 4f || d < 4f) return;
            float hx = w / 2f - 1.3f, hz = d / 2f - 1.3f;
            double t = s_rng.NextDouble();
            // Erdgeschoss
            if (t < 0.55)
            {
                Decor(new[] { "piece_table", "piece_table_round" }, new Vector3(p.x, y0, p.z + hz * 0.3f), 0f);
                Decor(new[] { "piece_bench01", "piece_chair" }, new Vector3(p.x, y0, p.z + hz * 0.3f - 1.1f), 0f);
                if (w >= 6f) Decor(new[] { "piece_chair", "piece_chair02" }, new Vector3(p.x + 1.6f, y0, p.z + hz * 0.3f), 270f);
            }
            else if (t < 0.8) Decor(new[] { "piece_workbench", "piece_table" }, new Vector3(p.x - hx + 0.6f, y0, p.z + hz), 0f);
            else Decor(new[] { "hearth", "fire_pit" }, new Vector3(p.x, y0, p.z + hz * 0.4f), 0f);
            Decor(new[] { "piece_chest_barrel" }, new Vector3(p.x + hx, y0, p.z + hz), 0f);
            if (s_rng.NextDouble() < 0.6) Decor(new[] { "rug_straw", "rug_deer", "jute_carpet" }, new Vector3(p.x, y0 + 0.02f, p.z - hz * 0.4f), 0f);
            // Obergeschoss
            float y1 = y0 + (levels > 1 ? lh : 0f);
            if (levels > 1 || s_rng.NextDouble() < 0.5)
            {
                Decor(new[] { "bed", "piece_bed02" }, new Vector3(p.x - hx + 0.8f, y1, p.z + hz - 0.4f), 0f);
                Decor(new[] { "piece_chest_wood", "piece_chest" }, new Vector3(p.x + hx - 0.6f, y1, p.z + hz - 0.2f), 180f);
            }
        }

        // ---------------------------------------------------------------- Drehung für ganze Gebäude

        static bool s_rot;
        static Vector3 s_pivot;
        static Quaternion s_q = Quaternion.identity;
        static float s_yaw;

        /// <summary>Baut ein Gebäude in lokalen Koordinaten um <paramref name="pivot"/>, gedreht um <paramref name="yaw"/>.</summary>
        static void WithFrame(Vector3 pivot, float yaw, Action build)
        {
            bool old = s_rot; var op = s_pivot; var oq = s_q; float oy = s_yaw;
            s_rot = true; s_pivot = pivot; s_q = Quaternion.Euler(0f, yaw, 0f); s_yaw = yaw;
            try { build(); }
            finally { s_rot = old; s_pivot = op; s_q = oq; s_yaw = oy; }
        }

        static Vector3 Tf(Vector3 p) => s_rot ? s_pivot + s_q * (p - s_pivot) : p;
        static float TfYaw(float yaw) => s_rot ? yaw + s_yaw : yaw;
        static Vector3 TfDir(Vector3 d) => s_rot ? s_q * d : d;
    }
}
