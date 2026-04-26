#!/usr/bin/env bash
# 安装到 /opt/InfoCompareAssistant。须 root 运行：sudo ./install.sh
# 或由同目录的 install-gui.sh / 桌面项「一键安装」调用（pkexec）。
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")" && pwd)"
TARGET="/opt/InfoCompareAssistant"
BIN="${TARGET}/InfoCompareAssistant"

is_skipped_bootstrap_file() {
  case "$1" in
  install.sh|install-gui.sh|install-to-opt.sh|UOS-INSTALL-zh.txt|InfoCompareAssistant-Install.desktop)
    return 0
    ;;
  esac
  return 1
}

if [[ ${EUID:-0} -ne 0 ]]; then
  echo "需要管理员权限。请在终端执行: sudo $HERE/install.sh" >&2
  echo "或双击同目录的「一键安装」图标（会弹出系统授权）。" >&2
  exit 1
fi

if [[ ! -f "$HERE/InfoCompareAssistant" ]]; then
  echo "未找到 $HERE/InfoCompareAssistant。请在本安装包根目录下运行本脚本。" >&2
  exit 1
fi

rm -rf "$TARGET"
mkdir -p "$TARGET"
shopt -s nullglob dotglob
for item in "$HERE"/*; do
  name="$(basename "$item")"
  if is_skipped_bootstrap_file "$name"; then
    continue
  fi
  cp -a "$item" "$TARGET"/
done
shopt -u nullglob dotglob

chmod a+x "$BIN" 2>/dev/null || true

DESKTOP_SRC="$HERE/InfoCompareAssistant.desktop"
if [[ -f "$DESKTOP_SRC" ]]; then
  install -m 0644 "$DESKTOP_SRC" /usr/share/applications/InfoCompareAssistant.desktop
  if command -v update-desktop-database &>/dev/null; then
    update-desktop-database /usr/share/applications/ || true
  fi
else
  echo "提示: 未附带 InfoCompareAssistant.desktop，已跳过菜单项安装。" >&2
fi

echo ""
echo "安装完成：$TARGET"
echo "运行: $BIN 或从开始菜单打开「信息比对助手」"
echo ""
