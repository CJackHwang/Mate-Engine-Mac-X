#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

CLANG="${CLANG:-$(command -v clang || true)}"
if [ -z "$CLANG" ]; then
  echo "[build_native_macos] clang not found" >&2
  exit 1
fi

SDK="${SDKROOT:-$(xcrun --sdk macosx --show-sdk-path 2>/dev/null || true)}"
if [ -z "$SDK" ]; then
  echo "[build_native_macos] macOS SDK not found; set SDKROOT" >&2
  exit 1
fi

build_bundle() {
  local name="$1"
  local bundle_id="$2"
  local bundle_version="$3"
  local src="$4"
  shift 4

  local out="$ROOT/Assets/Plugins/MacOS/$name.bundle"
  local exec_block=""
  local short_version_block=""
  if [ "$name" = "MacAudioMonitor" ]; then
    exec_block=$'    <key>CFBundleExecutable</key>\n    <string>MacAudioMonitor</string>\n'
    short_version_block=$'    <key>CFBundleShortVersionString</key>\n    <string>1.0</string>\n'
  fi
  mkdir -p "$out/Contents/MacOS"

  cat > "$out/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
$exec_block    <key>CFBundleIdentifier</key>
    <string>$bundle_id</string>
    <key>CFBundleName</key>
    <string>$name</string>
    <key>CFBundlePackageType</key>
    <string>BNDL</string>
$short_version_block    <key>CFBundleVersion</key>
    <string>$bundle_version</string>
</dict>
</plist>
PLIST

  for arch in arm64 x86_64; do
    "$CLANG" -arch "$arch" \
      -isysroot "$SDK" \
      -mmacosx-version-min=12.0 \
      -fobjc-arc \
      -O2 \
      -bundle \
      -undefined dynamic_lookup \
      "$@" \
      -o "$TMP/$name-$arch" \
      "$src"
  done

  lipo -create "$TMP/$name-arm64" "$TMP/$name-x86_64" \
    -output "$out/Contents/MacOS/$name"
  chmod +x "$out/Contents/MacOS/$name"

  echo "[build_native_macos] Built $out"
}

SRC_DIR="$ROOT/Assets/MATE ENGINE - System Tray/MacOSTray"

build_bundle MacSystem com.shinymoon.mateengine.macsystem 1.0 "$SRC_DIR/MacSystem.m" \
  -framework Cocoa \
  -framework CoreGraphics \
  -framework CoreVideo \
  -framework IOSurface \
  -framework ScreenCaptureKit \
  -framework ServiceManagement

build_bundle MacWindowList com.shinymoon.mateengine.macwindowlist 1.0 "$SRC_DIR/MacWindowList.m" \
  -framework Cocoa \
  -framework CoreGraphics

build_bundle MacAudioMonitor com.shinymoon.mateengine.macaudiomonitor 1 "$SRC_DIR/MacAudioMonitor.m" \
  -framework AVFoundation \
  -framework CoreAudio

build_bundle MacWindowFix com.shinymoon.mateengine.macwindowfix 1.0 "$SRC_DIR/MacWindowFix.m" \
  -framework Cocoa
