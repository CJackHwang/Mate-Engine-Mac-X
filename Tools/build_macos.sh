#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [ -n "${UNITY_BIN:-}" ]; then
  :
elif [ -x "/Applications/Unity/Hub/Editor/6000.4.8f1/Unity.app/Contents/MacOS/Unity" ]; then
  UNITY_BIN="/Applications/Unity/Hub/Editor/6000.4.8f1/Unity.app/Contents/MacOS/Unity"
elif [ -x "/Applications/Unity/Unity-6000.4.8f1/Unity.app/Contents/MacOS/Unity" ]; then
  UNITY_BIN="/Applications/Unity/Unity-6000.4.8f1/Unity.app/Contents/MacOS/Unity"
else
  UNITY_BIN=""
fi
OUTPUT="${OUTPUT:-Builds/macOS/MateEngineX.app}"
LOG_DIR="$ROOT/Builds"
LOG_FILE="$LOG_DIR/macos-build.log"
SIGN_IDENTITY="${SIGN_IDENTITY:--}"
NOTARIZE="${NOTARIZE:-0}"
PACKAGE_DMG="${PACKAGE_DMG:-0}"

if [ -z "$UNITY_BIN" ] || [ ! -x "$UNITY_BIN" ]; then
  echo "Unity editor not found at: $UNITY_BIN" >&2
  echo "Install Unity 6000.4.8f1, or set UNITY_BIN to the Unity executable." >&2
  exit 1
fi

mkdir -p "$LOG_DIR"

echo "[build_macos] Unity: $UNITY_BIN"
echo "[build_macos] Project: $ROOT"
echo "[build_macos] Output: $OUTPUT"
echo "[build_macos] Log: $LOG_FILE"

"$ROOT/Tools/build_native_macos.sh"

"$UNITY_BIN" -batchmode -quit \
  -projectPath "$ROOT" \
  -executeMethod MacBuild.BuildFromCommandLine \
  -output "$OUTPUT" \
  -logFile "$LOG_FILE"

if [ -d "$ROOT/$OUTPUT" ] || [ -d "$OUTPUT" ]; then
  APP_BUNDLE="$OUTPUT"
  [ -d "$APP_BUNDLE" ] || APP_BUNDLE="$ROOT/$OUTPUT"

  echo "[build_macos] Signing $APP_BUNDLE with '$SIGN_IDENTITY'"
  find "$APP_BUNDLE/Contents" \( -name '*.bundle' -type d -o -name '*.dylib' -type f \) -print0 \
    | xargs -0 -n1 codesign --force --sign "$SIGN_IDENTITY"
  codesign --force --deep --sign "$SIGN_IDENTITY" "$APP_BUNDLE"
  codesign --verify --deep --strict "$APP_BUNDLE"

  echo "[build_macos] Checking universal architectures"
  for binary in \
    "$APP_BUNDLE/Contents/MacOS/MateEngineX" \
    "$APP_BUNDLE"/Contents/PlugIns/*.bundle/Contents/MacOS/*; do
    [ -e "$binary" ] || continue
    if ! lipo -info "$binary" | grep -q "x86_64" || ! lipo -info "$binary" | grep -q "arm64"; then
      echo "[build_macos] Not universal: $binary" >&2
      lipo -info "$binary" >&2 || true
      exit 1
    fi
  done
  echo "[build_macos] Universal architecture check OK"

  if [ "$PACKAGE_DMG" = "1" ]; then
    DMG="$LOG_DIR/MateEngineX.dmg"
    rm -f "$DMG"
    hdiutil create \
      -volname "MateEngineX" \
      -srcfolder "$APP_BUNDLE" \
      -ov \
      -format UDZO \
      "$DMG"
    echo "[build_macos] DMG: $DMG"
  fi

  if [ "$NOTARIZE" = "1" ]; then
    APPLE_ID_PASSWORD="${APPLE_ID_PASSWORD:-${APPLE_PASSWORD:-}}"
    if [ -n "${APPLE_ID:-}" ] && [ -n "${APPLE_TEAM_ID:-}" ] && [ -n "$APPLE_ID_PASSWORD" ]; then
      ZIP="$LOG_DIR/MateEngineX-notarize.zip"
      rm -f "$ZIP"
      ditto -c -k --keepParent "$APP_BUNDLE" "$ZIP"
      xcrun notarytool submit "$ZIP" \
        --apple-id "$APPLE_ID" \
        --team-id "$APPLE_TEAM_ID" \
        --password "$APPLE_ID_PASSWORD" \
        --wait
      xcrun stapler staple "$APP_BUNDLE"
      echo "[build_macos] Notarized and stapled: $APP_BUNDLE"
    else
      echo "[build_macos] NOTARIZE=1 but APPLE_ID/APPLE_TEAM_ID/APPLE_ID_PASSWORD not set; skipping" >&2
    fi
  fi

  echo "[build_macos] Done: $APP_BUNDLE"
else
  echo "[build_macos] Build completed but output was not found; check $LOG_FILE" >&2
  exit 1
fi
