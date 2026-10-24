#!/bin/bash
set -euo pipefail
FA_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
[[ "$(uname -s)" == Darwin ]] || { echo 'This installer is for macOS.'; exit 1; }
mkdir -p "$FA_ROOT/artifacts/dotnet"
curl --fail --location https://dot.net/v1/dotnet-install.sh -o "$FA_ROOT/artifacts/dotnet-install.sh"
bash "$FA_ROOT/artifacts/dotnet-install.sh" --version 10.0.400 --install-dir "$FA_ROOT/artifacts/dotnet" --no-path
