# Valheim Mods

Eigene Mods für Valheim, gebaut mit BepInEx und Harmony.

## Mods

### Breitengrade

Kein Ozean mehr: Die Welt ist ein großer Kontinent, und die Biome liegen wie Klimazonen
von Süden nach Norden:

Ashlands → Plains → Meadows → Swamp → Mistlands → Black Forest → Mountain → Deep North

![Biom-Vorschau](biom-vorschau.png)

- Gilt **nur** für Welten mit dem Namen aus der Config (Standard: `Breitengrade`).
  Alle anderen Welten bleiben unverändert.
- Grenzen, Welligkeit, Mindesthöhe und Berg-Anhebung lassen sich in
  `BepInEx\config\aron.valheim.breitengrade.cfg` einstellen.
- Im Multiplayer brauchen alle Spieler (und der Server) die Mod.
- Ohne Ozean gibt es keine Seeschlangen und Leviathane.

Vorschau der Karte ohne Spielstart (`tools/BiomVorschau`):

```
dotnet run -- ../../biom-vorschau.png [seed] [grenzen] [welligkeit]
```

## Voraussetzungen

- Valheim (Steam)
- [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) im Valheim-Ordner
- .NET SDK 8

## Bauen

Im Ordner einer Mod:

```
dotnet build
```

Die fertige DLL wird automatisch nach `Valheim\BepInEx\plugins` kopiert.
Liegt Valheim nicht im Standard-Steam-Ordner, eine `Directory.Build.user.props`
anlegen und dort `ValheimPath` setzen.

## Hinweis

Spiel-DLLs (z. B. `assembly_valheim.dll`) und Spielstände gehören nicht ins Repo.
Das Projekt verweist nur auf den lokalen Valheim-Ordner.
