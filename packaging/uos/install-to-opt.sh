#!/usr/bin/env bash
# 兼容旧说明：与 install.sh 相同。请优先使用同目录的「一键安装」或 ./install.sh
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")" && pwd)"
exec /bin/bash "$HERE/install.sh"
