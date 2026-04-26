#!/usr/bin/env bash
# 图形化一键安装入口：双击打「一键安装」时优先用 pkexec，否则打开终端用 sudo
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
INSTALL_SH="$HERE/install.sh"

if [[ ! -f "$INSTALL_SH" ]]; then
  echo "缺少 install.sh: $INSTALL_SH" >&2
  exit 1
fi
chmod +x "$INSTALL_SH" 2>/dev/null || true

if command -v pkexec &>/dev/null; then
  exec pkexec /bin/bash "$INSTALL_SH"
  exit
fi

if [[ -n "${DISPLAY:-}" ]]; then
  RUNCMD="cd $(printf %q "$HERE") && echo '将请求管理员密码…' && sudo -E $(printf %q "$INSTALL_SH") && read -rp '按回车关闭' _"
  for t in x-terminal-emulator deepin-terminal qterminal konsole gnome-terminal xfce4-terminal mate-terminal; do
    if command -v "$t" &>/dev/null; then
      exec "$t" -e bash -c "$RUNCMD" || true
    fi
  done
fi

echo "未找到 pkexec 或常见终端。请手动在终端中执行:" >&2
echo "  cd $(printf %q "$HERE") && sudo ./install.sh" >&2
exit 1
