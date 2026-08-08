#!/usr/bin/env bash
# Copyright (c) marcschier. Licensed under the MIT License.
set -euo pipefail

platform="${1:-x11}"
switch_count="${OPENUSD_LINUX_STORM_SWITCH_COUNT:-100}"
survival_seconds="${OPENUSD_LINUX_STORM_SURVIVAL_SECONDS:-90}"
fresh_process_count="${OPENUSD_LINUX_STORM_FRESH_PROCESS_COUNT:-10}"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$repo_root/eng/storm-native-child-linux-lib.sh"
output_root="$repo_root/artifacts/storm-native-child-linux/$platform"
viewer_publish="$repo_root/artifacts/viewer/linux-x64"
vulkan_publish="$repo_root/artifacts/avalonia-vulkan-smoke/linux-x64"
loader="$(openusd_linux_vulkan_loader)"
icd="$(openusd_linux_vulkan_icd)"
server_pid=""
required="${OPENUSD_REQUIRE_VULKAN_PRESENTATION:-0}"
# A hosted Linux compositor accepts no external Vulkan images, so requiring Vulkan
# presentation here would demand evidence the host cannot produce. The allowance is
# opt-in from the workflow; see docs/testing.md, "Render gate capability limits".
if [[ "${OPENUSD_ALLOW_UNAVAILABLE_CAPABILITY:-0}" == "1" ]]; then
  required=0
fi

case "$platform" in
  x11|xwayland) ;;
  *) echo "platform must be x11 or xwayland" >&2; exit 2 ;;
esac

mkdir -p "$output_root"
resolve_openusd_dotnet_root
export LIBGL_ALWAYS_SOFTWARE=1
export MESA_GL_VERSION_OVERRIDE=4.5COMPAT
export MESA_GLSL_VERSION_OVERRIDE=450
export OPENUSD_RENDERER=Storm

cleanup() {
  if [[ -n "$server_pid" ]] && kill -0 "$server_pid" 2>/dev/null; then
    kill "$server_pid" 2>/dev/null || true
    wait "$server_pid" 2>/dev/null || true
  fi
  if [[ -n "${runtime_dir:-}" ]]; then
    rm -rf "$runtime_dir"
  fi
}
trap cleanup EXIT INT TERM

wait_for_x11() {
  for _ in $(seq 1 200); do
    if xdpyinfo -display "$DISPLAY" >/dev/null 2>&1; then return 0; fi
    sleep 0.05
  done
  echo "X11 display $DISPLAY did not become ready." >&2
  return 1
}

# A Unix domain socket path is capped at 108 bytes including the terminator, and
# "$output_root/runtime" plus the Weston socket name exceeds that on a hosted runner,
# so Weston died during startup with "File name too long". Keep it short and outside
# the artifacts tree.
runtime_dir="$(mktemp -d "${TMPDIR:-/tmp}/openusd-scl-XXXXXX")"
chmod 700 "$runtime_dir"
export XDG_RUNTIME_DIR="$runtime_dir"

report_weston() {
  if [[ -f "$output_root/weston.log" ]]; then
    echo "----- weston log -----" >&2
    cat "$output_root/weston.log" >&2 || true
  fi
}

if [[ "$platform" == "x11" ]]; then
  command -v Xvfb >/dev/null || { echo "Xvfb is required." >&2; exit 3; }
  export DISPLAY=:96
  unset WAYLAND_DISPLAY
  export XDG_SESSION_TYPE=x11
  export OPENUSD_VIEWER_PLATFORM=linux-x11
  Xvfb "$DISPLAY" -screen 0 1280x720x24 -ac -nolisten tcp \
    >"$output_root/xvfb.log" 2>&1 &
  server_pid=$!
  wait_for_x11
else
  command -v weston >/dev/null || { echo "Weston is required." >&2; exit 3; }
  export WAYLAND_DISPLAY=openusd-storm-child
  export XDG_SESSION_TYPE=wayland
  export OPENUSD_VIEWER_PLATFORM=linux-wayland
  unset DISPLAY
  weston --backend=headless-backend.so --socket="$WAYLAND_DISPLAY" \
    --idle-time=0 --width=1280 --height=720 --no-config --xwayland \
    >"$output_root/weston.log" 2>&1 &
  server_pid=$!
  for _ in $(seq 1 200); do
    [[ -S "$runtime_dir/$WAYLAND_DISPLAY" ]] && break
    kill -0 "$server_pid" 2>/dev/null ||
      { echo "Weston exited during startup." >&2; report_weston; exit 3; }
    sleep 0.05
  done
  for _ in $(seq 1 200); do
    display_value="$(
      grep -Eo 'display :[0-9]+' "$output_root/weston.log" 2>/dev/null |
        tail -n 1 | awk '{print $2}' || true)"
    if [[ -n "$display_value" ]]; then
      export DISPLAY="$display_value"
      break
    fi
    kill -0 "$server_pid" 2>/dev/null ||
      { echo "Weston exited before its managed XWayland/XWM was ready." >&2; report_weston; exit 3; }
    sleep 0.05
  done
  [[ -n "${DISPLAY:-}" ]] ||
    { echo "Weston did not publish its managed XWayland display." >&2; exit 3; }
  wait_for_x11
