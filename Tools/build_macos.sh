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

"$UNITY_BIN" -batchmode -quit \
  -projectPath "$ROOT" \
  -executeMethod MacBuild.BuildFromCommandLine \
  -output "$OUTPUT" \
  -logFile "$LOG_FILE"

if [ -d "$ROOT/$OUTPUT" ] || [ -d "$OUTPUT" ]; then
  echo "[build_macos] Done: $OUTPUT"
else
  echo "[build_macos] Build completed but output was not found; check $LOG_FILE" >&2
  exit 1
fi
