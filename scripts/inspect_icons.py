"""One-off helper: enumerate every PNG under the icon resource pack, grouped by pixel size,
so we can pick which textures are usable as launcher UI icons."""
import os
import sys
from collections import defaultdict
from PIL import Image

ICONS_ROOT = os.path.join("assets", "Icons v.1.13.3", "assets")

by_size = defaultdict(list)
for variant in ("icons", "simple_icons", "small_icons", "small_simple_icons", "barebones_icons"):
    base = os.path.join(ICONS_ROOT, variant, "textures")
    if not os.path.isdir(base):
        continue
    for root, _dirs, files in os.walk(base):
        for fname in files:
            if not fname.endswith(".png") or fname.endswith(".rpo"):
                continue
            p = os.path.join(root, fname)
            try:
                with Image.open(p) as im:
                    rel = os.path.relpath(p, ICONS_ROOT).replace(os.sep, "/")
                    by_size[(im.size, variant)].append(rel)
            except Exception as exc:
                sys.stderr.write(f"err {p}: {exc}\n")

for (size, variant), files in sorted(by_size.items()):
    print(f"{variant} {size}: {len(files)} files; e.g. {files[:3]}")
