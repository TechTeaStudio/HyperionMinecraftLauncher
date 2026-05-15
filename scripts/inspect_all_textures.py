"""Survey every PNG in the icon resource pack: group by category and size,
so we can pick which textures are useful for launcher UI."""
import os
import sys
from collections import defaultdict
from PIL import Image

ICONS_ROOT = os.path.join("assets", "Icons v.1.13.3", "assets")

by_category = defaultdict(list)
for root, _dirs, files in os.walk(ICONS_ROOT):
    for fname in files:
        if not fname.endswith(".png") or fname.endswith(".rpo"):
            continue
        p = os.path.join(root, fname)
        rel = os.path.relpath(p, ICONS_ROOT).replace(os.sep, "/")
        parts = rel.split("/")
        if len(parts) < 3:
            continue
        variant, _textures, *rest = parts
        category = rest[0] if rest else "misc"
        try:
            with Image.open(p) as im:
                by_category[(variant, category)].append((rel, im.size, im.mode))
        except Exception as exc:
            sys.stderr.write(f"err {p}: {exc}\n")

# only print one variant (simple_icons) to keep noise down, since the four variants
# carry the same file names with cosmetic differences.
for (variant, category), entries in sorted(by_category.items()):
    if variant != "simple_icons":
        continue
    print(f"\n=== {variant}/{category} ({len(entries)} files) ===")
    for rel, size, mode in entries[:8]:
        print(f"  {rel}  {size}  {mode}")
    if len(entries) > 8:
        print(f"  ... and {len(entries) - 8} more")
