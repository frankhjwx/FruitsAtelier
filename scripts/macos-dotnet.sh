#!/bin/bash
set -euo pipefail
FA_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export DOTNET_CLI_HOME="$FA_ROOT/artifacts/dotnet-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false
export DOTNET_ROOT="$FA_ROOT/artifacts/dotnet"
if [[ -x "$DOTNET_ROOT/dotnet" ]]; then
    FA_DOTNET="$DOTNET_ROOT/dotnet"
elif command -v dotnet >/dev/null 2>&1; then
    FA_DOTNET="$(command -v dotnet)"
    unset DOTNET_ROOT
else
    echo 'Install .NET SDK 10.0.400, or run: bash scripts/Install-Mac-SDK.sh'
    exit 1
fi
cd "$FA_ROOT/macOS"
