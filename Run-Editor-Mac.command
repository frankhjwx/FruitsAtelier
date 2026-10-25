#!/bin/bash
set -euo pipefail
source "$(cd "$(dirname "$0")" && pwd)/scripts/macos-dotnet.sh"
[[ "$(uname -s)" == Darwin ]] || { echo 'Use Run-Editor.cmd on Windows.'; exit 1; }
"$FA_DOTNET" build "$FA_ROOT/src/FruitsAtelier.Mac/FruitsAtelier.Mac.csproj" -c Release --nologo --verbosity minimal -p:RestoreLockedMode=true
exec "$FA_DOTNET" "$FA_ROOT/src/FruitsAtelier.Mac/bin/Release/net10.0/FruitsAtelier.Mac.dll" "$@"