fi

if [[ "$platform" == "x11" ]]; then
  # The shim build tree only exists when this run built the native inputs. A verified
  # archive ships native/install without native/build, so run the installed probe
  # directly, exactly as the macOS runner does. Either way the probe's own capability
  # report is what decides, rather than the absence of a build tree.
  probe_log="$output_root/native-storm-child.log"
  if [[ -d "$repo_root/native/build/shim/linux-x64" ]]; then
    ctest --test-dir "$repo_root/native/build/shim/linux-x64" \
      --output-on-failure | tee "$probe_log"
    probe_exit=${PIPESTATUS[0]}
  else
    installed_probe="$repo_root/native/install/shim/linux-x64/bin/openusd_storm_child_probe"
    if [[ -x "$installed_probe" ]]; then
      probe_library_path="$repo_root/native/install/shim/linux-x64/lib"
      probe_library_path+=":$repo_root/native/install/linux-x64/lib"
      probe_library_path+=":${LD_LIBRARY_PATH:-}"
      set +e
      LD_LIBRARY_PATH="$probe_library_path" \
        "$installed_probe" \
        --lifecycle-smoke \
        "$repo_root/native/install/linux-x64/plugin/usd" \
        "$repo_root/test-assets/minimal.usda" \
        "$repo_root/native/install/shim/linux-x64/lib" 2>&1 | tee "$probe_log"
      lifecycle_exit=${PIPESTATUS[0]}
      if [[ "$lifecycle_exit" -ne 0 && "$lifecycle_exit" -ne 125 ]]; then
        probe_exit="$lifecycle_exit"
      else
        LD_LIBRARY_PATH="$probe_library_path" \
          "$installed_probe" \
          "$repo_root/native/install/linux-x64/plugin/usd" \
          "$repo_root/test-assets/minimal.usda" \
          "$repo_root/native/install/shim/linux-x64/lib" 2>&1 | tee -a "$probe_log"
        probe_exit=${PIPESTATUS[0]}
      fi
      set -e
    else
      echo "The linux-x64 archive omitted the installed Storm child probe." | tee "$probe_log"
      probe_exit=0
    fi
  fi
  # 125 is the shared capability exit code. Every gate below needs the direct rendering
  # the probe just said is unavailable, so demanding their evidence would contradict its
  # own report, exactly as on macOS.
  if grep -q 'Skipping Storm child probe: ' "$probe_log" 2>/dev/null; then
    echo "Skipping the Linux Storm native-child evidence: the probe reported that direct rendering is unavailable on this host."
    mkdir -p "$output_root"
    python3 -c '
import json, sys
print(json.dumps({
  "schemaVersion": 1,
  "status": "skipped",
  "platform": sys.argv[1],
  "reason": "Direct rendering is unavailable on this host.",
  "nativeProbe": sys.argv[2]
}, indent=2))' "$platform" "$probe_log" | tee "$output_root/evidence.json"
    exit 0
  fi
  if [[ "$probe_exit" -ne 0 ]]; then
    echo "The Linux native Storm child probe failed with $probe_exit." >&2
    exit "$probe_exit"
  fi
  pwsh -NoProfile -File "$repo_root/eng/run-native-probe.ps1" \
    -Rid linux-x64 -SkipNativeAbiProbe |
    tee "$output_root/native-aot.log"
fi

pwsh -NoProfile -File "$repo_root/eng/publish-avalonia-vulkan-smoke.ps1" \
  -Rid linux-x64 | tee "$output_root/vulkan-publish.log"
capability_artifact="$vulkan_publish/x11-smoke.json"
publish_capability="$vulkan_publish/publish-capability.json"
runtime_state="$(openusd_linux_vulkan_runtime_ready "$publish_capability")"
runtime_ready=0
vulkan_exit=0
rm -f "$capability_artifact"
if [[ "$runtime_state" == "ready" ]]; then
  runtime_ready=1
  set +e
  bash "$repo_root/eng/run-avalonia-vulkan-smoke-linux.sh" \
    x11 \
    "$vulkan_publish" "$loader" "$icd" |
    tee "$output_root/vulkan-capability.log"
  vulkan_exit=${PIPESTATUS[0]}
  set -e
