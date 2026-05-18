#!/usr/bin/env bash
# Build macOS .app bundles for HyperionMinecraftLauncher (Intel x86_64 + Apple Silicon arm64).
#
# Steps:
#   1. Publish the App as self-contained osx-x64   (Intel).
#   2. Publish the App as self-contained osx-arm64 (Apple Silicon).
#   3. Assemble an .app bundle for each arch at:
#        publish/HyperionMinecraftLauncher-{arch}.app/Contents/
#      with Info.plist, MacOS/HyperionMinecraftLauncher, Resources/AppIcon.icns,
#      Resources/Localization/ (resx satellite assemblies).
#   4. If productbuild is on PATH, produce a .pkg for each arch. Skip with a
#      friendly notice when it's missing (productbuild ships with Xcode CLT).
#
# NOTE: The bundles are unsigned and un-notarized. Apple Developer credentials
#       are needed to sign + notarize. Users will need to right-click -> Open
#       the first time, or clear the Gatekeeper quarantine with:
#           xattr -d com.apple.quarantine HyperionMinecraftLauncher.app
#       This script intentionally leaves signing for a later pass.
#
# Run from the repo root (or anywhere, paths are resolved relative to the script):
#   ./scripts/build-macos-app.sh
#
# Requirements:
#   - .NET SDK 10 on PATH (`dotnet --version` should print 10.x.y).
#   - macOS host for `sips` + `iconutil` (icon conversion). On non-mac hosts the
#     script falls back to copying icon.png and prints a notice.
#   - (Optional) productbuild for .pkg generation (part of Xcode Command Line Tools).
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

APP_NAME="HyperionMinecraftLauncher"
APP_BINARY_NAME="HyperionMinecraftLauncher"
APP_VERSION="$(tr -d '[:space:]' < "${REPO_ROOT}/VERSION")"
BUNDLE_ID="cc.techteastudio.HyperionMinecraftLauncher"

ICON_SRC="${REPO_ROOT}/icon.png"
OUT_DIR="${REPO_ROOT}/publish"

