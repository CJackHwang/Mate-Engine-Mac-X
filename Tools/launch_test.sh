#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# 快捷启动 MateEngineX 并检查 macOS 舞蹈音频捕获的诊断日志
# 用法:  Tools/launch_test.sh
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$ROOT/Builds/macOS/MateEngineX.app"
LOG="$HOME/Library/Logs/Shinymoon/MateEngineX/Player.log"
APP_PROC="MateEngineX"

# 1. 停掉正在运行的实例
if pgrep -x "$APP_PROC" >/dev/null 2>&1; then
  echo ">> 停止已运行的 MateEngineX ..."
  pkill -x "$APP_PROC" 2>/dev/null || true
  sleep 2
fi

# 2. 清空日志，确保读到的都是本次启动的
rm -f "$LOG"

# 3. 启动
if [ ! -d "$APP" ]; then
  echo "!! 找不到 $APP（先运行 Tools/build_macos.sh 构建）" >&2
  exit 1
fi
echo ">> 启动 $APP ..."
open "$APP"

# 4. 等待启动并确认进程
sleep 12
if pgrep -x "$APP_PROC" >/dev/null 2>&1; then
  echo ">> 已运行 (PID $(pgrep -x "$APP_PROC" | head -1))"
else
  echo "!! 12 秒后仍未检测到进程" >&2
fi

# 5. 打印音频捕获诊断
echo ""
echo "── MacAudioMonitor 诊断 ──"
sleep 2
if [ -f "$LOG" ]; then
  grep -i "MacAudioMonitor" "$LOG" | tail -25
else
  echo "(尚无日志)"
fi

# 6. 提示
echo ""
if grep -qi "Screen Recording permission missing" "$LOG" 2>/dev/null; then
  echo "!! 屏幕录制权限缺失：请在弹出的系统提示中点【允许/Allow】后重跑本脚本"
  echo "   （或在 系统设置 → 隐私与安全 → 屏幕录制 里给 MateEngineX 开权限）"
elif grep -qi "SCStream system audio capture started" "$LOG" 2>/dev/null; then
  echo ">> 屏幕录制权限已生效，系统音频捕获已启动。放歌角色即随音乐跳舞，暂停即停。"
  echo "   诊断日志查看:  tail -f ~/Library/Logs/Shinymoon/MateEngineX/Player.log"
else
  echo ">> 已启动（未看到捕获日志，等几秒或检查上方输出）"
fi
