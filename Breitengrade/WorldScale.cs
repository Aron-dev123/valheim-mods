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
    /// Weltgröße: Valheim hat an vielen Stellen den Weltradius (10000 m, Rand bei 10500 m) fest
    /// eingetragen. Diese Zahlen werden hier zur Laufzeit mit dem Faktor multipliziert,
    /// aber nur in der Breitengrade-Welt. Alle anderen Welten bleiben normal groß.
    /// </summary>
    internal static class WorldScale
    {
        /// <summary>Aktueller Faktor (1 = normale Größe).</summary>
        public static float S = 1f;

        /// <summary>
        /// Größter erlaubter Faktor. Valheim verwaltet Objekte in einem festen Raster aus 512 x 512
        /// Feldern à 64 m, also nur bis ±16384 m. Alles weiter draußen landet in einem Sammelfeld und
        /// wird nicht mehr richtig entladen: Objekte stapeln sich, das Spiel wird immer langsamer.
        /// Der Weltrand (10500 m x Faktor) muss deshalb innerhalb von 16384 m bleiben.
        /// </summary>
        public const float MaxFactor = 1.55f;

        public static float Clamp(float requested)
        {
            float f = Math.Max(1f, requested);
            if (f > MaxFactor)
            {
                Plugin.Log.LogWarning($"[Weltgröße] Faktor {requested} ist zu groß (Valheim verwaltet Objekte nur bis " +
                                      $"16 km vom Zentrum). Nehme {MaxFactor}.");
                f = MaxFactor;
            }
            return f;
        }

        /// <summary>Feste Zahlen im Spielcode, die zum Weltradius gehören.</summary>
        static readonly HashSet<double> RadiusConstants = new HashSet<double>
            { 10000, 10100, 10150, 10420, 10490, 10500 };

        public static float Scaled(float v) => v * S;
        public static double ScaledD(double v) => v * S;
        public static float ScaledSq(float v) => v * S * S;

        static readonly MethodInfo M_Scaled = AccessTools.Method(typeof(WorldScale), nameof(Scaled));
        static readonly MethodInfo M_ScaledD = AccessTools.Method(typeof(WorldScale), nameof(ScaledD));
        static readonly MethodInfo M_ScaledSq = AccessTools.Method(typeof(WorldScale), nameof(ScaledSq));

        /// <summary>Hängt hinter jede Radius-Zahl einen Aufruf, der sie mit dem Faktor multipliziert.</summary>
        public static IEnumerable<CodeInstruction> ScaleConstants(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var ci in instructions)
            {
                yield return ci;
                if (ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f)
                {
                    if (RadiusConstants.Contains(f)) yield return new CodeInstruction(OpCodes.Call, M_Scaled);
                    else if (f == 110250000f) yield return new CodeInstruction(OpCodes.Call, M_ScaledSq);
                }
                else if (ci.opcode == OpCodes.Ldc_R8 && ci.operand is double d && RadiusConstants.Contains(d))
                {
                    yield return new CodeInstruction(OpCodes.Call, M_ScaledD);
                }
            }
        }
    }

    [HarmonyPatch]
    static class Patch_WorldScaleConstants
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var list = new List<MethodBase>
            {
                AccessTools.Method(typeof(Player), "EdgeOfWorldKill"),
                AccessTools.Method(typeof(Ship), "ApplyEdgeForce"),
                AccessTools.Method(typeof(EnvMan), "UpdateWind"),
                AccessTools.Method(typeof(WaterVolume), "GetWaterSurface"),
                AccessTools.Method(typeof(ZoneSystem), "GetRandomZone"),
                AccessTools.Method(typeof(WorldGenerator), "FindLakes"),
                AccessTools.Method(typeof(WorldGenerator), "FindStreamStartPoint"),
                AccessTools.Method(typeof(WorldGenerator), "GetBiomeHeight"),
                AccessTools.Method(typeof(WorldGenerator), "GetEdgeHeight"),
                AccessTools.Method(typeof(WorldGenerator), "GetAshlandsHeight"),
                AccessTools.Method(typeof(WorldGenerator), "GetBaseHeight"),
                AccessTools.Method(typeof(AltBiomeWorldData), "GenerateBiomePoints"),
            };
            // Platzierung der Strukturen läuft in einem Coroutine-Objekt mit generiertem Namen.
            foreach (var t in typeof(ZoneSystem).GetNestedTypes(BindingFlags.NonPublic))
                if (t.Name.StartsWith("<GenerateLocationsTimeSliced>") && t.GetMethod("MoveNext", AccessTools.all) != null)
                    list.Add(t.GetMethod("MoveNext", AccessTools.all));
            return list.Where(m => m != null);
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            WorldScale.ScaleConstants(instructions);
    }

    /// <summary>Das Raster der Biom-Varianten (2048 x 2048 Punkte à 12 m) wird gröber statt größer.</summary>
    [HarmonyPatch(typeof(AltBiomeWorldData), nameof(AltBiomeWorldData.MapSpaceToWorldSpace), typeof(float))]
    static class Patch_MapToWorld
    {
        static bool Prefix(float x, ref float __result)
        {
            if (WorldScale.S == 1f) return true;
            __result = (x - 1024f) * 12f * WorldScale.S + 6f * WorldScale.S;
            return false;
        }
    }

    [HarmonyPatch(typeof(AltBiomeWorldData), nameof(AltBiomeWorldData.WorldSpaceToMapSpace), typeof(float))]
    static class Patch_WorldToMap
    {
        static bool Prefix(float x, ref int __result)
        {
            if (WorldScale.S == 1f) return true;
            __result = (int)((x - 6f * WorldScale.S) / (12f * WorldScale.S) + 1024f);
            return false;
        }
    }

    /// <summary>Karte: jeder Kartenpixel deckt entsprechend mehr Fläche ab.</summary>
    internal static class MinimapScale
    {
        static float s_originalPixel = -1f;

        public static void Apply()
        {
            var map = Minimap.instance;
            if (map == null) return;
            if (s_originalPixel < 0f) s_originalPixel = map.m_pixelSize;
            map.m_pixelSize = s_originalPixel * WorldScale.S;
        }
    }

    [HarmonyPatch(typeof(Minimap), "Start")]
    static class Patch_MinimapStart
    {
        static void Prefix() => MinimapScale.Apply();
    }

    /// <summary>
    /// Ashlands: Valheim berechnet einen "Hauptinsel"-Faktor (großes Land, viel Lava) aus dem Abstand
    /// zu einer Linie im normalen Ring-Aufbau. Bei uns liegt das Band woanders, deshalb wäre fast
    /// alles "Nebeninsel" (klein, kaum Lava). Wir setzen den Faktor im ganzen Band auf Hauptinsel.
    /// </summary>
    [HarmonyPatch(typeof(WorldGenerator), "GetAshlandsHeight")]
    static class Patch_AshlandsMainIsland
    {
        static readonly MethodInfo M_Main = AccessTools.Method(typeof(Patch_AshlandsMainIsland), nameof(MainIsland));

        public static double MainIsland(double vanilla) =>
            State.Active ? Math.Max(vanilla, Plugin.AshlandsMainIsland.Value) : vanilla;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            // Gesucht: "ldloc V_4; ldloc V_5; mul; stloc V_4" (Hauptinsel-Faktor mal Mittellinien-Faktor).
            for (int i = 3; i < codes.Count; i++)
            {
                if (IsLocal(codes[i], 4, store: true) && codes[i - 1].opcode == OpCodes.Mul &&
                    IsLocal(codes[i - 2], 5, store: false) && IsLocal(codes[i - 3], 4, store: false))
                {
                    codes.InsertRange(i + 1, new[]
                    {
                        new CodeInstruction(OpCodes.Ldloc_S, codes[i].operand),
                        new CodeInstruction(OpCodes.Call, M_Main),
                        new CodeInstruction(OpCodes.Stloc_S, codes[i].operand),
                    });
                    return codes;
                }
            }
            Plugin.Log.LogWarning("[Ashlands] Hauptinsel-Stelle im Spielcode nicht gefunden, Ashlands bleiben normal.");
            return codes;
        }

        static bool IsLocal(CodeInstruction ci, int index, bool store)
        {
            if (store ? !ci.IsStloc() : !ci.IsLdloc()) return false;
            if (ci.operand is LocalBuilder lb) return lb.LocalIndex == index;
            if (store)
                return (index == 0 && ci.opcode == OpCodes.Stloc_0) || (index == 1 && ci.opcode == OpCodes.Stloc_1) ||
                       (index == 2 && ci.opcode == OpCodes.Stloc_2) || (index == 3 && ci.opcode == OpCodes.Stloc_3);
            return (index == 0 && ci.opcode == OpCodes.Ldloc_0) || (index == 1 && ci.opcode == OpCodes.Ldloc_1) ||
                   (index == 2 && ci.opcode == OpCodes.Ldloc_2) || (index == 3 && ci.opcode == OpCodes.Ldloc_3);
        }
    }
}

