#!/usr/bin/env bash
# Copyright (c) marcschier. Licensed under the MIT License.

resolve_openusd_dotnet_root() {
  local candidate=""
  if [[ -n "${OPENUSD_DOTNET_ROOT:-}" ]]; then
    candidate="$OPENUSD_DOTNET_ROOT"
  elif [[ -n "${DOTNET_ROOT:-}" && -x "$DOTNET_ROOT/dotnet" ]]; then
    candidate="$DOTNET_ROOT"
  else
    local executable
    executable="$(command -v dotnet 2>/dev/null || true)"
    [[ -n "$executable" ]] ||
      { echo "dotnet was not found; set OPENUSD_DOTNET_ROOT or DOTNET_ROOT." >&2; return 1; }
    executable="$(readlink -f "$executable")"
    candidate="$(dirname "$executable")"
  fi
  [[ -x "$candidate/dotnet" ]] ||
    { echo "The selected dotnet root has no executable dotnet: $candidate" >&2; return 1; }
  DOTNET_ROOT="$(cd "$candidate" && pwd)"
  export DOTNET_ROOT
  case ":$PATH:" in
    *":$DOTNET_ROOT:"*) ;;
    *) PATH="$DOTNET_ROOT:$PATH"; export PATH ;;
  esac
}

openusd_linux_vulkan_runtime_ready() {
  "${OPENUSD_PYTHON:-python3}" - "$1" <<'PY'
import json, sys
try:
    value = json.load(open(sys.argv[1], encoding="utf-8"))
except (OSError, json.JSONDecodeError) as error:
    raise SystemExit(f"Invalid publish capability artifact: {error}")
if not isinstance(value, dict):
    raise SystemExit("Publish capability artifact must be an object.")
for name in ("nativeRuntime", "pluginMetadata"):
    if not isinstance(value.get(name), bool):
        raise SystemExit(f"Publish capability artifact field {name} must be boolean.")
print("ready" if value["nativeRuntime"] and value["pluginMetadata"] else "unavailable")
PY
}

write_openusd_vulkan_unavailable() {
  "${OPENUSD_PYTHON:-python3}" - "$1" "$2" "$3" <<'PY'
import json, pathlib, sys
path = pathlib.Path(sys.argv[1])
path.parent.mkdir(parents=True, exist_ok=True)
path.write_text(json.dumps({
    "schemaVersion": 1,
    "producer": "openusd-linux-presentation-runner",
    "outcome": "unavailable",
    "platform": sys.argv[2],
    "blocker": sys.argv[3],
}, indent=2) + "\n", encoding="utf-8")
PY
}

validate_openusd_vulkan_artifact() {
  "${OPENUSD_PYTHON:-python3}" - "$1" "$2" <<'PY'
import json, sys
try:
    value = json.load(open(sys.argv[1], encoding="utf-8"))
except (OSError, json.JSONDecodeError) as error:
    raise SystemExit(f"Missing or malformed Vulkan artifact: {error}")
if not isinstance(value, dict) or value.get("schemaVersion") != 1:
    raise SystemExit("Vulkan artifact schemaVersion must be 1.")
if value.get("platform") != sys.argv[2]:
    raise SystemExit("Vulkan artifact platform does not match the requested platform.")
outcome = value.get("outcome")
if outcome == "unavailable":
    if not isinstance(value.get("blocker"), str) or not value["blocker"].strip():
        raise SystemExit("Unavailable Vulkan artifact requires a non-empty blocker.")
    print("unavailable")
elif outcome in ("passed", "capability-passed"):
    required = {
        "capabilityOnly": bool,
        "frameCount": int,
        "liveEditObserved": bool,
        "resizeObserved": bool,
        "diagnostics": dict,
        "identity": dict,
        "statuses": list,
    }
    for name, expected in required.items():
        if not isinstance(value.get(name), expected):
            raise SystemExit(f"Passed Vulkan artifact field {name} has the wrong type.")
    if outcome == "passed":
        if value["capabilityOnly"] or value["frameCount"] <= 0:
            raise SystemExit("Passed Vulkan artifact did not execute full presentation.")
        if not value["liveEditObserved"] or not value["resizeObserved"]:
            raise SystemExit("Passed Vulkan artifact lacks edit/resize evidence.")
    elif not value["capabilityOnly"]:
        raise SystemExit("Capability-only artifact was not marked capabilityOnly.")
    print(outcome)
else:
    raise SystemExit(f"Vulkan artifact outcome is not accepted: {outcome!r}")
PY
}

classify_openusd_vulkan_run() {
  local exit_code="$1"
  local artifact="$2"
  local platform="$3"
  local required="$4"
  local runtime_ready="$5"
  [[ "$exit_code" -eq 0 ]] ||
    { echo "Vulkan runner failed with exit code $exit_code." >&2; return 1; }
  local outcome
  outcome="$(validate_openusd_vulkan_artifact "$artifact" "$platform")" || return 1
  if [[ "$outcome" != "passed" && "$outcome" != "unavailable" ]]; then
    echo "Vulkan switching requires passed or typed unavailable evidence, not $outcome." >&2
    return 1
  fi
  if [[ "$outcome" == "unavailable" &&
        "$required" == "1" &&
        "$runtime_ready" == "1" ]]; then
    echo "Vulkan is required because runtime and plugin readiness are present." >&2
    return 1
  fi
  printf '%s\n' "$outcome"
}
