using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Breitengrade.Bau
{
    /// <summary>
    /// Überarbeitung nach dem Rundgang (siehe UEBERARBEITUNG.md): Burg-Varianten, Fan-Details je Ort,
    /// Hügel, Häfen, Schutt und Bewuchs in Ruinen, Statue in der Stadtmitte.
    /// </summary>
    public static partial class Builder
    {
        // ---------------------------------------------------------------- Feste Stellen (auch für Hügel)

        static Vector3 CasterlyRock(Vector3 c, float R) => c + new Vector3(R * 0.5f, 0f, R * 0.42f);
        static Vector3 NewCastle(Vector3 c, float R) => c + new Vector3(-R * 0.42f, 0f, R * 0.38f);
        static Vector3 GatesOfTheMoon(Vector3 c, float R) => c + new Vector3(0f, 0f, -R * 0.6f);

        /// <summary>Hügel einzelner Orte (Motte, Felsen, Oberstadt).</summary>
        static IEnumerable<(Vector3 pos, float r, float dh)> NamedHills(CityBuilder.Plan p)
        {
            var c = new Vector3(p.X, p.H, p.Z);
            switch (p.Name)
            {
                case "Deepwood Motte": yield return (c, 20f, 9f); break;                    // die Motte
                case "Lannisport": yield return (CasterlyRock(c, p.R), 30f, 30f); break;    // Casterly Rock
                case "White Harbor": yield return (NewCastle(c, p.R), 22f, 10f); break;     // New Castle
                case "Norvos": yield return (c, 30f, 12f); break;                           // Oberstadt
            }
        }

        // ---------------------------------------------------------------- Burg-Varianten

        /// <summary>Burg mit Innenhof: Ringmauer, Torhaus mit zwei Türmen, Bergfried hinten, zwei hintere Türme.</summary>
        static void CourtyardCastle(Vector3 c, Kit k, int levels)
        {
            RingSquare(c, 40f, 3, k);
            foreach (float sx in new[] { -1f, 1f })
            {
                TowerAt(c + new Vector3(sx * 4.5f, 0f, -20f), 5f, 3, k, true);   // Torhaus
                TowerAt(c + new Vector3(sx * 20f, 0f, 20f), 6f, levels + 1, k, false);
            }
            Castle(c + new Vector3(0f, 0f, 9f), 16f, 12f, k, levels, false);
            Reserve(c, 24f);
        }

        /// <summary>Hoher Bergfried mit vier Ecktürmchen oben.</summary>
        static void TallKeep(Vector3 c, Kit k, int levels)
        {
            Box(c, 14f, 14f, c.y, levels, k, door: true, pitched: false, merlons: true);
            GreatHall(c, 14f, 14f, c.y, k, HallKind.Lord);
            float top = c.y + levels * LevelHeight(k);
            foreach (var (sx, sz) in Corners)
                TowerAt(new Vector3(c.x + sx * 6f, top, c.z + sz * 6f), 4f, 2, k, false);
            Decor(new[] { "piece_brazierfloor02" }, new Vector3(c.x, top + 0.2f, c.z));
            Reserve(c, 12f);
        }

        /// <summary>Gestufter Turm mit Dach auf jeder Stufe (Pagode, Yi Ti).</summary>
        static void Pagoda(Vector3 c, Kit wall, Kit roofKit, int tiers)
        {
            float y = c.y, s = 12f;
            for (int t = 0; t < tiers && s >= 4f; t++)
            {
                var p = new Vector3(c.x, y, c.z);
                Box(p, s, s, y, 1, wall, door: t == 0, pitched: false, merlons: false, top: false);
                float lh = LevelHeight(wall);
                // umlaufendes Vordach: vier Dachschrägen über die Kanten hinaus
                if (roofKit.Roof != null) Roof(new Vector3(c.x, y + lh, c.z), s + 2f, s + 2f, y + lh - roofKit.Roof.Height, roofKit.Roof);
                y += lh + 1f;
                s -= 2f;
            }
            Decor(new[] { "piece_brazierfloor01" }, new Vector3(c.x, y + 0.2f, c.z));
        }

        /// <summary>Generische Burgen nach Ort (statt überall derselbe Klotz). true = gebaut.</summary>
        static bool NamedCastle(string name, Vector3 c, float R, Kit keep, Kit house)
        {
            var dark = KitOf(Mat.Marble).Wall != null ? KitOf(Mat.Marble) : keep;
            switch (name)
            {
                case "Karhold": TallKeep(c, keep, 7); return true;
                case "The Dreadfort":
                    // hohe düstere Türme aus dunklem Stein, Banner (Flayed Man)
                    TallKeep(c, dark, 9);
                    foreach (var (sx, sz) in Corners) TowerAt(c + new Vector3(sx * 14f, 0f, sz * 14f), 5f, 8, dark, false);
                    Reserve(c, 20f);
                    return true;
                case "Pentos": case "Lys": case "Myr": case "Lorath": case "Qohor":
                    CourtyardCastle(c, keep, 4);
                    return true;
                case "Norvos":
                    // Oberstadt auf dem Hügel mit drei Glockentürmen
                    var up = c + Vector3.up * 12f;
                    CourtyardCastle(up, keep, 4);
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * 2f * Mathf.PI / 3f + 0.4f;
                        TowerAt(up + new Vector3(Mathf.Cos(a) * 26f, 0f, Mathf.Sin(a) * 26f), 5f, 7, keep, true);
                    }
                    Reserve(c, 34f);
                    return true;
                case "Yin":
                    TallKeep(c, keep, 5);
                    foreach (var (sx, sz) in Corners)
                    {
                        var p = c + new Vector3(sx * R * 0.35f, 0f, sz * R * 0.35f);
                        Pagoda(p, keep, KitOf(Mat.Dark), 5);
                        Reserve(p, 9f);
                    }
                    return true;
                case "White Harbor":
                    // New Castle auf dem Hügel, unten ein Torbau
                    var nc = NewCastle(c, R) + Vector3.up * 10f;
                    CourtyardCastle(nc, keep, 5);
                    Reserve(NewCastle(c, R), 30f);
                    TowerAt(c + new Vector3(10f, 0f, 10f), 6f, 4, keep, true);
                    Reserve(c + new Vector3(10f, 0f, 10f), 5f);
                    return true;
                case "Riverrun":
                    // dreieckige Ringmauer (Burg im Winkel zweier Flüsse), runde Türme an den Ecken
                    var tri = new[] { c + new Vector3(0f, 0f, 30f), c + new Vector3(28f, 0f, -18f), c + new Vector3(-28f, 0f, -18f) };
                    for (int i = 0; i < 3; i++)
                    {
                        Run(tri[i], tri[(i + 1) % 3], c.y, 4, keep.Wall, new List<Vector3> { c + new Vector3(0f, 0f, -18f) });
                        RoundTower(tri[i], 4.5f, 5, keep, true);
                    }
                    Castle(c + new Vector3(0f, 0f, 4f), 16f, 12f, keep, 4, false);
                    Reserve(c, 32f);
                    return true;
                case "Lannisport":
                    // Casterly Rock: Burg oben auf dem Felsen, unten die Stadtburg
                    var cr = CasterlyRock(c, R) + Vector3.up * 30f;
                    Castle(cr, 22f, 18f, keep, 5, true);
                    Reserve(CasterlyRock(c, R), 40f);
                    CourtyardCastle(c, keep, 3);
                    return true;
                case "Deepwood Motte":
                    // Holzburg auf der Motte mit Palisade
                    var top = c + Vector3.up * 9f;
                    var wood = KitOf(Mat.Wood);
                    RingWall(top, 18f, 2, wood, wood, towers: false, gates: 1);
                    Castle(top, 14f, 12f, wood, 3, false);
                    TowerAt(top + new Vector3(9f, 0f, 9f), 4f, 4, wood, true);
                    Reserve(c, 26f);
                    return true;
                case "Dragonstone":
                    // Stone Drum (runder Bergfried), bemalter Tisch, Drachen als Wasserspeier
                    RoundTower(c, 10f, 7, dark, true);
                    GreatHall(c, 12f, 12f, c.y, dark, HallKind.PaintedTable);
                    float t2 = c.y + 7 * LevelHeight(dark);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI / 4f;
                        Decor(new[] { "wood_dragon1", "darkwood_raven" }, new Vector3(c.x + Mathf.Cos(a) * 10.6f, t2 + 0.1f, c.z + Mathf.Sin(a) * 10.6f), -a * Mathf.Rad2Deg + 90f);
                    }
                    foreach (var (sx, sz) in Corners) RoundTower(c + new Vector3(sx * 16f, 0f, sz * 16f), 4f, 5, dark, false);
                    Reserve(c, 22f);
                    return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- Zusätze je Ort

        /// <summary>Fan-Details, die zu einem Ort zusätzlich gehören (nach dem Wahrzeichen).</summary>
        static void NamedExtras(string name, Style s, Vector3 c, float R, Kit wall, Kit house, Kit keep)
        {
            var marble = KitOf(Mat.Marble).Wall != null ? KitOf(Mat.Marble) : keep;
            var wood = KitOf(Mat.Wood);
            switch (name)
            {
                case "Lands of Always Winter":
                    for (int i = 0; i < 7; i++)
                    {
                        float a = i * 2f * Mathf.PI / 7f;
                        Decor(new[] { "Birch1_aut", "Birch2_aut" }, c + new Vector3(Mathf.Cos(a) * 6f, 0f, Mathf.Sin(a) * 6f), i * 51f);
                    }
                    Reserve(c, 12f);
                    break;
                case "Thenn":
                    Box(c, 20f, 10f, c.y, 1, house, door: true, pitched: true, merlons: false);
                    GreatHall(c, 20f, 10f, c.y, house, HallKind.Longhall);
                    Reserve(c, 14f);
                    break;
                case "Castle Black":
                    WithRuin(0.6f, () => TowerAt(c + new Vector3(-24f, 0f, -14f), 7f, 6, keep, false)); // Hardin's Tower
                    Reserve(c + new Vector3(-24f, 0f, -14f), 6f);
                    break;
                case "Winterfell":
                    // Eingang zur Gruft: Treppe hinab, Statuen der Könige des Nordens mit Wolfsfellen
                    var crypt = c + new Vector3(-34f, 0f, 16f);
                    Box(crypt, 8f, 8f, c.y, 1, keep, door: true, pitched: false, merlons: true);
                    for (int i = 0; i < 4; i++)
                    {
                        var sp = crypt + new Vector3(-6f + i * 4f, 0f, -7f);
                        var col = PickAny("stone_pillar");
                        if (col != null) Put(col, sp, 0f, 0.5f);
                        Decor(new[] { "blackmarble_head01", "blackmarble_head02" }, sp + Vector3.up * 2.1f, 0f);
                        Decor(new[] { "rug_wolf" }, sp + new Vector3(0f, 0.02f, -1.4f), 0f);
                    }
                    Reserve(crypt, 9f);
                    break;
                case "The Neck":
                    // Moat Cailin: drei verfallene Türme
                    foreach (var off in new[] { new Vector3(R * 0.55f, 0f, -R * 0.2f), new Vector3(R * 0.6f, 0f, R * 0.1f), new Vector3(R * 0.45f, 0f, R * 0.35f) })
                    {
                        WithRuin(0.5f, () => RoundTower(c + off, 5f, 6, KitOf(Mat.Stone), false));
                        Reserve(c + off, 7f);
                    }
                    break;
                case "The Twins":
                    TowerAt(c + new Vector3(0f, 0f, 0f), 6f, 5, keep, true); // Wasserturm auf der Brücke
                    break;
                case "The Eyrie":
                    var gm = GatesOfTheMoon(c, R);
                    CourtyardCastle(gm, keep, 3);
                    break;
                case "King's Landing":
                    // Flea Bottom: dichtes Viertel aus kleinen Hütten
                    var fb = c + new Vector3(-R * 0.25f, 0f, -R * 0.55f);
                    for (int i = 0; i < 26; i++)
                    {
                        var hp = fb + new Vector3((i % 6) * 6.5f - 16f, 0f, (i / 6) * 6.5f - 13f);
                        if (!Free(hp, 3f)) continue;
                        Hut(hp, 4f, 4f, wood, false, 0);
                    }
                    Reserve(fb, 26f);
                    break;
                case "Highgarden":
                    for (int i = 0; i < 10; i++)
                    {
                        float a = i * 2f * Mathf.PI / 10f;
                        var gp = c + new Vector3(Mathf.Cos(a) * 30f, 0f, Mathf.Sin(a) * 30f);
                        if (Free(gp, 5f)) { Garden(gp, 8f, house); Reserve(gp, 5f); }
                    }
                    break;
                case "Oldtown":
                    // Starry Sept (Kuppel) und Citadel mit Bibliothek
                    var ss = c + new Vector3(-R * 0.35f, 0f, R * 0.3f);
                    Box(ss, 18f, 18f, c.y, 2, marble, door: true, pitched: false, merlons: false);
                    Dome(ss + Vector3.up * (2 * LevelHeight(marble)), 18f, marble);
                    SeptInterior(ss, 18f, 18f, c.y, marble, 2 * LevelHeight(marble));
                    Reserve(ss, 14f);
                    var lib = c + new Vector3(R * 0.3f, 0f, R * 0.2f) + new Vector3(0f, 0f, -14f);
                    Box(lib, 14f, 10f, c.y, 2, keep, door: true, pitched: false, merlons: true);
                    for (float x = -5f; x <= 5f; x += 2.5f)
                        Decor(new[] { "piece_table", "piece_table_oak" }, lib + new Vector3(x, 0.05f, 2f), 90f);
                    Reserve(lib, 9f);
                    break;
                case "Braavos":
                    // Iron Bank: Säulenhalle
                    var ib = c + new Vector3(-R * 0.3f, 0f, R * 0.25f);
                    Box(ib, 18f, 12f, c.y, 2, keep, door: true, pitched: false, merlons: false);
                    var pil = PickAny("stone_pillar");
                    if (pil != null)
                        for (float x = -8f; x <= 8f; x += 4f)
                            for (float y = 0f; y < 7.9f; y += pil.Height)
                                Put(pil, ib + new Vector3(x, y, -7.5f), 0f, 0.5f);
                    Tiles(ib + new Vector3(0f, 0f, -7.5f), 18f, 3f, c.y + 8f, keep.Floor);
                    Reserve(ib, 12f);
                    break;
                case "Lorath":
                    Maze(c, R * 0.55f, R * 0.7f, KitOf(Mat.Stone));
                    break;
                case "Qohor":
                    var bg = c + new Vector3(R * 0.3f, 0f, R * 0.3f);
                    Box(bg, 14f, 10f, c.y, 2, marble, door: true, pitched: false, merlons: true);
                    BlackAndWhite(bg, 14f, 10f, c.y, marble); // dunkler Tempel der Schwarzen Ziege
                    Reserve(bg, 10f);
                    break;
                case "Volantis":
                    // Tempel des Herrn des Lichts: riesig, Feuerschalen ringsum
                    var rt = c + new Vector3(-R * 0.35f, 0f, -R * 0.3f);
                    Box(rt, 26f, 20f, c.y, 3, keep, door: true, pitched: false, merlons: true);
                    Dome(rt + Vector3.up * (3 * LevelHeight(keep)), 18f, keep);
                    GreatHall(rt, 26f, 20f, c.y, keep, HallKind.Hearths);
                    foreach (var (sx, sz) in Corners)
                        Decor(new[] { "piece_brazierfloor01" }, rt + new Vector3(sx * 15f, 0f, sz * 12f), 0f);
                    Reserve(rt, 18f);
                    break;
                case "Valyria":
                    // riesige zerbrochene Statuen
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * Mathf.PI / 2f + 0.5f;
                        var sp = c + new Vector3(Mathf.Cos(a) * 36f, 0f, Mathf.Sin(a) * 36f);
                        WithRuin(0.4f, () => Box(sp, 4f, 4f, c.y, 2, marble, door: false, pitched: false, merlons: false));
                        Decor(new[] { "blackmarble_head_big02", "blackmarble_head_big01" }, sp + new Vector3(3f, 0f, 2f), i * 70f);
                        Reserve(sp, 5f);
                    }
                    break;
                case "Meereen":
                    // Daznaks Kampfgrube: runde Arena
                    var pit = c + new Vector3(R * 0.1f, 0f, -R * 0.55f);
                    RingWall(pit, 18f, 3, keep, keep, towers: false, gates: 2);
                    RingWall(pit, 12f, 1, keep, keep, towers: false, gates: 2);
                    Reserve(pit, 20f);
                    break;
                case "Qarth":
                    // Haus der Unsterblichen: schwarzer, fensterloser Bau, umgeben von dunklen Bäumen
                    var hu = c + new Vector3(R * 0.38f, 0f, -R * 0.3f);
                    Box(hu, 14f, 14f, c.y, 4, marble, door: true, pitched: false, merlons: false);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI / 4f;
                        Decor(new[] { "SwampTree1", "Birch1_aut" }, hu + new Vector3(Mathf.Cos(a) * 12f, 0f, Mathf.Sin(a) * 12f), i * 40f);
                    }
                    Reserve(hu, 14f);
                    break;
            }

            // Magister-Paläste (ummauerter Hof mit Garten) in den Freien Städten
            if (name == "Pentos" || name == "Lys" || name == "Myr" || name == "Braavos" || name == "Volantis")
                foreach (var (sx, sz) in Corners)
                {
                    var mp = c + new Vector3(sx * R * 0.48f, 0f, sz * R * 0.48f);
                    if (!Free(mp, 11f)) continue;
                    RingSquare(mp, 20f, 2, KitOf(Mat.Stone));
                    Box(mp + new Vector3(0f, 0f, 4f), 12f, 8f, c.y, 2, KitOf(Mat.Stone), door: true, pitched: false, merlons: false);
                    Tree(mp + new Vector3(-5f, 0f, -5f)); Tree(mp + new Vector3(5f, 0f, -5f));
                    Reserve(mp, 11f);
                }
        }

        // ---------------------------------------------------------------- Mitte, Häfen, Ruinen

        /// <summary>Statue auf einem Sockel mit Fackeln in der Stadtmitte.</summary>
        static void CenterStatue(Vector3 c)
        {
            var stone = KitOf(Mat.Stone);
            if (!Free(c, 2.5f) || stone.Floor == null) return;
            FloorAt(c, 4f, 4f, c.y + 1.05f, stone.Floor);
            var col = PickAny("stone_pillar");
            if (col != null) Put(col, c + Vector3.up * 1.05f, 0f, 0.5f);
            Decor(new[] { "blackmarble_head_big01", "blackmarble_head01" }, c + Vector3.up * 3.1f, 180f);
            foreach (var (sx, sz) in Corners)
                Decor(new[] { "piece_groundtorch", "piece_groundtorch_wood" }, c + new Vector3(sx * 2.6f, 0f, sz * 2.6f), 0f);
        }

        /// <summary>Holzsteg mit Pfählen ins Wasser an der Küstenseite der Stadt.</summary>
        static void Docks(CityBuilder.Plan p)
        {
            var wg = WorldGenerator.instance;
            var floor = Pick("wood_floor");
            var pole = PickAny("wood_pole_log_4", "wood_pole2");
            if (floor == null) return;
            var c = new Vector3(p.X, p.H, p.Z);
            int built = 0;
            for (int i = 0; i < 16 && built < 3; i++)
            {
                float a = i * Mathf.PI / 8f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                // Uferlinie entlang des Strahls suchen
                Vector3? shore = null;
                for (float d = p.R * 0.5f; d < p.R + 120f; d += 2f)
                {
                    var q = c + dir * d;
                    if (wg.GetHeight(q.x, q.z) < 30.2f) { shore = q; break; }
                }
                if (shore == null) continue;
                // Wasser muss davor tief genug sein
                var end = shore.Value + dir * 20f;
                if (wg.GetHeight(end.x, end.z) > 29f) continue;
                if (built > 0 && i % 5 != 0) continue;
                float deck = 31f;
                float yaw = Mathf.Atan2(-dir.z, dir.x) * Mathf.Rad2Deg;
                var side = new Vector3(-dir.z, 0f, dir.x);
                for (float d = -2f; d <= 22f; d += 2f)
                    foreach (float o in new[] { -1f, 1f })
                        Put(floor, shore.Value + dir * d + side * o + Vector3.up * (deck - shore.Value.y), yaw, 0.5f);
                if (pole != null)
                    for (float d = 2f; d <= 22f; d += 4f)
                        foreach (float o in new[] { -2f, 2f })
                        {
                            var pp = shore.Value + dir * d + side * o;
                            Put(pole, new Vector3(pp.x, deck - pole.Height, pp.z), 0f, 0.5f);
                        }
                Decor(new[] { "piece_groundtorch_wood" }, shore.Value + dir * 22f + side * 1.6f + Vector3.up * (deck - shore.Value.y), 0f);
                built++;
            }
        }

        /// <summary>Ruinen: Schutt, und in Dschungel-Ruinen Bewuchs.</summary>
        static void RuinDetails(Vector3 p, float w, float d, bool jungle)
        {
            string[] rubble = s_biome == Latitude.AshLands ? new[] { "grausten_pile", "stone_pile" }
                            : s_biome == Latitude.Mistlands ? new[] { "blackmarble_pile", "stone_pile" }
                            : new[] { "stone_pile" };
            int n = 1 + s_rng.Next(2);
            for (int i = 0; i < n; i++)
                Decor(rubble, p + new Vector3(((float)s_rng.NextDouble() - 0.5f) * w * 0.6f, 0f, ((float)s_rng.NextDouble() - 0.5f) * d * 0.6f), (float)s_rng.NextDouble() * 360f);
            if (jungle && s_rng.NextDouble() < 0.5)
                Decor(new[] { "YggaShoot_small1", "YggaShoot1", "SwampTree1", "Beech_small1" }, p + new Vector3(0f, 0f, 0f), (float)s_rng.NextDouble() * 360f);
        }

        static bool JungleRuins(string name) => name == "Sothoryos" || name == "Yeen" || name == "Ulthos" || name == "The Sorrows";
    }
}
