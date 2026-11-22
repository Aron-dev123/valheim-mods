using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Breitengrade
{
    /// <summary>
    /// Lädt das Stadtbau-Modul (Breitengrade.Bau*.dll aus Valheim/BepInEx/breitengrade_bau/).
    /// Es kann im laufenden Spiel neu geladen werden: Jede neu gebaute Version hat einen eigenen
    /// Dateinamen, die neueste wird genommen.
    /// </summary>
    public static class BauLoader
    {
        static MethodInfo s_build, s_layout, s_streets, s_hills;
        static string s_file;

        public static string Folder => Path.Combine(BepInEx.Paths.BepInExRootPath, "breitengrade_bau");

        public static bool EnsureLoaded() => s_build != null || Reload();

        public static bool Reload()
        {
            try
            {
                if (!Directory.Exists(Folder)) { Plugin.Log.LogWarning($"[Bau] Ordner fehlt: {Folder}"); return false; }
                var file = Directory.GetFiles(Folder, "Breitengrade.Bau*.dll").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (file == null) { Plugin.Log.LogWarning("[Bau] Kein Stadtbau-Modul gefunden."); return false; }
                if (file == s_file && s_build != null) return true;
                var asm = Assembly.Load(File.ReadAllBytes(file));
                var type = asm.GetType("Breitengrade.Bau.Builder", true);
                s_build = type.GetMethod("Build", BindingFlags.Public | BindingFlags.Static);
                s_layout = type.GetMethod("Layout", BindingFlags.Public | BindingFlags.Static);
                s_streets = type.GetMethod("Streets", BindingFlags.Public | BindingFlags.Static);
                s_hills = type.GetMethod("Hills", BindingFlags.Public | BindingFlags.Static);
                s_file = file;
                Plugin.Log.LogInfo($"[Bau] Stadtbau-Modul geladen: {Path.GetFileName(file)}");
                return s_build != null && s_layout != null;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("[Bau] Laden fehlgeschlagen: " + e);
                return false;
            }
        }

        public static string Loaded => s_file != null ? Path.GetFileName(s_file) : "-";

        public static int Build(CityBuilder.Plan p)
        {
            if (!EnsureLoaded()) return 0;
            try { return (int)s_build.Invoke(null, new object[] { p }); }
            catch (TargetInvocationException e) { throw e.InnerException ?? e; }
        }

        public static void Streets(CityBuilder.Plan p, System.Collections.Generic.List<RoadNet.Seg> segs, System.Collections.Generic.List<RoadNet.Area> areas)
        {
            if (!EnsureLoaded() || s_streets == null) return;
            try { s_streets.Invoke(null, new object[] { p, segs, areas }); }
            catch (Exception e) { Plugin.Log.LogWarning($"[Bau] Straßen {p.Name}: {e.InnerException?.Message ?? e.Message}"); }
        }

        public static void Hills(CityBuilder.Plan p, System.Collections.Generic.List<PointsOfInterest.Plateau> list)
        {
            if (!EnsureLoaded() || s_hills == null) return;
            try { s_hills.Invoke(null, new object[] { p, list }); }
            catch (Exception e) { Plugin.Log.LogWarning($"[Bau] Hügel {p.Name}: {e.InnerException?.Message ?? e.Message}"); }
        }

        public static (float inner, float cell, bool paved) Layout(CityBuilder.Plan p)
        {
            if (!EnsureLoaded()) return (p.R - 16f, 17f, false);
            return ((float, float, bool))s_layout.Invoke(null, new object[] { p });
        }
    }
}
