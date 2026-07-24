#!/usr/bin/env bash
# Copyright (c) marcschier. Licensed under the MIT License.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INSTALL_DIR="$REPO_ROOT/.dotnet"
VERSION="10.0.301"
ERROR_MESSAGE="Required .NET SDK 10.0.301 not found. Run ./eng/install-dotnet.ps1 or ./eng/install-dotnet.sh."
INSTALL_SCRIPT="$(mktemp "${TMPDIR:-/tmp}/dotnet-install.XXXXXX")"
GLOBAL_JSON_TMP=""

cleanup() {
    rm -f "$INSTALL_SCRIPT"
    [ -n "$GLOBAL_JSON_TMP" ] && rm -f "$GLOBAL_JSON_TMP"
}
trap cleanup EXIT

cd "$REPO_ROOT"
curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$INSTALL_SCRIPT"
bash "$INSTALL_SCRIPT" --version "$VERSION" --install-dir "$INSTALL_DIR"
INSTALLED_VERSION="$("$INSTALL_DIR/dotnet" --version)"
if [ "$INSTALLED_VERSION" != "$VERSION" ]; then
    echo "Installed SDK '$INSTALLED_VERSION' does not match '$VERSION'." >&2
    exit 1
fi

cp global.json global.json.bak
if ! command -v jq >/dev/null 2>&1; then
    echo "SDK installation succeeded, but jq is required to preserve global.json." >&2
    echo "Merge sdk.paths=['.dotnet','\$host\$'] and rollForward='disable' manually." >&2
    exit 1
fi
GLOBAL_JSON_TMP="$(mktemp "${TMPDIR:-/tmp}/global-json.XXXXXX")"
jq \
    --arg version "$VERSION" \
    --arg errorMessage "$ERROR_MESSAGE" \
    '.sdk = ((.sdk // {}) + {
        version: $version,
        rollForward: "disable",
        allowPrerelease: false,
        paths: [".dotnet", "$host$"],
        errorMessage: $errorMessage
    })' \
    global.json >"$GLOBAL_JSON_TMP"
mv "$GLOBAL_JSON_TMP" global.json
GLOBAL_JSON_TMP=""

echo "Installed repository-local .NET SDK $INSTALLED_VERSION."
