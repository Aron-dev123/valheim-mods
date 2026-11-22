"""
Wandelt die Planetos-Fan-Karte (als RGB-Rohdaten) in eine Valheim-Biom-Karte um.

Eingabe:  planetos.rgb  (1080 x 658, je Pixel 3 Bytes R G B, zeilenweise)
          Erzeugen unter Windows z. B. mit System.Drawing aus dem JPG (siehe README).
Ausgabe:  ../../Breitengrade/Planetos.biome.gz  (Kopf: Breite, Höhe als uint16; danach 1 Byte je Pixel)
          ../../planetos-vorschau.png

Regeln (vom Nutzer vorgegeben):
  Norden / Schnee  -> Deep North      Süden / Eis        -> Ashlands (Hauptinsel)
  Grasland         -> Meadows         nördliches Gras    -> Black Forest
  Wüste            -> Plains          Dschungel (Süden)  -> Mistlands
  Mountain         -> Streifen südlich der Mauer, zwischen Deep North und Black Forest
  Swamp            -> ein paar Flecken in der Mitte der Karte (Essos, die Eng in Westeros)

Aufruf: python -I umwandeln.py <planetos.rgb>
"""
import gzip
import math
import struct
import sys
import zlib
from pathlib import Path

W, H = 1080, 658
OCEAN, MEADOWS, SWAMP, MOUNTAIN, BLACKFOREST, PLAINS, ASHLANDS, DEEPNORTH, MISTLANDS = range(9)

# Breitengrade (Pixelzeilen) der Klimazonen
WALL_V = 192          # die Mauer
MOUNTAIN_END = 207    # südlich der Mauer: Mountain bis hier
BLACKFOREST_END = 245 # danach Black Forest bis hier (nur Grünland)
JUNGLE_START = 395    # Sothoryos: Grünland ab hier ist Dschungel
MID_V = H / 2

# Sumpf-Flecken: (Mitte x, Mitte y, Radius x, Radius y)
SWAMPS = [
    (306, 258, 15, 5),   # die Eng (Westeros)
    (468, 300, 26, 12),  # Essos Mitte
    (548, 292, 20, 10),
    (505, 332, 18, 8),
    (430, 318, 12, 6),
]

COLORS = {
    OCEAN: (30, 60, 120), MEADOWS: (120, 190, 70), SWAMP: (95, 80, 55), MOUNTAIN: (215, 215, 225),
    BLACKFOREST: (35, 85, 40), PLAINS: (222, 196, 90), ASHLANDS: (150, 40, 25),
    DEEPNORTH: (170, 210, 245), MISTLANDS: (110, 95, 140),
}


def classify_color(r, g, b, y):
    """0 Wasser, 1 Eis, 2 Wüste/Steppe, 3 Grünland. Eis nur in Polnähe (sonst ist Helles Wüste)."""
    light = (r + g + b) / 3
    sat = max(r, g, b) - min(r, g, b)
    if light > 185 and sat < 45 and (y < 175 or y > 480):
        return 1
    if light > 150:
        return 3 if y > 470 else 2  # heller Dunst am Südrand von Sothoryos ist Dschungel, sonst Wüste
    if light < 30 or (b >= g - 4 and r < g):
        return 0
    if r >= g + 4:
        return 2
    return 3


# Meerenge zwischen Westeros und Essos (auf dem Bild nur eine schmale, flache Rinne)
CHANNEL_X0, CHANNEL_X1, CHANNEL_Y0, CHANNEL_Y1 = 343, 363, 232, 342


def in_channel(x, y):
    if not (CHANNEL_Y0 <= y <= CHANNEL_Y1):
        return False
    wobble = 2 * math.sin(y * 0.11) + 1.2 * math.sin(y * 0.37 + 1)
    return CHANNEL_X0 + wobble <= x <= CHANNEL_X1 + wobble


