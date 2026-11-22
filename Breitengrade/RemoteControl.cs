using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Breitengrade
{
    /// <summary>
    /// Fernsteuerung für die Entwicklung: Befehle stehen in Valheim/BepInEx/claude/befehl.txt
    /// (eine Zeile je Befehl). Die Mod führt sie im laufenden Spiel aus, schreibt die Ergebnisse nach
    /// antwort.txt und löscht die Befehlsdatei. So kann ohne Eingaben im Spiel gebaut und fotografiert
    /// werden – das Spiel muss nur in der Welt laufen.
    ///
    ///   laden                      Stadtbau-Modul neu laden
    ///   neu Stadt                  Stadt abreißen und mit dem neuesten Modul neu bauen
    ///   foto Stadt [1,2,8]         Fotos (alle oder nur diese Blickwinkel)
    ///   kamera x y z bx by bz name ein Foto von (x,y,z) mit Blick auf (bx,by,bz)
    ///   tp Stadt | tp x z          hinreisen
    ///   teile                      alle Bauteile mit Maßen nach teile.txt
    ///   staedte                    Lage und Größe aller Städte nach staedte.txt
    /// </summary>
    public static class RemoteControl
    {
        public static string Folder => Path.Combine(BepInEx.Paths.BepInExRootPath, "claude");
        static string CommandFile => Path.Combine(Folder, "befehl.txt");
        static string AnswerFile => Path.Combine(Folder, "antwort.txt");
        static float s_next;
        static bool s_busy;
        static readonly StringBuilder s_answer = new StringBuilder();

        public static void Tick()
        {
            if (s_busy || Time.time < s_next) return;
            s_next = Time.time + 0.5f;
            if (Player.m_localPlayer == null || Plugin.Instance == null || !File.Exists(CommandFile)) return;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(CommandFile);
                File.Delete(CommandFile);
            }
            catch { return; }
            Plugin.Instance.StartCoroutine(Run(lines.Where(l => l.Trim().Length > 0).ToList()));
        }

        static void Say(string s)
        {
            s_answer.AppendLine(s);
            Plugin.Log.LogInfo("[Fernsteuerung] " + s);
        }

        static IEnumerator Run(List<string> lines)
        {
            s_busy = true;
            s_answer.Clear();
            Say($"START {DateTime.Now:HH:mm:ss}");
            foreach (var line in lines)
            {
                var parts = line.Trim().Split(new[] { ' ' }, 2);
                string cmd = parts[0].ToLowerInvariant(), arg = parts.Length > 1 ? parts[1].Trim() : "";
                IEnumerator job = null;
                try
                {
                    switch (cmd)
                    {
                        case "laden": Say(BauLoader.Reload() ? $"OK geladen {BauLoader.Loaded}" : "FEHLER laden"); break;
                        case "neu": Rebuild(arg); break;
                        case "foto": job = Photo(arg); break;
                        case "kamera": job = Camera(arg); break;
                        case "tp": job = Travel(arg); break;
                        case "teile": DumpPieces(); break;
                        case "staedte": DumpCities(); break;
                        default: Say($"FEHLER unbekannter Befehl: {line}"); break;
                    }
                }
                catch (Exception e) { Say($"FEHLER {line}: {e}"); }
                if (job != null) yield return job;
            }
            Say($"FERTIG {DateTime.Now:HH:mm:ss}");
            try { File.WriteAllText(AnswerFile, s_answer.ToString()); } catch { }
            s_busy = false;
        }

        static CityBuilder.Plan Find(string name) =>
            CityBuilder.Plans.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? CityBuilder.Plans.FirstOrDefault(p => p.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);

        // ---------------------------------------------------------------- Neu bauen

        static void Rebuild(string name)
        {
            var plans = name.Equals("alle", StringComparison.OrdinalIgnoreCase) ? CityBuilder.Plans.ToList()
                      : new List<CityBuilder.Plan> { Find(name) };
            if (plans.Count == 0 || plans[0] == null) { Say($"FEHLER keine Stadt {name}"); return; }
            if (!BauLoader.Reload()) { Say("FEHLER Modul"); return; }
            // Gassen, Plätze, Hügel neu berechnen und das geladene Gelände neu erzeugen
            CityBuilder.RefreshCityLayout();
            CityBuilder.RefreshPlateaus();
            foreach (var plan in plans)
            {
                int removed = Clear(plan);
                var t0 = DateTime.Now;
                int n = BauLoader.Build(plan);
                Say($"OK neu {plan.Name}: {removed} alte Teile entfernt, {n} neue Bauteile ({(DateTime.Now - t0).TotalSeconds:0.0} s, {BauLoader.Loaded})");
            }
            try { Heightmap.ForceGenerateAll(); } catch (Exception e) { Say("Gelände: " + e.Message); }
        }

        /// <summary>Entfernt alle von der Mod gebauten Teile im Stadtgebiet.</summary>
        static int Clear(CityBuilder.Plan p)
        {
            var dict = HarmonyLib.Traverse.Create(ZDOMan.instance).Field("m_objectsByID").GetValue<IDictionary<ZDOID, ZDO>>();
            if (dict == null) return 0;
            float r = p.R + 25f;
            var doomed = dict.Values.Where(z =>
            {
                var pos = z.GetPosition();
                float dx = pos.x - p.X, dz = pos.z - p.Z;
                return dx * dx + dz * dz < r * r && z.GetBool(MageTower.BuildMarker);
            }).ToList();
            foreach (var z in doomed) ZDOMan.instance.DestroyZDO(z);
            return doomed.Count;
        }

        // ---------------------------------------------------------------- Reisen und Fotos

        static IEnumerator Travel(string arg)
        {
            Vector3 target;
            var nums = arg.Split(' ').Select(a => float.TryParse(a.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? (float?)v : null).ToList();
            if (nums.Count == 2 && nums.All(v => v.HasValue))
                target = new Vector3(nums[0].Value, WorldGenerator.instance.GetHeight(nums[0].Value, nums[1].Value) + 2f, nums[1].Value);
            else
            {
                var plan = Find(arg);
                if (plan == null) { Say($"FEHLER keine Stadt {arg}"); yield break; }
                target = new Vector3(plan.X, plan.H + 2f, plan.Z);
            }
            yield return GoTo(target);
            Say($"OK tp {target.x:0} {target.y:0} {target.z:0}");
        }

        /// <summary>Reist hin (nur wenn nötig) und wartet, bis Gelände und Bauteile geladen sind.</summary>
        static IEnumerator GoTo(Vector3 target)
        {
            var player = Player.m_localPlayer;
            var now = player.transform.position;
            bool far = (new Vector2(now.x - target.x, now.z - target.z)).magnitude > 60f;
            if (far)
            {
                player.TeleportTo(target, player.transform.rotation, true);
                yield return new WaitForSeconds(1f);
                float until = Time.time + 60f;
                while (Time.time < until && (player.IsTeleporting() || !ZNetScene.instance.IsAreaReady(target)))
                    yield return new WaitForSeconds(0.5f);
                yield return new WaitForSeconds(6f);
            }
            else yield return new WaitForSeconds(2f); // neu gebaute Teile erscheinen
        }

        static IEnumerator Photo(string arg)
        {
            // "Stadt" oder "Stadt 1,2,8"
            string name = arg; string only = null;
            var lastSpace = arg.LastIndexOf(' ');
            if (lastSpace > 0 && arg.Substring(lastSpace + 1).All(ch => char.IsDigit(ch) || ch == ','))
            {
                name = arg.Substring(0, lastSpace);
                only = arg.Substring(lastSpace + 1);
            }
            var plan = Find(name);
            if (plan == null) { Say($"FEHLER keine Stadt {name}"); yield break; }
            var c = new Vector3(plan.X, plan.H, plan.Z);
            yield return GoTo(c + new Vector3(0f, 2f, 0f));
            var state = PhotoTour.Begin();
            try
            {
                var wanted = only?.Split(',').ToHashSet();
                foreach (var (k, pos, look) in PhotoTour.Shots(c, plan.R))
                {
                    if (wanted != null && !wanted.Contains(k.Split('_')[0])) continue;
                    string file = $"{PhotoTour.Safe(plan.Name)}_{k}.png";
                    yield return PhotoTour.Shot(pos, look, file);
                    Say($"BILD {Path.Combine(PhotoTour.Folder, file)}");
                }
            }
            finally { PhotoTour.End(state); }
        }

        static IEnumerator Camera(string arg)
        {
            var p = arg.Split(' ');
            if (p.Length < 7) { Say("FEHLER kamera x y z bx by bz name"); yield break; }
            var inv = CultureInfo.InvariantCulture;
            var f = p.Take(6).Select(s => float.Parse(s.Replace(',', '.'), inv)).ToArray();
            var pos = new Vector3(f[0], f[1], f[2]);
            var look = new Vector3(f[3], f[4], f[5]);
            yield return GoTo(new Vector3(look.x, WorldGenerator.instance.GetHeight(look.x, look.z) + 2f, look.z));
            var state = PhotoTour.Begin();
            try
            {
                string file = PhotoTour.Safe(p[6]) + ".png";
                yield return PhotoTour.Shot(pos, look, file);
                Say($"BILD {Path.Combine(PhotoTour.Folder, file)}");
            }
            finally { PhotoTour.End(state); }
        }

        // ---------------------------------------------------------------- Listen

        static void DumpPieces()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Name;Kategorie;Anzeigename;Andock-Min;Andock-Max;Kollider-Min;Kollider-Max;Haltbarkeit");
            foreach (var go in ZNetScene.instance.m_prefabs.OrderBy(g => g.name))
            {
                var piece = go.GetComponent<Piece>();
                if (piece == null) continue;
                var box = PieceBox.Of(go.name);
                var bounds = ColliderBounds(go);
                var wnt = go.GetComponent<WearNTear>();
                string name = piece.m_name;
                sb.AppendLine(string.Join(";", go.name, piece.m_category, name,
                    box != null ? V(box.Min) : "-", box != null ? V(box.Max) : "-",
                    bounds.HasValue ? V(bounds.Value.min) : "-", bounds.HasValue ? V(bounds.Value.max) : "-",
                    wnt != null ? wnt.m_health.ToString("0", CultureInfo.InvariantCulture) : "-"));
            }
            string file = Path.Combine(Folder, "teile.txt");
            File.WriteAllText(file, sb.ToString());
            Say($"OK teile {file}");
        }

        static void DumpCities()
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            foreach (var p in CityBuilder.Plans)
                sb.AppendLine(string.Format(inv, "{0};{1:0};{2:0};{3:0};{4:0.0}", p.Name, p.X, p.Z, p.R, p.H));
            string file = Path.Combine(Folder, "staedte.txt");
            File.WriteAllText(file, sb.ToString());
            Say($"OK staedte {file}");
        }

        static string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1:0.##} {2:0.##}", v.x, v.y, v.z);

        public static Bounds? ColliderBounds(GameObject prefab)
        {
            Bounds? b = null;
            foreach (var col in prefab.GetComponentsInChildren<Collider>(true))
            {
                if (col.isTrigger) continue;
                Bounds lb;
                if (col is BoxCollider bc) lb = new Bounds(bc.center, bc.size);
                else if (col is MeshCollider mc && mc.sharedMesh != null) lb = mc.sharedMesh.bounds;
                else if (col is CapsuleCollider cc) lb = new Bounds(cc.center, new Vector3(cc.radius * 2f, cc.height, cc.radius * 2f));
                else if (col is SphereCollider sc) lb = new Bounds(sc.center, Vector3.one * sc.radius * 2f);
                else continue;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? lb.min.x : lb.max.x, (i & 2) == 0 ? lb.min.y : lb.max.y, (i & 4) == 0 ? lb.min.z : lb.max.z);
                    var lp = prefab.transform.InverseTransformPoint(col.transform.TransformPoint(corner));
                    if (b == null) b = new Bounds(lp, Vector3.zero); else { var bb = b.Value; bb.Encapsulate(lp); b = bb; }
                }
            }
            return b;
        }
    }
}
