"""Download official Minecraft GUI / block / item textures to use as launcher iconography.

We try multiple mirrors per texture (>=5 candidate URLs) and keep the first 200-response.
The textures live under permissive mirrors of Mojang's own published assets, which is the
same source the in-game client uses.

Sources tried, in order:
  1. raw.githubusercontent.com/InventivetalentDev/minecraft-assets, branches `1.20.4`, `master`, `1.21`, `1.21.4`
  2. raw.githubusercontent.com/PrismarineJS/minecraft-jar-extractor (mirror)
  3. mcasset.cloud (a third-party CDN that resolves Mojang asset hashes to filenames)
  4. raw.githubusercontent.com/Mojang/bedrock-samples (Bedrock has different file names but
     fallback for a few names)
  5. raw.githubusercontent.com/destruc7i0n/scicraft-bot/master/resources (community archive)
"""
import os
import sys
import urllib.request
import urllib.error

DEST = os.path.join("src", "HyperionMinecraftLauncher.App", "Assets", "Icons", "MC")
os.makedirs(DEST, exist_ok=True)

INVENTIVE_BRANCHES = ("1.20.4", "1.21", "1.21.4", "master")
MCASSET_VERSIONS = ("1.20.4", "1.21", "1.21.4", "1.20.1")


def candidate_urls(category: str, name: str) -> list[str]:
    """Return a ranked list of mirrors for `assets/minecraft/textures/<category>/<name>.png`."""
    urls: list[str] = []
    # Inventivetalent mirror across several version branches
    for br in INVENTIVE_BRANCHES:
        urls.append(
            f"https://raw.githubusercontent.com/InventivetalentDev/minecraft-assets/"
            f"{br}/assets/minecraft/textures/{category}/{name}.png"
        )
    # mcasset.cloud (resolves by version path)
    for ver in MCASSET_VERSIONS:
        urls.append(
            f"https://mcasset.cloud/{ver}/assets/minecraft/textures/{category}/{name}.png"
        )
    # PrismarineJS minecraft-data archive of textures (varies by version)
    urls.append(
        f"https://raw.githubusercontent.com/PrismarineJS/minecraft-data/master/"
        f"data/pc/1.20.4/textures/{category}/{name}.png"
    )
    # Mojang's bedrock-samples - file names sometimes differ but worth trying as last resort
    urls.append(
        f"https://raw.githubusercontent.com/Mojang/bedrock-samples/main/"
        f"resource_pack/textures/blocks/{name}.png"
    )
    urls.append(
        f"https://raw.githubusercontent.com/Mojang/bedrock-samples/main/"
        f"resource_pack/textures/items/{name}.png"
    )
    return urls


def fetch_first_ok(urls: list[str], dest: str) -> str | None:
    for url in urls:
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "HyperionMinecraftLauncher icon fetcher"})
            with urllib.request.urlopen(req, timeout=15) as resp:
                if resp.status != 200:
                    continue
                data = resp.read()
                if len(data) < 50:
                    # tiny response is almost certainly an HTML 404 page, not a PNG
                    continue
                # Quick PNG-magic sanity check
                if not data.startswith(b"\x89PNG"):
                    continue
                with open(dest, "wb") as out:
                    out.write(data)
                return url
        except urllib.error.URLError:
            continue
        except urllib.error.HTTPError:
            continue
        except Exception:
            continue
    return None


# (category, filename-without-ext, alias-to-save-as)
WANTED = [
    ("block", "grass_block_side", "grass_block_side"),
    ("block", "grass_block_top", "grass_block_top"),
    ("block", "dirt", "dirt"),
    ("block", "cobblestone", "cobblestone"),
    ("block", "oak_planks", "oak_planks"),
    ("block", "stone", "stone"),
    ("item", "chest", "chest"),
    ("item", "ender_pearl", "ender_pearl"),
    ("item", "writable_book", "writable_book"),
    ("item", "book", "book"),
    ("item", "compass_00", "compass"),
    ("item", "clock_00", "clock"),
    ("item", "comparator", "comparator"),
    ("item", "redstone", "redstone"),
    ("item", "diamond_pickaxe", "diamond_pickaxe"),
    ("item", "iron_pickaxe", "iron_pickaxe"),
    ("item", "leather_helmet", "leather_helmet"),
    ("item", "player_head", "player_head"),
    ("entity/player/wide", "steve", "steve"),
    ("gui", "icons", "gui_icons"),
]


def main() -> int:
    successes = []
    failures = []
    for category, name, alias in WANTED:
        dest = os.path.join(DEST, f"{alias}.png")
        urls = candidate_urls(category, name)
        winner = fetch_first_ok(urls, dest)
        if winner:
            successes.append((alias, winner))
        else:
            failures.append((category, name))

    for alias, url in successes:
        sys.stderr.write(f"OK   {alias:24s}  <- {url}\n")
    for cat, name in failures:
        sys.stderr.write(f"FAIL {cat}/{name} (no mirror returned a PNG)\n")

    sys.stderr.write(f"\n{len(successes)}/{len(WANTED)} textures saved.\n")
    return 0 if successes else 1


if __name__ == "__main__":
    raise SystemExit(main())
