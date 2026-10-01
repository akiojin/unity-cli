#!/usr/bin/env python3
"""Measure the Game-view capture: unique colours, tile bounding boxes, block size, per-tile pattern match.
Pure stdlib PNG decode (8-bit RGB/RGBA, non-interlaced)."""
import sys, zlib, struct, json
from collections import Counter

def load(path):
    d = open(path, "rb").read(); assert d[:8] == b"\x89PNG\r\n\x1a\n"
    i = 8; idat = b""
    while i < len(d):
        n, tag = struct.unpack(">I4s", d[i:i+8]); body = d[i+8:i+8+n]; i += 12 + n
        if tag == b"IHDR": w, h, bd, ct, _, _, il = struct.unpack(">IIBBBBB", body)
        elif tag == b"IDAT": idat += body
    assert bd == 8 and il == 0 and ct in (2, 6)
    bpp = 3 if ct == 2 else 4; raw = zlib.decompress(idat); stride = w * bpp
    rows = []; prev = bytearray(stride); p = 0
    for _ in range(h):
        f = raw[p]; line = bytearray(raw[p+1:p+1+stride]); p += 1 + stride
        for x in range(stride):
            a = line[x-bpp] if x >= bpp else 0; b = prev[x]; c = prev[x-bpp] if x >= bpp else 0
            if f == 1: line[x] = (line[x] + a) & 255
            elif f == 2: line[x] = (line[x] + b) & 255
            elif f == 3: line[x] = (line[x] + ((a + b) >> 1)) & 255
            elif f == 4:
                pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2 * c)
                line[x] = (line[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        rows.append([tuple(line[x*bpp:x*bpp+3]) for x in range(w)]); prev = line
    return w, h, rows

w, h, px = load(sys.argv[1])
counts = Counter(c for row in px for c in row)
bg = counts.most_common(1)[0][0]
A, B, C, D = (90,197,79), (30,111,80), (232,160,64), (110,60,30)
expected = {"background": (24,28,44), "grass_light": A, "grass_dark": B, "edge_orange": C, "edge_brown": D}
non_bg = [(x, y) for y in range(h) for x in range(w) if px[y][x] != bg]
xs = [p[0] for p in non_bg]; ys = [p[1] for p in non_bg]
bbox = (min(xs), min(ys), max(xs) + 1, max(ys) + 1)

def grass(x, y):
    if x in (0, 15) or y in (0, 15): return B
    if 7 <= x <= 8 and 7 <= y <= 8: return B
    if (x, y) in ((3,3),(12,3),(3,12),(12,12)): return B
    return A
def edge(x, y): return D if (x // 2) % 2 == 0 else C

SCALE = 4; TILE = 16 * SCALE
# world cell (cx, cy) -> screen: camera centre (1.5,1.5) at (640,360); 1 unit = 64 px; screen y is down.
def tile_origin(cx, cy): return (640 + int((cx - 1.5) * TILE), 360 - int((cy + 1 - 1.5) * TILE))
def check(cx, cy, fn):
    ox, oy = tile_origin(cx, cy); bad = 0
    for sy in range(TILE):
        for sx in range(TILE):
            tx = sx // SCALE; ty = 15 - (sy // SCALE)   # PNG fixture row 0 is the top row; Unity sprite y is up
            want = fn(tx, 15 - ty)                        # fixture y index counted from the top
            if px[oy + sy][ox + sx] != want: bad += 1
    return {"cell": [cx, cy], "screenOrigin": [ox, oy], "sizePx": TILE, "mismatchedPixels": bad, "exact": bad == 0}

tiles = [check(0,0,grass), check(1,0,grass), check(2,0,grass), check(0,2,edge), check(1,2,grass)]
tile_area = set()
for t in tiles:
    ox, oy = t["screenOrigin"]
    tile_area |= {(x, y) for y in range(oy, oy+TILE) for x in range(ox, ox+TILE)}
stray = [p for p in non_bg if p not in tile_area]
gaps = [p for p in tile_area if px[p[1]][p[0]] == bg]
result = {
    "image": sys.argv[1], "width": w, "height": h,
    "uniqueColours": {str(k): v for k, v in counts.most_common()},
    "uniqueColourCount": len(counts),
    "onlyExpectedColours": set(counts) <= set(expected.values()),
    "expectedColours": {k: list(v) for k, v in expected.items()},
    "nonBackgroundBBox": bbox, "expectedBBox": [544, 264, 736, 456],
    "pixelScale": SCALE, "tiles": tiles,
    "nonBackgroundPixelsOutsideTiles": len(stray), "backgroundPixelsInsideTiles(seams)": len(gaps),
    "allTilesPixelExact": all(t["exact"] for t in tiles),
}
print(json.dumps(result, indent=1))
