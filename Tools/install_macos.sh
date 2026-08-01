#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="${SRC:-$ROOT/Builds/macOS/MateEngineX.app}"
DEST="${DEST:-/Applications/MateEngineX.app}"

if [ ! -d "$SRC" ]; then
  echo "[install_macos] Build not found: $SRC" >&2
  exit 1
fi

osascript -e 'tell application "MateEngineX" to quit' >/dev/null 2>&1 || true
rm -rf "$DEST"
ditto "$SRC" "$DEST"
codesign --verify --deep --strict "$DEST"

echo "[install_macos] Installed: $DEST"
