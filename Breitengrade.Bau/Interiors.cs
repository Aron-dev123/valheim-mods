using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Breitengrade.Bau
{
    /// <summary>
    /// Innenräume der Hauptgebäude nach der Lore. Alle Räume sind achsparallel, Tür auf der Südseite (−z),
    /// der wichtigste Platz (Thron, Altar) an der Nordwand.
    /// </summary>
    public static partial class Builder
    {
        public enum HallKind { Lord, North, Iron, Common, Hearths, Longhall, PaintedTable, HighHall }

        // ---------------------------------------------------------------- Bausteine

        /// <summary>Boden aus Platten, Oberkante auf <paramref name="top"/>.</summary>
        static void FloorAt(Vector3 c, float w, float d, float top, PieceBox floor)
        {
            if (floor == null) return;
            float y = top - floor.Height;
            Tiles(new Vector3(c.x, y, c.z), w, d, y, floor);
        }

        /// <summary>Wandfackeln an den Innenseiten der Seitenwände.</summary>
        static void WallTorches(Vector3 c, float w, float d, float y, float step = 4f)
        {
            for (float z = -d / 2f + 2.5f; z < d / 2f - 1.5f; z += step)
            {
                Decor(new[] { "piece_walltorch" }, new Vector3(c.x + w / 2f - 0.45f, y + 2.4f, c.z + z), 180f);
                Decor(new[] { "piece_walltorch" }, new Vector3(c.x - w / 2f + 0.45f, y + 2.4f, c.z + z), 0f);
            }
        }

        static string HouseBanner() => $"piece_banner{1 + (int)((uint)CityBuilder.Seed(s_plan) % 11u):00}";

        /// <summary>Banner an der Rückwand (hinter dem Thron).</summary>
        static void BackBanners(Vector3 c, float w, float d, float y, int count, float height = 3.6f)
        {
            string banner = HouseBanner();
            for (int i = 0; i < count; i++)
            {
                float x = count == 1 ? 0f : -w * 0.35f + i * (w * 0.7f / (count - 1));
                Decor(new[] { banner, "piece_banner01" }, new Vector3(c.x + x, y + height, c.z + d / 2f - 0.35f), 90f);
            }
        }

        /// <summary>Lange Tafel aus Eichentischen entlang z, mit Bänken auf beiden Seiten.</summary>
        static void LongTable(Vector3 p, float len, float y)
        {
            for (float z = -len / 2f + 3.3f; z <= len / 2f - 3.2f + 0.01f; z += 6.5f)
            {
                Decor(new[] { "piece_table_oak", "piece_table" }, new Vector3(p.x, y, p.z + z), 90f);
                for (float bz = -2.2f; bz <= 2.2f; bz += 2.2f)
                {
                    Decor(new[] { "piece_bench01" }, new Vector3(p.x - 1.4f, y, p.z + z + bz), 90f);
                    Decor(new[] { "piece_bench01" }, new Vector3(p.x + 1.4f, y, p.z + z + bz), 270f);
                }
            }
        }

        /// <summary>Podest an der Rückwand (Oberkante y + h), mit Stufen davor.</summary>
        static float Dais(Vector3 c, float w, float d, float y, float width, float depth, float h, Kit k)
        {
            var floor = k.Floor;
            float top = y + h;
            var center = new Vector3(c.x, y, c.z + d / 2f - depth / 2f - 0.3f);
            FloorAt(center, width, depth, top, floor);
            // Stufen vor dem Podest (nur wenn es hoch ist)
            var stair = Pick("stone_stair", "wood_stair");
            if (stair != null && h >= 0.9f)
                for (float x = -width / 2f + 1f; x < width / 2f; x += 2f)
                    PutSloped(stair, new Vector3(c.x + x, y + h - stair.Height, center.z - depth / 2f - stair.Run / 2f), Vector3.forward, 0.2f);
            return top;
        }

        // ---------------------------------------------------------------- Säle

        /// <summary>
        /// Großer Saal: Boden, Läufer zum Podest, Thron (oder Stuhl) und hohe Tafel auf dem Podest,
        /// lange Tafeln mit Bänken, Kamin an der Seite, Fackeln, Banner, Felle.
        /// </summary>
        static void GreatHall(Vector3 c, float w, float d, float y, Kit k, HallKind kind)
        {
            if (s_ruin > 0.25f || w < 8f || d < 8f) return;
            var floorKit = k.Floor;
            FloorAt(c, w - 0.6f, d - 0.6f, y + 0.05f, floorKit);
            float fy = y + 0.05f;

            // Läufer von der Tür bis zum Podest
            for (float z = -d / 2f + 2f; z < d / 2f - 5f; z += 3f)
                Decor(new[] { kind == HallKind.Iron ? "jute_carpet_blue" : "jute_carpet", "rug_straw" }, new Vector3(c.x, fy + 0.02f, c.z + z), 90f);

            // Podest mit Thron und hoher Tafel
            bool throne = kind != HallKind.Common && kind != HallKind.Longhall && kind != HallKind.Hearths;
            float daisTop = Dais(c, w, d, fy, Math.Min(w - 2f, 10f), 4f, kind == HallKind.Iron ? 1.0f : 0.6f, k);
            var backZ = c.z + d / 2f - 1.6f;
            if (throne)
                Decor(kind == HallKind.Iron ? new[] { "piece_throne02", "piece_throne01" } : new[] { "piece_throne01", "piece_throne02" },
                      new Vector3(c.x, daisTop, backZ), 180f);
            else Decor(new[] { "piece_chair03", "piece_chair" }, new Vector3(c.x, daisTop, backZ), 180f);
            if (kind != HallKind.Iron)
            {
                Decor(new[] { "piece_table_oak", "piece_table" }, new Vector3(c.x, daisTop, backZ - 1.9f), 0f);
                foreach (float x in new[] { -2.2f, 2.2f })
                    Decor(new[] { "piece_chair", "piece_chair02" }, new Vector3(c.x + x, daisTop, backZ - 0.8f), 180f);
            }

            // Lange Tafeln
            float tablesLen = d - 9f;
            if (tablesLen >= 6.5f && kind != HallKind.Iron && kind != HallKind.PaintedTable)
            {
                float tz = c.z - d / 2f + 2f + tablesLen / 2f;
                float tx = Math.Min(w / 4f + 0.5f, w / 2f - 2.6f);
                LongTable(new Vector3(c.x - tx, fy, tz), tablesLen, fy);
                if (w >= 12f) LongTable(new Vector3(c.x + tx, fy, tz), tablesLen, fy);
            }
            else if (w >= 14f && kind != HallKind.Iron && kind != HallKind.PaintedTable)
            {
                // breiter, flacher Saal: Tafeln quer, links und rechts vom Läufer
                float z = c.z - d / 2f + 3.2f;
                foreach (float sgn in new[] { -1f, 1f })
                    for (float x = 4.5f; x + 3.3f <= w / 2f - 1f; x += 7f)
                    {
                        var tp = new Vector3(c.x + sgn * x, fy, z);
                        Decor(new[] { "piece_table_oak", "piece_table" }, tp, 0f);
                        foreach (float bx in new[] { -2.2f, 0f, 2.2f })
                        {
                            Decor(new[] { "piece_bench01" }, tp + new Vector3(bx, 0f, -1.4f), 0f);
                            Decor(new[] { "piece_bench01" }, tp + new Vector3(bx, 0f, 1.4f), 180f);
                        }
                    }
            }

            // Kamin / Feuer
            if (kind == HallKind.Hearths)
            {
                // Halle der hundert Herde (Harrenhal): Feuerstellen an beiden Seiten
                for (float z = -d / 2f + 2.5f; z < d / 2f - 5f; z += 3f)
                {
                    Decor(new[] { "fire_pit" }, new Vector3(c.x - w / 2f + 1.5f, fy, c.z + z), 0f);
                    Decor(new[] { "fire_pit" }, new Vector3(c.x + w / 2f - 1.5f, fy, c.z + z), 0f);
                }
            }
            else if (kind == HallKind.Longhall)
            {
                for (float z = -d / 2f + 3f; z < d / 2f - 5f; z += 5f)
                    Decor(new[] { "fire_pit" }, new Vector3(c.x, fy, c.z + z), 0f);
            }
            else if (w >= 10f)
                Decor(new[] { "hearth" }, new Vector3(c.x + w / 2f - 1.9f, fy, c.z), 90f);

            // Felle vor dem Kamin, Fackeln, Banner, Truhen
            if (kind == HallKind.North || kind == HallKind.Lord || kind == HallKind.Common)
                Decor(new[] { kind == HallKind.North ? "rug_wolf" : "rug_Bjorn", "rug_fur" }, new Vector3(c.x + w / 2f - 4.6f, fy + 0.02f, c.z), 90f);
            WallTorches(c, w, d, fy);
            BackBanners(c, w, d, fy, w >= 14f ? 3 : 2);
            Decor(new[] { "piece_chest", "piece_chest_wood" }, new Vector3(c.x - w / 2f + 1.2f, fy, c.z + d / 2f - 1.4f), 90f);
            Decor(new[] { "piece_chest_barrel" }, new Vector3(c.x - w / 2f + 1.0f, fy, c.z - d / 2f + 1.4f), 0f);
            Decor(new[] { "piece_chest_barrel" }, new Vector3(c.x - w / 2f + 1.9f, fy, c.z - d / 2f + 1.2f), 0f);

            switch (kind)
            {
                case HallKind.Iron:
                    // Iron Throne: Spitzen aus Eisen links und rechts, Eisengitter dahinter, Säulenreihen
                    Decor(new[] { "piece_dvergr_sharpstakes" }, new Vector3(c.x - 1.8f, daisTop, backZ - 0.2f), 135f);
                    Decor(new[] { "piece_dvergr_sharpstakes" }, new Vector3(c.x + 1.8f, daisTop, backZ - 0.2f), 45f);
                    var grate = Pick("iron_grate");
                    if (grate != null) foreach (float x in new[] { -2f, 0f, 2f }) Put(grate, new Vector3(c.x + x, daisTop, c.z + d / 2f - 0.6f), 0f, 0.3f);
                    var pillar = k.Pillar ?? PickAny("stone_pillar");
                    if (pillar != null)
                        for (float z = -d / 2f + 3f; z < d / 2f - 5f; z += 4f)
                            foreach (float x in new[] { -w / 2f + 3f, w / 2f - 3f })
                                for (float py = 0f; py < 8f; py += Math.Max(1f, pillar.Height))
                                    Put(pillar, new Vector3(c.x + x, fy + py, c.z + z), 0f, 0.5f);
                    foreach (float x in new[] { -3.2f, 3.2f })
                        Decor(new[] { "piece_groundtorch", "piece_groundtorch_wood" }, new Vector3(c.x + x, daisTop, backZ + 0.4f), 0f);
                    break;
                case HallKind.PaintedTable:
                    // Dragonstone: der bemalte Tisch (Karte von Westeros) mitten im Raum
                    foreach (var (sx, sz) in Corners)
                        Decor(new[] { "piece_cartographytable", "piece_table_oak" }, new Vector3(c.x + sx * 2.1f, fy, c.z + sz * 1.0f), 0f);
                    foreach (var (sx, sz) in Corners)
                        Decor(new[] { "piece_groundtorch", "piece_groundtorch_wood" }, new Vector3(c.x + sx * 5f, fy, c.z + sz * 3.4f), 0f);
                    break;
                case HallKind.HighHall:
                    // Eyrie: weiße Halle, Mondtür als Kreis aus Marmor im Boden vor dem Thron
                    var tile = Pick("blackmarble_floor_triangle", "blackmarble_tile_floor_1x1");
                    if (tile != null)
                        for (int i = 0; i < 8; i++)
                        {
                            float a = i * Mathf.PI / 4f;
                            Put(tile, new Vector3(c.x + Mathf.Cos(a) * 1.6f, fy - tile.Height + 0.06f, c.z + d / 2f - 8f + Mathf.Sin(a) * 1.6f), a * Mathf.Rad2Deg, 0.2f);
                        }
                    break;
            }
        }

        /// <summary>Great Sept of Baelor: Altar unter der Kuppel, sieben Statuen der Sieben, Bänke, viele Kerzen.</summary>
        static void SeptInterior(Vector3 c, float w, float d, float y, Kit marble, float height)
        {
            if (s_ruin > 0.25f) return;
            float fy = y + 0.05f;
            FloorAt(c, w - 0.6f, d - 0.6f, fy, marble.Floor);
            Decor(new[] { "piece_blackmarble_table", "piece_table" }, new Vector3(c.x, fy, c.z), 0f); // Altar
            var col = PickAny("blackmarble_column_1", "stone_pillar");
            for (int i = 0; i < 7; i++)
            {
                float a = i * 2f * Mathf.PI / 7f + Mathf.PI / 2f;
                var p = new Vector3(c.x + Mathf.Cos(a) * 6f, fy, c.z + Mathf.Sin(a) * 6f);
                if (col != null) for (float py = 0f; py < 2.9f; py += Math.Max(1f, col.Height)) Put(col, new Vector3(p.x, fy + py, p.z), 0f, 0.5f);
                Decor(new[] { "blackmarble_head01", "blackmarble_head02" }, new Vector3(p.x, fy + 3.5f, p.z), -a * Mathf.Rad2Deg + 90f);
                Decor(new[] { "piece_groundtorch", "piece_groundtorch_wood" }, p + new Vector3(Mathf.Cos(a) * -1.4f, 0f, Mathf.Sin(a) * -1.4f), 0f);
            }
            // Bankreihen zur Tür hin
            for (float z = -d / 2f + 2.5f; z < -4f; z += 2.2f)
                foreach (float x in new[] { -6f, -3.4f, 3.4f, 6f })
                    Decor(new[] { "piece_blackmarble_bench", "piece_bench01" }, new Vector3(c.x + x, fy, c.z + z), 0f);
            // Hängende Feuerschale unter der Kuppel, Kerzen an den Wänden
            Decor(new[] { "piece_brazierceiling01" }, new Vector3(c.x, y + height - 2.2f, c.z), 0f);
            WallTorches(c, w, d, fy, 3f);
            Decor(new[] { "jute_carpet_blue", "jute_carpet" }, new Vector3(c.x, fy + 0.02f, c.z - 6f), 90f);
        }

        /// <summary>House of Black and White (Braavos): Säulen in Schwarz und Weiß, Statuen, viele Kerzen.</summary>
        static void BlackAndWhite(Vector3 c, float w, float d, float y, Kit marble)
        {
            if (s_ruin > 0.25f) return;
            float fy = y + 0.05f;
            FloorAt(c, w - 0.6f, d - 0.6f, fy, marble.Floor);
            var black = PickAny("blackmarble_column_1"); var white = PickAny("stone_pillar");
            int i = 0;
            for (float z = -d / 2f + 2.5f; z < d / 2f - 2f; z += 3f, i++)
                foreach (float x in new[] { -w / 2f + 2.5f, w / 2f - 2.5f })
                {
                    var pil = (i % 2 == 0) ? black : white;
                    if (pil != null) for (float py = 0f; py < 7.9f; py += Math.Max(1f, pil.Height)) Put(pil, new Vector3(c.x + x, fy + py, c.z + z), 0f, 0.5f);
                    Decor(new[] { "piece_groundtorch_mist", "piece_groundtorch" }, new Vector3(c.x + x * 0.7f, fy, c.z + z), 0f);
                }
            // Statuen der vielen Gesichter an der Rückwand
            for (float x = -w / 2f + 2f; x <= w / 2f - 2f; x += 2f)
                Decor(new[] { "blackmarble_head01", "blackmarble_head02" }, new Vector3(c.x + x, fy + 1.2f, c.z + d / 2f - 0.8f), 180f);
            Decor(new[] { "piece_blackmarble_table" }, new Vector3(c.x, fy, c.z + d / 2f - 3f), 0f);
        }
    }
}
