using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Breitengrade
{
    /// <summary>
    /// Valheim prüft an vielen Stellen "bin ich in den Ashlands / im Deep North?" mit der normalen
    /// Ring-Geometrie (Wetter, Lava-Schaden, Wasser, Karte, Fische). Die Prüf-Funktionen sind so klein,
    /// dass das Spiel sie teils direkt in die aufrufenden Stellen einbaut. Ein Patch der Funktion selbst
    /// kommt dort nicht an. Deshalb ersetzen wir jeden Aufruf direkt an der Aufrufstelle.
    ///
    /// Beispiel für den Fehler: Die Insel liegt weit im Osten. Nach Valheims Ring-Rechnung ist das
    /// "Deep North", also wählte das Wetter die Schnee-Variante der Plains (verschneite Bäume).
    /// </summary>
    internal static class Geo
    {
        public static bool IsDeepnorth(float x, float z) =>
            ++PerfMonitor.GeoCalls > 0 && State.Active ? Latitude.DeepNorthDepth(x, z) > 0f : WorldGenerator.IsDeepnorth(x, z);

        /// <summary>
        /// Das Wetter ruft die Prüfung mit (x, Höhe) statt (x, Nord-Süd) auf. Bei uns nehmen wir die
        /// echte Nord-Süd-Position der Kamera.
        /// </summary>
        public static bool IsDeepnorthEnv(float x, float y)
        {
            if (!State.Active) return WorldGenerator.IsDeepnorth(x, y);
            var cam = Utils.GetMainCamera();
            float z = cam != null ? cam.transform.position.z : 0f;
            return Latitude.DeepNorthDepth(x, z) > 0f;
        }

        public static bool IsAshlands(float x, float z) =>
            ++PerfMonitor.GeoCalls > 0 && State.Active ? Latitude.AshlandsDepth(x, z) > 0f : WorldGenerator.IsAshlands(x, z);

        public static float GetAshlandsOceanGradient(float x, float z) =>
            State.Active ? (Latitude.AshlandsDepth(x, z) + 300f) / 300f : WorldGenerator.GetAshlandsOceanGradient(x, z);

        public static double CreateDeepNorthGap(float x, float z) =>
            State.Active ? 1.0 : WorldGenerator.CreateDeepNorthGap(x, z);

        public static double DeepNorthWaveFade(float x, float z)
        {
            PerfMonitor.GeoCalls++;
            if (!State.Active) return WorldGenerator.DeepNorthWaveFade(x, z);
            double v = (Latitude.DeepNorthDepth(x, z) + 200f) / 200f;
            return v < 0 ? 0 : (v > 1 ? 1 : v);
        }
    }

    [HarmonyPatch]
    static class Patch_GeoCallSites
    {
        static readonly MethodInfo W_IsDeepnorth = AccessTools.Method(typeof(WorldGenerator), nameof(WorldGenerator.IsDeepnorth));
        static readonly MethodInfo W_IsAshlands = AccessTools.Method(typeof(WorldGenerator), nameof(WorldGenerator.IsAshlands));
        static readonly MethodInfo W_Gradient = AccessTools.Method(typeof(WorldGenerator), nameof(WorldGenerator.GetAshlandsOceanGradient), new[] { typeof(float), typeof(float) });
        static readonly MethodInfo W_Gap = AccessTools.Method(typeof(WorldGenerator), nameof(WorldGenerator.CreateDeepNorthGap));
        static readonly MethodInfo W_Fade = AccessTools.Method(typeof(WorldGenerator), nameof(WorldGenerator.DeepNorthWaveFade));

        static IEnumerable<MethodBase> TargetMethods()
        {
            var list = new List<MethodBase>
            {
                AccessTools.Method(typeof(EnvMan), "UpdateEnvironment"),
                AccessTools.Method(typeof(EnvMan), "GetBiome"),
                AccessTools.Method(typeof(TerrainComp), "PaintCleared"),
                AccessTools.Method(typeof(WorldGenerator), "RenderRivers"),
                AccessTools.Method(typeof(Character), "UpdateLava"),
                AccessTools.Method(typeof(Character), "UpdateAshlandsWater"),
                AccessTools.Method(typeof(Minimap), "GetMaskColor"),
                AccessTools.Method(typeof(Ship), "TakeAshlandsDamage"),
                AccessTools.Method(typeof(Player), "Update"),
                AccessTools.Method(typeof(Fish), "Start"),
                AccessTools.Method(typeof(Fish), "CustomFixedUpdate"),
                AccessTools.Method(typeof(WaterVolume), "GetWaterSurface"),
                AccessTools.Method(typeof(WorldGenerator), "GetBiomeHeight"),
                AccessTools.Method(typeof(WorldGenerator), nameof(WorldGenerator.GetAshlandsOceanGradient), new[] { typeof(Vector2) }),
                AccessTools.Method(typeof(WorldGenerator), nameof(WorldGenerator.GetAshlandsOceanGradient), new[] { typeof(Vector3) }),
            };
            return list.Where(m => m != null);
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            bool env = original.DeclaringType == typeof(EnvMan);
            foreach (var ci in instructions)
            {
                if (ci.opcode == OpCodes.Call && ci.operand is MethodInfo mi)
                {
                    if (mi == W_IsDeepnorth) ci.operand = AccessTools.Method(typeof(Geo), env ? nameof(Geo.IsDeepnorthEnv) : nameof(Geo.IsDeepnorth));
                    else if (mi == W_IsAshlands) ci.operand = AccessTools.Method(typeof(Geo), nameof(Geo.IsAshlands));
                    else if (mi == W_Gradient) ci.operand = AccessTools.Method(typeof(Geo), nameof(Geo.GetAshlandsOceanGradient));
                    else if (mi == W_Gap) ci.operand = AccessTools.Method(typeof(Geo), nameof(Geo.CreateDeepNorthGap));
                    else if (mi == W_Fade) ci.operand = AccessTools.Method(typeof(Geo), nameof(Geo.DeepNorthWaveFade));
                }
                yield return ci;
            }
        }
    }
}