else
  write_openusd_vulkan_unavailable \
    "$capability_artifact" \
    x11 \
    "Linux hdSilk runtime/plugin metadata is unavailable."
  cat "$capability_artifact" | tee "$output_root/vulkan-capability.log"
fi
vulkan_outcome="$(
  classify_openusd_vulkan_run \
    "$vulkan_exit" \
    "$capability_artifact" \
    x11 \
    "$required" \
    "$runtime_ready")"
vulkan_available=0
if [[ "$vulkan_outcome" == "passed" ]]; then
  vulkan_available=1
else
  # Without Vulkan there is no renderer switch, so the fresh-process loop below can
  # only re-prove that a fresh viewer process renders Storm. The one Storm-only soak
  # already proves that, and the only viewer invocation that renders on a hosted Linux
  # runner is the shared-stage soak, whose long-run memory ceilings are about soak
  # behaviour rather than process lifecycle: repeating it ten times imports those
  # assertions into this gate and has already failed on working-set growth without
  # telling us anything new. Run it once.
  fresh_process_count=1
fi

if [[ "$vulkan_available" -eq 1 ]]; then
  viewer_evidence_path="$output_root/switching-evidence.json"
  pwsh -NoProfile -File "$repo_root/eng/run-viewer.ps1" \
    -Rid linux-x64 -RendererSwitchSoak -SwitchCount "$switch_count" \
    -SwitchSoakSeconds "$survival_seconds" -SimulateStormContextLoss \
    -EvidencePath "$output_root/switching-evidence.json" \
    -EvidenceScenario "linux-$platform-switching" |
    tee "$output_root/switching.log"
  switching_outcome="passed"
  blocker=""
else
  # Vulkan composition is unavailable on this host, so there is no renderer switch to
  # evidence. schemaVersion 8 switching evidence is meaningless without a switch, and
  # the viewer has no code path that produces it for a Storm-only run: setting
  # OPENUSD_VIEWER_EVIDENCE_PATH puts it into a switching-evidence session that waits
  # for switch commands that never arrive, so it initialises and then idles forever.
  # Prove Storm still renders on this shell with the shared-stage soak, which is the
  # viewer invocation that demonstrably renders here: the linux-x11 and linux-wayland
  # platform smokes earlier in this same job use it and pass. It is also stronger
  # evidence than a single frame.
  viewer_evidence_path=""
  pwsh -NoProfile -File "$repo_root/eng/run-viewer.ps1" \
    -Rid linux-x64 -SharedStageSoak -SoakSeconds 90 |
    tee "$output_root/storm-host.log"
  switching_outcome="vulkan-unavailable"
  blocker="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1])).get("blocker","opaque-FD Vulkan composition unavailable"))' "$capability_artifact" 2>/dev/null || echo 'opaque-FD Vulkan composition unavailable')"
fi

if [[ -n "$viewer_evidence_path" ]]; then
python3 -c '
import base64, hashlib, json, math, re, struct, sys
def get(value, name, default=None):
    if not isinstance(value, dict):
        return default
    for key, item in value.items():
        if key.lower() == name.lower():
            return item
    return default
path = sys.argv[1]
value = json.load(open(path, encoding="utf-8"))
if get(value, "schemaVersion") != 8:
    raise SystemExit("Viewer evidence schemaVersion must be 8.")
if get(value, "stormChildAbiVersion") != 7:
    raise SystemExit("Viewer evidence Storm child ABI must be 7.")
if get(value, "nativeNavigation"):
    raise SystemExit("Linux evidence must not contain Win32 native navigation proof.")
states = get(value, "states") or []
pixels = get(value, "pixels") or []
transitions = get(value, "cameraTransitions") or []
if not states or not pixels or not transitions:
    raise SystemExit("Viewer camera evidence is incomplete.")
