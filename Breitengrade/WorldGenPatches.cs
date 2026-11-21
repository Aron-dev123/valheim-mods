using System;
using HarmonyLib;

namespace Breitengrade
{
    /// <summary>Merkt sich, ob die gerade geladene Welt unsere Breitengrad-Welt ist.</summary>
    internal static class State
    {
        public static bool Active;

        public static void OnWorldGenerator(World world)
        {
            Active = world != null && !world.m_menu &&
                     string.Equals(world.m_name, Plugin.WorldName.Value, StringComparison.OrdinalIgnoreCase);

            if (!Active) return;

            if (!Latitude.TryParseBounds(Plugin.BandBounds.Value, out var bounds))
            {
                Plugin.Log.LogWarning($"Grenzen \"{Plugin.BandBounds.Value}\" ungültig, nehme Standard.");
                Latitude.TryParseBounds(Latitude.DefaultBounds, out bounds);
            }
            Latitude.Configure(world.m_seed, bounds, Plugin.Wobble.Value);
            Plugin.Log.LogInfo($"Breitengrade AKTIV für Welt \"{world.m_name}\" (Seed {world.m_seedName}).");
        }
    }

    [HarmonyPatch(typeof(WorldGenerator), MethodType.Constructor, typeof(World))]
    static class Patch_Ctor
    {
        static void Prefix(World world) => State.OnWorldGenerator(world);
    }

    /// <summary>Biom nach Breitengrad statt nach Entfernung zur Mitte.</summary>
    [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GetBiome),
        typeof(float), typeof(float), typeof(float), typeof(bool))]
    static class Patch_GetBiome
    {
        static bool Prefix(WorldGenerator __instance, float wx, float wy, float oceanLevel,
            bool waterAlwaysOcean, ref Heightmap.Biome __result)
        {
            if (!State.Active) return true;

            if (waterAlwaysOcean)
            {
                if (__instance.GetHeight(wx, wy) <= oceanLevel)
                {
                    __result = Heightmap.Biome.Ocean;
                    return false;
                }
            }
            else if (Terrain.BaseHeight(__instance, wx, wy) <= oceanLevel)
            {
                // Passiert nur noch am äußersten Weltrand.
                __result = Heightmap.Biome.Ocean;
                return false;
            }

            __result = (Heightmap.Biome)Latitude.GetBiome(wx, wy);
            return false;
        }
    }

    /// <summary>Grundhöhe ohne Ozean: alles Land, nur am Weltrand fällt es ab.</summary>
    [HarmonyPatch(typeof(WorldGenerator), "GetBaseHeight")]
    static class Patch_GetBaseHeight
    {
        static bool Prefix(WorldGenerator __instance, float wx, float wy, bool menuTerrain, ref float __result)
        {
            if (!State.Active || menuTerrain) return true;
            __result = Terrain.BaseHeight(__instance, wx, wy);
            return false;
        }
    }

    [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.IsAshlands))]
    static class Patch_IsAshlands
    {
        static bool Prefix(float x, float y, ref bool __result)
        {
            if (!State.Active) return true;
            __result = Latitude.AshlandsDepth(x, y) > 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.IsDeepnorth))]
    static class Patch_IsDeepnorth
    {
        static bool Prefix(float x, float y, ref bool __result)
        {
            if (!State.Active) return true;
            __result = Latitude.DeepNorthDepth(x, y) > 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GetAshlandsOceanGradient),
        typeof(float), typeof(float))]
    static class Patch_AshlandsGradient
    {
        static bool Prefix(float x, float y, ref float __result)
        {
            if (!State.Active) return true;
            __result = (Latitude.AshlandsDepth(x, y) + 300f) / 300f;
            return false;
        }
    }

    /// <summary>Vanilla schneidet einen Wassergraben um Ashlands und Deep North. Den lassen wir weg.</summary>
    [HarmonyPatch(typeof(WorldGenerator), "CreateAshlandsGap")]
    static class Patch_AshlandsGap
    {
        static bool Prefix(ref double __result)
        {
            if (!State.Active) return true;
            __result = 1.0;
            return false;
        }
    }

    [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.CreateDeepNorthGap))]
    static class Patch_DeepNorthGap
    {
        static bool Prefix(ref double __result)
        {
            if (!State.Active) return true;
            __result = 1.0;
            return false;
        }
    }

    [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.DeepNorthWaveFade))]
    static class Patch_DeepNorthWaveFade
    {
        static bool Prefix(float wx, float wy, ref double __result)
        {
            if (!State.Active) return true;
            double v = (Latitude.DeepNorthDepth(wx, wy) + 200f) / 200f;
            __result = v < 0 ? 0 : (v > 1 ? 1 : v);
            return false;
        }
    }

    internal static class Terrain
    {
        static readonly AccessTools.FieldRef<WorldGenerator, float> Offset0 =
            AccessTools.FieldRefAccess<WorldGenerator, float>("m_offset0");
        static readonly AccessTools.FieldRef<WorldGenerator, float> Offset1 =
            AccessTools.FieldRefAccess<WorldGenerator, float>("m_offset1");

        /// <summary>
        /// Nachbau von WorldGenerator.GetBaseHeight ohne die Wasserkanäle zwischen den Inseln.
        /// Die Höhe wird so angehoben, dass alles über dem Meeresspiegel liegt.
        /// </summary>
        public static float BaseHeight(WorldGenerator wg, float wx, float wy)
        {
            double x = wx + 100000.0 + Offset0(wg);
            double y = wy + 100000.0 + Offset1(wg);

            float h = 0f;
            h += DUtils.PerlinNoise(x * 0.002 * 0.5, y * 0.002 * 0.5) *
                 DUtils.PerlinNoise(x * 0.003 * 0.5, y * 0.003 * 0.5);
            h += DUtils.PerlinNoise(x * 0.002, y * 0.002) *
                 DUtils.PerlinNoise(x * 0.003, y * 0.003) * h * 0.9f;
            h += DUtils.PerlinNoise(x * 0.005, y * 0.005) *
                 DUtils.PerlinNoise(x * 0.01, y * 0.01) * 0.5f * h;
            h -= 0.07f;

            // Ozean raus: alles auf Landhöhe anheben, Form der Hügel bleibt erhalten.
            h = Plugin.LandMin.Value + Math.Max(h, 0f) * 0.8f;

            // Mountain-Band anheben, damit dort echte Berge stehen.
            h += Plugin.MountainLift.Value * Latitude.MountainFactor(wx, wy);

            // Weltrand wie im Original: ab 10000 m fällt das Land ins Meer ab.
            float dist = DUtils.Length(wx, wy);
            if (dist > 10000f)
            {
                h = Lerp(h, -0.2f, DUtils.LerpStep(10000f, 10500f, dist));
                if (dist > 10490f) h = Lerp(h, -2f, DUtils.LerpStep(10490f, 10500f, dist));
            }
            return h;
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
