"""Copy the menu/chat banner textures from the user-supplied resource pack
into the Avalonia App's embedded Assets folder so they can be referenced via
`avares://HyperionMinecraftLauncher/Assets/Banners/<name>.png`.

We use the `small_simple_icons` variant because its PNGs are RGBA (vs the indexed-palette
`P` mode in the other variants), which renders correctly with Avalonia's bitmap pipeline
without a palette conversion step.
"""
import os
import shutil
import sys

SRC_VARIANT = "small_simple_icons"
SRC_ROOT = os.path.join("assets", "Icons v.1.13.3", "assets", SRC_VARIANT, "textures")
DEST_ROOT = os.path.join("src", "HyperionMinecraftLauncher.App", "Assets", "Banners")

os.makedirs(DEST_ROOT, exist_ok=True)

copied = 0
for sub in ("menu", "chat"):
    src_dir = os.path.join(SRC_ROOT, sub)
    if not os.path.isdir(src_dir):
        continue
    dest_dir = os.path.join(DEST_ROOT, sub)
    os.makedirs(dest_dir, exist_ok=True)
    for fname in sorted(os.listdir(src_dir)):
        if not fname.endswith(".png") or fname.endswith(".rpo"):
            continue
        shutil.copy2(os.path.join(src_dir, fname), os.path.join(dest_dir, fname))
        copied += 1

print(f"Copied {copied} banner textures into {DEST_ROOT}", file=sys.stderr)
