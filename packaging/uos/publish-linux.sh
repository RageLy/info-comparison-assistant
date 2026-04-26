#!/usr/bin/env bash
# 在仓库根目录执行: bash packaging/uos/publish-linux.sh
# 依赖: .NET 8 SDK
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$ROOT/packaging/uos/build"
RID="linux-x64"
[[ "${1:-}" == "arm64" || "${1:-}" == "linux-arm64" ]] && RID="linux-arm64"

rm -rf "$OUT"
mkdir -p "$OUT"

dotnet publish "$ROOT/InfoCompareAssistant/InfoCompareAssistant.csproj" -c Release -r "$RID" --self-contained true -o "$OUT"
HERE="$(cd "$(dirname "$0")" && pwd)"
cp -f "$HERE/InfoCompareAssistant.desktop" "$OUT/"
cp -f "$HERE/install.sh" "$OUT/"
cp -f "$HERE/install-gui.sh" "$OUT/"
cp -f "$HERE/install-to-opt.sh" "$OUT/"
cp -f "$HERE/InfoCompareAssistant-Install.desktop" "$OUT/"
cp -f "$HERE/UOS-INSTALL-zh.txt" "$OUT/"
echo "Done: $OUT"
echo "Zip the build folder, copy to UOS, then read UOS-INSTALL-zh.txt"
