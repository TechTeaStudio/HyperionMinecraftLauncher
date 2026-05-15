"""Round 2 of MC icon assembly:
  * download the chest entity texture (it's not a simple item file in vanilla)
  * crop Steve's face out of the default-skin PNG to get a 16x16 player-head icon
  * upscale the player head to 32x32 with nearest-neighbour so the launcher chrome
    renders crisp pixels at the size we display
"""
import os
import sys
import urllib.request
from PIL import Image

DEST = os.path.join("src", "HyperionMinecraftLauncher.App", "Assets", "Icons", "MC")
os.makedirs(DEST, exist_ok=True)

# Chest: it lives at assets/minecraft/textures/entity/chest/normal.png in vanilla.
# We try a couple of mirrors with various Minecraft versions (1.20.4 changed the chest layout).
CHEST_URLS = [
    "https://raw.githubusercontent.com/InventivetalentDev/minecraft-assets/1.20.4/assets/minecraft/textures/entity/chest/normal.png",
    "https://raw.githubusercontent.com/InventivetalentDev/minecraft-assets/1.20.4/assets/minecraft/textures/entity/chest/normal_left.png",
    "https://raw.githubusercontent.com/InventivetalentDev/minecraft-assets/1.20.1/assets/minecraft/textures/entity/chest/normal.png",
    "https://mcasset.cloud/1.20.4/assets/minecraft/textures/entity/chest/normal.png",
    "https://raw.githubusercontent.com/InventivetalentDev/minecraft-assets/master/assets/minecraft/textures/entity/chest/normal.png",
]


def fetch(urls: list[str], dest: str) -> bool:
    for url in urls:
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "HyperionMinecraftLauncher"})
            with urllib.request.urlopen(req, timeout=15) as resp:
                if resp.status == 200:
                    data = resp.read()
                    if data.startswith(b"\x89PNG"):
                        with open(dest, "wb") as out:
                            out.write(data)
                        sys.stderr.write(f"OK chest <- {url}\n")
                        return True
        except Exception:
            continue
    return False


fetch(CHEST_URLS, os.path.join(DEST, "chest_entity.png"))

# Crop Steve's face from the 64x64 skin texture.
# Skin layout: head front-face occupies [8:16, 8:16] (x, y).
src = os.path.join(DEST, "steve.png")
if os.path.exists(src):
    with Image.open(src) as im:
        face = im.crop((8, 8, 16, 16))  # 8x8 native
        # Upscale to 32x32 with nearest-neighbour so we render pixel-crisp at our usual chip size.
        face_32 = face.resize((32, 32), Image.NEAREST)
        face_32.save(os.path.join(DEST, "steve_face.png"))
        sys.stderr.write("OK steve_face.png (cropped+upscaled to 32x32)\n")

        # Also produce a 16x16 hat-overlay-merged variant: stack the hat layer ([40:48, 8:16])
        # over the bare face so caps/hair show on the avatar.
        hat = im.crop((40, 8, 48, 16))
        merged = face.copy()
        merged.alpha_composite(hat) if face.mode == "RGBA" else None
        merged_32 = merged.resize((32, 32), Image.NEAREST)
        merged_32.save(os.path.join(DEST, "steve_face_with_hat.png"))
        sys.stderr.write("OK steve_face_with_hat.png\n")
else:
    sys.stderr.write("steve.png missing - cannot crop face\n")

# A grass-block "isometric" composite is too heavy to render in C# at startup;
# instead, build a simple two-square composite (top + side stacked) as a hint of the
# block shape. Useful as the Installations icon.
top = os.path.join(DEST, "grass_block_top.png")
side = os.path.join(DEST, "grass_block_side.png")
if os.path.exists(top) and os.path.exists(side):
    with Image.open(top) as t, Image.open(side) as s:
        # tint top texture green; in-game biome shader does this dynamically
        t = t.convert("RGBA")
        green = Image.new("RGBA", t.size, (107, 168, 64, 255))
        t = Image.blend(t, green, 0.6)
        composite = Image.new("RGBA", (16, 32), (0, 0, 0, 0))
        composite.paste(t, (0, 0))
        composite.paste(s.convert("RGBA"), (0, 16))
        composite_64 = composite.resize((32, 64), Image.NEAREST)
        composite_64.save(os.path.join(DEST, "grass_block_stack.png"))
        sys.stderr.write("OK grass_block_stack.png\n")
