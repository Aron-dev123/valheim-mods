using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace Breitengrade
{
    /// <summary>
    /// Seit dem Deep-North-Update gibt es Schnee als Bodenobjekte. Einige davon dürfen in mehreren
    /// Biomen erscheinen, aber nur weit draußen (großer Abstand zur Weltmitte), also in normalen
    /// Welten nahe am Deep North. In unserer Welt ist "weit draußen" das Ost- und Westende jedes
    /// Bands. Deshalb erlauben wir Schnee-Objekte nur noch in Deep North und Mountain.
    /// </summary>
    internal static class VegetationAdjuster
    {
        const int SnowBiomes = Latitude.DeepNorth | Latitude.Mountain;

        sealed class Original
        {
            public Heightmap.Biome Biome;
            public bool Enable;
            public float MinDist, MaxDist;
        }

        static readonly Dictionary<ZoneSystem.ZoneVegetation, Original> s_original =
            new Dictionary<ZoneSystem.ZoneVegetation, Original>();
        static bool s_appliedForWorld, s_appliedState;

        public static void OnWorldChanged() => s_appliedForWorld = false;

        public static void Apply(ZoneSystem zs)
        {
            if (s_appliedForWorld && s_appliedState == State.Active) return;
            s_appliedForWorld = true;
            s_appliedState = State.Active;
            if (zs?.m_vegetation == null) return;

            int changed = 0;
            foreach (var veg in zs.m_vegetation)
            {
                if (veg == null) continue;
                if (!s_original.TryGetValue(veg, out var o))
                    s_original[veg] = o = new Original
                    {
                        Biome = veg.m_biome, Enable = veg.m_enable,
                        MinDist = veg.m_minDistanceFromCenter, MaxDist = veg.m_maxDistanceFromCenter,
                    };

                veg.m_biome = o.Biome;
                veg.m_enable = o.Enable;
                veg.m_minDistanceFromCenter = o.MinDist;
                veg.m_maxDistanceFromCenter = o.MaxDist;
                if (!State.Active || !o.Enable) continue;

                // Abstandsregeln (z. B. Frost-Varianten weit draußen) wachsen mit der Weltgröße mit,
                // sonst gilt bei großer Welt fast alles als "ganz außen".
                if (WorldScale.S != 1f && (o.MinDist > 0f || o.MaxDist > 0f))
                {
                    veg.m_minDistanceFromCenter = o.MinDist * WorldScale.S;
                    veg.m_maxDistanceFromCenter = o.MaxDist * WorldScale.S;
                }
                if (o.MinDist > 0f)
                    Plugin.Log.LogInfo($"[Pflanzen] {Name(veg)} ({o.Biome}): nur ab {o.MinDist:0} m vom Zentrum " +
                                       $"(jetzt {veg.m_minDistanceFromCenter:0} m)");

                if (!IsSnow(veg)) continue;

                int mask = (int)o.Biome;
                if ((mask & ~SnowBiomes) == 0) continue; // nur Deep North/Mountain: passt schon

                int restricted = mask & SnowBiomes;
                if (restricted == 0) veg.m_enable = false;
                else veg.m_biome = (Heightmap.Biome)restricted;
                changed++;
                Plugin.Log.LogInfo($"[Schnee] {Name(veg)}: Biome {o.Biome} -> " +
                                   (restricted == 0 ? "aus" : ((Heightmap.Biome)restricted).ToString()) +
                                   $" (Abstand zur Mitte {veg.m_minDistanceFromCenter:0}-{veg.m_maxDistanceFromCenter:0})");
            }
            if (State.Active)
                Plugin.Log.LogInfo($"[Schnee] {changed} Schnee-Objekttypen auf Deep North/Mountain beschränkt.");
        }

        static bool IsSnow(ZoneSystem.ZoneVegetation veg)
        {
            string n = Name(veg);
            foreach (var word in new[] { "snow", "frost", "frozen", "winter", "icy" })
                if (n.IndexOf(word, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return veg.m_prefab != null && veg.m_prefab.GetComponentsInChildren<SnowDestruction>(true).Any();
        }

        static string Name(ZoneSystem.ZoneVegetation veg) =>
            veg.m_prefab != null ? veg.m_prefab.name : veg.m_name ?? "?";
    }

    [HarmonyPatch(typeof(ZoneSystem), "PlaceVegetation")]
    static class Patch_PlaceVegetation_Snow
    {
        static void Prefix(ZoneSystem __instance) => VegetationAdjuster.Apply(__instance);
    }
}
