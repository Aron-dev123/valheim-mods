"""
Wandelt die Planetos-Karte (Westeros/Essos/Sothoryos mit Ortsnamen, 2000 x 1666) in eine
Valheim-Biom-Karte um.

Eingabe:  RGB-Rohdaten der Karte (je Pixel 3 Bytes R G B, zeilenweise), unter Windows z. B. mit
          System.Drawing aus dem JPG erzeugt (siehe README).
Ausgabe:  ../../Breitengrade/Planetos.biome.gz  (Kopf: Breite, Höhe als uint16; danach 1 Byte je Pixel)
          ../../planetos-vorschau.png

Farben der Karte: Meer blau, Land weiß, Wald grün, Gebirge grau, Schrift/Linien dunkel.

Biome (Vorgaben):
  nördlich der Mauer            -> Deep North
  Streifen südlich der Mauer    -> Mountain
  der Norden bis zur Eng, Ibben -> Black Forest
  Westeros und Freie Städte     -> Meadows (Dorne -> Plains)
  Essos östlich davon           -> Plains (Dothrakisee, Rote Wüste, Ghiscar …), Wälder dort -> Meadows
  Sothoryos und Ulthos          -> Mistlands
  Sümpfe: die Eng, die Sorrows am Rhoyne, Essos Mitte
Unter der Mauer wird durchgehend Land gesetzt (keine Bucht, kein Fluss).

Aufruf: python -I umwandeln.py <karte.rgb>
"""
import gzip
import math
import struct
import sys
import zlib
from pathlib import Path

W, H = 2000, 1666
OCEAN, MEADOWS, SWAMP, MOUNTAIN, BLACKFOREST, PLAINS, ASHLANDS, DEEPNORTH, MISTLANDS = range(9)

WALL_V = 455            # die Mauer (Bildzeile)
MOUNTAIN_END = 480      # Mountain-Streifen südlich der Mauer
BLACKFOREST_END = 690   # der Norden bis zur Eng
ESSOS_PLAINS_U = 820    # östlich davon ist Essos Steppe/Wüste
JUNGLE_V = 1300         # Sothoryos

# Westeros-Ausschnitt (für die Mauer)
WESTEROS_U0, WESTEROS_U1 = 150, 515
CASTLE_BLACK_U = 398

SWAMPS = [
    (305, 690, 34, 9),     # die Eng
    (700, 955, 45, 24),    # die Sorrows am Rhoyne
    (1010, 905, 40, 16),   # Essos Mitte
]

COLORS = {
    OCEAN: (30, 60, 120), MEADOWS: (120, 190, 70), SWAMP: (95, 80, 55), MOUNTAIN: (215, 215, 225),
    BLACKFOREST: (35, 85, 40), PLAINS: (222, 196, 90), ASHLANDS: (150, 40, 25),
    DEEPNORTH: (170, 210, 245), MISTLANDS: (110, 95, 140),
}

UNKNOWN, WATER, LAND, FOREST = 255, 0, 1, 2


def classify(r, g, b):
    if max(r, g, b) < 90:
        return UNKNOWN                      # Schrift, Linien
    if b - r > 25 and b >= g:
        return WATER
    if g - r > 20 and g > b:
        return FOREST
    return LAND                             # weiß, grau (Gebirge), Straßen


def swamp_at(x, y):
    for cx, cy, rx, ry in SWAMPS:
        ang = math.atan2(y - cy, x - cx)
        wobble = 1 + 0.25 * math.sin(ang * 5 + cx) + 0.15 * math.sin(ang * 9 + cy)
        if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 < wobble ** 2:
            return True
    return False


