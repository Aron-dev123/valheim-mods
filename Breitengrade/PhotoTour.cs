using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Breitengrade
{
    /// <summary>
    /// Fotos zum Prüfen der Städte (für die Entwicklung): Die Kamera fliegt um eine Stadt und speichert
    /// Bilder aus mehreren Blickwinkeln in Valheim/BepInEx/fotos/.
    ///   bg_foto              – Stadt in der Nähe (oder alle Blickwinkel um die eigene Position)
    ///   bg_foto Name         – zu dieser Stadt reisen und fotografieren
    ///   bg_foto hier         – rundum von der eigenen Position aus
    ///   bg_rundgang [ab Nr.] – alle Städte nacheinander (dauert ein paar Minuten)
    /// Während der Fotos: Mittag, klares Wetter, keine Anzeigen, Gottmodus.
    /// </summary>
    internal static class PhotoTour
    {
        static bool s_running;
        public static string Folder => Path.Combine(BepInEx.Paths.BepInExRootPath, "fotos");

        public static void Command(Terminal t, string[] args, bool all)
        {
            if (s_running) { t?.AddString("Fotos laufen schon."); return; }
            if (Player.m_localPlayer == null || Plugin.Instance == null) return;
            Directory.CreateDirectory(Folder);
            var plans = CityBuilder.Plans.ToList();
            if (all)
            {
                int from = args.Length > 1 && int.TryParse(args[1], out int f) ? Math.Max(0, f) : 0;
                Plugin.Instance.StartCoroutine(Run(plans.Skip(from).ToList(), t));
                t?.AddString($"Rundgang: {plans.Count - from} Städte, Bilder in {Folder}");
                return;
            }
            string name = args.Length > 1 ? string.Join(" ", args.Skip(1)) : "";
            if (name.Equals("hier", StringComparison.OrdinalIgnoreCase))
            {
                Plugin.Instance.StartCoroutine(Here(t));
                return;
            }
            CityBuilder.Plan plan;
            if (name.Length > 0)
                plan = plans.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                       ?? plans.FirstOrDefault(p => p.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
            else
            {
                var me = Player.m_localPlayer.transform.position;
                plan = plans.OrderBy(p => (p.X - me.x) * (p.X - me.x) + (p.Z - me.z) * (p.Z - me.z)).FirstOrDefault();
            }
            if (plan == null) { t?.AddString("Keine Stadt gefunden."); return; }
            Plugin.Instance.StartCoroutine(Run(new List<CityBuilder.Plan> { plan }, t));
            t?.AddString($"Fotos von {plan.Name}, Bilder in {Folder}");
        }

        static IEnumerator Run(List<CityBuilder.Plan> plans, Terminal t)
        {
            s_running = true;
            var state = Begin();
            try
            {
                int i = 0;
                foreach (var p in plans)
                {
                    i++;
                    var player = Player.m_localPlayer;
                    if (player == null) yield break;
                    var c = new Vector3(p.X, p.H, p.Z);
                    player.TeleportTo(c + new Vector3(0f, 2f, 0f), Quaternion.identity, true);
                    float until = Time.time + 60f;
                    yield return new WaitForSeconds(1f);
                    while (Time.time < until && (player.IsTeleporting() || !ZNetScene.instance.IsAreaReady(c)))
                        yield return new WaitForSeconds(0.5f);
                    yield return new WaitForSeconds(8f); // Gebäude erscheinen nach und nach
                    string baseName = Safe(p.Name);
                    float R = p.R;
                    foreach (var (k, pos, look) in Shots(c, R))
                        yield return Shot(pos, look, $"{baseName}_{k}.png");
                    Plugin.Log.LogInfo($"[Fotos] {p.Name} fertig ({i}/{plans.Count}).");
                }
                t?.AddString($"Fotos fertig: {Folder}");
            }
            finally
            {
                End(state);
                s_running = false;
            }
        }

        static IEnumerator Here(Terminal t)
        {
            s_running = true;
            var state = Begin();
            try
            {
                var p = Player.m_localPlayer.transform.position;
                string stamp = DateTime.Now.ToString("HHmmss");
                for (int k = 0; k < 4; k++)
                {
                    float a = k * 90f * Mathf.Deg2Rad;
                    var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    yield return Shot(p + new Vector3(0f, 12f, 0f) - dir * 5f, p + dir * 120f, $"hier_{stamp}_{k}.png");
                }
                yield return Shot(p + new Vector3(0f, 160f, -60f), p, $"hier_{stamp}_oben.png");
                t?.AddString($"Fotos fertig: {Folder}");
            }
            finally
            {
                End(state);
                s_running = false;
            }
        }

        /// <summary>Blickwinkel: schräg von oben aus vier Richtungen, senkrecht von oben, zwei auf Straßenhöhe, Wahrzeichen.</summary>
        internal static IEnumerable<(string k, Vector3 pos, Vector3 look)> Shots(Vector3 c, float R)
        {
            yield return ("1_uebersicht", c + new Vector3(-0.7f * R, 0.5f * R, -0.7f * R), c);
            yield return ("2_ost", c + new Vector3(1.0f * R, 0.45f * R, -0.5f * R), c);
            yield return ("3_nord", c + new Vector3(0.4f * R, 0.4f * R, 1.0f * R), c);
            yield return ("4_west", c + new Vector3(-1.0f * R, 0.35f * R, 0.4f * R), c);
            yield return ("5_oben", c + new Vector3(0f, 1.1f * R, -0.25f * R), c);
            yield return ("6_strasse", c + new Vector3(3f, 2f, -0.75f * R), c + new Vector3(3f, 4f, 0f));
            yield return ("7_gasse", c + new Vector3(0.45f * R, 2.2f, 0.35f * R), c + new Vector3(0.1f * R, 3f, 0.6f * R));
            yield return ("8_nah", c + new Vector3(25f, 14f, -38f), c + new Vector3(0f, 10f, 0f));
        }

        internal static IEnumerator Shot(Vector3 pos, Vector3 look, string file)
        {
            var cam = Camera.main;
            if (cam == null) yield break;
            for (int f = 0; f < 4; f++)
            {
                cam.transform.position = pos;
                cam.transform.rotation = Quaternion.LookRotation(look - pos);
                yield return new WaitForEndOfFrame();
            }
            ScreenCapture.CaptureScreenshot(Path.Combine(Folder, file));
            for (int f = 0; f < 3; f++)
            {
                cam.transform.position = pos;
                cam.transform.rotation = Quaternion.LookRotation(look - pos);
                yield return new WaitForEndOfFrame();
            }
        }

        internal sealed class Saved { public bool God, CamEnabled, Hud; public Vector3 Pos; }

        internal static Saved Begin()
        {
            var s = new Saved();
            var player = Player.m_localPlayer;
            s.God = player.InGodMode();
            s.Pos = player.transform.position;
            player.SetGodMode(true);
            if (GameCamera.instance != null) { s.CamEnabled = GameCamera.instance.enabled; GameCamera.instance.enabled = false; }
            if (Hud.instance != null && Hud.instance.m_rootObject != null) { s.Hud = Hud.instance.m_rootObject.activeSelf; Hud.instance.m_rootObject.SetActive(false); }
            if (EnvMan.instance != null)
            {
                EnvMan.instance.m_debugTimeOfDay = true;
                EnvMan.instance.m_debugTime = 0.45f;
                EnvMan.instance.SetForceEnvironment("Clear");
            }
            return s;
        }

        internal static void End(Saved s)
        {
            var player = Player.m_localPlayer;
            if (player != null) player.SetGodMode(s.God);
            if (GameCamera.instance != null) GameCamera.instance.enabled = true;
            if (Hud.instance != null && Hud.instance.m_rootObject != null) Hud.instance.m_rootObject.SetActive(true);
            if (EnvMan.instance != null)
            {
                EnvMan.instance.m_debugTimeOfDay = false;
                EnvMan.instance.SetForceEnvironment("");
            }
        }

        internal static string Safe(string name) => new string(name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
    }
}
