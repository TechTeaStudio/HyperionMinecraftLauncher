#!/usr/bin/env bash
# Build a Linux AppImage for HyperionMinecraftLauncher.
#
# Steps:
#   1. Publish the App as a self-contained linux-x64 single-file binary.
#   2. Assemble an AppDir with AppRun shim, .desktop entry, 256x256 icon.
#   3. If appimagetool is on PATH, package the AppDir into a .AppImage.
#      Otherwise print a friendly hint - the AppDir is still left on disk
#      so callers can package it manually.
#
# Run from the repo root:
#   ./scripts/build-appimage.sh
#
# Requirements:
#   - .NET SDK 10 on PATH (`dotnet --version` should print 10.x.y).
#   - (Optional) appimagetool: https://github.com/AppImage/AppImageKit/releases
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

APP_NAME="HyperionMinecraftLauncher"
APP_BINARY_NAME="HyperionMinecraftLauncher"
# Single source of truth for the version: the repo-root VERSION file. Matches the same
# pattern used by scripts/build-macos-app.sh and scripts/build-macos-dmg.sh - bump VERSION
# once and every script + the .csproj <Version> pick it up.
APP_VERSION="$(tr -d '[:space:]' < "${REPO_ROOT}/VERSION")"
RUNTIME="linux-x64"

PUBLISH_DIR="${REPO_ROOT}/publish/${RUNTIME}"
APPDIR="${REPO_ROOT}/publish/${APP_NAME}.AppDir"
ICON_SRC="${REPO_ROOT}/icon.png"
OUT_DIR="${REPO_ROOT}/publish"
OUT_FILE="${OUT_DIR}/${APP_NAME}-${APP_VERSION}-${RUNTIME}.AppImage"

echo ">> Publishing ${APP_NAME} ${APP_VERSION} for ${RUNTIME}..."
dotnet publish \
    "${REPO_ROOT}/src/HyperionMinecraftLauncher.App" \
    -r "${RUNTIME}" \
    -c Release \
    --self-contained true \
    -p:PublishSingleFile=true \
    -o "${PUBLISH_DIR}"

if [[ ! -f "${PUBLISH_DIR}/${APP_BINARY_NAME}" ]]; then
    echo "!! Expected published binary at ${PUBLISH_DIR}/${APP_BINARY_NAME}, but it was not produced."
    echo "   Is the App AssemblyName still '${APP_BINARY_NAME}'?"
    exit 1
fi

echo ">> Laying out AppDir at ${APPDIR}..."
rm -rf "${APPDIR}"
mkdir -p "${APPDIR}/usr/bin"
mkdir -p "${APPDIR}/usr/share/applications"
mkdir -p "${APPDIR}/usr/share/icons/hicolor/256x256/apps"

# Copy the entire publish output - the single-file binary still ships
# alongside skin / icon resources and any AOT pre-jitted files.
cp -r "${PUBLISH_DIR}/." "${APPDIR}/usr/bin/"

# AppRun shim: makes the AppDir executable from anywhere via appimagetool.
cat > "${APPDIR}/AppRun" <<'APPRUN'
#!/usr/bin/env bash
HERE="$(dirname "$(readlink -f "${0}")")"
export PATH="${HERE}/usr/bin:${PATH}"
exec "${HERE}/usr/bin/HyperionMinecraftLauncher" "$@"
APPRUN
chmod +x "${APPDIR}/AppRun"

# .desktop entry: surfaced to the user's launcher and used by appimagetool to
# pick up the application metadata when packing.
DESKTOP_FILE="${APPDIR}/${APP_NAME}.desktop"
cat > "${DESKTOP_FILE}" <<DESKTOP
[Desktop Entry]
Type=Application
Name=HyperionMinecraftLauncher
GenericName=Minecraft Launcher
Comment=Modern Avalonia desktop launcher for Minecraft.
Exec=HyperionMinecraftLauncher %U
Icon=HyperionMinecraftLauncher
Terminal=false
Categories=Game;
StartupNotify=true
StartupWMClass=HyperionMinecraftLauncher
DESKTOP

# Mirror the .desktop into the standard share/applications path so XDG-aware
# tools can find it once the AppImage is mounted.
cp "${DESKTOP_FILE}" "${APPDIR}/usr/share/applications/${APP_NAME}.desktop"

# Icons: appimagetool wants the icon at the AppDir root by its basename, and
# Linux desktops want it under hicolor/256x256/apps.
if [[ ! -f "${ICON_SRC}" ]]; then
    echo "!! Could not find ${ICON_SRC} - falling back to a blank placeholder."
    # Produce a tiny 1x1 PNG so packing still succeeds.
    printf '\x89PNG\r\n\x1a\n\x00\x00\x00\rIHDR\x00\x00\x00\x01\x00\x00\x00\x01\x08\x06\x00\x00\x00\x1f\x15\xc4\x89\x00\x00\x00\rIDATx\xdacddbf\x00\x00\x00\x06\x00\x02\x06\x00\x00\x00\x00\x00IEND\xaeB`\x82' \
        > "${ICON_SRC}.placeholder.png"
    cp "${ICON_SRC}.placeholder.png" "${APPDIR}/${APP_NAME}.png"
    cp "${ICON_SRC}.placeholder.png" "${APPDIR}/usr/share/icons/hicolor/256x256/apps/${APP_NAME}.png"
    rm -f "${ICON_SRC}.placeholder.png"
else
    cp "${ICON_SRC}" "${APPDIR}/${APP_NAME}.png"
    cp "${ICON_SRC}" "${APPDIR}/usr/share/icons/hicolor/256x256/apps/${APP_NAME}.png"
fi

mkdir -p "${OUT_DIR}"

if command -v appimagetool >/dev/null 2>&1; then
    echo ">> Packing AppDir with appimagetool..."
    # `appimagetool` is itself shipped as an AppImage, which normally requires libfuse.so.2
    # at runtime. Modern Ubuntu (24.04+) and GitHub Actions ubuntu-latest images no longer
    # ship FUSE 2 by default - only FUSE 3 - so a naive `appimagetool ...` call dies with
    # `dlopen(): error loading libfuse.so.2`. APPIMAGE_EXTRACT_AND_RUN=1 tells the AppImage
    # runtime to self-extract into a tmp dir and exec from there, bypassing FUSE entirely.
    # Slightly slower (one-time extract per invocation) but robust across every Linux host.
    ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 appimagetool "${APPDIR}" "${OUT_FILE}"
    echo ">> Built ${OUT_FILE}"
else
    cat <<EOF
!!
!! appimagetool was not found on PATH - skipping the .AppImage pack step.
!!
!! The AppDir is ready at:
!!   ${APPDIR}
!!
!! Install appimagetool to finish the build, e.g.:
!!   wget -O \$HOME/.local/bin/appimagetool \\
!!     https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage
!!   chmod +x \$HOME/.local/bin/appimagetool
!! Then rerun this script.
!!
EOF
    exit 0
fi
