#!/bin/bash
set -euo pipefail
source "$(cd "$(dirname "$0")" && pwd)/macos-dotnet.sh"
FA_ARCH="$(uname -m)"
case "$FA_ARCH" in arm64) FA_RID=osx-arm64 ;; x86_64) FA_RID=osx-x64 ;; *) echo 'Unsupported Mac architecture'; exit 1 ;; esac
FA_APP="$FA_ROOT/artifacts/macos/FruitsAtelier.app"
mkdir -p "$FA_APP/Contents/MacOS" "$FA_APP/Contents/Resources"
cp "$FA_ROOT/assets/branding/app-icon.icns" "$FA_APP/Contents/Resources/AppIcon.icns"
"$FA_DOTNET" publish "$FA_ROOT/src/FruitsAtelier.Mac" -c Release -r "$FA_RID" --self-contained true -o "$FA_APP/Contents/MacOS" -p:NuGetLockFilePath=packages.publish.lock.json
cat > "$FA_APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>FruitsAtelier.Mac</string>
<key>CFBundleIdentifier</key><string>io.github.frankhjwx.FruitsAtelier</string>
<key>CFBundleIconFile</key><string>AppIcon</string>
<key>CFBundleName</key><string>FruitsAtelier</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>0.1.0</string>
<key>CFBundleVersion</key><string>1</string>
<key>LSMinimumSystemVersion</key><string>12.0</string>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
mkdir -p "$FA_APP/Contents/Resources/zh-Hans.lproj" "$FA_APP/Contents/Resources/en.lproj"
cat > "$FA_APP/Contents/Resources/zh-Hans.lproj/InfoPlist.strings" <<'ZH'
"CFBundleDisplayName" = "水果工坊";
"CFBundleName" = "水果工坊";
ZH
cat > "$FA_APP/Contents/Resources/en.lproj/InfoPlist.strings" <<'EN'
"CFBundleDisplayName" = "FruitsAtelier";
"CFBundleName" = "FruitsAtelier";
EN
codesign --force --deep --sign - "$FA_APP"
echo "$FA_APP"
