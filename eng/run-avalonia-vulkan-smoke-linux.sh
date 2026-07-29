#!/usr/bin/env bash
# Copyright (c) marcschier. Licensed under the MIT License.
set -euo pipefail

platform="${1:-x11}"
publish_root="${2:-artifacts/avalonia-vulkan-smoke/linux-x64}"
loader_path="${3:-/usr/lib/x86_64-linux-gnu/libvulkan.so.1}"
icd_path="${4:-/usr/share/vulkan/icd.d/lvp_icd.x86_64.json}"
required="${OPENUSD_REQUIRE_VULKAN_PRESENTATION:-0}"
timeout_seconds="${OPENUSD_AVALONIA_VULKAN_TIMEOUT_SECONDS:-90}"

case "$platform" in
  x11|wayland) ;;
  *) echo "platform must be x11 or wayland" >&2; exit 2 ;;
esac

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$repo_root/eng/storm-native-child-linux-lib.sh"
publish_root="$(cd "$repo_root" && realpath -m "$publish_root")"
identity_path="$publish_root/build-identity.json"
identity_script="$repo_root/eng/avalonia-vulkan-smoke-identity.ps1"
artifact="$publish_root/$platform-smoke.json"
stdout_log="$publish_root/$platform.stdout.log"
stderr_log="$publish_root/$platform.stderr.log"
compositor_log="$publish_root/$platform-compositor.log"
app_pid=""
compositor_pid=""