namespace Breitengrade
{
    /// <summary>
    /// Der Wasser-Shader hat eigene Werte für die Weltgröße: Wasser wird nur bis _WaterEdge (10500 m)
    /// gezeichnet, und das rote Ashlands-Wasser beginnt bei _AshlandsDistance/_AshlandsYOffset.
    /// Bei größerer Welt werden diese Werte mitskaliert (in allen Wasser-Materialien und an jeder Wasserfläche).
    /// </summary>
    internal static class WaterShaderScale
    {
        static readonly int WaterEdge = UnityEngine.Shader.PropertyToID("_WaterEdge");
        static readonly int AshDistance = UnityEngine.Shader.PropertyToID("_AshlandsDistance");
        static readonly int AshOffset = UnityEngine.Shader.PropertyToID("_AshlandsYOffset");

        public static void ApplyAll()
        {
            try
            {
                foreach (var mat in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Material>())
                    if (mat != null && mat.shader != null && mat.HasProperty(WaterEdge)) Apply(mat);
                foreach (var w in WaterVolume.Instances)
                    if (w != null) ApplyTo(w);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Wasser] " + e.Message);
            }
        }

        static void Apply(UnityEngine.Material mat)
        {
            float s = WorldScale.S;
            mat.SetFloat(WaterEdge, 10500f * s);
            if (mat.HasProperty(AshDistance)) mat.SetFloat(AshDistance, 12000f * s);
            if (mat.HasProperty(AshOffset)) mat.SetFloat(AshOffset, 4000f * s);
        }

        public static void ApplyTo(WaterVolume w)
        {
            var r = w.m_waterSurface;
            if (r == null) return;
            if (r.sharedMaterial != null && r.sharedMaterial.HasProperty(WaterEdge)) Apply(r.sharedMaterial);
            var block = new UnityEngine.MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            float s = WorldScale.S;
            block.SetFloat(WaterEdge, 10500f * s);
            block.SetFloat(AshDistance, 12000f * s);
            block.SetFloat(AshOffset, 4000f * s);
            r.SetPropertyBlock(block);
        }
    }

    [HarmonyPatch(typeof(WaterVolume), "SetupMaterial")]
    static class Patch_WaterSetupMaterial
    {
        static void Postfix(WaterVolume __instance) => WaterShaderScale.ApplyTo(__instance);
    }

    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.UpdateMaterials))]
    static class Patch_WaterUpdateMaterials
    {
        static void Postfix(WaterVolume __instance) => WaterShaderScale.ApplyTo(__instance);
    }
}
