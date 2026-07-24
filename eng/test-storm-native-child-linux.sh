#!/usr/bin/env bash
# Copyright (c) marcschier. Licensed under the MIT License.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$repo_root/eng/storm-native-child-linux-lib.sh"
root="$repo_root/artifacts/storm-native-child-linux-script-tests"
rm -rf "$root"
trap 'rm -rf "$root"' EXIT
mkdir -p "$root/override" "$root/existing" "$root/path/bin" "$root/path/runtime"
printf '#!/usr/bin/env bash\nexit 0\n' >"$root/override/dotnet"
printf '#!/usr/bin/env bash\nexit 0\n' >"$root/existing/dotnet"
printf '#!/usr/bin/env bash\nexit 0\n' >"$root/path/runtime/dotnet"
chmod +x "$root/override/dotnet" "$root/existing/dotnet" "$root/path/runtime/dotnet"
ln -s ../runtime/dotnet "$root/path/bin/dotnet"

(
  export OPENUSD_DOTNET_ROOT="$root/override"
  export DOTNET_ROOT="$root/existing"
  resolve_openusd_dotnet_root
  [[ "$DOTNET_ROOT" == "$root/override" ]]
)
(
  unset OPENUSD_DOTNET_ROOT
  export DOTNET_ROOT="$root/existing"
  resolve_openusd_dotnet_root
  [[ "$DOTNET_ROOT" == "$root/existing" ]]
)
(
  unset OPENUSD_DOTNET_ROOT DOTNET_ROOT
  export PATH="$root/path/bin:$PATH"
  expected_root="$(dirname "$(readlink -f "$root/path/bin/dotnet")")"
  resolve_openusd_dotnet_root
  [[ "$DOTNET_ROOT" == "$expected_root" ]]
)

unavailable="$root/unavailable.json"
write_openusd_vulkan_unavailable "$unavailable" x11 "typed test blocker"
[[ "$(classify_openusd_vulkan_run 0 "$unavailable" x11 0 0)" == "unavailable" ]]
if classify_openusd_vulkan_run 17 "$unavailable" x11 0 0; then
  echo "A failed runner was incorrectly accepted." >&2
  exit 1
fi
if classify_openusd_vulkan_run 0 "$unavailable" x11 1 1; then
  echo "Required Vulkan was incorrectly downgraded." >&2
  exit 1
fi
printf '{malformed' >"$root/malformed.json"
if classify_openusd_vulkan_run 0 "$root/malformed.json" x11 0 0; then
  echo "A malformed Vulkan artifact was incorrectly accepted." >&2
  exit 1
fi
if classify_openusd_vulkan_run 0 "$root/missing.json" x11 0 0; then
  echo "A missing Vulkan artifact was incorrectly accepted." >&2
  exit 1
fi

echo "Linux Storm runner script tests passed."