# build_app <runtime> <arch-label>
#   runtime    = "osx-x64" | "osx-arm64"
#   arch-label = "x86_64" | "arm64" (used in the bundle/pkg filename)
build_app() {
    local runtime="$1"
    local arch_label="$2"

    local publish_dir="${OUT_DIR}/${runtime}"
    local app_dir="${OUT_DIR}/${APP_NAME}-${arch_label}.app"
    local contents_dir="${app_dir}/Contents"
    local macos_dir="${contents_dir}/MacOS"
    local resources_dir="${contents_dir}/Resources"

    echo ">> Publishing ${APP_NAME} ${APP_VERSION} for ${runtime}..."
    dotnet publish \
        "${REPO_ROOT}/src/HyperionMinecraftLauncher.App" \
        -r "${runtime}" \
        -c Release \
        --self-contained \
        -p:PublishSingleFile=false \
        -o "${publish_dir}"

    if [[ ! -f "${publish_dir}/${APP_BINARY_NAME}" ]]; then
        echo "!! Expected published binary at ${publish_dir}/${APP_BINARY_NAME}, but it was not produced."
        echo "   Is the App AssemblyName still '${APP_BINARY_NAME}'?"
        exit 1
    fi

    echo ">> Assembling .app bundle at ${app_dir}..."
    rm -rf "${app_dir}"
    mkdir -p "${macos_dir}"
    mkdir -p "${resources_dir}"

    # Copy the full publish tree into MacOS/ so the binary keeps its sidecar
    # DLLs, the runtime, the native libs and any localized satellites.
    cp -R "${publish_dir}/." "${macos_dir}/"

    # The main executable must be marked +x.
    chmod +x "${macos_dir}/${APP_BINARY_NAME}"

    # Info.plist. Apple wants CFBundleVersion + CFBundleShortVersionString.
    cat > "${contents_dir}/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>en</string>
    <key>CFBundleDisplayName</key>
    <string>HyperionMinecraftLauncher</string>
    <key>CFBundleExecutable</key>
    <string>${APP_BINARY_NAME}</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon.icns</string>
    <key>CFBundleIdentifier</key>
    <string>${BUNDLE_ID}</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleName</key>
    <string>HyperionMinecraftLauncher</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>${APP_VERSION}</string>
    <key>CFBundleVersion</key>
    <string>${APP_VERSION}</string>
    <key>LSMinimumSystemVersion</key>
    <string>10.15</string>
    <key>LSApplicationCategoryType</key>
    <string>public.app-category.games</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSHumanReadableCopyright</key>
    <string>Copyright (c) Tech Tea Studio 2026. MIT licensed.</string>
</dict>
</plist>
PLIST

    # AppIcon.icns. On a real macOS host we can use sips + iconutil to produce
    # a proper multi-resolution .icns from icon.png. Off-mac (or if either tool
    # is missing) we just copy the PNG next to the bundle and rename it - macOS
    # will fall back to a generic icon but the bundle still launches.
    if [[ ! -f "${ICON_SRC}" ]]; then
        echo "!! Could not find ${ICON_SRC} - skipping icon assembly."
    elif command -v sips >/dev/null 2>&1 && command -v iconutil >/dev/null 2>&1; then
        echo ">> Converting icon.png -> AppIcon.icns via sips + iconutil..."
        local iconset_dir
        iconset_dir="$(mktemp -d)/AppIcon.iconset"
        mkdir -p "${iconset_dir}"

        # Standard 10-image .iconset layout. sips silently upscales when icon.png
        # is smaller than the requested size; that's fine for the launcher icon.
        sips -z 16   16   "${ICON_SRC}" --out "${iconset_dir}/icon_16x16.png"     >/dev/null
        sips -z 32   32   "${ICON_SRC}" --out "${iconset_dir}/icon_16x16@2x.png"  >/dev/null
        sips -z 32   32   "${ICON_SRC}" --out "${iconset_dir}/icon_32x32.png"     >/dev/null
        sips -z 64   64   "${ICON_SRC}" --out "${iconset_dir}/icon_32x32@2x.png"  >/dev/null
        sips -z 128  128  "${ICON_SRC}" --out "${iconset_dir}/icon_128x128.png"   >/dev/null
        sips -z 256  256  "${ICON_SRC}" --out "${iconset_dir}/icon_128x128@2x.png" >/dev/null
        sips -z 256  256  "${ICON_SRC}" --out "${iconset_dir}/icon_256x256.png"   >/dev/null
        sips -z 512  512  "${ICON_SRC}" --out "${iconset_dir}/icon_256x256@2x.png" >/dev/null
        sips -z 512  512  "${ICON_SRC}" --out "${iconset_dir}/icon_512x512.png"   >/dev/null
        sips -z 1024 1024 "${ICON_SRC}" --out "${iconset_dir}/icon_512x512@2x.png" >/dev/null

        if iconutil -c icns "${iconset_dir}" -o "${resources_dir}/AppIcon.icns"; then
            echo ">> AppIcon.icns generated."
        else
            echo "!! iconutil failed - falling back to copying icon.png as AppIcon.icns."
            cp "${ICON_SRC}" "${resources_dir}/AppIcon.icns"
        fi
        rm -rf "$(dirname "${iconset_dir}")"
    else
        echo "!! sips and/or iconutil not on PATH (likely a non-macOS host)."
        echo "   Copying icon.png to Resources/AppIcon.icns as a fallback."
        echo "   Run this script on a macOS host to get a real multi-resolution .icns."
        cp "${ICON_SRC}" "${resources_dir}/AppIcon.icns"
    fi

    # Resources/Localization/: the resx satellite assemblies live under MacOS/{culture}/
    # already (.NET publish output layout). Mirror them into Resources/Localization/
    # so anyone inspecting the bundle from Finder sees a familiar layout.
    mkdir -p "${resources_dir}/Localization"
    local copied_any=0
    for culture_dir in "${macos_dir}"/*/; do
        [[ -d "${culture_dir}" ]] || continue
        local culture
        culture="$(basename "${culture_dir}")"
        # Only treat dirs whose name looks like a culture code (2 letters, with or without -region).
        if [[ "${culture}" =~ ^[a-z]{2}(-[A-Za-z]+)?$ ]] && [[ -f "${culture_dir}/${APP_BINARY_NAME}.resources.dll" ]]; then
            mkdir -p "${resources_dir}/Localization/${culture}"
            cp "${culture_dir}/${APP_BINARY_NAME}.resources.dll" "${resources_dir}/Localization/${culture}/"
            copied_any=1
        fi
    done
    if [[ "${copied_any}" -eq 0 ]]; then
        echo ">> No resx satellite assemblies found in publish output (this is OK)."
    fi

    echo ">> Bundle ready: ${app_dir}"

    # Optional: productbuild .pkg.
    if command -v productbuild >/dev/null 2>&1; then
        local pkg_out="${OUT_DIR}/${APP_NAME}-${runtime}.pkg"
        echo ">> Packaging .pkg with productbuild..."
        productbuild \
            --component "${app_dir}" /Applications \
            --identifier "${BUNDLE_ID}" \
            --version "${APP_VERSION}" \
            "${pkg_out}"
        echo ">> Wrote ${pkg_out}"
    else
        echo ">> productbuild not on PATH - skipping .pkg generation for ${runtime}."
        echo "   Install Xcode Command Line Tools (xcode-select --install) on macOS to enable it."
    fi
}

mkdir -p "${OUT_DIR}"

build_app "osx-x64"   "x86_64"
build_app "osx-arm64" "arm64"

echo ""
echo ">> Done."
echo "   Intel .app:        ${OUT_DIR}/${APP_NAME}-x86_64.app"
echo "   Apple Silicon .app:${OUT_DIR}/${APP_NAME}-arm64.app"
echo ""
echo "   Bundles are UNSIGNED. First-run Gatekeeper bypass:"
echo "     - Right-click the .app in Finder -> Open, then click Open in the dialog, OR"
echo "     - Clear quarantine: xattr -d com.apple.quarantine \"${APP_NAME}-x86_64.app\""
