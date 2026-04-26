# 在「信息比对助手」解决方案根目录执行：生成 linux-x64 自包含 UOS 发行包
# 示例： cd "E:\...\信息比对助手"
#        .\packaging\uos\publish-linux.ps1
# 需要：.NET 8 SDK

$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$proj = Join-Path $root "InfoCompareAssistant/InfoCompareAssistant.csproj"
$out = Join-Path $root "packaging/uos/build"

if (-not (Test-Path $proj)) {
  Write-Error "找不到项目文件: $proj"
}

$rid = "linux-x64"
if ($args[0] -in @("arm64", "linux-arm64")) { $rid = "linux-arm64" }

if (Test-Path $out) {
  Remove-Item -Recurse -Force $out
}
New-Item -ItemType Directory -Path $out -Force | Out-Null

dotnet publish $proj -c Release -r $rid --self-contained true -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item -Force (Join-Path $PSScriptRoot "InfoCompareAssistant.desktop") $out
Copy-Item -Force (Join-Path $PSScriptRoot "install.sh") $out
Copy-Item -Force (Join-Path $PSScriptRoot "install-gui.sh") $out
Copy-Item -Force (Join-Path $PSScriptRoot "install-to-opt.sh") $out
Copy-Item -Force (Join-Path $PSScriptRoot "InfoCompareAssistant-Install.desktop") $out
Copy-Item -Force (Join-Path $PSScriptRoot "UOS-INSTALL-zh.txt") $out

# After publish, zip the whole "build" folder and copy to UOS; see UOS-INSTALL-zh.txt
Write-Host "Done: $out"
Write-Host "Zip the 'build' folder, copy to UOS, then read UOS-INSTALL-zh.txt"
