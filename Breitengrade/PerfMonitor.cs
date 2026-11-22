using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Breitengrade
{
    /// <summary>
    /// Leistungs-Protokoll: schreibt alle 15 Sekunden ins Log, was im Spiel los ist
    /// (FPS, Anzahl Objekte und Kreaturen, häufigste Objekte, Geländeaufrufe, Laufgeschwindigkeit).
    /// Damit lässt sich sehen, was bei schleichendem Lag immer mehr wird.
    /// </summary>
    internal static class PerfMonitor
    {
        public static int BiomeHeightCalls, GeoCalls, SupportCalls;
        static float s_next, s_frames, s_time;

        static readonly AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>> Instances =
            AccessTools.FieldRefAccess<ZNetScene, Dictionary<ZDO, ZNetView>>("m_instances");

        public static void Tick()
        {
            if (!State.Active || !Plugin.PerfLog.Value) return;
            s_frames++;
            s_time += Time.unscaledDeltaTime;
            if (Time.unscaledTime < s_next) return;
            s_next = Time.unscaledTime + 15f;

            try
            {
                float fps = s_time > 0f ? s_frames / s_time : 0f;
                s_frames = 0f; s_time = 0f;

                var player = Player.m_localPlayer;
                string pos = "-", speed = "-", biome = "-";
                if (player != null)
                {
                    var p = player.transform.position;
                    pos = $"({p.x:0}, {p.y:0}, {p.z:0})";
                    var body = player.GetComponent<Rigidbody>();
                    speed = body != null ? new Vector3(body.velocity.x, 0f, body.velocity.z).magnitude.ToString("0.0") : "?";
                    biome = player.GetCurrentBiome().ToString();
                }

                int objects = 0;
                string top = "-";
                var scene = ZNetScene.instance;
                if (scene != null)
                {
                    var dict = Instances(scene);
                    if (dict != null)
                    {
                        objects = dict.Count;
                        top = string.Join(", ", dict.Values
                            .Where(v => v != null)
                            .GroupBy(v => Utils.GetPrefabName(v.gameObject))
                            .OrderByDescending(g => g.Count())
                            .Take(8)
                            .Select(g => $"{g.Key} {g.Count()}"));
                    }
                }

                int creatures = Character.GetAllCharacters().Count;
                long mem = GC.GetTotalMemory(false) / (1024 * 1024);

                Plugin.Log.LogInfo($"[Leistung] {fps:0} FPS | Objekte {objects} | Kreaturen {creatures} | " +
                                   $"Speicher {mem} MB | Gelände-Aufrufe {BiomeHeightCalls} | Biom-Prüfungen {GeoCalls} | " +
                                   $"Stütz-Prüfungen {SupportCalls} | Pos {pos} {biome} | Tempo {speed}");
                Plugin.Log.LogInfo($"[Leistung]   häufigste Objekte: {top}");
                BiomeHeightCalls = 0; GeoCalls = 0; SupportCalls = 0;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Leistung] " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(WorldGenerator), "GetBiomeHeight")]
    static class Patch_CountBiomeHeight
    {
        static void Prefix() => PerfMonitor.BiomeHeightCalls++;
    }
}