def swamp_at(x, y):
    for cx, cy, rx, ry in SWAMPS:
        ang = math.atan2(y - cy, x - cx)
        wobble = 1 + 0.25 * math.sin(ang * 5 + cx) + 0.15 * math.sin(ang * 9 + cy)
        if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 < wobble ** 2:
            return True
    return False


def main():
    src = Path(sys.argv[1])
    rgb = src.read_bytes()
    assert len(rgb) == W * H * 3, "falsche Bildgröße"

    kind = bytearray(W * H)
    for i in range(W * H):
        kind[i] = classify_color(rgb[3 * i], rgb[3 * i + 1], rgb[3 * i + 2], i // W)

    # Störpixel glätten: Mehrheit im 3x3-Feld (zweimal)
    for _ in range(2):
        out = bytearray(kind)
        for y in range(1, H - 1):
            for x in range(1, W - 1):
                counts = [0, 0, 0, 0]
                for dy in (-1, 0, 1):
                    row = (y + dy) * W
                    for dx in (-1, 0, 1):
                        counts[kind[row + x + dx]] += 1
                best = max(range(4), key=lambda k: counts[k])
                if counts[best] >= 5:
                    out[y * W + x] = best
        kind = out

    # Eisrand oben/unten (Dunst) ist kein Land
    for y in range(H):
        if y < 152 or y > 575:
            for x in range(W):
                if y >= 140 and y < 152 and 265 < x < 335:
                    continue  # Nordspitze von Westeros bleibt
                if kind[y * W + x] != 0:
                    kind[y * W + x] = 0

    # sehr kleine Landsplitter entfernen (< 60 Pixel)
    seen = bytearray(W * H)
    for start in range(W * H):
        if kind[start] == 0 or seen[start]:
            continue
        stack, comp = [start], []
        seen[start] = 1
        while stack:
            i = stack.pop()
            comp.append(i)
            x, y = i % W, i // W
            for j in (i - 1 if x > 0 else -1, i + 1 if x < W - 1 else -1, i - W if y > 0 else -1, i + W if y < H - 1 else -1):
                if j >= 0 and not seen[j] and kind[j] != 0:
                    seen[j] = 1
                    stack.append(j)
        if len(comp) < 60:
            for i in comp:
                kind[i] = 0

    biome = bytearray(W * H)
    for y0 in range(H):
        for x in range(W):
            k = kind[y0 * W + x]
            # wellige Klimagrenzen statt gerader Linien
            y = y0 + 5 * math.sin(x * 0.045) + 3 * math.sin(x * 0.13 + 1.7) + 1.5 * math.sin(x * 0.37)
            if k == 0:
                b = OCEAN
            elif k == 1:
                b = OCEAN  # Polareis wird Meer; die Polkappen setzt die Mod selbst (viel kleiner)
            elif y < WALL_V:
                b = DEEPNORTH
            elif y < MOUNTAIN_END:
                b = MOUNTAIN
            elif k == 2:
                b = PLAINS
            elif y < BLACKFOREST_END:
                b = BLACKFOREST
            elif y >= JUNGLE_START:
                b = MISTLANDS
            else:
                b = MEADOWS
            if b in (MEADOWS, PLAINS) and swamp_at(x, y0):
                b = SWAMP
            if in_channel(x, y0):
                b = OCEAN
            biome[y0 * W + x] = b

    root = Path(__file__).resolve().parents[2]
    out = root / "Breitengrade" / "Planetos.biome.gz"
    with gzip.open(out, "wb", compresslevel=9) as f:
        f.write(struct.pack("<HH", W, H))
        f.write(bytes(biome))
    print("Biom-Karte:", out, out.stat().st_size, "Bytes")

    counts = [0] * 9
    for v in biome:
        counts[v] += 1
    names = ["Ozean", "Meadows", "Swamp", "Mountain", "Black Forest", "Plains", "Ashlands", "Deep North", "Mistlands"]
    for n, c in zip(names, counts):
        print(f"  {n:13s} {100 * c / len(biome):5.1f} %")

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
