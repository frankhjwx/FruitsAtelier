#!/bin/bash
set -euo pipefail
source "$(cd "$(dirname "$0")" && pwd)/macos-dotnet.sh"
FA_ARCH="$(uname -m)"
case "$FA_ARCH" in arm64) FA_RID=osx-arm64 ;; x86_64) FA_RID=osx-x64 ;; *) echo 'Unsupported Mac architecture'; exit 1 ;; esac
FA_APP="$FA_ROOT/artifacts/macos/FruitsAtelier.app"
FA_VERSION="$("$FA_DOTNET" msbuild "$FA_ROOT/src/FruitsAtelier.Mac/FruitsAtelier.Mac.csproj" -getProperty:Version)"
mkdir -p "$FA_APP/Contents/MacOS" "$FA_APP/Contents/Resources"
cp "$FA_ROOT/assets/branding/app-icon.icns" "$FA_APP/Contents/Resources/AppIcon.icns"
cp "$FA_ROOT/assets/branding/faz-file.icns" "$FA_APP/Contents/Resources/FazFile.icns"
cp "$FA_ROOT/assets/branding/fad-file.icns" "$FA_APP/Contents/Resources/FadFile.icns"
cp "$FA_ROOT/assets/branding/fpt-file.icns" "$FA_APP/Contents/Resources/FptFile.icns"
"$FA_DOTNET" publish "$FA_ROOT/src/FruitsAtelier.Mac" -c Release -r "$FA_RID" --self-contained true -o "$FA_APP/Contents/MacOS" -p:NuGetLockFilePath=packages.publish.lock.json
python3 "$FA_ROOT/scripts/Build-MapsetVerifier.py" --runtime "$FA_RID"
mkdir -p "$FA_APP/Contents/MacOS/mapset-verifier"
cp -R "$FA_ROOT/artifacts/mapset-verifier/$FA_RID/." "$FA_APP/Contents/MacOS/mapset-verifier/"
# Dotted resource directories in MacOS are interpreted as nested code bundles by codesign.
for FA_RESOURCE in licenses source; do
    mkdir -p "$FA_APP/Contents/Resources/mapset-verifier/$FA_RESOURCE"
    cp -R "$FA_APP/Contents/MacOS/mapset-verifier/$FA_RESOURCE/." "$FA_APP/Contents/Resources/mapset-verifier/$FA_RESOURCE/"
    rm -rf "$FA_APP/Contents/MacOS/mapset-verifier/$FA_RESOURCE"
done
cat > "$FA_APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>FruitsAtelier.Mac</string>
<key>CFBundleIdentifier</key><string>io.github.frankhjwx.FruitsAtelier</string>
<key>CFBundleIconFile</key><string>AppIcon</string>
<key>CFBundleName</key><string>FruitsAtelier</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>$FA_VERSION</string>
<key>CFBundleVersion</key><string>$FA_VERSION</string>
<key>LSMinimumSystemVersion</key><string>12.0</string>
<key>NSHighResolutionCapable</key><true/>
<key>CFBundleURLTypes</key><array><dict>
<key>CFBundleURLName</key><string>osu! links</string>
<key>CFBundleURLSchemes</key><array><string>osu</string></array>
<key>CFBundleTypeRole</key><string>Viewer</string>
</dict></array>
<key>CFBundleDocumentTypes</key><array><dict>
<key>CFBundleTypeName</key><string>Fruits Atelier Project</string>
<key>CFBundleTypeRole</key><string>Editor</string>
<key>LSHandlerRank</key><string>Owner</string>
<key>CFBundleTypeIconFile</key><string>FazFile.icns</string>
<key>LSItemContentTypes</key><array><string>io.github.frankhjwx.fruitsatelier.faz</string></array>
</dict><dict>
<key>CFBundleTypeName</key><string>Fruits Atelier Difficulty</string>
<key>CFBundleTypeRole</key><string>Editor</string>
<key>LSHandlerRank</key><string>Owner</string>
<key>CFBundleTypeIconFile</key><string>FadFile.icns</string>
<key>LSItemContentTypes</key><array><string>io.github.frankhjwx.fruitsatelier.fad</string></array>
</dict><dict>
<key>CFBundleTypeName</key><string>Fruits Atelier Patterns</string>
<key>CFBundleTypeRole</key><string>None</string>
<key>CFBundleTypeIconFile</key><string>FptFile.icns</string>
<key>LSItemContentTypes</key><array><string>io.github.frankhjwx.fruitsatelier.fpt</string></array>
</dict></array>
<key>UTExportedTypeDeclarations</key><array><dict>
<key>UTTypeIdentifier</key><string>io.github.frankhjwx.fruitsatelier.faz</string>
<key>UTTypeConformsTo</key><array><string>public.zip-archive</string></array>
<key>UTTypeDescription</key><string>Fruits Atelier Project</string>
<key>UTTypeTagSpecification</key><dict><key>public.filename-extension</key><array><string>faz</string></array><key>public.mime-type</key><string>application/x-fruitsatelier</string></dict>
</dict><dict>
<key>UTTypeIdentifier</key><string>io.github.frankhjwx.fruitsatelier.fad</string>
<key>UTTypeConformsTo</key><array><string>public.zip-archive</string></array>
<key>UTTypeDescription</key><string>Fruits Atelier Difficulty</string>
<key>UTTypeTagSpecification</key><dict><key>public.filename-extension</key><array><string>fad</string></array><key>public.mime-type</key><string>application/x-fruitsatelier-difficulty</string></dict>
</dict><dict>
<key>UTTypeIdentifier</key><string>io.github.frankhjwx.fruitsatelier.fpt</string>
<key>UTTypeConformsTo</key><array><string>public.zip-archive</string></array>
<key>UTTypeDescription</key><string>Fruits Atelier Patterns</string>
<key>UTTypeTagSpecification</key><dict><key>public.filename-extension</key><array><string>fpt</string></array><key>public.mime-type</key><string>application/x-fruitsatelier-patterns</string></dict>
</dict></array>
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
codesign --verify --deep --strict "$FA_APP"
echo "$FA_APP"
