"""Download Monocraft (SIL OFL 1.1) from IdreesInc/Monocraft - same author as the pixel
Minecraft-Font we shipped in v0.2 but much more legible: it's a monospaced programming
font designed for code editors, still in the Minecraft pixel idiom. The pixel Minecraft
font is hard to read in the launcher chrome at 11-14 px.

We keep the original Minecraft.otf in tree as a fallback (referenced as `MinecraftPixelFont`
in App.axaml) so view code can still pick the chunky pixel face when it wants flavour.
"""
import os
import sys
import urllib.request


def fetch(url: str, dest: str) -> int:
    req = urllib.request.Request(url, headers={"User-Agent": "HyperionMinecraftLauncher/0.x"})
    with urllib.request.urlopen(req, timeout=30) as resp, open(dest, "wb") as out:
        out.write(resp.read())
    return os.path.getsize(dest)


DEST_DIR = os.path.join("src", "HyperionMinecraftLauncher.App", "Assets", "Fonts")
os.makedirs(DEST_DIR, exist_ok=True)

TARGETS = [
    (
        "https://raw.githubusercontent.com/IdreesInc/Monocraft/main/dist/Monocraft-ttf/Monocraft.ttf",
        os.path.join(DEST_DIR, "Monocraft.ttf"),
    ),
    (
        "https://raw.githubusercontent.com/IdreesInc/Monocraft/main/LICENSE",
        os.path.join(DEST_DIR, "Monocraft.LICENSE.txt"),
    ),
]

for url, dest in TARGETS:
    try:
        size = fetch(url, dest)
        print(f"OK  {dest}  ({size} bytes)", file=sys.stderr)
    except Exception as exc:
        print(f"ERR {url}: {exc}", file=sys.stderr)
        sys.exit(1)
