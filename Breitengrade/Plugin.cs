using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace Breitengrade
{
    [BepInPlugin(Guid, ModName, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "aron.valheim.breitengrade";
        public const string ModName = "Breitengrade";
        public const string Version = "0.2.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<string> WorldName;
        internal static ConfigEntry<string> BandBounds;
        internal static ConfigEntry<float> Wobble;
        internal static ConfigEntry<float> LandMin;
        internal static ConfigEntry<float> MountainLift;
        internal static ConfigEntry<float> MaxStructureFactor;
        internal static ConfigEntry<float> SectorSize;

        /// <summary>Standard-Grenzen der Version 0.1.0, werden automatisch auf die neuen umgestellt.</summary>
        const string OldDefaultBounds = "-7500,-3000,1500,3000,4500,6000,7800";

        void Awake()
        {
            Log = Logger;

            WorldName = Config.Bind("Allgemein", "WeltName", "Breitengrade",
                "Die Mod gilt NUR für Welten mit genau diesem Namen. Alle anderen Welten bleiben normal.");
            BandBounds = Config.Bind("Biome", "Grenzen", Latitude.DefaultBounds,
                "7 Grenzen in Metern von Süden nach Norden zwischen: Ashlands | Plains | Meadows | Swamp | " +
                "Mistlands | Black Forest | Mountain | Deep North. Die Welt reicht von -10000 bis 10000. " +
                "Meadows sollte die 0 enthalten, dort ist der Startpunkt.");
            Wobble = Config.Bind("Biome", "Welligkeit", 400f,
                "Wie viele Meter die Biom-Grenzen nach Norden/Süden schwanken. 0 = schnurgerade.");
            LandMin = Config.Bind("Gelände", "Mindesthoehe", 0.2f,
                "Niedrigste Grundhöhe des Landes (Meeresspiegel liegt bei 0.15). Höher = weniger Wasserstellen.");
            MountainLift = Config.Bind("Gelände", "BergAnhebung", 0.25f,
                "Wie stark das Mountain-Band zusätzlich angehoben wird, damit es echte Berge gibt.");
            MaxStructureFactor = Config.Bind("Strukturen", "MaxFaktor", 6f,
                "Strukturen werden an die Biom-Fläche angepasst (größeres Biom = mehr Strukturen). " +
                "Höchstens um diesen Faktor mehr als in normalem Valheim.");
            SectorSize = Config.Bind("Strukturen", "TeilgebietGroesse", 1500f,
                "Größe (Meter) der Teilgebiete, in die die Bänder für Biom-Varianten zerschnitten werden.");

            if (BandBounds.Value == OldDefaultBounds)
            {
                BandBounds.Value = Latitude.DefaultBounds;
                Log.LogInfo("Biom-Grenzen auf die neuen Standardwerte umgestellt.");
            }

            new Harmony(Guid).PatchAll();
            Log.LogInfo($"{ModName} {Version} geladen. Aktiv nur für die Welt \"{WorldName.Value}\".");
        }
    }
}
