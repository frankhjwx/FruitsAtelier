#!/bin/bash
set -euo pipefail
source "$(cd "$(dirname "$0")" && pwd)/macos-dotnet.sh"
if [[ "${1:-}" != --native-only ]]; then
    for suite in Core Gameplay; do
        "$FA_DOTNET" run --project "$FA_ROOT/tests/FruitsAtelier.$suite.Tests" -c Release
    done
    "$FA_DOTNET" run --project "$FA_ROOT/tests/FruitsAtelier.Formats.Tests" -c Release -- --skip-external-fixtures
    for suite in App Skinning SkinArchive; do
        "$FA_DOTNET" run --project "$FA_ROOT/macOS/tests/$suite" -c Release
    done
fi
if [[ "${1:-}" == --skip-device-tests ]]; then
    "$FA_DOTNET" run --project "$FA_ROOT/tests/FruitsAtelier.Mac.Tests" -c Release -- --input-check
    echo 'SKIP Mac native device tests (explicitly excluded on headless CI)'
else
    "$FA_DOTNET" run --project "$FA_ROOT/tests/FruitsAtelier.Mac.Tests" -c Release
fi
