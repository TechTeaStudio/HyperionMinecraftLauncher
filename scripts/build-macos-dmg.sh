#!/usr/bin/env bash
# Wrap the previously-built macOS .app bundle into a drag-to-install .dmg.
#
# Steps:
#   1. Look up the produced bundles at publish/HyperionMinecraftLauncher-{arch}.app.
#      Run scripts/build-macos-app.sh first if they're missing.
#   2. Stage each bundle into a temporary folder with a symlink to /Applications -
#      that's the standard "drag the .app onto the Applications icon" affordance.
#   3. Call hdiutil create -srcfolder ... -ov -format UDZO to produce a compressed .dmg
#      at publish/HyperionMinecraftLauncher-{arch}.dmg.
#
# NOTE: hdiutil is part of macOS itself. On non-mac hosts this script exits early
#       with a friendly message - DMG packaging cannot run anywhere else.
#       The produced DMGs are unsigned (see build-macos-app.sh for the signing note).
#
# Run from anywhere:
#   ./scripts/build-macos-dmg.sh
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

APP_NAME="HyperionMinecraftLauncher"
APP_VERSION="$(tr -d '[:space:]' < "${REPO_ROOT}/VERSION")"
OUT_DIR="${REPO_ROOT}/publish"

if ! command -v hdiutil >/dev/null 2>&1; then
    echo "!! hdiutil is not on PATH. This script only works on macOS."
    echo "   .app bundles can still be produced by scripts/build-macos-app.sh on any host."
    exit 0
fi

# pack_dmg <arch-label>
pack_dmg() {
    local arch_label="$1"
    local app_dir="${OUT_DIR}/${APP_NAME}-${arch_label}.app"
    local dmg_out="${OUT_DIR}/${APP_NAME}-${APP_VERSION}-${arch_label}.dmg"

    if [[ ! -d "${app_dir}" ]]; then
        echo "!! ${app_dir} not found - run scripts/build-macos-app.sh first."
        return 1
    fi

    echo ">> Staging .dmg payload for ${arch_label}..."
    local stage_dir
    stage_dir="$(mktemp -d)"
    cp -R "${app_dir}" "${stage_dir}/"
    # The drag-to-install affordance: a symlink named "Applications" in the .dmg root.
    ln -s /Applications "${stage_dir}/Applications"

    rm -f "${dmg_out}"

    echo ">> Creating ${dmg_out}..."
    hdiutil create \
        -srcfolder "${stage_dir}" \
        -volname "${APP_NAME} ${APP_VERSION}" \
        -fs HFS+ \
        -format UDZO \
        -imagekey zlib-level=9 \
        -ov \
        "${dmg_out}"

    rm -rf "${stage_dir}"
    echo ">> Wrote ${dmg_out}"
}

mkdir -p "${OUT_DIR}"
pack_dmg "x86_64"
pack_dmg "arm64"

echo ""
echo ">> Done. DMGs at:"
echo "   ${OUT_DIR}/${APP_NAME}-${APP_VERSION}-x86_64.dmg"
echo "   ${OUT_DIR}/${APP_NAME}-${APP_VERSION}-arm64.dmg"
