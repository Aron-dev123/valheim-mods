using System;
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
        public const string Version = "1.4.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<string> WorldName;
        internal static ConfigEntry<string> BandBounds;
        internal static ConfigEntry<float> Wobble;
        internal static ConfigEntry<float> LandMin;
        internal static ConfigEntry<float> MountainLift;
        internal static ConfigEntry<float> MaxStructureFactor;
        internal static ConfigEntry<float> SectorSize;
        internal static ConfigEntry<bool> TestPortalsEnabled, TravelPortalsEnabled, PortalPins, FlattenSpawn, MageHouse;
        internal static ConfigEntry<float> MageHeight;
        internal static ConfigEntry<bool> PerfLog;
        internal static ConfigEntry<string> PlanetosName;
        internal static ConfigEntry<float> WallTop;
        internal static ConfigEntry<bool> SeaEnabled;
        internal static ConfigEntry<float> SeaX, SeaZ, SeaRadius, IslandRadius;
        internal static ConfigEntry<float> SouthHeightCap, NorthHeightCap, AshlandsMainIsland, WorldScaleFactor;

        /// <summary>Standard-Grenzen älterer Versionen, werden automatisch auf die neuen umgestellt.</summary>
        static readonly string[] OldDefaultBounds =
        {
            "-7500,-3000,1500,3000,4500,6000,7800",
            "-6650,-3200,1000,1900,4900,7000,8150",
        };

        void Update()
        {
            TestPortals.Tick();
            PerfMonitor.Tick();
        }

        void Awake()
        {
            Log = Logger;

            WorldName = Config.Bind("Allgemein", "WeltName", "Breitengrade",
                "Die Mod gilt NUR für Welten mit genau diesem Namen. Alle anderen Welten bleiben normal.");
            PlanetosName = Config.Bind("Allgemein", "PlanetosWeltName", "Planetos",
                "Welten mit diesem Namen werden nach der Planetos-Karte (Westeros, Essos, Sothoryos) aufgebaut.");
            WallTop = Config.Bind("Planetos", "MauerHoehe", 260f,
                "Höhe der Mauer in Metern (Wasser liegt bei 30 m).");
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
            SouthHeightCap = Config.Bind("Gelände", "HoeheSueden", 0.36f,
                "Höchste Grundhöhe südlich des Black Forest (0.36 ≈ 72 m). Darüber färbt Valheim den Boden " +
                "verschneit. Normales Land liegt bei 0.15 bis 0.4.");
            NorthHeightCap = Config.Bind("Gelände", "HoeheNordrandBlackForest", 0.6f,
                "Höchste Grundhöhe am Nordrand des Black Forest. Der Black Forest steigt nach Norden " +
                "von HoeheSueden bis zu diesem Wert an, sodass dort Schneekuppen entstehen.");
            AshlandsMainIsland = Config.Bind("Gelände", "AshlandsHauptinsel", 1f,
                "Wie sehr die Ashlands eine große Hauptinsel sind (viel Lava, großes Land). " +
                "0 = wie Valheim es an dieser Stelle berechnen würde (eher kleine Nebeninseln), 1 = überall Hauptinsel.");
            WorldScaleFactor = Config.Bind("Allgemein", "Weltgroesse", 1.5f,
                "Faktor für den Weltradius (1 = normal 10 km, 1.5 = 15 km). Höchstens 1.55: Valheim verwaltet " +
                "Objekte nur bis 16 km vom Zentrum, darüber wird das Spiel immer langsamer. Nur für NEUE Welten ändern.");
            if (WorldScaleFactor.Value > WorldScale.MaxFactor)
            {
                WorldScaleFactor.Value = 1.5f;
                Log.LogInfo("Weltgröße auf 1.5 gesetzt (größere Welten lassen das Spiel einfrieren).");
            }
            MaxStructureFactor = Config.Bind("Strukturen", "MaxFaktorDichte", 12f,
                "Strukturen werden an die Biom-Fläche angepasst, damit die Dichte wie in normalem Valheim ist " +
                "(größeres Biom = mehr Strukturen). Höchstens um diesen Faktor mehr als in normalem Valheim.");
            TestPortalsEnabled = Config.Bind("Test", "TestPortale", true,
                "Beim Laden einer Breitengrade-Welt (einmalig) einen Ring aus Portalen in jedes Biom um den Startpunkt aufstellen.");
            TravelPortalsEnabled = Config.Bind("Test", "Reisepunkte", true,
                "Zusätzlich je Band vier Reisepunkt-Portale (Fernwest, West, Ost, Fernost) im äußeren Ring am Start.");
            PortalPins = Config.Bind("Test", "PortaleAufKarte", true,
                "Die Ziele der Portale auf der Karte markieren.");
            PerfLog = Config.Bind("Test", "LeistungsProtokoll", true,
                "Alle 15 Sekunden FPS, Objekte und Kreaturen ins Log schreiben (zum Finden von Lag).");
            MageHouse = Config.Bind("Test", "Magierhaus", true,
                "Magierhaus mit Portalturm auf einem Tafelberg in den Meadows. Am Start steht dann nur ein Portal dorthin.");
            MageHeight = Config.Bind("Test", "PodestHoehe", 170f,
                "Höhe des Podests mit dem Magierhaus in Metern (Wasser liegt bei 30 m). Oben kann es verschneit aussehen.");
            FlattenSpawn = Config.Bind("Test", "SpawnEinebnen", true,
                "Den Platz um den Startpunkt (100 m) flach machen. Nur für neue Welten.");
            SectorSize = Config.Bind("Strukturen", "TeilgebietGroesse", 1500f,
                "Größe (Meter) der Teilgebiete, in die die Bänder für Biom-Varianten zerschnitten werden.");
            SeaEnabled = Config.Bind("Meer", "Aktiv", true,
                "Großes rundes Meer mit Insel (Meadows im Norden, Plains im Süden) auf der Höhe der Meadows.");
            SeaX = Config.Bind("Meer", "MitteX", Latitude.SeaX, "Mitte des Meeres (Ost-West, Meter).");
            SeaZ = Config.Bind("Meer", "MitteZ", Latitude.SeaZ, "Mitte des Meeres (Nord-Süd, Meter).");
            SeaRadius = Config.Bind("Meer", "Radius", Latitude.SeaRadius,
                "Außenradius des Meeres in Metern (Standard: Wasser ca. 1/8 der Karte).");
            IslandRadius = Config.Bind("Meer", "InselRadius", Latitude.IslandRadius,
                "Radius der Insel in Metern (Standard: ca. 1/20 der Karte).");

            if (Array.IndexOf(OldDefaultBounds, BandBounds.Value) >= 0)
            {
                BandBounds.Value = Latitude.DefaultBounds;
                Log.LogInfo("Biom-Grenzen auf die neuen Standardwerte umgestellt.");
            }

            new Harmony(Guid).PatchAll();
            Log.LogInfo($"{ModName} {Version} geladen. Aktiv nur für die Welt \"{WorldName.Value}\".");
        }
    }
}