def main():
    rgb = Path(sys.argv[1]).read_bytes()
    assert len(rgb) == W * H * 3, f"falsche Bildgröße: {len(rgb)}"

    kind = bytearray(W * H)
    for i in range(W * H):
        kind[i] = classify(rgb[3 * i], rgb[3 * i + 1], rgb[3 * i + 2])
    # Legende unten links ist kein Land
    for y in range(1345, 1495):
        for x in range(100, 390):
            kind[y * W + x] = WATER

    # Schrift/Linien aus den Nachbarn auffüllen (Mehrheit), bis nichts Unbekanntes übrig ist
    for _ in range(8):
        unknown = [i for i in range(W * H) if kind[i] == UNKNOWN]
        if not unknown:
            break
        out = bytearray(kind)
        for i in unknown:
            x, y = i % W, i // W
            counts = {}
            for dy in (-2, -1, 0, 1, 2):
                yy = y + dy
                if yy < 0 or yy >= H:
                    continue
                for dx in (-2, -1, 0, 1, 2):
                    xx = x + dx
                    if 0 <= xx < W:
                        k = kind[yy * W + xx]
                        if k != UNKNOWN:
                            counts[k] = counts.get(k, 0) + 1
            if counts:
                out[i] = max(counts, key=counts.get)
        kind = out
    for i in range(W * H):
        if kind[i] == UNKNOWN:
            kind[i] = WATER

    # kleine Splitter entfernen (Inseln < 40 Pixel)
    seen = bytearray(W * H)
    for start in range(W * H):
        if kind[start] == WATER or seen[start]:
            continue
        stack, comp = [start], []
        seen[start] = 1
        while stack:
            i = stack.pop()
            comp.append(i)
            x, y = i % W, i // W
            for j in (i - 1 if x > 0 else -1, i + 1 if x < W - 1 else -1, i - W if y > 0 else -1, i + W if y < H - 1 else -1):
                if j >= 0 and not seen[j] and kind[j] != WATER:
                    seen[j] = 1
                    stack.append(j)
        if len(comp) < 40:
            for i in comp:
                kind[i] = WATER

    # Unter der Mauer durchgehend Land: von der Schwarzen Festung aus nach Westen und Osten bis zur
    # Küste des Festlands. Buchten/Flüsse bis 20 Pixel werden überbrückt, breitere Meeresrinnen nicht.
    # Die Bucht östlich der Mauer (mit der kleinen Insel dahinter) wird Land, damit die Mauer bis
    # zur Ostküste durchläuft und kein Wasser die Mauer unterbricht.
    ecx, ecy, erx, ery = 466, 462, 44, 28
    for y in range(ecy - ery - 6, ecy + ery + 7):
        for x in range(ecx - erx - 6, ecx + erx + 7):
            ang = math.atan2(y - ecy, x - ecx)
            wob = 1 + 0.12 * math.sin(ang * 5 + 1.3) + 0.07 * math.sin(ang * 11)
            if ((x - ecx) / erx) ** 2 + ((y - ecy) / ery) ** 2 < wob ** 2 and kind[y * W + x] == WATER:
                kind[y * W + x] = LAND

    row = [kind[WALL_V * W + u] != WATER for u in range(W)]

    def extend(start, step):
        u, last_land = start, start
        while WESTEROS_U0 <= u < WESTEROS_U1:
            if row[u]:
                last_land = u
            elif abs(u - last_land) > 20:
                break
            u += step
        return last_land

    if row[CASTLE_BLACK_U]:
        u0, u1 = extend(CASTLE_BLACK_U, -1), extend(CASTLE_BLACK_U, 1)
        for v in range(WALL_V - 7, WALL_V + 8):
            for u in range(u0, u1 + 1):
                if kind[v * W + u] == WATER:
                    kind[v * W + u] = LAND
        print(f"Mauer: Spalten {u0} bis {u1}")

    biome = bytearray(W * H)
    for y0 in range(H):
        for x in range(W):
            k = kind[y0 * W + x]
            # wellige Klimagrenzen (die Mauer selbst bleibt gerade)
            wob = 9 * math.sin(x * 0.021) + 5 * math.sin(x * 0.067 + 1.7) + 2.5 * math.sin(x * 0.19)
            y = y0 + wob
            xw = x + 14 * math.sin(y0 * 0.017) + 6 * math.sin(y0 * 0.061 + 0.8)
            if k == WATER:
                b = OCEAN
            elif y0 < WALL_V:
                b = DEEPNORTH
            elif y0 < MOUNTAIN_END:
                b = MOUNTAIN
            elif y < BLACKFOREST_END:
                b = BLACKFOREST
            elif y >= JUNGLE_V and not (x > 1580 and y < 1430):
                b = MISTLANDS
            elif xw >= ESSOS_PLAINS_U and k != FOREST:
                b = PLAINS
            elif x > 290 and x < 520 and y > 985:
                b = PLAINS  # Dorne ist Wüste
            else:
                b = MEADOWS
            if b in (MEADOWS, PLAINS) and swamp_at(x, y0):
                b = SWAMP
            biome[y0 * W + x] = b

    root = Path(__file__).resolve().parents[2]
    out = root / "Breitengrade" / "Planetos.biome.gz"
    with gzip.open(out, "wb", compresslevel=9) as f:
        f.write(struct.pack("<HH", W, H))
        f.write(bytes(biome))
    print("Biom-Karte:", out, out.stat().st_size, "Bytes")

    # Vorschau-PNG
    raw = bytearray()
    for y in range(H):
        raw.append(0)
        for x in range(W):
            raw.extend(COLORS[biome[y * W + x]])
    png = root / "planetos-vorschau.png"

    def chunk(t, data):
        c = struct.pack(">I", len(data)) + t + data
        return c + struct.pack(">I", zlib.crc32(t + data) & 0xFFFFFFFF)

    png.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", W, H, 8, 2, 0, 0, 0)) +
                    chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b""))
    print("Vorschau:", png)


if __name__ == "__main__":
    main()