cleanup() {
  if [[ -n "$app_pid" ]] && kill -0 "$app_pid" 2>/dev/null; then
    kill "$app_pid" 2>/dev/null || true
    wait "$app_pid" 2>/dev/null || true
  fi
  if [[ -n "$compositor_pid" ]] && kill -0 "$compositor_pid" 2>/dev/null; then
    kill "$compositor_pid" 2>/dev/null || true
    wait "$compositor_pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT INT TERM

unavailable() {
  local blocker="$1"
  mkdir -p "$publish_root"
  write_openusd_vulkan_unavailable "$artifact" "$platform" "$blocker"
  cat "$artifact"
  [[ "$required" == "1" ]] && return 1 || return 0
}

[[ -f "$loader_path" ]] || { unavailable "Vulkan loader unavailable: $loader_path"; exit $?; }
[[ -f "$icd_path" ]] || { unavailable "Vulkan ICD unavailable: $icd_path"; exit $?; }
[[ -f "$identity_path" ]] ||
  { unavailable "Published smoke build identity unavailable"; exit $?; }
[[ -x "$publish_root/OpenUsd.AvaloniaVulkanSmoke" ]] ||
  { unavailable "Published smoke executable unavailable"; exit $?; }
if [[ ! -f "$publish_root/lib/libopenusd_hdsilk.so" ]]; then
  if [[ "$required" == "1" ]]; then
    unavailable "Linux hdSilk shim runtime unavailable"
    exit $?
  fi
  export OPENUSD_AVALONIA_VULKAN_CAPABILITY_ONLY=1
fi

mkdir -p "$publish_root"
assert_identity() {
  pwsh -NoProfile -Command \
    ". '$identity_script'; \
     \$expected = Get-Content '$identity_path' -Raw | ConvertFrom-Json; \
     \$source = Get-AvaloniaVulkanSmokeSourceIdentity -RepoRoot '$repo_root'; \
     \$executable = Get-AvaloniaVulkanSmokeExecutableIdentity \
       -ExecutablePath (Join-Path '$publish_root' \$expected.executableFile); \
     Assert-AvaloniaVulkanSmokeIdentity \
       -Expected \$expected -Source \$source -Executable \$executable"
}
identity_value() {
  python3 -c 'import json,sys; print(json.load(open(sys.argv[1], encoding="utf-8"))[sys.argv[2]])' \
    "$identity_path" "$1"
}
assert_identity
runtime_dir="$publish_root/runtime-$platform"
rm -rf "$runtime_dir"
mkdir -p "$runtime_dir"
chmod 700 "$runtime_dir"
export XDG_RUNTIME_DIR="$runtime_dir"

if [[ "$platform" == "x11" ]]; then
  command -v Xvfb >/dev/null || { unavailable "Xvfb unavailable"; exit $?; }
  export DISPLAY=:97
  unset WAYLAND_DISPLAY
  Xvfb "$DISPLAY" -screen 0 1280x720x24 -nolisten tcp >"$compositor_log" 2>&1 &
  compositor_pid=$!
else
  command -v weston >/dev/null || { unavailable "Weston unavailable"; exit $?; }
  unset DISPLAY
  export WAYLAND_DISPLAY=openusd-vulkan-smoke
  weston --backend=headless-backend.so --socket="$WAYLAND_DISPLAY" --idle-time=0 \
    >"$compositor_log" 2>&1 &
  compositor_pid=$!
fi

for _ in $(seq 1 100); do
  kill -0 "$compositor_pid" 2>/dev/null ||
    { echo "$platform compositor exited during startup." >&2; exit 3; }
  if [[ "$platform" == "x11" ]] &&
    xdpyinfo -display "$DISPLAY" >/dev/null 2>&1; then break; fi
  if [[ "$platform" == "wayland" ]] && [[ -S "$runtime_dir/$WAYLAND_DISPLAY" ]]; then break; fi
  sleep 0.05
done
if [[ "$platform" == "x11" ]]; then
  xdpyinfo -display "$DISPLAY" >/dev/null 2>&1 ||
    { echo "Xvfb did not become ready." >&2; exit 3; }
else
  [[ -S "$runtime_dir/$WAYLAND_DISPLAY" ]] ||
    { echo "Weston did not become ready." >&2; exit 3; }
fi

export LD_LIBRARY_PATH="$(dirname "$loader_path"):$publish_root/lib:$publish_root/bin:${LD_LIBRARY_PATH:-}"
export VK_DRIVER_FILES="$icd_path"
export VK_ICD_FILENAMES="$icd_path"
export VK_LOADER_DEBUG="${VK_LOADER_DEBUG:-error,warn}"
export OPENUSD_PLUGIN_PATH="$publish_root/plugin/usd"
export OPENUSD_STAGE_PATH="$publish_root/avalonia-vulkan-smoke.usda"
export OPENUSD_AVALONIA_VULKAN_PLATFORM="$platform"
export OPENUSD_AVALONIA_VULKAN_ARTIFACT="$artifact"
export OPENUSD_AVALONIA_VULKAN_TIMEOUT_SECONDS="$timeout_seconds"
export OPENUSD_AVALONIA_VULKAN_SOURCE_SHA256="$(identity_value sourceSha256)"
export OPENUSD_AVALONIA_VULKAN_SOURCE_FILE_COUNT="$(identity_value sourceFileCount)"
export OPENUSD_AVALONIA_VULKAN_SOURCE_LATEST_WRITE_UTC="$(identity_value latestSourceWriteUtc)"
export OPENUSD_AVALONIA_VULKAN_EXECUTABLE_SHA256="$(identity_value executableSha256)"
export OPENUSD_AVALONIA_VULKAN_EXECUTABLE_LENGTH="$(identity_value executableLength)"
export OPENUSD_AVALONIA_VULKAN_EXECUTABLE_LAST_WRITE_UTC="$(identity_value executableLastWriteUtc)"
export OPENUSD_AVALONIA_VULKAN_BUILD_COMPLETED_UTC="$(identity_value buildCompletedUtc)"
export OPENUSD_AVALONIA_VULKAN_RUN_STARTED_UTC="$(date -u +'%Y-%m-%dT%H:%M:%S.%NZ')"

"$publish_root/OpenUsd.AvaloniaVulkanSmoke" >"$stdout_log" 2>"$stderr_log" &
app_pid=$!
deadline=$((SECONDS + timeout_seconds))
while kill -0 "$app_pid" 2>/dev/null; do
  if (( SECONDS >= deadline )); then
    kill "$app_pid" 2>/dev/null || true
    wait "$app_pid" 2>/dev/null || true
    echo "Avalonia Vulkan smoke timed out after $timeout_seconds seconds." >&2
    exit 124
  fi
  sleep 0.1
done
set +e
wait "$app_pid"
exit_code=$?
set -e
app_pid=""

cat "$stdout_log" || true
cat "$stderr_log" || true
cat "$artifact" 2>/dev/null || true
assert_identity
[[ "$exit_code" -eq 0 ]] ||
  { echo "Avalonia Vulkan smoke exited with code $exit_code." >&2; exit "$exit_code"; }
validate_openusd_vulkan_artifact "$artifact" "$platform" >/dev/null
