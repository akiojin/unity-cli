#!/usr/bin/env python3
"""Pixel-exact check: every painted cell must equal a 4x nearest-neighbour upscale of its source PNG."""
import json, struct, sys, zlib

def load(path):
    d = open(path, "rb").read(); i = 8; idat = b""; hdr = None
    while i < len(d):
        l, = struct.unpack(">I", d[i:i+4]); t = d[i+4:i+8]
        if t == b"IHDR": hdr = struct.unpack(">IIBBBBB", d[i+8:i+8+l])
        if t == b"IDAT": idat += d[i+8:i+8+l]
        i += 12 + l
    w, h, depth, ctype = hdr[:4]
    assert depth == 8 and ctype in (2, 6), hdr
    bpp = 3 if ctype == 2 else 4
    raw = zlib.decompress(idat); stride = w * bpp; rows = []; prev = bytearray(stride); p = 0
    for _ in range(h):
        f = raw[p]; cur = bytearray(raw[p+1:p+1+stride]); p += 1 + stride
        for x in range(stride):
            a = cur[x-bpp] if x >= bpp else 0; b = prev[x]; c = prev[x-bpp] if x >= bpp else 0
            if f == 1: cur[x] = (cur[x] + a) & 255
            elif f == 2: cur[x] = (cur[x] + b) & 255
            elif f == 3: cur[x] = (cur[x] + ((a + b) >> 1)) & 255
            elif f == 4:
                pa = abs(b - c); pb = abs(a - c); pc = abs(a + b - 2 * c)
                cur[x] = (cur[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        rows.append([tuple(cur[x*bpp:x*bpp+3]) for x in range(w)]); prev = cur
    return w, h, rows

shot, fix = sys.argv[1], sys.argv[2]
W, H, img = load(shot)
src = {n: load(f"{fix}/{n}.png")[2] for n in ("grass379", "edge379")}
scale = H // 180; ppu = 16; cam = (2.0, 1.5)
cells = {(0, 0): "grass379", (1, 0): "grass379", (2, 0): "grass379", (0, 2): "edge379", (1, 2): "grass379", (3, 2): "grass379"}
colors = {}
for row in img:
    for px in row: colors[px] = colors.get(px, 0) + 1
res = {"image": shot, "width": W, "height": H, "integerScale": scale, "distinctColors": len(colors),
       "colorHistogram": {str(k): v for k, v in sorted(colors.items(), key=lambda kv: -kv[1])}, "cells": []}
covered = 0
for (cx, cy), name in cells.items():
    x0 = round(W / 2 + (cx - cam[0]) * ppu * scale); ytop = round(H / 2 - (cy + 1 - cam[1]) * ppu * scale)
    bad = 0
    for sy in range(16):
        for sx in range(16):
            want = src[name][sy][sx]
            for dy in range(scale):
                for dx in range(scale):
                    if img[ytop + sy * scale + dy][x0 + sx * scale + dx] != want: bad += 1
    covered += (16 * scale) ** 2
    res["cells"].append({"cell": [cx, cy], "sprite": name, "screenRect": [x0, ytop, 16 * scale, 16 * scale], "mismatchedPixels": bad})
bg = max(colors.items(), key=lambda kv: kv[1])
res["background"] = {"color": list(bg[0]), "pixels": bg[1]}
res["nonBackgroundPixels"] = W * H - bg[1]
res["expectedTilePixels"] = covered
res["passed"] = all(c["mismatchedPixels"] == 0 for c in res["cells"]) and res["nonBackgroundPixels"] == covered and len(colors) == 5
print(json.dumps(res, indent=2))
