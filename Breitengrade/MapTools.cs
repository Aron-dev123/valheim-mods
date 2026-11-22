using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Breitengrade
{
    /// <summary>
    /// Test-Hilfen für große Welten:
    /// - Große Karte: unter dem Biom-Namen stehen die Koordinaten (X, Z) und die Bodenhöhe unter dem Mauszeiger.
    /// - bg_pins: alle Kartenmarkierungen mit Koordinaten und Bodenhöhe.
    /// - bg_tp x z | bg_tp x y z | bg_tp Markierungsname: Teleport.
    /// </summary>
    internal static class MapTools
    {
        static readonly MethodInfo ScreenToWorld = AccessTools.Method(typeof(Minimap), "ScreenToWorldPoint");

        /// <summary>Bodenhöhe: echtes Gelände, wenn geladen, sonst die berechnete Höhe aus dem Weltgenerator.</summary>
        public static float Ground(float x, float z)
        {
            var p = new Vector3(x, 0f, z);
            if (ZoneSystem.instance != null && ZoneSystem.instance.IsZoneLoaded(p) &&
                ZoneSystem.instance.GetGroundHeight(p, out float h)) return h;
            return WorldGenerator.instance != null ? WorldGenerator.instance.GetHeight(x, z) : 0f;
        }

        public static void UpdateLargeMap(Minimap map)
        {
            if (!State.Active || map == null || map.m_mode != Minimap.MapMode.Large || ScreenToWorld == null) return;
            if (map.m_biomeNameLarge == null) return;
            var world = (Vector3)ScreenToWorld.Invoke(map, new object[] { ZInput.pointerPosition });
            string baseText = map.m_biomeNameLarge.text ?? "";
            int nl = baseText.IndexOf('\n');
            if (nl >= 0) baseText = baseText.Substring(0, nl);
            float ground = Ground(world.x, world.z);
            map.m_biomeNameLarge.text = $"{baseText}\n<size=60%>X {world.x:0}   Z {world.z:0}   Boden {ground:0.0} m</size>";
        }

        public static void ListPins(Terminal terminal)
        {
            var map = Minimap.instance;
            if (map == null) return;
            var pins = Traverse.Create(map).Field("m_pins").GetValue<System.Collections.Generic.List<Minimap.PinData>>();
            if (pins == null || pins.Count == 0) { terminal?.AddString("Keine Markierungen."); return; }
            foreach (var p in pins.OrderBy(p => p.m_name))
            {
                string line = $"{(string.IsNullOrEmpty(p.m_name) ? "(ohne Namen)" : p.m_name)}: " +
                              $"{p.m_pos.x:0} {Ground(p.m_pos.x, p.m_pos.z):0.0} {p.m_pos.z:0}";
                terminal?.AddString(line);
                Plugin.Log.LogInfo("[bg_pins] " + line);
            }
            terminal?.AddString("Format: Name: X Höhe Z  (für bg_tp X Höhe Z)");
        }

        public static void Teleport(Terminal terminal, string[] args)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;
            var inv = CultureInfo.InvariantCulture;
            var nums = args.Skip(1).Select(a => float.TryParse(a.Replace(',', '.'), NumberStyles.Float, inv, out float v) ? (float?)v : null).ToList();

            Vector3 target;
            if (nums.Count == 2 && nums.All(n => n.HasValue))
            {
                float x = nums[0].Value, z = nums[1].Value;
                target = new Vector3(x, Ground(x, z) + 1f, z);
            }
            else if (nums.Count == 3 && nums.All(n => n.HasValue))
            {
                target = new Vector3(nums[0].Value, nums[1].Value, nums[2].Value);
            }
            else if (args.Length > 1)
            {
                string name = string.Join(" ", args.Skip(1));
                var pins = Traverse.Create(Minimap.instance).Field("m_pins").GetValue<System.Collections.Generic.List<Minimap.PinData>>();
                var pin = pins?.FirstOrDefault(p => string.Equals(p.m_name, name, StringComparison.OrdinalIgnoreCase))
                          ?? pins?.FirstOrDefault(p => p.m_name != null && p.m_name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                if (pin == null) { terminal?.AddString($"Keine Markierung \"{name}\" gefunden."); return; }
                target = new Vector3(pin.m_pos.x, Ground(pin.m_pos.x, pin.m_pos.z) + 1f, pin.m_pos.z);
            }
            else
            {
                terminal?.AddString("bg_tp X Z  |  bg_tp X Höhe Z  |  bg_tp Markierungsname");
                return;
            }
            player.TeleportTo(target, player.transform.rotation, true);
            terminal?.AddString($"Teleport nach {target.x:0} {target.y:0.0} {target.z:0}");
        }
    }

    [HarmonyPatch(typeof(Minimap), "Update")]
    static class Patch_MinimapCoordinates
    {
        static void Postfix(Minimap __instance)
        {
            try { MapTools.UpdateLargeMap(__instance); }
            catch { /* Anzeige ist nur eine Hilfe */ }
        }
    }
}