for state in states:
    if any(key.lower() == "camera" for key in state):
        raise SystemExit("Opaque Viewer camera strings are forbidden.")
    mode = get(state, "cameraMode")
    expected_mode = {"Automatic": 0, "Matrices": 1}.get(mode)
    if expected_mode is None:
        raise SystemExit("Viewer camera mode is invalid.")
    try:
        payload = base64.b64decode(get(state, "cameraPayload"), validate=True)
    except (KeyError, TypeError, ValueError) as error:
        raise SystemExit(f"Viewer camera payload is invalid: {error}")
    if len(payload) != 524 or int.from_bytes(payload[:4], "little") != expected_mode:
        raise SystemExit("Viewer camera payload shape or mode is invalid.")
    clip_plane_count = int.from_bytes(payload[260:264], "little")
    if clip_plane_count > 8:
        raise SystemExit("Viewer camera payload contains too many clip planes.")
    if not all(math.isfinite(item) for item in struct.unpack("<32d", payload[4:260])):
        raise SystemExit("Viewer camera payload contains non-finite values.")
    clip_plane_end = 268 + (clip_plane_count * 4 * 8)
    if clip_plane_count and not all(math.isfinite(item) for item in struct.unpack(f"<{clip_plane_count * 4}d", payload[268:clip_plane_end])):
        raise SystemExit("Viewer camera payload contains non-finite clip plane values.")
    if hashlib.sha256(payload).hexdigest().upper() != get(state, "cameraSignature", ""):
        raise SystemExit("Viewer camera SHA-256 does not match its payload.")
    if not re.fullmatch(r"[0-9A-F]{16}", get(state, "nativeCameraSignature", "")):
        raise SystemExit("Viewer native camera signature is invalid.")
state_backends = {get(state, "backend") for state in states}
transition_backends = {get(transition, "backend") for transition in transitions}
required = state_backends.intersection({"Storm", "D3D12", "Vulkan"})
if not required.issubset(transition_backends):
    raise SystemExit("Viewer explicit-camera transitions are missing.")
for transition in transitions:
    explicit = [
        state for state in states
        if get(state, "backend") == get(transition, "backend")
        and get(state, "phase") == get(transition, "explicitPhase")
    ]
    if len(explicit) != 1 or get(explicit[0], "cameraMode") != "Matrices":
        raise SystemExit("Viewer explicit-camera state is missing.")
    if get(transition, "backend") == "Storm":
        expected = get(explicit[0], "nativeCameraSignature")
        if not get(transition, "asyncCoalescingValidated") \
                or get(transition, "latestRequestedRevision") != get(explicit[0], "revision") \
                or get(transition, "latestRequestedCameraSignature") != expected \
                or get(transition, "latestRenderedCameraSignature") != expected:
            raise SystemExit("Storm latest camera diagnostics are invalid.")
if any(
    get(pixel, "backend") == "Storm"
    and get(pixel, "captureApi") !=
        "openusd_storm_child_capture_framebuffer(ABI7,preserved-texture)"
    for pixel in pixels
):
    raise SystemExit("Storm Viewer pixels did not use the ABI 7 capture label.")
' "$viewer_evidence_path"
fi

for process_index in $(seq 1 "$fresh_process_count"); do
  if [[ "$vulkan_available" -eq 1 ]]; then
    pwsh -NoProfile -File "$repo_root/eng/run-viewer.ps1" \
      -Rid linux-x64 -RendererSwitchSoak -SwitchCount 2 \
      -SwitchSoakSeconds 1 |
      tee "$output_root/fresh-$process_index.log"
  else
    # Same reason as the Storm-only proof above: the plain smoke branch has never
    # rendered on a hosted Linux runner, while the shared-stage soak branch does. This
    # still proves what the loop is for, that each fresh process starts, renders Storm
    # and shuts down.
    pwsh -NoProfile -File "$repo_root/eng/run-viewer.ps1" \
      -Rid linux-x64 -SharedStageSoak -SoakSeconds 90 -ReusePublishedOutput |
      tee "$output_root/fresh-$process_index.log"
  fi
done

python3 -c '
import json,sys
print(json.dumps({
  "schemaVersion": 1,
  "platform": sys.argv[1],
  "shell": "X11" if sys.argv[1] == "x11" else "whole-shell XWayland",
  "stormHost": "GLX native child",
  "switches": int(sys.argv[2]) if sys.argv[3] == "passed" else 0,
  "freshProcesses": int(sys.argv[4]),
  "switchingOutcome": sys.argv[3],
  "vulkanCapability": "opaque-FD available" if sys.argv[3] == "passed" else "typed unavailable",
  "blocker": sys.argv[5] or None,
  "stormCapture": "preserved pre-swap texture",
  "shellCapture": "XGetImage mapped Viewer viewport",
  "xwaylandOwner": "Weston XWM" if sys.argv[1] == "xwayland" else None,
  "cpuReadback": "diagnostics only"
}, indent=2))
' "$platform" "$switch_count" "$switching_outcome" "$fresh_process_count" "$blocker" |
  tee "$output_root/evidence.json"
