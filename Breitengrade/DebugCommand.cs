using System;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace Breitengrade
{
    /// <summary>
    /// Konsolenbefehl "bg_hier" (F5): schreibt alle Biom-Daten an der eigenen Position in die
    /// Konsole und ins Log. Hilft herauszufinden, warum eine Stelle anders aussieht als erwartet.
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    static class DebugCommand
    {
        static bool s_registered;

        static void Postfix()
        {
            if (s_registered) return;
            s_registered = true;
            new Terminal.ConsoleCommand("bg_hier", "Breitengrade: Biom-Daten an deiner Position ins Log schreiben",
                args => Report(args.Context),
                false, false, false, false, false, false, null, false, false, false);
            new Terminal.ConsoleCommand("bg_ausdauer", "Breitengrade: unendliche Ausdauer an/aus",
                args =>
                {
                    InfiniteStamina.On = !InfiniteStamina.On;
                    args.Context?.AddString("Unendliche Ausdauer: " + (InfiniteStamina.On ? "AN" : "AUS"));
                },
                false, false, false, false, false, false, null, false, false, false);
        }

        static void Report(Terminal terminal)
        {
            var player = Player.m_localPlayer;
            if (player == null) { terminal?.AddString("Kein Spieler geladen."); return; }
            var p = player.transform.position;
            var wg = WorldGenerator.instance;
            var sb = new StringBuilder();
            sb.AppendLine($"[bg_hier] Position ({p.x:0}, {p.y:0}, {p.z:0}), Mod aktiv: {State.Active}");
            sb.AppendLine($"[bg_hier] Band laut Mod: {(Heightmap.Biome)Latitude.GetBiome(p.x, p.z)}");

            if (wg != null)
            {
                sb.AppendLine($"[bg_hier] WorldGenerator.GetBiome: {wg.GetBiome(p)}");
                float h = wg.GetHeight(p.x, p.z, out Color mask);
                sb.AppendLine($"[bg_hier] Generator-Höhe {h:0.0} m, Maske {mask}");
                sb.AppendLine($"[bg_hier] Sektor hier: {Describe(wg.GetBiomeSector(p, false))}");
            }

            var hm = Heightmap.FindHeightmap(p);
            if (hm != null)
            {
                sb.AppendLine($"[bg_hier] Heightmap-Biom: {hm.GetBiome(p, 0.02f, false)}, Malmaske {hm.GetPaintMask(p)}");
                var corners = Traverse.Create(hm).Field("m_cornerBiomes").GetValue<BiomeSector[]>();
                if (corners != null)
                    for (int i = 0; i < corners.Length; i++)
                        sb.AppendLine($"[bg_hier] Ecke {i}: {Describe(corners[i])}, Bodenfarbe {Heightmap.GetBiomeColor(corners[i])}");
            }

            var env = EnvMan.instance?.GetCurrentEnvironment();
            if (env != null) sb.AppendLine($"[bg_hier] Wetter/Umgebung: {env.m_name}");

            string text = sb.ToString().TrimEnd();
            Plugin.Log.LogInfo(text);
            foreach (var line in text.Split('\n')) terminal?.AddString(line.TrimEnd());
        }

        static string Describe(BiomeSector s)
        {
            if (s == null) return "keiner";
            var alts = s.AltBiomes == null || s.AltBiomes.Count == 0 ? "-" : string.Join(", ", s.AltBiomes.Select(a => a.m_name));
            return $"{s.Biome} (Varianten: {alts})";
        }
    }

    internal static class SectorCheck
    {
        /// <summary>Zählt Rasterpunkte, deren Sektor-Biom nicht zum Band passt (z. B. Schnee in den Plains).</summary>
        public static void Run(AltBiomeWorldData data)
        {
            if (!State.Active || data?.PointSectors == null) return;
            int size = data.Size, wrong = 0, total = 0;
            var counts = new System.Collections.Generic.Dictionary<string, int>();
            var samples = new StringBuilder();
            for (int y = 0; y < size; y += 4)
            for (int x = 0; x < size; x += 4)
            {
                float wx = AltBiomeWorldData.MapSpaceToWorldSpace((float)x);
                float wz = AltBiomeWorldData.MapSpaceToWorldSpace((float)y);
                float r = 10000f * WorldScale.S;
                if (wx * wx + wz * wz > r * r) continue;
                var sector = data.PointSectors[x, y];
                if (sector == null) continue;
                total++;
                var expected = (Heightmap.Biome)Latitude.GetBiome(wx, wz);
                if (sector.Biome == expected) continue;
                wrong++;
                string key = $"{expected} -> {sector.Biome}";
                counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
                if ((sector.Biome == Heightmap.Biome.Mountain || sector.Biome == Heightmap.Biome.DeepNorth) &&
                    samples.Length < 300)
                    samples.Append($" ({wx:0}, {wz:0})");
            }
            Plugin.Log.LogInfo($"[Diagnose] Raster: {wrong} von {total} Punkten haben ein anderes Biom als ihr Band.");
            foreach (var kv in counts.OrderByDescending(k => k.Value).Take(12))
                Plugin.Log.LogInfo($"[Diagnose]   {kv.Key}: {kv.Value}");
            if (samples.Length > 0) Plugin.Log.LogInfo("[Diagnose]   Schnee-Biom an:" + samples);
        }
    }
}

namespace Breitengrade
{
    /// <summary>Konsolenbefehl "bg_ausdauer": Ausdauer wird nicht mehr verbraucht (nur für den eigenen Spieler).</summary>
    internal static class InfiniteStamina
    {
        public static bool On;
    }

    /// <summary>Kein Ausdauerverbrauch mit "bg_ausdauer" oder im God-Modus ("god").</summary>
    [HarmonyLib.HarmonyPatch(typeof(Player), nameof(Player.UseStamina))]
    static class Patch_UseStamina
    {
        static bool Prefix(Player __instance) =>
            !(__instance == Player.m_localPlayer && (InfiniteStamina.On || __instance.InGodMode()));
    }

    /// <summary>
    /// Valheim-Fehler: Verliert ein Gebiet seinen Geländebezug, wirft das Spawn-System alle paar
    /// Sekunden einen Fehler. Wir suchen das Gelände neu oder überspringen die Runde.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(SpawnSystem), "UpdateSpawnList")]
    static class Patch_SpawnSystemHeightmap
    {
        static readonly HarmonyLib.AccessTools.FieldRef<SpawnSystem, Heightmap> HeightmapRef =
            HarmonyLib.AccessTools.FieldRefAccess<SpawnSystem, Heightmap>("m_heightmap");

        static bool Prefix(SpawnSystem __instance)
        {
            if (HeightmapRef(__instance) != null) return true;
            var hm = Heightmap.FindHeightmap(__instance.transform.position);
            if (hm == null) return false;
            HeightmapRef(__instance) = hm;
            return true;
        }
    }
}
