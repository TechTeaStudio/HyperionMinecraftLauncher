"""Download the Minecraft Font (SIL OFL 1.1) from IdreesInc/Minecraft-Font and place it
in the Avalonia App's Assets/Fonts folder.

We pick this font (not Minecraftia) because IdreesInc/Minecraft-Font is licensed under
SIL OFL 1.1 and therefore safe to bundle in a redistributable launcher binary.
"""
import os
import sys
import urllib.request

# The repo ships .otf (not .ttf) since the FontForge cleanup. Avalonia handles .otf identically.
URL_REGULAR = "https://github.com/IdreesInc/Minecraft-Font/raw/main/Minecraft.otf"
URL_BOLD = "https://github.com/IdreesInc/Minecraft-Font/raw/main/Minecraft-Bold.otf"
DEST_DIR = os.path.join("src", "HyperionMinecraftLauncher.App", "Assets", "Fonts")
DEST_REGULAR = os.path.join(DEST_DIR, "Minecraft.otf")
DEST_BOLD = os.path.join(DEST_DIR, "Minecraft-Bold.otf")
LICENSE_URL = "https://raw.githubusercontent.com/IdreesInc/Minecraft-Font/main/LICENSE"
LICENSE_DEST = os.path.join(DEST_DIR, "Minecraft.LICENSE.txt")

os.makedirs(DEST_DIR, exist_ok=True)


def fetch(url, dest):
    req = urllib.request.Request(url, headers={"User-Agent": "HyperionMinecraftLauncher/0.x setup script"})
    with urllib.request.urlopen(req, timeout=30) as resp, open(dest, "wb") as out:
        out.write(resp.read())
    return os.path.getsize(dest)


for url, dest in (
    (URL_REGULAR, DEST_REGULAR),
    (URL_BOLD, DEST_BOLD),
    (LICENSE_URL, LICENSE_DEST),
):
    try:
        size = fetch(url, dest)
        print(f"OK  {dest}  ({size} bytes)", file=sys.stderr)
    except Exception as exc:
        print(f"ERR {url}: {exc}", file=sys.stderr)
        sys.exit(1)
