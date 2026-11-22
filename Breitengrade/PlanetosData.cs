using System.IO;

namespace Breitengrade
{
    /// <summary>Liest die eingebettete Planetos-Biomkarte aus der Mod-DLL.</summary>
    internal static class PlanetosData
    {
        public static byte[] Read()
        {
            using (var s = typeof(PlanetosData).Assembly.GetManifestResourceStream("Breitengrade.Planetos.biome.gz"))
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }
    }
}
