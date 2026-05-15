"""Download the Avalonia <-> MinecraftSkinRender.OpenGL glue code from Coloryr's
ColorMC repository (Apache-2.0) so we can drop it into our App and feed the
Coloryr skin renderer through Avalonia's OpenGlControlBase.

We preserve the original file contents verbatim - they ship under Apache-2.0,
and the LICENSE / NOTICE in the same destination directory record that.
"""
import os
import sys
import urllib.request

BASE = "https://raw.githubusercontent.com/Coloryr/ColorMC/main/src/ColorMC.Gui/UI/Controls/Skin/OpenGL"
DEST = os.path.join("src", "HyperionMinecraftLauncher.App", "Controls", "SkinRender")
os.makedirs(DEST, exist_ok=True)

FILES = ("SkinRender.cs", "AvaloniaApi.cs")
COLORMC_LICENSE = "https://raw.githubusercontent.com/Coloryr/ColorMC/main/LICENSE"


def fetch(url: str, dest: str) -> int:
    req = urllib.request.Request(url, headers={"User-Agent": "HyperionMinecraftLauncher"})
    with urllib.request.urlopen(req, timeout=30) as resp, open(dest, "wb") as out:
        out.write(resp.read())
    return os.path.getsize(dest)


for fname in FILES:
    try:
        size = fetch(f"{BASE}/{fname}", os.path.join(DEST, fname))
        sys.stderr.write(f"OK  {fname}  ({size} bytes)\n")
    except Exception as exc:
        sys.stderr.write(f"ERR {fname}: {exc}\n")
        sys.exit(1)

try:
    size = fetch(COLORMC_LICENSE, os.path.join(DEST, "ColorMC.LICENSE.txt"))
    sys.stderr.write(f"OK  ColorMC.LICENSE.txt  ({size} bytes)\n")
except Exception as exc:
    sys.stderr.write(f"ERR LICENSE: {exc}\n")
    sys.exit(1)
