"""Render a properly isometric chest icon from the unwrapped chest-entity texture.

Mojang's chest texture is a 64x64 atlas of the *flat unwrapped cuboid* (top / sides /
front / back / bottom of the lid box and body box stacked). Showing it raw on the
sidebar looks like a confusing strip of pixels - what the user actually wants is the
chest as it would be rendered in-game: three visible faces (top + front + right side)
in 2:1 pixel-art isometric projection.

We do this with PIL's PERSPECTIVE transform: for each visible face we map the four
source corners (in the entity-texture image) to four destination corners on an
isometric grid, then alpha-composite back-to-front.

Output:  src/HyperionMinecraftLauncher.App/Assets/Icons/MC/chest_iso.png
"""
import os
import sys
import numpy as np
from PIL import Image

SRC_PATH = os.path.join("src", "HyperionMinecraftLauncher.App", "Assets", "Icons", "MC", "chest_entity.png")
DST_PATH = os.path.join("src", "HyperionMinecraftLauncher.App", "Assets", "Icons", "MC", "chest_iso.png")


def perspective_coeffs(src_pts, dst_pts):
    """Eight coefficients that drive Image.transform(PERSPECTIVE) so that the
    given source quad lands on the given destination quad."""
    matrix = []
    for (sx, sy), (dx, dy) in zip(src_pts, dst_pts):
        matrix.append([dx, dy, 1, 0,  0,  0, -sx * dx, -sx * dy])
        matrix.append([0,  0,  0, dx, dy, 1, -sy * dx, -sy * dy])
    A = np.array(matrix, dtype=np.float64)
    b = np.array([c for pt in src_pts for c in pt], dtype=np.float64)
    return tuple(np.linalg.solve(A, b))


def warp(image, src_quad, dst_quad, out_size):
    coeffs = perspective_coeffs(src_quad, dst_quad)
    return image.transform(out_size, Image.PERSPECTIVE, coeffs, Image.NEAREST)


def main() -> int:
    src = Image.open(SRC_PATH).convert("RGBA")

    # UV layout per minecraft.wiki/w/Chest.png-atlas (single chest, 1.14+).
    # Lid box (14 wide x 5 tall x 14 deep): unwrap rows 0..19.
    # Body box (14 wide x 10 tall x 14 deep): unwrap rows 19..43.
    lid_top    = src.crop((14, 0,  28, 14))   # 14x14 - top of the lid box
    lid_front  = src.crop((14, 14, 28, 19))   # 14x5  - front strip of lid
    lid_right  = src.crop((0,  14, 14, 19))   # 14x5  - right side of lid
    body_front = src.crop((14, 33, 28, 43))   # 14x10 - body front
    body_right = src.crop((0,  33, 14, 43))   # 14x10 - body right side

    # Stack lid + body to produce 14 x 15 full-height faces.
    full_front = Image.new("RGBA", (14, 15), (0, 0, 0, 0))
    full_front.paste(lid_front, (0, 0))
    full_front.paste(body_front, (0, 5))

    full_right = Image.new("RGBA", (14, 15), (0, 0, 0, 0))
    full_right.paste(lid_right, (0, 0))
    full_right.paste(body_right, (0, 5))

    # Scale up for clean nearest-neighbour edges in the iso projection.
    s = 12
    top_big   = lid_top.resize((14 * s, 14 * s), Image.NEAREST)
    front_big = full_front.resize((14 * s, 15 * s), Image.NEAREST)
    right_big = full_right.resize((14 * s, 15 * s), Image.NEAREST)

    # 2:1 pixel-art isometric grid. F = face size (width) in canvas pixels.
    F = 14 * s
    Fv = 15 * s             # body height in canvas pixels (lid 5 + body 10)
    W = 4 * F
    H = int(3 * F)

    out_size = (W, H)

    # Top face: rhombus with corners at (F, F/2) / (2F, 0) / (3F, F/2) / (2F, F).
    top_iso = warp(
        top_big,
        [(0, 0), (14 * s, 0), (14 * s, 14 * s), (0, 14 * s)],
        [(F, F // 2), (2 * F, 0), (3 * F, F // 2), (2 * F, F)],
        out_size,
    )

    # Front face: parallelogram receding down-right from the top-front edge of the rhombus.
    front_iso = warp(
        front_big,
        [(0, 0), (14 * s, 0), (14 * s, 15 * s), (0, 15 * s)],
        [(2 * F, F), (3 * F, F // 2), (3 * F, F // 2 + Fv), (2 * F, F + Fv)],
        out_size,
    )

    # Right side: parallelogram receding down-left from the left-front edge.
    right_iso = warp(
        right_big,
        [(0, 0), (14 * s, 0), (14 * s, 15 * s), (0, 15 * s)],
        [(F, F // 2), (2 * F, F), (2 * F, F + Fv), (F, F // 2 + Fv)],
        out_size,
    )

    # Composite back-to-front. Right side is farthest, then top, then front (nearest).
    canvas = Image.new("RGBA", out_size, (0, 0, 0, 0))
    canvas = Image.alpha_composite(canvas, right_iso)
    canvas = Image.alpha_composite(canvas, top_iso)
    canvas = Image.alpha_composite(canvas, front_iso)

    # Trim transparent padding and write.
    bbox = canvas.getbbox()
    if bbox is None:
        sys.stderr.write("ERR: composite output is fully transparent - check UV layout\n")
        return 1
    canvas.crop(bbox).save(DST_PATH)
    sys.stderr.write(f"OK  wrote {DST_PATH} ({canvas.crop(bbox).size})\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
