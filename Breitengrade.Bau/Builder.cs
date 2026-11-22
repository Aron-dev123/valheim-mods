using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Breitengrade.Bau
{
    /// <summary>
    /// Stadtbau für Planetos (nachladbares Modul): Stile je Ort, Mauern, Wahrzeichen, Häuser, Details.
    /// Wird vom Hauptmod geladen (BauLoader) und kann im laufenden Spiel neu geladen werden
    /// (Befehl "neu &lt;Stadt&gt;" der Fernsteuerung), ohne Valheim neu zu starten.
    /// </summary>
    public static partial class Builder
    {
        // ---------------------------------------------------------------- Stile

        public enum Mat { Wood, Dark, Stone, Marble, Grausten }

        public enum Mark
        {
            Castle, Winterfell, KingsLanding, Hightower, Pyramid, Harrenhal, Eyrie, StormsEnd, Titan,
            Tents, Huts, Ruins, Twins, Concentric, SunTower, CastleBlack, Hardhome, Pyke, Stilts, Asshai,
        }

        public class Style
        {
            public Mat WallMat = Mat.Stone, HouseMat = Mat.Wood, KeepMat = Mat.Stone;
            public int Rings = 1;          // Stadtmauern (0 = keine)
            public int WallRows = 3;       // Höhe der Stadtmauer in Wandreihen
            public bool Pitched = true;    // Häuser mit Satteldach (sonst Flachdach)
            public Mark Mark = Mark.Castle;
            public float Ruin;             // Anteil fehlender Teile (Ruinen)
            public float Houses = 1f;      // Dichte der Häuser (0..1)
            public int MaxLevels = 1;      // Stockwerke der Häuser
            public Mat? UpperMat;          // Obergeschosse aus anderem Material (z. B. Fachwerk über Stein)
            public bool RoundTowers;       // runde Trommeltürme statt eckiger Ecktürme
        }

        static Style St(Mark mark, Mat wall = Mat.Stone, Mat house = Mat.Wood, Mat keep = Mat.Stone,
                        int rings = 1, bool pitched = true, float ruin = 0f, float houses = 1f, int levels = 1, int wallRows = 3, Mat? upper = null) =>
            new Style { Mark = mark, WallMat = wall, HouseMat = house, KeepMat = keep, Rings = rings, Pitched = pitched,
                        Ruin = ruin, Houses = houses, MaxLevels = levels, WallRows = wallRows, UpperMat = upper };

        /// <summary>Aussehen je Ort (nach der Lore).</summary>
        static readonly Dictionary<string, Style> Styles = new Dictionary<string, Style>
        {
            // Jenseits der Mauer und der Norden
            ["Lands of Always Winter"] = St(Mark.Huts, rings: 0, houses: 0.5f, house: Mat.Dark),
            ["Thenn"] = St(Mark.Huts, rings: 0, houses: 0.7f, house: Mat.Dark),
            ["Hardhome"] = St(Mark.Hardhome, wall: Mat.Wood, house: Mat.Wood, rings: 1, wallRows: 2),
            ["Castle Black"] = St(Mark.CastleBlack, rings: 0, house: Mat.Wood, houses: 0.6f),
            ["Deepwood Motte"] = St(Mark.Castle, wall: Mat.Wood, house: Mat.Wood, keep: Mat.Wood, wallRows: 2),
            ["Karhold"] = St(Mark.Castle, house: Mat.Dark),
            ["The Dreadfort"] = St(Mark.Castle, house: Mat.Dark, wallRows: 4),
            ["Winterfell"] = St(Mark.Winterfell, house: Mat.Dark, rings: 2, wallRows: 4, levels: 2),
            ["White Harbor"] = St(Mark.Castle, house: Mat.Stone, levels: 2),
            ["The Neck"] = St(Mark.Stilts, rings: 0, house: Mat.Wood, houses: 0.6f),
            // Westeros
            ["The Twins"] = St(Mark.Twins, rings: 1),
            ["Pyke"] = St(Mark.Pyke, rings: 0, house: Mat.Dark, houses: 0.5f),
            ["The Eyrie"] = St(Mark.Eyrie, rings: 1, house: Mat.Stone, houses: 0.6f),
            ["Riverrun"] = St(Mark.Castle, rings: 1).Round(),
            ["Harrenhal"] = St(Mark.Harrenhal, rings: 1, wallRows: 5, houses: 0.4f),
            ["Lannisport"] = St(Mark.Castle, house: Mat.Stone, upper: Mat.Wood, levels: 2),
            ["Dragonstone"] = St(Mark.Castle, wall: Mat.Marble, keep: Mat.Marble, house: Mat.Dark),
            ["King's Landing"] = St(Mark.KingsLanding, house: Mat.Stone, upper: Mat.Dark, levels: 3, wallRows: 4).Round(),
            ["Storm's End"] = St(Mark.StormsEnd, rings: 1, wallRows: 4),
            ["Highgarden"] = St(Mark.Concentric, rings: 3, house: Mat.Stone, levels: 2).Round(),
            ["Oldtown"] = St(Mark.Hightower, house: Mat.Stone, upper: Mat.Dark, levels: 3),
            ["Sunspear"] = St(Mark.SunTower, rings: 3, house: Mat.Stone, pitched: false),
            // Freie Städte
            ["Braavos"] = St(Mark.Titan, rings: 0, house: Mat.Stone, pitched: false, levels: 2),
            ["Lorath"] = St(Mark.Castle, house: Mat.Stone, pitched: false),
            ["Norvos"] = St(Mark.Castle, house: Mat.Wood),
            ["Pentos"] = St(Mark.Castle, house: Mat.Stone, pitched: false, levels: 2),
            ["Qohor"] = St(Mark.Castle, house: Mat.Dark),
            ["Myr"] = St(Mark.Castle, house: Mat.Stone, pitched: false),
            ["Lys"] = St(Mark.Castle, house: Mat.Marble, pitched: false),
            ["Volantis"] = St(Mark.Concentric, rings: 2, wall: Mat.Marble, house: Mat.Stone, pitched: false, levels: 2),
            ["Valyria"] = St(Mark.Ruins, wall: Mat.Marble, house: Mat.Marble, keep: Mat.Marble, pitched: false, ruin: 0.55f),
            ["The Sorrows"] = St(Mark.Ruins, house: Mat.Stone, pitched: false, ruin: 0.5f, houses: 0.6f),
            // Essos
            ["Vaes Dothrak"] = St(Mark.Tents, rings: 0, house: Mat.Dark),
            ["Meereen"] = St(Mark.Pyramid, house: Mat.Stone, pitched: false, levels: 2),
            ["Astapor"] = St(Mark.Pyramid, house: Mat.Stone, pitched: false),
            ["Old Ghis"] = St(Mark.Pyramid, house: Mat.Stone, pitched: false, ruin: 0.4f),
            ["The Red Waste"] = St(Mark.Ruins, house: Mat.Stone, pitched: false, ruin: 0.5f, houses: 0.5f, rings: 0),
            ["Qarth"] = St(Mark.Concentric, rings: 3, house: Mat.Stone, pitched: false, levels: 2),
            ["Samyriana"] = St(Mark.Ruins, house: Mat.Stone, pitched: false, ruin: 0.45f),
            ["Port of Ibben"] = St(Mark.Castle, wall: Mat.Wood, house: Mat.Dark, keep: Mat.Wood, wallRows: 2),
            ["Yin"] = St(Mark.Castle, house: Mat.Dark, levels: 2, wallRows: 4),
            ["Asshai"] = St(Mark.Asshai, wall: Mat.Marble, house: Mat.Marble, keep: Mat.Marble, pitched: false, wallRows: 5, levels: 2),
            // Süden
            ["Sothoryos"] = St(Mark.Ruins, wall: Mat.Marble, house: Mat.Stone, keep: Mat.Marble, pitched: false, ruin: 0.5f),
            ["Yeen"] = St(Mark.Ruins, wall: Mat.Marble, house: Mat.Marble, keep: Mat.Marble, pitched: false, ruin: 0.4f),
            ["Ulthos"] = St(Mark.Ruins, house: Mat.Stone, pitched: false, ruin: 0.5f),
            ["Land of Fire"] = St(Mark.Ruins, wall: Mat.Grausten, house: Mat.Grausten, keep: Mat.Grausten, pitched: false, ruin: 0.45f),
        };

        static Style Round(this Style s) { s.RoundTowers = true; return s; }

        static Style DefaultStyle(int biome)
        {
            if (biome == Latitude.DeepNorth) return St(Mark.Huts, rings: 0, house: Mat.Dark);
            if (biome == Latitude.Swamp) return St(Mark.Stilts, rings: 0);
            if (biome == Latitude.Mistlands) return St(Mark.Ruins, wall: Mat.Marble, house: Mat.Marble, keep: Mat.Marble, pitched: false, ruin: 0.45f);
            if (biome == Latitude.AshLands) return St(Mark.Ruins, wall: Mat.Grausten, house: Mat.Grausten, keep: Mat.Grausten, pitched: false, ruin: 0.45f);
            if (biome == Latitude.Plains) return St(Mark.Castle, house: Mat.Stone, pitched: false);
            if (biome == Latitude.BlackForest) return St(Mark.Castle, house: Mat.Dark);
            return St(Mark.Castle);
        }

        // ---------------------------------------------------------------- Bauteile

        sealed class Kit
        {
            public PieceBox Wall, Floor, Roof, Pillar, Merlon, RoofTop, Gable, Corner, Door, DoorTop;
            public bool DoubleDoor; // zwei Torflügel (Stein: 4 m breite Öffnung)
        }

        static readonly Dictionary<string, PieceBox> s_boxes = new Dictionary<string, PieceBox>();

        /// <summary>Wie Pick, aber auch für senkrechte Teile (Pfosten, Säulen) ohne Breite.</summary>
        static PieceBox PickAny(params string[] names)
        {
            foreach (var n in names)
            {
                if (!s_boxes.TryGetValue(n, out var box)) s_boxes[n] = box = PieceBox.Of(n);
                if (box != null && box.Height >= 0.3f) return box;
            }
            return null;
        }

        static PieceBox Pick(params string[] names)
        {
            foreach (var n in names)
            {
                if (!s_boxes.TryGetValue(n, out var box)) s_boxes[n] = box = PieceBox.Of(n);
                if (box != null && box.Width >= 0.5f) return box;
            }
            return null;
        }

        static Kit KitOf(Mat m)
        {
            var k = KitBase(m);
            bool dark = m == Mat.Dark || m == Mat.Stone || m == Mat.Marble;
            k.RoofTop = dark ? Pick("darkwood_roof_top_45", "darkwood_roof_top", "wood_roof_top_45")
                             : Pick("wood_roof_top_45", "wood_roof_top", "darkwood_roof_top_45");
            k.Gable = Pick("wood_wall_roof_45", "wood_wall_roof");
            switch (m)
            {
                case Mat.Wood:
                    k.Door = Pick("wood_gate"); k.DoorTop = Pick("wood_wall_half");
                    break;
                case Mat.Dark:
                    k.Door = Pick("wood_gate"); k.DoorTop = Pick("wood_wall_half");
                    break;
                case Mat.Marble:
                    k.Door = Pick("wood_gate"); k.DoorTop = Pick("blackmarble_tile_wall_1x1");
                    break;
                case Mat.Grausten:
                    k.Corner = PickAny("Piece_grausten_pillarbase_medium", "stone_pillar");
                    k.Door = Pick("darkwood_gate"); k.DoubleDoor = true;
                    break;
                default:
                    k.Corner = PickAny("stone_pillar");
                    k.Door = Pick("darkwood_gate"); k.DoubleDoor = true;
                    break;
            }
            return k;
        }

        static Kit KitBase(Mat m)
        {
            switch (m)
            {
                case Mat.Wood:
                    return new Kit
                    {
                        Wall = Pick("woodwall", "wood_wall_half"), Floor = Pick("wood_floor"),
                        Roof = Pick("wood_roof_45", "wood_roof"), Pillar = PickAny("wood_pole2", "wood_pole"),
                    };
                case Mat.Dark:
                    return new Kit
                    {
                        Wall = Pick("woodwall"), Floor = Pick("wood_floor"),
                        Roof = Pick("darkwood_roof_45", "darkwood_roof", "wood_roof_45"), Pillar = PickAny("darkwood_pole", "wood_pole2"),
                    };
                case Mat.Marble:
                    return new Kit
                    {
                        Wall = Pick("blackmarble_tile_wall_2x2", "stone_wall_2x1"), Floor = Pick("blackmarble_floor", "stone_floor_2x2"),
                        Roof = Pick("darkwood_roof_45"), Pillar = PickAny("blackmarble_column_1", "stone_pillar"),
                        Merlon = Pick("blackmarble_tile_wall_1x1", "stone_wall_1x1"),
                    };
                case Mat.Grausten:
                    return new Kit
                    {
                        Wall = Pick("Piece_grausten_wall_4x2", "piece_grausten_wall_4x2", "stone_wall_4x2"),
                        Floor = Pick("Piece_grausten_floor_4x4", "Piece_grausten_floor_2x2", "stone_floor_2x2"),
                        Roof = null, Pillar = PickAny("Piece_grausten_pillarbase_medium", "stone_pillar"),
                        Merlon = Pick("Piece_grausten_wall_1x2", "stone_wall_1x1"),
                    };
                default:
                    return new Kit
                    {
                        Wall = Pick("stone_wall_4x2", "stone_wall_2x1"), Floor = Pick("stone_floor_2x2"),
                        Roof = Pick("darkwood_roof_45", "wood_roof_45"), Pillar = PickAny("stone_pillar"),
                        Merlon = Pick("stone_wall_1x1"),
                    };
            }
        }

        // ---------------------------------------------------------------- Bauen

        static System.Random s_rng = new System.Random(1);
        static float s_ruin;
        static int s_count;
        static readonly List<(Vector3 c, float r)> s_reserved = new List<(Vector3, float)>();

        /// <summary>Innenbereich (Häuser), Zellgröße der Häuserblöcke und Pflaster für eine Stadt.</summary>
        public static (float inner, float cell, bool paved) Layout(CityBuilder.Plan p)
        {
            var place = Planetos.Places.FirstOrDefault(q => q.name == p.Name);
            var s = Styles.TryGetValue(p.Name, out var st) ? st : DefaultStyle(place.biome);
            float wallR = p.R - 4f;
            float inner = s.Rings > 0 ? wallR - 12f : p.R - 6f;
            float cell = s.Mark == Mark.Huts || s.Mark == Mark.Stilts || s.Mark == Mark.Hardhome ? 15f : 17f;
            bool paved = s.HouseMat == Mat.Stone || s.HouseMat == Mat.Marble || s.Mark == Mark.KingsLanding || s.Mark == Mark.Winterfell;
            if (s.Mark == Mark.Ruins || s.Mark == Mark.Huts || s.Mark == Mark.Stilts || s.Mark == Mark.Hardhome || s.Mark == Mark.Tents) paved = false;
            return (inner, cell, paved);
        }


        static bool s_water;
        static Style s_style;
        static CityBuilder.Plan s_plan;
        static int s_biome;

        /// <summary>Baut eine Stadt und gibt die Zahl der Bauteile zurück.</summary>
        public static int Build(CityBuilder.Plan plan)
        {
            s_occupied.Clear();
            int cleared = ClearOld(plan);
            if (cleared > 0) Plugin.Log.LogInfo($"[Bau] {plan.Name}: {cleared} alte Bauteile entfernt.");
            s_count = 0;
            s_plan = plan;
            s_water = true;
            var place = Planetos.Places.First(p => p.name == plan.Name);
            var style = Styles.TryGetValue(plan.Name, out var st) ? st : DefaultStyle(place.biome);
            s_biome = place.biome;
            s_style = style;
            s_rng = new System.Random(plan.Name.GetStableHashCode() ^ WorldGenerator.instance.GetSeed());
            s_ruin = style.Ruin;
            s_reserved.Clear();
            s_rings.Clear();

            var c = new Vector3(plan.X, plan.H, plan.Z);
            float R = plan.R;
            var wallKit = KitOf(style.WallMat);
            var houseKit = KitOf(style.HouseMat);
            var keepKit = KitOf(style.KeepMat);
            if (wallKit.Wall == null || houseKit.Wall == null || keepKit.Wall == null)
            {
                Plugin.Log.LogWarning($"[Städte] {plan.Name}: Bauteile fehlen.");
                return 0;
            }

            // Stadtmauern mit Toren an den Hauptstraßen
            float wallR = R - 4f;
            for (int i = 0; i < style.Rings; i++)
            {
                float rr = wallR * (1f - i * 0.28f);
                int rows = style.WallRows + i;
                RingWall(c, rr, rows, wallKit, keepKit, towers: true);
            }

            // Wahrzeichen
            Landmark(style, c, R, wallKit, houseKit, keepKit);

            // Häuser an den Straßen (Zelte: eigenes Muster)
            float inner = Layout(plan).inner;
            if (style.Mark == Mark.Tents) Houses(style, c, inner, houseKit);
            else BuildTown(style, plan, c, houseKit);
            try { Docks(plan); } catch (Exception e) { Plugin.Log.LogWarning($"[Bau] Hafen {plan.Name}: {e.Message}"); }
            try { MovePin(plan); } catch (Exception e) { Plugin.Log.LogWarning($"[Bau] Markierung {plan.Name}: {e.Message}"); }
            return s_count;
        }

        /// <summary>
        /// Entfernt alle von der Mod gebauten Teile im Stadtgebiet (auch in nicht geladenen Gebieten:
        /// Valheim löscht nur Objekte, die man besitzt, deshalb vorher den Besitz übernehmen).
        /// </summary>
        static int ClearOld(CityBuilder.Plan p)
        {
            var dict = HarmonyLib.Traverse.Create(ZDOMan.instance).Field("m_objectsByID").GetValue<IDictionary<ZDOID, ZDO>>();
            if (dict == null) return 0;
            float r = p.R + 25f;
            long me = ZDOMan.GetSessionID();
            var doomed = dict.Values.Where(z =>
            {
                var pos = z.GetPosition();
                float dx = pos.x - p.X, dz = pos.z - p.Z;
                return dx * dx + dz * dz < r * r && z.GetBool(MageTower.BuildMarker);
            }).ToList();
            foreach (var z in doomed)
            {
                if (!z.IsOwner()) z.SetOwner(me);
                ZDOMan.instance.DestroyZDO(z);
            }
            return doomed.Count;
        }

        /// <summary>Platz für etwas Großes freihalten (dort keine Häuser).</summary>
        static void Reserve(Vector3 p, float r) => s_reserved.Add((p, r));

        static readonly List<(Vector3 c, float r0, float r1)> s_rings = new List<(Vector3, float, float)>();

        static bool Free(Vector3 p, float r) =>
            s_reserved.All(q => Flat(q.c - p).magnitude > q.r + r) &&
            s_rings.All(q => { float d = Flat(q.c - p).magnitude; return d + r < q.r0 || d - r > q.r1; });

        // ---------------------------------------------------------------- Wahrzeichen

        static void Landmark(Style s, Vector3 c, float R, Kit wall, Kit house, Kit keep)
        {
            LandmarkCore(s, c, R, wall, house, keep);
            if (s_plan != null) NamedExtras(s_plan.Name, s, c, R, wall, house, keep);
        }

        static void LandmarkCore(Style s, Vector3 c, float R, Kit wall, Kit house, Kit keep)
        {
            Reserve(c, 10f); // Platz in der Mitte
            switch (s.Mark)
            {
                case Mark.Winterfell:
                    // Great Keep, Great Hall, viele Türme, der zerbrochene Turm, Glashaus (offen), Godswood frei
                    Castle(c + new Vector3(-10f, 0f, 8f), 26f, 20f, keep, 4, true, HallKind.North);
                    Box(c + new Vector3(16f, 0f, 10f), 22f, 12f, c.y, 2, keep, door: true, pitched: true, merlons: false);
                    GreatHall(c + new Vector3(16f, 0f, 10f), 22f, 12f, c.y, keep, HallKind.North);
                    Reserve(c + new Vector3(16f, 0f, 10f), 14f);
                    foreach (var (dx, dz, lv) in new[] { (-26f, -16f, 6), (24f, -18f, 5), (-30f, 22f, 7), (28f, 28f, 5), (0f, 34f, 6), (4f, -30f, 5) })
                        TowerAt(c + new Vector3(dx, 0f, dz), 7f, lv, keep, beacon: false);
                    // Broken Tower: oben zerfallen
                    WithRuin(0.5f, () => TowerAt(c + new Vector3(-36f, 0f, -2f), 8f, 9, keep, beacon: false));
                    // Godswood: der weiße Herzbaum (Birke mit rotem Herbstlaub) in der Mitte, Nadelbäume ringsum
                    {
                        var gw = c + new Vector3(30f, 0f, -36f);
                        Decor(new[] { "Birch1_aut", "Birch2_aut", "Birch1" }, gw, 0f);
                        for (int i = 0; i < 12; i++)
                        {
                            float a = i * 2f * Mathf.PI / 12f + 0.3f, r = 9f + (i % 3) * 4f;
                            Decor(new[] { "FirTree", "Pinetree_01" }, gw + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r), i * 37f);
                        }
                        Reserve(gw, 22f);
                        // Glasgärten: Gewächshaus aus Kristallwänden
                        var glass = new Kit { Wall = Pick("crystal_wall_1x1"), Floor = keep.Floor, Corner = PickAny("wood_pole2") };
                        if (glass.Wall != null)
                        {
                            var gh = c + new Vector3(-34f, 0f, -26f);
                            Box(gh, 12f, 6f, c.y, 1, glass, door: false, pitched: false, merlons: false, rowsPerLevel: 3);
                            Reserve(gh, 8f);
                        }
                    }
                    break;

                case Mark.KingsLanding:
                {
                    // Drei Hügel: Aegon's High Hill (Red Keep, Südosten über der Bucht),
                    // Visenya's Hill (Great Sept of Baelor), Rhaenys's Hill (Dragonpit, eingestürzte Kuppel)
                    var (rk0, sept0, pit0) = KingsLandingSpots(c, R);
                    var rk = rk0 + Vector3.up * 12f; var sept = sept0 + Vector3.up * 7f; var pit = pit0 + Vector3.up * 8f;
                    var marble = KitOf(Mat.Marble).Wall != null ? KitOf(Mat.Marble) : keep;
                    var dark = KitOf(Mat.Dark);
                    // Red Keep: Ringmauer mit sieben Trommeltürmen, Maegor's Holdfast, Thronsaal, Tower of the Hand
                    RingSquare(rk, 56f, 5, keep);
                    foreach (var (sx, sz) in Corners) RoundTower(rk + new Vector3(sx * 28f, 0f, sz * 28f), 5.5f, 7, keep, true);
                    TowerAt(rk + new Vector3(-7f, 0f, -28f), 6f, 6, keep, false);   // Torhaus
                    TowerAt(rk + new Vector3(7f, 0f, -28f), 6f, 6, keep, false);
                    TowerAt(rk + new Vector3(28f, 0f, 0f), 7f, 6, keep, false);
                    Castle(rk + new Vector3(-9f, 0f, 9f), 18f, 16f, keep, 6, true, HallKind.Lord);    // Maegor's Holdfast
                    Box(rk + new Vector3(8f, 0f, 8f), 20f, 12f, rk.y, 3, keep, door: true, pitched: false, merlons: true); // Thronsaal
                    Roof(rk + new Vector3(8f, 0f, 8f), 20f, 12f, rk.y + 3 * LevelHeight(keep), dark);
                    GreatHall(rk + new Vector3(8f, 0f, 8f), 20f, 12f, rk.y, keep, HallKind.Iron);
                    TowerAt(rk + new Vector3(12f, 0f, -12f), 8f, 8, keep, true);       // Tower of the Hand
                    Reserve(rk0, 36f);
                    // Great Sept of Baelor: Marmorbau mit großer Kuppel und sieben Türmen
                    Box(sept, 22f, 22f, sept.y, 3, marble, door: true, pitched: false, merlons: false);
                    SeptInterior(sept, 22f, 22f, sept.y, marble, 3 * LevelHeight(marble));
                    Dome(sept + Vector3.up * (3 * LevelHeight(marble)), 22f, marble);
                    for (int i = 0; i < 7; i++)
                    {
                        float a = i * 2f * Mathf.PI / 7f + 0.2f;
                        TowerAt(sept + new Vector3(Mathf.Cos(a) * 16f, 0f, Mathf.Sin(a) * 16f), 6f, 8, marble, true);
                    }
                    Decor(new[] { "piece_brazierfloor02" }, sept + new Vector3(0f, 0.1f, -14f));
                    Reserve(sept0, 26f);
                    // Dragonpit: hohe Rundmauer, Kuppel eingestürzt (Reste stark beschädigt)
                    RingWall(pit, 24f, 9, keep, keep, towers: false, gates: 1);
                    WithRuin(0.6f, () => Dome(pit + Vector3.up * (9 * keep.Wall.Height), 40f, keep));
                    Reserve(pit0, 28f);
                    break;
                }

                case Mark.Hightower:
                    // gestufter Turm mit Leuchtfeuer, dazu die Citadel
                    StepTower(c + new Vector3(0f, 0f, -R * 0.35f), new[] { (20f, 5), (16f, 5), (12f, 4), (8f, 4) }, keep);
                    Reserve(c + new Vector3(0f, 0f, -R * 0.35f), 14f);
                    var cit = c + new Vector3(R * 0.3f, 0f, R * 0.2f);
                    Castle(cit, 20f, 14f, keep, 2, false);
                    TowerAt(cit + new Vector3(-12f, 0f, 9f), 6f, 4, keep, true);
                    Reserve(cit, 20f);
                    break;

                case Mark.Pyramid:
                    // Große Pyramide (in Meereen 800 Fuß hoch): viele Stufen, oben die Wohnkammer
                    Pyramid(c, 52f, 11, keep, chamber: true);
                    Reserve(c, 32f);
                    foreach (var (sx, sz) in Corners)
                    {
                        var p = c + new Vector3(sx * R * 0.5f, 0f, sz * R * 0.5f);
                        Pyramid(p, 22f, 5, keep, chamber: false);
                        Reserve(p, 14f);
                    }
                    break;

                case Mark.Harrenhal:
                    // riesige Mauer, fünf gewaltige, teils geschmolzene Türme
                    RingSquare(c, 70f, 6, keep);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * 2f * Mathf.PI / 5f + 0.3f;
                        var p = c + new Vector3(Mathf.Cos(a) * 28f, 0f, Mathf.Sin(a) * 28f);
                        WithRuin(0.25f, () => TowerAt(p, 12f, 9, keep, false));
                    }
                    Box(c, 24f, 14f, c.y, 2, keep, door: true, pitched: true, merlons: false);
                    GreatHall(c, 24f, 14f, c.y, keep, HallKind.Hearths);     // Halle der hundert Herde
                    Reserve(c, 42f);
                    break;

                case Mark.Eyrie:
                    // schlanke weiße Türme, eng zusammen, sehr hoch
                    foreach (var (dx, dz, lv) in new[] { (0f, 0f, 12), (9f, 3f, 10), (-8f, 5f, 9), (3f, -9f, 10), (-6f, -7f, 8), (10f, -6f, 8), (-2f, 10f, 9) })
                        TowerAt(c + new Vector3(dx, 0f, dz), 6f, lv, keep, true);
                    {
                        var hh = c + new Vector3(0f, 0f, -18f);
                        var white = KitOf(Mat.Stone);
                        Box(hh, 16f, 12f, c.y, 2, white, door: true, pitched: false, merlons: true);
                        GreatHall(hh, 16f, 12f, c.y, white, HallKind.HighHall);
                        Reserve(hh, 11f);
                    }
                    Reserve(c, 18f);
                    break;

                case Mark.StormsEnd:
                    // ein gewaltiger runder Turm
                    // Storm's End: ein riesiger Trommelturm (die Mauer ist 30 m dick in den Büchern),
                    // innen ein Saal, außen eine runde Vormauer
                    RoundTower(c, 13f, 8, keep, true);
                    GreatHall(c, 14f, 14f, c.y, keep, HallKind.Lord);
                    RingWall(c, 24f, 4, keep, keep, towers: true, gates: 1);
                    Reserve(c, 26f);
                    break;

                case Mark.Titan:
                    // der Titan: ein riesiger Wächter am Hafen (hoher Turm), dazu Kuppelbauten
                    {
                        // Titan von Braavos: steinerner Krieger, Füße auf zwei Granitfelsen, die Einfahrt zwischen
                        // den Beinen; ein Arm mit dem Schwert erhoben, Augen aus Feuer
                        var ti = c + new Vector3(0f, 0f, -R * 0.62f);
                        var granite = KitOf(Mat.Marble).Wall != null ? KitOf(Mat.Marble) : keep;
                        float lv = LevelHeight(keep);
                        foreach (float sx in new[] { -1f, 1f })
                        {
                            Box(ti + new Vector3(sx * 9f, 0f, 0f), 10f, 10f, ti.y, 2, granite, door: false, pitched: false, merlons: false); // Felsen
                            TowerAt(ti + new Vector3(sx * 9f, 2 * LevelHeight(granite), 0f), 6f, 6, keep, false);                       // Beine
                        }
                        float hipY = ti.y + 2 * LevelHeight(granite) + 6 * lv;
                        Box(new Vector3(ti.x, hipY, ti.z), 24f, 8f, hipY, 1, keep, door: false, pitched: false, merlons: false);   // Hüfte
                        float chestY = hipY + lv;
                        Box(new Vector3(ti.x, chestY, ti.z), 14f, 8f, chestY, 4, keep, door: false, pitched: false, merlons: false); // Oberkörper
                        float shoulderY = chestY + 4 * lv;
                        Box(new Vector3(ti.x, shoulderY - lv, ti.z), 22f, 8f, shoulderY - lv, 1, keep, door: false, pitched: false, merlons: false); // Schultern
                        TowerAt(new Vector3(ti.x - 10f, shoulderY, ti.z), 4f, 4, keep, false);                            // erhobener Arm
                        Column(new Vector3(ti.x - 10f, shoulderY + 4 * lv, ti.z), keep, 8);                              // Schwert
                        TowerAt(new Vector3(ti.x + 10f, shoulderY - 4 * lv, ti.z), 4f, 3, keep, false);                  // herabhängender Arm
                        Box(new Vector3(ti.x, shoulderY, ti.z), 6f, 6f, shoulderY, 2, keep, door: false, pitched: false, merlons: true); // Kopf
                        foreach (float ex in new[] { -1.2f, 1.2f })
                            Decor(new[] { "piece_brazierceiling01", "piece_groundtorch" }, new Vector3(ti.x + ex, shoulderY + lv * 1.3f, ti.z - 3.4f)); // Feueraugen
                        Reserve(ti, 22f);
                        // House of Black and White: dunkler Tempel mit Kuppel
                        var hbw = c + new Vector3(R * 0.35f, 0f, R * 0.2f);
                        var marble = KitOf(Mat.Marble);
                        Box(hbw, 16f, 12f, c.y, 2, marble.Wall != null ? marble : keep, door: true, pitched: false, merlons: false);
                        BlackAndWhite(hbw, 16f, 12f, c.y, marble.Wall != null ? marble : keep);
                        Dome(hbw + new Vector3(0f, 2 * LevelHeight(keep), 0f), 10f, keep);
                        Reserve(hbw, 12f);
                        Castle(c, 18f, 18f, keep, 3, false); // Sealord's Palace
                        Reserve(c, 16f);
                    }
                    break;

                case Mark.Tents:
                {
                    // Horse Gate: zwei sich aufbäumende Pferde aus Bronze, deren Hufe sich über der Straße
                    // treffen (hier: zwei hohe Pfeiler mit Köpfen, darüber ein Bogen)
                    var gate = c + new Vector3(0f, 0f, -R + 10f);
                    foreach (float sx in new[] { -1f, 1f })
                    {
                        TowerAt(gate + new Vector3(sx * 7f, 0f, 0f), 4f, 7, keep, false);
                        Decor(new[] { "blackmarble_head_big01", "blackmarble_head01" }, gate + new Vector3(sx * 6f, 7 * LevelHeight(keep) + 0.5f, 0f), sx > 0 ? 270f : 90f);
                    }
                    Tiles(gate, 14f, 4f, gate.y + 7 * LevelHeight(keep) - 1f, keep.Floor);
                    // Godsway: breite Straße mit Statuen geraubter Götter auf beiden Seiten
                    var pillar = PickAny("stone_pillar", "blackmarble_column_1");
                    for (float z = -R + 22f; z < R * 0.3f; z += 10f)
                        foreach (float sx in new[] { -1f, 1f })
                        {
                            var sp = c + new Vector3(sx * 8f, 0f, z);
                            if (pillar != null) for (float py = 0f; py < 3.9f; py += Math.Max(1f, pillar.Height)) Put(pillar, new Vector3(sp.x, c.y + py, sp.z), 0f, 0.5f);
                            Decor(new[] { (z / 10f) % 2 == 0 ? "blackmarble_head_big02" : "blackmarble_head_big01", "blackmarble_head01" }, sp + new Vector3(0f, 4f, 0f), sx > 0 ? 270f : 90f);
                        }
                    // Tempel der Dosh Khaleen mit Kuppel
                    var temple = c + new Vector3(0f, 0f, R * 0.4f);
                    Box(temple, 16f, 12f, c.y, 2, keep, door: true, pitched: false, merlons: false);
                    Dome(temple + Vector3.up * (2 * LevelHeight(keep)), 12f, keep);
                    GreatHall(temple, 16f, 12f, c.y, keep, HallKind.Lord);
                    Reserve(temple, 12f);
                    break;
                }

                case Mark.Twins:
                    foreach (float sx in new[] { -1f, 1f })
                    {
                        var p = c + new Vector3(sx * 30f, 0f, 0f);
                        Castle(p, 18f, 18f, keep, 4, true);
                        Reserve(p, 18f);
                    }
                    Tiles(c, 40f, 6f, c.y + 4f, keep.Floor); // Brücke
                    break;

                case Mark.Concentric:
                    Castle(c, 24f, 20f, keep, 4, true);
                    Reserve(c, 22f);
                    if (s.Rings >= 3) Maze(c, R * 0.62f, R * 0.84f, KitOf(Mat.Stone));
                    break;

                case Mark.SunTower:
                    // Tower of the Sun mit Kuppel, daneben der Spear Tower
                    Box(c + new Vector3(-14f, 0f, -6f), 24f, 14f, c.y, 2, keep, door: true, pitched: false, merlons: true); // Old Palace
                    GreatHall(c + new Vector3(-14f, 0f, -6f), 24f, 14f, c.y, keep, HallKind.Lord);
                    TowerAt(c + new Vector3(6f, 0f, 4f), 12f, 5, keep, false);                          // Tower of the Sun
                    Dome(c + new Vector3(6f, 5 * LevelHeight(keep), 4f), 12f, keep);
                    TowerAt(c + new Vector3(22f, 0f, -10f), 6f, 10, keep, false);                       // Spear Tower
                    Column(c + new Vector3(22f, 10 * LevelHeight(keep), -10f), keep, 4);                // die Speerspitze
                    Reserve(c, 26f);
                    break;

                case Mark.CastleBlack:
                    // Holzbauten und der King's Tower aus Stein
                    TowerAt(c + new Vector3(-12f, 0f, 10f), 8f, 6, keep, true);
                    Box(c + new Vector3(10f, 0f, 8f), 20f, 10f, c.y, 1, KitOf(Mat.Wood), door: true, pitched: true, merlons: false);
                    GreatHall(c + new Vector3(10f, 0f, 8f), 20f, 10f, c.y, KitOf(Mat.Wood), HallKind.Common);
                    Box(c + new Vector3(8f, 0f, -12f), 16f, 8f, c.y, 1, KitOf(Mat.Wood), door: true, pitched: true, merlons: false);
                    Reserve(c, 26f);
                    break;

                case Mark.Hardhome:
                    // Langhaus in der Mitte, Hütten drumherum
                    Box(c, 24f, 10f, c.y, 1, house, door: true, pitched: true, merlons: false);
                    GreatHall(c, 24f, 10f, c.y, house, HallKind.Longhall);
                    Reserve(c, 16f);
                    break;

                case Mark.Pyke:
                    {
                        var pts = new[] { (0f, 0f, 8), (18f, 8f, 6), (-16f, 10f, 7), (6f, -18f, 6) };
                        foreach (var (dx, dz, lv) in pts) TowerAt(c + new Vector3(dx, 0f, dz), 7f, lv, keep, false);
                        // Brücken zwischen den Türmen (auf Höhe des niedrigeren Turms)
                        for (int q = 1; q < pts.Length; q++)
                        {
                            var a = c + new Vector3(pts[0].Item1, 0f, pts[0].Item2);
                            var b = c + new Vector3(pts[q].Item1, 0f, pts[q].Item2);
                            float by = c.y + Math.Min(pts[0].Item3, pts[q].Item3) * LevelHeight(keep) - LevelHeight(keep);
                            Bridge(a, b, by, KitOf(Mat.Wood));
                        }
                        Reserve(c, 26f);
                    }
                    break;

                case Mark.Asshai:
                    Castle(c, 26f, 26f, keep, 6, true);
                    Reserve(c, 24f);
                    break;

                case Mark.Ruins:
                    Castle(c, 22f, 18f, keep, 3, true);
                    Reserve(c, 20f);
                    break;

                case Mark.Huts:
                case Mark.Stilts:
                    break;

                default:
                    if (s_plan != null && NamedCastle(s_plan.Name, c, R, keep, house)) break;
                    if (s_plan != null && s_plan.Name == "Dragonstone")
                    {
                        Castle(c, 22f, 18f, keep, 4, true, HallKind.PaintedTable);
                        // Drachen statt Zinnen (Wasserspeier), auf allen vier Seiten
                        float top = c.y + 4 * LevelHeight(keep);
                        for (float x = -9f; x <= 9f; x += 6f)
                        {
                            Decor(new[] { "wood_dragon1", "darkwood_raven" }, new Vector3(c.x + x, top + 0.1f, c.z - 9.2f), 180f);
                            Decor(new[] { "wood_dragon1", "darkwood_raven" }, new Vector3(c.x + x, top + 0.1f, c.z + 9.2f), 0f);
                        }
                    }
                    else Castle(c, 22f, 18f, keep, 4, true);
                    Reserve(c, 20f);
                    break;
            }
        }

        /// <summary>Kuppel aus gestuften Ringen (Tower of the Sun, House of Black and White).</summary>
        static void Dome(Vector3 c, float size, Kit k)
        {
            // Halbkugel aus Bodenplatten: Ringe nach oben enger, jeweils ringsum gesetzt
            var floor = k.Floor;
            if (floor == null) return;
            float t = floor.Width, R = size / 2f;
            int rings = Math.Max(3, (int)(R / t * 1.6f));
            for (int i = 0; i < rings; i++)
            {
                float th = (i + 0.5f) / rings * Mathf.PI / 2f;
                float r = R * Mathf.Cos(th), y = c.y + R * Mathf.Sin(th) * 0.8f;
                if (r < t * 0.6f) { Put(floor, new Vector3(c.x, y, c.z), 0f, 1f); break; }
                int n = Math.Max(4, (int)Math.Ceiling(2f * Mathf.PI * r / (t * 0.95f)));
                for (int q = 0; q < n; q++)
                {
                    float a = q * 2f * Mathf.PI / n;
                    Put(floor, new Vector3(c.x + Mathf.Cos(a) * r, y, c.z + Mathf.Sin(a) * r), -a * Mathf.Rad2Deg, (i + 1f) / rings);
                }
            }
            Decor(new[] { "piece_brazierfloor01", "piece_brazierfloor02" }, new Vector3(c.x, c.y + R * 0.8f + 0.5f, c.z));
        }

        /// <summary>Niedriges Labyrinth aus Mauerstücken in einem Ring (Heckenlabyrinth von Highgarden).</summary>
        static void Maze(Vector3 c, float r0, float r1, Kit k)
        {
            if (k.Merlon == null && k.Wall == null) return;
            var piece = k.Wall;
            for (float r = r0; r < r1; r += 6f)
            {
                int n = Math.Max(8, (int)(2f * Mathf.PI * r / piece.Width));
                for (int i = 0; i < n; i++)
                {
                    if (s_rng.NextDouble() < 0.22) continue; // Durchgänge
                    float a0 = i * 2f * Mathf.PI / n, a1 = (i + 1) * 2f * Mathf.PI / n;
                    var p0 = new Vector3(c.x + Mathf.Cos(a0) * r, c.y, c.z + Mathf.Sin(a0) * r);
                    var p1 = new Vector3(c.x + Mathf.Cos(a1) * r, c.y, c.z + Mathf.Sin(a1) * r);
                    if (Math.Abs(p0.x - c.x) < 5f || Math.Abs(p0.z - c.z) < 5f) continue; // Hauptwege frei
                    Run(p0, p1, c.y, 1, piece, null);
                }
            }
            s_rings.Add((c, r0 - 3f, r1 + 3f));
        }

        /// <summary>Hängebrücke (Bodenplatten) zwischen zwei Punkten auf Höhe y.</summary>
        static void Bridge(Vector3 a, Vector3 b, float y, Kit k)
        {
            if (k.Floor == null) return;
            var d = Flat(b - a);
            float len = d.magnitude;
            if (len < 1f) return;
            var dir = d / len;
            float t = k.Floor.Width;
            float yaw = Mathf.Atan2(-dir.z, dir.x) * Mathf.Rad2Deg;
            for (float s = 3.5f; s < len - 3.5f; s += t)
            {
                var p = a + dir * s;
                Put(k.Floor, new Vector3(p.x, y, p.z), yaw, 0.5f);
            }
        }

        /// <summary>
        /// Runder Turm (Trommelturm): Mauerring aus ganzen Wandstücken, oben Plattform und Zinnen,
        /// unten eine Toröffnung nach Süden.
        /// </summary>
        static void RoundTower(Vector3 c, float r, int levels, Kit k, bool beacon)
        {
            var wall = k.Wall;
            int sides = Math.Max(6, (int)Math.Round(2f * Mathf.PI * r / wall.Width));
            r = wall.Width / (2f * Mathf.Sin(Mathf.PI / sides));
            int rows = RowsFor(k) * levels;
            if (s_ruin > 0.25f && rows > 1) rows = Math.Max(1, rows - s_rng.Next(rows / 2 + 1));
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * 2f * Mathf.PI / sides - Mathf.PI / 2f, a1 = (i + 1) * 2f * Mathf.PI / sides - Mathf.PI / 2f;
                var p0 = new Vector3(c.x + Mathf.Cos(a0) * r, c.y, c.z + Mathf.Sin(a0) * r);
                var p1 = new Vector3(c.x + Mathf.Cos(a1) * r, c.y, c.z + Mathf.Sin(a1) * r);
                var mid = (p0 + p1) / 2f;
                var dir = Flat(p1 - p0).normalized;
                float yaw = Mathf.Atan2(-dir.z, dir.x) * Mathf.Rad2Deg;
                bool doorSide = i == 0 || i == sides - 1 ? false : false;
                for (int row = 0; row < rows; row++)
                {
                    // Toröffnung im südlichsten Stück, zwei Reihen hoch
                    if (i == sides * 3 / 4 && row * wall.Height < 3.9f && r > 3f) continue;
                    Put(wall, new Vector3(mid.x, c.y + row * wall.Height, mid.z), yaw, (row + 1f) / rows);
                }
                // Zinnen
                if (k.Merlon != null && s_ruin <= 0.25f && i % 2 == 0)
                    Put(k.Merlon, new Vector3(mid.x, c.y + rows * wall.Height, mid.z), yaw, 1f);
            }
            // Pfeiler an den Ecken des Rings
            if (k.Corner != null)
                for (int i = 0; i < sides; i++)
                {
                    float a = i * 2f * Mathf.PI / sides - Mathf.PI / 2f;
                    for (float y = 0f; y < rows * wall.Height - 0.05f; y += Math.Max(0.5f, k.Corner.Height))
                        Put(k.Corner, new Vector3(c.x + Mathf.Cos(a) * r, c.y + y, c.z + Mathf.Sin(a) * r), 0f, 0.5f);
                }
            // Plattform oben (runde Fläche aus Platten)
            float top = c.y + rows * wall.Height;
            if (k.Floor != null && s_ruin <= 0.25f)
            {
                float t = k.Floor.Width;
                for (float x = -r + t / 2f; x < r; x += t)
                    for (float z = -r + t / 2f; z < r; z += t)
                        if (x * x + z * z < (r - 0.4f) * (r - 0.4f)) Put(k.Floor, new Vector3(c.x + x, top - k.Floor.Height + 0.05f, c.z + z), 0f, 1f);
            }
            if (beacon) Decor(new[] { "piece_brazierfloor02", "piece_brazierfloor01" }, new Vector3(c.x, top + 0.2f, c.z));
        }

        static readonly (float, float)[] Corners = { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) };

        static void WithRuin(float extra, Action a)
        {
            float old = s_ruin;
            s_ruin = Math.Min(0.8f, s_ruin + extra);
            try { a(); } finally { s_ruin = old; }
        }

        /// <summary>Burg: Bergfried mit vier Ecktürmen.</summary>
        static void Castle(Vector3 c, float w, float d, Kit k, int levels, bool cornerTowers, HallKind hall = HallKind.Lord)
        {
            Box(c, w, d, c.y, levels, k, door: true, pitched: false, merlons: true);
            GreatHall(c, w, d, c.y, k, hall);
            // Banner des Hauses an der Front und an den Seiten
            if (s_ruin <= 0.25f)
            {
                string banner = $"piece_banner{1 + (int)((uint)CityBuilder.Seed(s_plan) % 11u):00}";
                float by = c.y + Math.Max(2f, levels * LevelHeight(k) - 4.5f);
                foreach (float o in new[] { -w / 4f, w / 4f })
                    Decor(new[] { banner, "piece_banner01" }, new Vector3(c.x + o, by, c.z - d / 2f - 0.25f), 0f);
                Decor(new[] { banner, "piece_banner01" }, new Vector3(c.x + w / 2f + 0.25f, by, c.z), 270f);
                Decor(new[] { banner, "piece_banner01" }, new Vector3(c.x - w / 2f - 0.25f, by, c.z), 90f);
            }
            if (cornerTowers)
                foreach (var (sx, sz) in Corners)
                {
                    var tp = c + new Vector3(sx * w / 2f, 0f, sz * d / 2f);
                    if (s_style != null && s_style.RoundTowers) RoundTower(tp, 4.5f, levels + 2, k, false);
                    else TowerAt(tp, 6f, levels + 2, k, false);
                }
        }

        static void TowerAt(Vector3 c, float size, int levels, Kit k, bool beacon)
        {
            Box(c, size, size, c.y, levels, k, door: true, pitched: false, merlons: true);
            if (beacon)
            {
                float top = c.y + levels * LevelHeight(k);
                Decor(new[] { "piece_brazierfloor02", "piece_brazierfloor01" }, new Vector3(c.x, top + 0.2f, c.z));
            }
        }

        /// <summary>Gestufter Turm (Hightower): jede Stufe schmaler, oben ein Leuchtfeuer.</summary>
        static void StepTower(Vector3 c, (float size, int levels)[] steps, Kit k)
        {
            float y = c.y;
            foreach (var (size, levels) in steps)
            {
                Box(new Vector3(c.x, y, c.z), size, size, y, levels, k, door: y == c.y, pitched: false, merlons: true);
                y += levels * LevelHeight(k);
                Tiles(new Vector3(c.x, y, c.z), size, size, y, k.Floor);
            }
            Decor(new[] { "piece_brazierfloor02", "piece_brazierfloor01" }, new Vector3(c.x, y + 0.2f, c.z));
        }

        /// <summary>Stufenpyramide (Meereen, Astapor, Ghis).</summary>
        static void Pyramid(Vector3 c, float baseSize, int tiers, Kit k, bool chamber = false)
        {
            float tierH = LevelHeight(k);
            float unit = k.Wall.Width;
            float step = Math.Max(unit, (float)Math.Round(baseSize / (tiers + 2) / unit) * unit);
            float s = baseSize;
            int t = 0;
            for (; t < tiers && s - step >= 8f; t++)
            {
                float y = c.y + t * tierH;
                Box(new Vector3(c.x, y, c.z), s, s, y, 1, k, door: t == 0, pitched: false, merlons: false);
                float next = s - step;
                Band(new Vector3(c.x, y + tierH, c.z), s, next, y + tierH, k.Floor);
                s = next;
            }
            float topY = c.y + t * tierH;
            if (!chamber)
            {
                Tiles(new Vector3(c.x, topY, c.z), s, s, topY, k.Floor);
                Decor(new[] { "piece_brazierfloor02", "piece_brazierfloor01" }, new Vector3(c.x, topY + 0.2f, c.z));
                return;
            }
            // Wohnkammer auf der Spitze (wie in der Serie): Boden, Raum mit Tor und Fenstern, Terrasse
            Tiles(new Vector3(c.x, topY, c.z), s, s, topY, k.Floor);
            float room = Math.Max(8f, s - 2f * unit);
            Box(new Vector3(c.x, topY, c.z), room, room, topY, 2, k, door: true, pitched: false, merlons: true);
            Windows(new Vector3(c.x, topY, c.z), room, room, topY, 2, k, k);
            float lh = LevelHeight(k);
            if (k.Floor != null) Tiles(new Vector3(c.x, topY + lh, c.z), room, room, topY + lh, k.Floor);
            Decor(new[] { "piece_throne02", "piece_throne01" }, new Vector3(c.x, topY + 0.3f, c.z + room / 2f - 1.5f), 180f);
            Decor(new[] { "jute_carpet_blue", "jute_carpet" }, new Vector3(c.x, topY + 0.35f, c.z), 0f);
            Decor(new[] { "piece_bed02", "bed" }, new Vector3(c.x - room / 2f + 1.6f, topY + lh + 0.3f, c.z + room / 2f - 1.8f), 0f);
            Decor(new[] { "piece_table_oak", "piece_table" }, new Vector3(c.x + room / 2f - 2f, topY + 0.3f, c.z), 90f);
            foreach (var (sx, sz) in Corners)
                Decor(new[] { "piece_brazierfloor01", "piece_brazierfloor02" }, new Vector3(c.x + sx * (s / 2f - 1.2f), topY + 0.3f, c.z + sz * (s / 2f - 1.2f)));
        }

        static void Column(Vector3 p, Kit k, int count)
        {
            if (k.Pillar == null) return;
            for (int i = 0; i < count; i++) Put(k.Pillar, new Vector3(p.x, p.y + i * Math.Max(1f, k.Pillar.Height), p.z), 0f, (i + 1f) / count);
        }

        // ---------------------------------------------------------------- Häuser

        static void Houses(Style s, Vector3 c, float inner, Kit k)
        {
            if (s.Mark == Mark.Tents) { Tents(c, inner, k); return; }
            float cell = s.Mark == Mark.Huts || s.Mark == Mark.Stilts || s.Mark == Mark.Hardhome ? 15f : 17f;
            float street = 5f;
            int seed = CityBuilder.Seed(s_plan);
            int nCells = (int)(2f * inner / cell);
            for (int ix = 0; ix <= nCells; ix++)
            for (int iz = 0; iz <= nCells; iz++)
            {
                float gx = -inner + ix * cell, gz = -inner + iz * cell;
                // Hauptstraßen (Kreuz) frei lassen
                if (Math.Abs(gx) < cell * 0.6f || Math.Abs(gz) < cell * 0.6f) continue;
                var cellPos = new Vector3(c.x + gx, c.y, c.z + gz);
                if (Flat(cellPos - c).magnitude > inner - cell * 0.5f) continue;
                if (Latitude.CoastDistance(cellPos.x, cellPos.z) > -8f) continue;
                int kind = RoadNet.CellKind(seed, ix, iz);
                if (kind == 1) { if (Free(cellPos, cell * 0.35f)) Garden(cellPos, cell * 0.6f, k); continue; }
                if (kind == 2 || s_rng.NextDouble() > s.Houses) continue;
                var p = cellPos;

                float wall = k.Wall.Width;
                int nw = Math.Max(2, (int)((cell - street) / wall));
                int w = 2 + s_rng.Next(Math.Max(1, nw - 1));
                int d = Math.Max(2, Math.Min(w, 2 + s_rng.Next(Math.Max(1, nw - 2))));
                float fw = w * wall, fd = d * wall;
                if (!Free(p, Math.Max(fw, fd) * 0.7f)) continue;

                // Tür zur nächsten Hauptstraße
                int doorSide = Math.Abs(gx) < Math.Abs(gz) ? (gx > 0 ? 3 : 2) : (gz > 0 ? 0 : 1);
                if (s.Mark == Mark.Huts || s.Mark == Mark.Hardhome || s.Mark == Mark.Stilts)
                {
                    Hut(p, fw, fd, k, s.Mark == Mark.Stilts, doorSide);
                    continue;
                }
                House(s, p, fw, fd, k, doorSide);
            }
            StreetLife(s, c, inner);
        }

        /// <summary>Ein Haus in einer von mehreren Formen, mit Tür, kleiner Einrichtung und Details.</summary>
        static void House(Style s, Vector3 p, float w, float d, Kit k, int doorSide)
        {
            bool pitched = s.Pitched && k.Roof != null;
            double t = s_rng.NextDouble();
            int levels = 1 + (s.MaxLevels > 1 && s_rng.NextDouble() < 0.45 ? 1 : 0);
            float wall = k.Wall.Width;

            if (t < 0.15 && !pitched)
            {
                // Wohnturm (Essos, Dorne): schmal, hoch, Zinnen
                float sz = Math.Max(2, (int)Math.Round(Math.Min(w, d) / wall)) * wall;
                Box(p, sz, sz, p.y, levels + 1, k, door: true, pitched: false, merlons: true, doorSide: doorSide);
                Furnish(p, sz, sz, k, doorSide);
                return;
            }
            if (t < 0.35 && w >= 3 * wall)
            {
                // L-Form: Haupthaus und Anbau
                Box(p, w, d, p.y, levels, k, door: true, pitched: pitched, merlons: !pitched, doorSide: doorSide);
                float aw = Math.Max(wall, (int)Math.Round(w / 2f / wall) * wall), ad = Math.Max(wall, (int)Math.Round(d * 0.75f / wall) * wall);
                var annex = p + new Vector3(w / 2f - aw / 2f, 0f, (doorSide == 1 ? -1f : 1f) * (d / 2f + ad / 2f));
                if (Free(annex, Math.Max(aw, ad) * 0.5f))
                {
                    Box(annex, aw, ad, p.y, 1, k, door: false, pitched: pitched, merlons: false);
                    Reserve(annex, Math.Max(aw, ad) * 0.5f);
                }
                Furnish(p, w, d, k, doorSide);
                return;
            }
            Box(p, w, d, p.y, levels, k, door: true, pitched: pitched, merlons: !pitched && (levels > 1 || s_rng.NextDouble() < 0.5), doorSide: doorSide);
            // Schornstein aus Stein an einer Giebelseite (Westeros, Norden)
            if (pitched && s_rng.NextDouble() < 0.45)
            {
                var stone = KitOf(Mat.Stone).Pillar;
                if (stone != null)
                {
                    float top = p.y + levels * LevelHeight(k) + (k.Roof != null ? k.Roof.Height * Math.Max(1, (int)Math.Round(Math.Min(w, d) / 2f / Math.Max(0.5f, k.Roof.Run))) : 2f);
                    var cp = w >= d ? new Vector3(p.x + w / 2f - 0.8f, p.y, p.z) : new Vector3(p.x, p.y, p.z + d / 2f - 0.8f);
                    for (float y = p.y; y < top + 1f; y += Math.Max(1f, stone.Height)) Put(stone, new Vector3(cp.x, y, cp.z), 0f, 0.5f);
                }
            }
            // Vordach mit zwei Pfosten an manchen Häusern
            if (k.Pillar != null && s_rng.NextDouble() < 0.25)
            {
                var (dx, dz) = SideDir(doorSide);
                var front = p + new Vector3(dx * (w / 2f + 1.6f), 0f, dz * (d / 2f + 1.6f));
                var across = new Vector3(Math.Abs(dz), 0f, Math.Abs(dx));
                foreach (float o in new[] { -1.6f, 1.6f })
                {
                    var q = front + across * o;
                    for (float y = 0f; y < 2.4f; y += Math.Max(1f, k.Pillar.Height)) Put(k.Pillar, new Vector3(q.x, p.y + y, q.z), 0f, 0.5f);
                }
                Tiles(new Vector3(front.x, p.y + 2.4f, front.z), Math.Abs(dz) > 0 ? 4f : 3f, Math.Abs(dx) > 0 ? 4f : 3f, p.y + 2.4f, k.Floor);
            }
            Furnish(p, w, d, k, doorSide);
        }

        static (float dx, float dz) SideDir(int side) =>
            side == 0 ? (0f, -1f) : side == 1 ? (0f, 1f) : side == 2 ? (1f, 0f) : (-1f, 0f);

        /// <summary>Tür im offenen Wandstück; bei breiten Wandstücken (Stein) schmale Füllteile daneben.</summary>
        static void Door(Vector3 c, float w, float d, float y0, int side, Kit k, float wallHeight = 4f)
        {
            // Niedrige Hütten (Wand unter 4 m): kleine Holztür statt großem Tor
            if (wallHeight < 3.9f)
            {
                var (sdx, sdz) = SideDir(side);
                bool ax = side < 2;
                float l = ax ? w : d;
                int nn = Math.Max(1, (int)Math.Round(l / k.Wall.Width));
                float sg = l / nn, oo = -l / 2f + sg * (nn / 2 + 0.5f);
                var dp = ax ? new Vector3(c.x + oo, y0, c.z + sdz * d / 2f) : new Vector3(c.x + sdx * w / 2f, y0, c.z + oo);
                if (s_ruin <= 0.3f) DoorPiece(dp, ax ? (sdz < 0 ? 0f : 180f) : (sdx > 0 ? 270f : 90f));
                return;
            }
            var wall = k.Wall;
            bool alongX = side < 2;
            float len = alongX ? w : d;
            int n = Math.Max(1, (int)Math.Round(len / wall.Width));
            float seg = len / n;
            float o = -len / 2f + seg * (n / 2 + 0.5f);
            var (dx, dz) = SideDir(side);
            Vector3 p = alongX ? new Vector3(c.x + o, y0, c.z + dz * d / 2f) : new Vector3(c.x + dx * w / 2f, y0, c.z + o);
            float yaw = alongX ? 0f : 90f;
            var dirAlong = alongX ? Vector3.right : Vector3.forward;
            bool dbl = k.DoubleDoor && k.Door != null && seg >= 2f * k.Door.Width - 0.1f;
            float doorW = k.Door != null ? k.Door.Width * (dbl ? 2f : 1f) : 2f, doorH = k.Door != null ? k.Door.Height : 3f;
            // Füllteile neben dem Tor, wenn das Wandstück breiter ist
            if (seg > doorW + 0.4f && k.Merlon != null)
            {
                float fill = (seg - doorW) / 2f;
                foreach (float sgn in new[] { -1f, 1f })
                    for (float fy = 0f; fy < 4f - 0.05f; fy += Math.Max(0.5f, k.Merlon.Height))
                        for (float fo = doorW / 2f + k.Merlon.Width / 2f; fo < doorW / 2f + fill; fo += k.Merlon.Width)
                            Put(k.Merlon, p + dirAlong * (sgn * fo) + new Vector3(0f, fy, 0f), yaw, 0.3f);
            }
            // über dem Tor bis zur Höhe von zwei Wandreihen schließen (über die ganze Torbreite)
            if (k.DoorTop != null && doorH < 3.95f)
            {
                int nt = Math.Max(1, (int)Math.Round(doorW / Math.Max(0.5f, k.DoorTop.Width)));
                float tw = doorW / nt;
                for (float fy = doorH; fy < 4f - 0.05f; fy += Math.Max(0.5f, k.DoorTop.Height))
                    for (int q = 0; q < nt; q++)
                        Put(k.DoorTop, p + dirAlong * (-doorW / 2f + tw * (q + 0.5f)) + new Vector3(0f, fy, 0f), yaw, 0.5f);
            }
            if (s_ruin > 0.3f) return; // Ruinen ohne Türen
            if (k.Door == null) { DoorPiece(p, alongX ? (dz < 0 ? 0f : 180f) : (dx > 0 ? 270f : 90f)); return; }
            if (dbl)
            {
                // zwei Flügel, der rechte gespiegelt: zusammen ein ganzes Tor
                // Scharniere außen, Griffe in der Mitte (wie ein großes Tor)
                Put(k.Door, p + dirAlong * (k.Door.Width / 2f), yaw, 0.2f);
                Put(k.Door, p - dirAlong * (k.Door.Width / 2f), yaw + 180f, 0.2f);
            }
            else Put(k.Door, p, yaw, 0.2f);
        }

        static readonly Dictionary<string, Vector3> s_doorCenter = new Dictionary<string, Vector3>();

        /// <summary>Tür so setzen, dass ihre Mitte (nicht die Angel) in der Öffnung liegt.</summary>
        static void DoorPiece(Vector3 p, float yaw)
        {
            const string name = "wood_door";
            var prefab = ZNetScene.instance.GetPrefab(name);
            if (prefab == null) return;
            if (!s_doorCenter.TryGetValue(name, out var mid))
            {
                Bounds? b = null;
                foreach (var col in prefab.GetComponentsInChildren<Collider>(true))
                {
                    if (col.isTrigger) continue;
                    Bounds lb;
                    if (col is BoxCollider bc) lb = new Bounds(bc.center, bc.size);
                    else if (col is MeshCollider mc && mc.sharedMesh != null) lb = mc.sharedMesh.bounds;
                    else continue;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = new Vector3((i & 1) == 0 ? lb.min.x : lb.max.x, (i & 2) == 0 ? lb.min.y : lb.max.y, (i & 4) == 0 ? lb.min.z : lb.max.z);
                        var lp = prefab.transform.InverseTransformPoint(col.transform.TransformPoint(corner));
                        if (b == null) b = new Bounds(lp, Vector3.zero); else { var bb = b.Value; bb.Encapsulate(lp); b = bb; }
                    }
                }
                mid = b.HasValue ? new Vector3(b.Value.center.x, b.Value.min.y, b.Value.center.z) : Vector3.zero;
                s_doorCenter[name] = mid;
            }
            if (s_dry) return;
            p = Tf(p); yaw = TfYaw(yaw);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            MageTower.Spawn(name, p - rot * mid, rot);
            s_count++;
        }

        /// <summary>Garten: bestelltes Beet (gemalt), Zaun aus Pfosten und Balken, ein Baum.</summary>
        static void Garden(Vector3 c, float size, Kit k)
        {
            var post = PickAny("wood_pole", "wood_pole2");
            var beam = Pick("wood_beam", "wood_beam_1");
            float h = size / 2f;
            // Pfosten an jeder Balkenstoßstelle, damit nichts schwebt
            if (post != null)
            {
                float step = beam != null ? beam.Width : 2f;
                int n = Math.Max(1, (int)Math.Round(size / step));
                float seg = size / n;
                for (int i = 0; i <= n; i++)
                {
                    float o = -h + seg * i;
                    foreach (var q in new[] { new Vector3(c.x + o, c.y, c.z - h), new Vector3(c.x + o, c.y, c.z + h),
                                              new Vector3(c.x - h, c.y, c.z + o), new Vector3(c.x + h, c.y, c.z + o) })
                        Put(post, q, 0f, 0.2f);
                }
            }
            if (beam != null)
            {
                var a = new Vector3(c.x - h, c.y + 0.8f, c.z - h); var b = new Vector3(c.x + h, c.y + 0.8f, c.z - h);
                var e = new Vector3(c.x + h, c.y + 0.8f, c.z + h); var f = new Vector3(c.x - h, c.y + 0.8f, c.z + h);
                var gate = new List<Vector3> { (a + b) / 2f };
                Run(a, b, c.y + 0.8f, 1, beam, gate);
                Run(b, e, c.y + 0.8f, 1, beam, null);
                Run(e, f, c.y + 0.8f, 1, beam, null);
                Run(f, a, c.y + 0.8f, 1, beam, null);
            }
            if (s_rng.NextDouble() < 0.6) Tree(c + new Vector3(h * 0.5f, 0f, h * 0.5f));
            Reserve(c, h + 1f);
        }

        /// <summary>Ein Baum passend zum Biom.</summary>
        static void Tree(Vector3 p)
        {
            string[] names;
            if (s_biome == Latitude.BlackForest || s_biome == Latitude.Mountain || s_biome == Latitude.DeepNorth) names = new[] { "FirTree_small", "FirTree_small_dead", "Beech_small1" };
            else if (s_biome == Latitude.Swamp) names = new[] { "SwampTree1", "Beech1" };
            else if (s_biome == Latitude.Plains) names = new[] { "Birch1_aut", "Birch1", "Beech1" };
            else if (s_biome == Latitude.Mistlands) names = new[] { "YggaShoot_small1", "YggaShoot1", "Beech1" };
            else if (s_biome == Latitude.AshLands) names = new[] { "AshlandsTree3", "AshlandsTree1", "Beech1" };
            else names = new[] { "Beech_small1", "Beech_small2", "Birch1" };
            Decor(names, p, (float)s_rng.NextDouble() * 360f);
        }

        /// <summary>Minimale Einrichtung: Teppich und ein bis zwei Möbel an der Rückwand.</summary>
        static void Furnish(Vector3 p, float w, float d, Kit k, int doorSide)
        {
            if (s_ruin > 0.2f || s_rng.NextDouble() < 0.25 || w < 4f || d < 4f) return;
            var (dx, dz) = SideDir(doorSide);
            var back = p - new Vector3(dx * (w / 2f - 1.3f), 0f, dz * (d / 2f - 1.3f));
            float yaw = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            double t = s_rng.NextDouble();
            if (t < 0.4) Decor(new[] { "piece_table", "piece_table_oak" }, back, yaw);
            else if (t < 0.7) Decor(new[] { "bed", "piece_bed02" }, back, yaw);
            else Decor(new[] { "piece_chest_wood", "piece_chest" }, back, yaw);
            if (s_rng.NextDouble() < 0.5) Decor(new[] { "rug_deer", "jute_carpet", "rug_fur" }, new Vector3(p.x, p.y + 0.05f, p.z), yaw);
            if (s_rng.NextDouble() < 0.35) Decor(new[] { "piece_bench01", "piece_chair" }, p + new Vector3(-dz * 1.2f, 0f, dx * 1.2f), yaw);
        }

        /// <summary>Fackeln an den Hauptstraßen, ein Brunnen und Marktstände am Platz.</summary>
        static void StreetLife(Style s, Vector3 c, float inner)
        {
            if (s.Mark == Mark.Ruins) return;
            string[] torch = s.HouseMat == Mat.Stone || s.HouseMat == Mat.Marble
                ? new[] { "piece_groundtorch", "piece_groundtorch_wood" } : new[] { "piece_groundtorch_wood", "piece_groundtorch" };

            // Statue in der Stadtmitte
            CenterStatue(c);
            // Marktstände mit Dach rund um den Platz, dazu Bäume an den Ecken
            var pole = PickAny("wood_pole2", "wood_pole");
            var roof = KitOf(Mat.Dark).Roof;
            for (int i = 0; i < 8; i++)
            {
                float a = (i + 0.5f) * Mathf.PI / 4f;
                var q = c + new Vector3(Mathf.Cos(a) * 14f, 0f, Mathf.Sin(a) * 14f);
                if (!Free(q, 2.5f) || s_rng.NextDouble() < 0.25) continue;
                if (pole != null && roof != null)
                {
                    foreach (var (sx, sz) in Corners)
                        for (float y = 0f; y < 2.2f; y += Math.Max(1f, pole.Height))
                            Put(pole, new Vector3(q.x + sx * 1.8f, c.y + y, q.z + sz * 1.3f), 0f, 0.3f);
                    Roof(new Vector3(q.x, c.y, q.z), 4f, 4f, c.y + 2.2f, roof);
                }
                Decor(new[] { "piece_table", "piece_table_oak" }, q, Mathf.Atan2(c.x - q.x, c.z - q.z) * Mathf.Rad2Deg);
                if (s_rng.NextDouble() < 0.5) Decor(new[] { "piece_chest_wood" }, q + (q - c).normalized * 1.2f, 0f);
            }
            foreach (var (sx, sz) in Corners)
            {
                var q = c + new Vector3(sx * 20f, 0f, sz * 20f);
                if (Free(q, 2f)) Tree(q);
            }
        }

        static void Hut(Vector3 p, float w, float d, Kit k, bool stilts, int doorSide = 0)
        {
            float y = p.y;
            if (stilts && k.Pillar != null)
            {
                foreach (var (sx, sz) in Corners) Column(new Vector3(p.x + sx * w / 2f, y, p.z + sz * d / 2f), k, 1);
                y += Math.Max(1.5f, k.Pillar.Height);
                Tiles(new Vector3(p.x, y, p.z), w, d, y, k.Floor);
            }
            Box(new Vector3(p.x, y, p.z), w, d, y, 1, k, door: true, pitched: k.Roof != null, merlons: false, rowsPerLevel: 1, doorSide: doorSide);
            if (!stilts && s_rng.NextDouble() < 0.5) Decor(new[] { "fire_pit", "hearth" }, new Vector3(p.x + (w / 2f + 2.5f), p.y, p.z), 0f);
        }

        /// <summary>Dothraki-Lager: Gruppen aus Zelten um eine Feuerstelle, größere Zelte der Khals, Pferdepfähle.</summary>
        static void Tents(Vector3 c, float inner, Kit k)
        {
            var roof = k.Roof ?? Pick("darkwood_roof_45", "wood_roof_45");
            var pole = PickAny("wood_pole2", "darkwood_pole");
            if (roof == null) return;
            for (int camp = 0; camp < 220; camp++)
            {
                float a = (float)s_rng.NextDouble() * 2f * Mathf.PI, r = 18f + Mathf.Sqrt((float)s_rng.NextDouble()) * (inner - 22f);
                var cc = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                if (Math.Abs(cc.x - c.x) < 13f || !Free(cc, 10f)) continue; // Godsway frei
                Reserve(cc, 10f);
                Decor(new[] { "fire_pit" }, cc, 0f);
                int n = 3 + s_rng.Next(3);
                for (int t = 0; t < n; t++)
                {
                    float ta = t * 2f * Mathf.PI / n + (float)s_rng.NextDouble() * 0.4f;
                    bool big = t == 0 && s_rng.NextDouble() < 0.5;
                    float w = big ? 10f : 6f, d = big ? 8f : 4f;
                    var tp = cc + new Vector3(Mathf.Cos(ta) * (big ? 8.5f : 7f), 0f, Mathf.Sin(ta) * (big ? 8.5f : 7f));
                    WithFrame(tp, -ta * Mathf.Rad2Deg + 90f, () =>
                    {
                        // Zelt: Stangen an den Ecken, Stoffdach darüber
                        if (pole != null)
                            foreach (var (sx, sz) in Corners) Put(pole, new Vector3(tp.x + sx * (w / 2f - 0.2f), tp.y, tp.z + sz * (d / 2f - 0.2f)), 0f, 0.3f);
                        Roof(tp, w, d, tp.y + 1.4f, roof);
                        Decor(new[] { "rug_hare", "rug_straw" }, new Vector3(tp.x, tp.y + 0.03f, tp.z), 0f);
                    });
                }
                // Pferdepfahl
                if (pole != null && s_rng.NextDouble() < 0.6)
                    Put(pole, cc + new Vector3(4f, 0f, -4f), 0f, 0.3f);
            }
        }

        // ---------------------------------------------------------------- Grundformen

        static float LevelHeight(Kit k) => Math.Max(1f, k.Wall.Height) * RowsFor(k);

        /// <summary>Wandreihen je Stockwerk (etwa 4 m).</summary>
        static int RowsFor(Kit k) => Math.Max(1, (int)Math.Round(4f / Math.Max(1f, k.Wall.Height)));

        /// <summary>
        /// Rechteckiger Bau (achsparallel): Wände je Stockwerk, oben Satteldach oder flaches Dach mit Zinnen.
        /// Tür in der Mitte der Südseite.
        /// </summary>
        static void Box(Vector3 c, float w, float d, float y0, int levels, Kit k, bool door, bool pitched, bool merlons, int rowsPerLevel = 0, int doorSide = 0, bool top = true)
        {
            var wall = k.Wall;
            // Maße auf ganze Wandstücke runden, damit an den Ecken keine Lücken bleiben
            w = Math.Max(1, (int)Math.Round(w / wall.Width)) * wall.Width;
            d = Math.Max(1, (int)Math.Round(d / wall.Width)) * wall.Width;
            if (pitched && k.Roof != null)
            {
                // Spannweite = ganze Dachreihen auf beiden Seiten (sonst bleibt am First eine Lücke)
                float run2 = 2f * Math.Max(0.5f, k.Roof.Run);
                if (w >= d) d = Math.Max(1, (int)Math.Round(d / run2)) * run2;
                else w = Math.Max(1, (int)Math.Round(w / run2)) * run2;
            }
            int rows = (rowsPerLevel > 0 ? rowsPerLevel : RowsFor(k)) * levels;
            if (s_ruin > 0.25f && rows > 1)
            {
                rows = Math.Max(1, rows - s_rng.Next(rows / 2 + 1));
                pitched = false;
                merlons = false;
            }
            for (int r = 0; r < rows; r++)
            {
                float y = y0 + r * wall.Height;
                float frac = (r + 1f) / rows;
                bool doorRow = door && r * wall.Height < 3.9f;
                for (int sd = 0; sd < 4; sd++) Side(c, w, d, y, sd, wall, doorRow && sd == doorSide, frac);
            }
            CornerPosts(c, w, d, y0, rows * wall.Height, k);
            if (door) Door(c, w, d, y0, doorSide, k, rows * wall.Height);
            float topY = y0 + rows * wall.Height;
            if (pitched && k.Roof != null) Roof(c, w, d, topY, k);
            else
            {
                if (top && k.Floor != null && s_ruin <= 0.25f) Tiles(new Vector3(c.x, topY, c.z), w, d, topY, k.Floor);
                if (merlons && k.Merlon != null)
                {
                    float step = Math.Max(k.Merlon.Width, 1f) * 2f;
                    for (float x = -w / 2f + k.Merlon.Width / 2f; x < w / 2f; x += step)
                    {
                        Put(k.Merlon, new Vector3(c.x + x, topY, c.z + d / 2f), 0f, 1f);
                        Put(k.Merlon, new Vector3(c.x + x, topY, c.z - d / 2f), 0f, 1f);
                    }
                    for (float z = -d / 2f + k.Merlon.Width / 2f; z < d / 2f; z += step)
                    {
                        Put(k.Merlon, new Vector3(c.x + w / 2f, topY, c.z + z), 90f, 1f);
                        Put(k.Merlon, new Vector3(c.x - w / 2f, topY, c.z + z), 90f, 1f);
                    }
                }
            }
        }

        /// <summary>Pfosten/Säulen an den vier Ecken: schließen die Lücke, wo zwei Wände zusammenstoßen.</summary>
        static void CornerPosts(Vector3 c, float w, float d, float y0, float height, Kit k)
        {
            var post = k.Corner;
            if (post == null) return;
            float step = Math.Max(0.5f, post.Height);
            foreach (var (sx, sz) in Corners)
                for (float y = 0f; y < height - 0.05f; y += step)
                    Put(post, new Vector3(c.x + sx * w / 2f, y0 + y, c.z + sz * d / 2f), 0f, (y + step) / height);
        }

        /// <summary>Eine Wandseite: 0 = Süd, 1 = Nord, 2 = Ost, 3 = West.</summary>
        static void Side(Vector3 c, float w, float d, float y, int side, PieceBox wall, bool door, float frac)
        {
            bool alongX = side < 2;
            float len = alongX ? w : d;
            int n = Math.Max(1, (int)Math.Round(len / wall.Width));
            float seg = len / n;
            for (int i = 0; i < n; i++)
            {
                float o = -len / 2f + seg * (i + 0.5f);
                if (door && i == n / 2) continue;
                Vector3 p;
                switch (side)
                {
                    case 0: p = new Vector3(c.x + o, y, c.z - d / 2f); break;
                    case 1: p = new Vector3(c.x + o, y, c.z + d / 2f); break;
                    case 2: p = new Vector3(c.x + w / 2f, y, c.z + o); break;
                    default: p = new Vector3(c.x - w / 2f, y, c.z + o); break;
                }
                Put(wall, p, alongX ? 0f : 90f, frac);
            }
        }

        /// <summary>Satteldach, First entlang der längeren Seite, mit Firstteil und geschlossenen Giebeln.</summary>
        static void Roof(Vector3 c, float w, float d, float y, Kit k) => Roof(c, w, d, y, k.Roof, k.RoofTop, k.Gable, k.Wall);

        static void Roof(Vector3 c, float w, float d, float y, PieceBox roof, PieceBox top = null, PieceBox gable = null, PieceBox gableWall = null)
        {
            bool ridgeX = w >= d;
            float span = ridgeX ? d : w, len = ridgeX ? w : d;
            float run = Math.Max(0.5f, roof.Run), rise = roof.Height;
            int rows = Math.Max(1, (int)Math.Round(span / 2f / run));
            int cols = Math.Max(1, (int)Math.Round(len / roof.StairWidth));
            float colW = len / cols;
            Vector3 along = ridgeX ? Vector3.right : Vector3.forward;   // Firstrichtung
            Vector3 across = ridgeX ? Vector3.forward : Vector3.right;  // quer dazu
            for (int sIdx = 0; sIdx < 2; sIdx++)
            {
                float s = sIdx == 0 ? 1f : -1f;
                for (int i = 0; i < rows; i++)
                {
                    float off = s * (span / 2f - run * (i + 0.5f));
                    float yy = y + rise * i;
                    for (int j = 0; j < cols; j++)
                    {
                        float a = -len / 2f + colW * (j + 0.5f);
                        var p = c + along * a + across * off;
                        PutSloped(roof, new Vector3(p.x, yy, p.z), -s * across, 1f);
                    }
                }
            }
            // Firstteil
            float ridgeY = y + rise * rows;
            if (top != null)
            {
                int n = Math.Max(1, (int)Math.Round(len / top.Width));
                float seg = len / n;
                for (int j = 0; j < n; j++)
                {
                    var p = c + along * (-len / 2f + seg * (j + 0.5f));
                    Put(top, new Vector3(p.x, ridgeY - top.Height, p.z), ridgeX ? 0f : 90f, 1f);
                }
            }
            // Giebel: Wandstücke stufenweise, außen je ein Dreieck
            if (gableWall == null) return;
            float gw = gableWall.Width, gh = Math.Max(0.5f, gableWall.Height);
            foreach (float e in new[] { -1f, 1f })
            {
                var end = c + along * (e * len / 2f);
                for (int i = 0; i < rows; i++)
                {
                    float yy = y + rise * i;
                    float half = span / 2f - run * (i + 1);
                    if (half > 0.1f)
                    {
                        int n = Math.Max(1, (int)Math.Round(2f * half / gw));
                        float seg = 2f * half / n;
                        for (int q = 0; q < n; q++)
                        {
                            var p = end + across * (-half + seg * (q + 0.5f));
                            for (float hy = 0f; hy < rise - 0.05f; hy += gh)
                                Put(gableWall, new Vector3(p.x, yy + hy, p.z), Yaw(across), 1f);
                        }
                    }
                    if (gable != null && gable.TallX != 0f)
                        foreach (float side in new[] { -1f, 1f })
                        {
                            var p = end + across * (side * (half + run / 2f));
                            PutTriangle(gable, new Vector3(p.x, yy, p.z), across, -side * across);
                        }
                }
            }
        }

        /// <summary>Drehung, bei der die Breite eines Teils entlang <paramref name="u"/> liegt.</summary>
        static float Yaw(Vector3 u) => Mathf.Atan2(-u.z, u.x) * Mathf.Rad2Deg;

        static void PutTriangle(PieceBox box, Vector3 p, Vector3 widthDir, Vector3 tallDir)
        {
            if (s_dry) return;
            float yaw = Yaw(widthDir);
            if (Vector3.Dot(widthDir * box.TallX, tallDir) < 0f) yaw += 180f;
            p = Tf(p); yaw = TfYaw(yaw);
            // Place dreht Teile, deren Breite entlang z liegt, selbst um 90°: hier direkt setzen
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var localMid = new Vector3((box.Min.x + box.Max.x) / 2f, box.Min.y, (box.Min.z + box.Max.z) / 2f);
            Damage(MageTower.Spawn(box.Prefab, p - rot * localMid, rot), box.Prefab, 1f);
            Occupy(p);
            s_count++;
        }

        static void Tiles(Vector3 c, float w, float d, float y, PieceBox floor)
        {
            if (floor == null) return;
            float t = floor.Width;
            int nx = Math.Max(1, (int)Math.Round(w / t)), nz = Math.Max(1, (int)Math.Round(d / t));
            for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
                Put(floor, new Vector3(c.x - w / 2f + t * (ix + 0.5f), y, c.z - d / 2f + t * (iz + 0.5f)), 0f, 1f);
        }

        /// <summary>Quadratischer Rand zwischen zwei Größen (Pyramidenabsätze).</summary>
        static void Band(Vector3 c, float outer, float inner, float y, PieceBox floor)
        {
            if (floor == null) return;
            float t = floor.Width;
            int n = Math.Max(1, (int)Math.Round(outer / t));
            for (int ix = 0; ix < n; ix++)
            for (int iz = 0; iz < n; iz++)
            {
                float lx = -outer / 2f + t * (ix + 0.5f), lz = -outer / 2f + t * (iz + 0.5f);
                if (Math.Abs(lx) < inner / 2f - 0.1f && Math.Abs(lz) < inner / 2f - 0.1f) continue;
                Put(floor, new Vector3(c.x + lx, y, c.z + lz), 0f, 1f);
            }
        }

        /// <summary>Runde Stadtmauer (Vieleck) mit Toren an den vier Hauptstraßen und Türmen.</summary>
        static void RingWall(Vector3 c, float r, int rows, Kit wallKit, Kit towerKit, bool towers, int gates = 4)
        {
            var wall = wallKit.Wall;
            // Seitenlänge = ganze Mauerstücke, Radius passend dazu
            int per = Math.Max(2, (int)Math.Round(16f / wall.Width));
            float chord = per * wall.Width;
            int sides = Math.Max(8, (int)Math.Round(2f * Mathf.PI * r / chord));
            r = chord / (2f * Mathf.Sin(Mathf.PI / sides));
            var gatePts = new List<Vector3>();
            for (int g = 0; g < gates; g++)
            {
                float a = -Mathf.PI / 2f + g * Mathf.PI / 2f;
                gatePts.Add(new Vector3(c.x + Mathf.Cos(a) * r, c.y, c.z + Mathf.Sin(a) * r));
            }
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * 2f * Mathf.PI / sides, a1 = (i + 1) * 2f * Mathf.PI / sides;
                var p0 = new Vector3(c.x + Mathf.Cos(a0) * r, c.y, c.z + Mathf.Sin(a0) * r);
                var p1 = new Vector3(c.x + Mathf.Cos(a1) * r, c.y, c.z + Mathf.Sin(a1) * r);
                Run(p0, p1, c.y, rows, wall, gatePts);
                // Pfeiler an der Ecke schließt den Spalt zwischen zwei Mauerstücken
                if (wallKit.Pillar != null)
                    for (float py = 0f; py < rows * wall.Height - 0.1f; py += Math.Max(1f, wallKit.Pillar.Height))
                        Put(wallKit.Pillar, new Vector3(p0.x, c.y + py, p0.z), a0 * Mathf.Rad2Deg, (py + 1f) / (rows * wall.Height));
                if (towers && i % 3 == 0 && !gatePts.Any(g => Flat(g - p0).magnitude < 10f))
                    TowerAt(p0, 5f, Math.Max(2, rows * (int)Math.Round(wall.Height) / 4 + 1), towerKit, false);
            }
            if (towers)
                foreach (var g in gatePts)
                {
                    var dir = Flat(g - c).normalized;
                    var side = new Vector3(-dir.z, 0f, dir.x);
                    TowerAt(g + side * 6f, 5f, Math.Max(2, rows * (int)Math.Round(wall.Height) / 4 + 2), towerKit, true);
                    TowerAt(g - side * 6f, 5f, Math.Max(2, rows * (int)Math.Round(wall.Height) / 4 + 2), towerKit, true);
                }
        }

        /// <summary>Quadratische Mauer (Burghof).</summary>
        static void RingSquare(Vector3 c, float size, int rows, Kit k)
        {
            var gate = new List<Vector3> { new Vector3(c.x, c.y, c.z - size / 2f) };
            var a = new Vector3(c.x - size / 2f, c.y, c.z - size / 2f);
            var b = new Vector3(c.x + size / 2f, c.y, c.z - size / 2f);
            var e = new Vector3(c.x + size / 2f, c.y, c.z + size / 2f);
            var f = new Vector3(c.x - size / 2f, c.y, c.z + size / 2f);
            Run(a, b, c.y, rows, k.Wall, gate);
            Run(b, e, c.y, rows, k.Wall, gate);
            Run(e, f, c.y, rows, k.Wall, gate);
            Run(f, a, c.y, rows, k.Wall, gate);
        }

        /// <summary>Gerade Mauer von a nach b (beliebige Richtung), mit Torlücken.</summary>
        static void Run(Vector3 a, Vector3 b, float y0, int rows, PieceBox wall, List<Vector3> gates)
        {
            var delta = Flat(b - a);
            float len = delta.magnitude;
            if (len < 0.5f) return;
            var dir = delta / len;
            int n = Math.Max(1, (int)Math.Round(len / wall.Width));
            float seg = len / n;
            float yaw = Mathf.Atan2(-dir.z, dir.x) * Mathf.Rad2Deg;
            for (int i = 0; i < n; i++)
            {
                var p = a + dir * (seg * (i + 0.5f));
                bool gap = gates != null && gates.Any(g => Flat(g - p).magnitude < 3f);
                for (int r = 0; r < rows; r++)
                {
                    float y = y0 + r * wall.Height;
                    if (gap && y - y0 < 4f) continue;
                    Put(wall, new Vector3(p.x, y, p.z), yaw, (r + 1f) / rows);
                }
            }
        }

        // ---------------------------------------------------------------- Setzen

        static bool s_dry;
        static readonly HashSet<long> s_occupied = new HashSet<long>();

        static void Occupy(Vector3 p)
        {
            int x = Mathf.FloorToInt(p.x / 2f), z = Mathf.FloorToInt(p.z / 2f);
            s_occupied.Add(((long)x << 32) ^ (uint)z);
        }

        /// <summary>Steht im Umkreis von r Metern ein gebautes Teil?</summary>
        static bool Occupied(Vector3 p, float r)
        {
            int n = Mathf.CeilToInt(r / 2f);
            int x0 = Mathf.FloorToInt(p.x / 2f), z0 = Mathf.FloorToInt(p.z / 2f);
            for (int dx = -n; dx <= n; dx++)
            for (int dz = -n; dz <= n; dz++)
                if (s_occupied.Contains(((long)(x0 + dx) << 32) ^ (uint)(z0 + dz))) return true;
            return false;
        }

        static void Put(PieceBox box, Vector3 p, float yaw, float heightFrac)
        {
            if (box == null || s_dry) return;
            p = Tf(p); yaw = TfYaw(yaw);
            if (s_water && Latitude.CoastDistance(p.x, p.z) > -1f) return; // nicht ins Meer bauen
            Damage(box.Place(p, yaw), box.Prefab, heightFrac);
            Occupy(p);
            s_count++;
        }

        static void PutSloped(PieceBox box, Vector3 p, Vector3 riseDir, float heightFrac)
        {
            if (box == null || s_dry) return;
            Damage(box.PlaceSloped(Tf(p), TfDir(riseDir)), box.Prefab, heightFrac);
            Occupy(Tf(p));
            s_count++;
        }

        static readonly Dictionary<string, float> s_maxHp = new Dictionary<string, float>();

        /// <summary>
        /// Ruinen und Altersspuren: Bauteile bekommen weniger Haltbarkeit (Valheim zeigt dann Risse),
        /// statt dass Steine fehlen. In Ruinen sind alle Teile stark beschädigt, sonst nur einzelne leicht.
        /// </summary>
        static void Damage(ZDO zdo, string prefab, float heightFrac)
        {
            if (zdo == null) return;
            float f;
            if (s_ruin > 0f) f = 0.05f + (float)s_rng.NextDouble() * 0.45f * (1f - 0.4f * heightFrac);
            else return;
            if (!s_maxHp.TryGetValue(prefab, out float max))
            {
                var wnt = ZNetScene.instance.GetPrefab(prefab)?.GetComponent<WearNTear>();
                s_maxHp[prefab] = max = wnt != null ? wnt.m_health : 0f;
            }
            if (max > 0f) zdo.Set(ZDOVars.s_health, max * f);
        }

        static void Decor(string[] names, Vector3 p, float yaw = 0f)
        {
            if (s_dry) return;
            p = Tf(p); yaw = TfYaw(yaw);
            foreach (var n in names)
            {
                if (ZNetScene.instance.GetPrefab(n) == null) continue;
                MageTower.Spawn(n, p, Quaternion.Euler(0f, yaw, 0f));
                Occupy(p);
                s_count++;
                return;
            }
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
