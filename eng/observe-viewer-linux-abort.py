#!/usr/bin/env python3
# Copyright (c) marcschier. Licensed under the MIT License.
"""Temporary, single owned post-failure replay; never changes the original verdict."""

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import selectors
import shutil
import signal
import subprocess
import sys
import time

PREFIX = "[DEBUG-viewer-linux-abort] "
TEXT_LIMIT = 65536
GDB_LOCATIONS = (
    "/usr/bin/gdb", "/bin/gdb", "/usr/local/bin/gdb",
    "/usr/bin/gdb-12", "/usr/bin/gdb-13", "/usr/bin/gdb-14",
    "/usr/bin/gdb-15", "/usr/bin/gdb-16", "/usr/bin/gdb-17",
)


def process_identity(pid):
    stat = Path(f"/proc/{pid}/stat").read_text()
    fields = stat.rsplit(")", 1)[1].split()
    return {
        "pid": pid,
        "startTicks": fields[19],
        "parentPid": int(fields[1]),
        "exe": os.path.realpath(f"/proc/{pid}/exe"),
    }


def same_process(expected):
    try:
        actual = process_identity(expected["pid"])
        return all(actual[key] == expected[key] for key in ("pid", "startTicks", "exe"))
    except (FileNotFoundError, ProcessLookupError):
        return False


def collect_bounded(process, seconds):
    result = bytearray()
    truncated = False
    deadline = time.monotonic() + seconds
    with selectors.DefaultSelector() as selector:
        selector.register(process.stdout, selectors.EVENT_READ)
        while selector.get_map() and time.monotonic() < deadline:
            for key, _ in selector.select(max(0, min(0.1, deadline - time.monotonic()))):
                chunk = os.read(key.fd, 4096)
                if not chunk:
                    selector.unregister(key.fileobj)
                    continue
                remaining = TEXT_LIMIT - len(result)
                result.extend(chunk[:remaining])
                truncated |= len(chunk) > remaining
        if process.poll() is None:
            try:
                process.wait(timeout=max(0, deadline - time.monotonic()))
            except subprocess.TimeoutExpired:
                pass
        timed_out = process.poll() is None and time.monotonic() >= deadline
    return bytes(result), truncated, timed_out


def supported_debugger(gdb):
    for arguments, required in (
        (["--help"], ("--batch", "--nx", "--args", "--command")),
        (["--nx", "--batch", "-ex", 'python print("OPENUSD_GDB_PYTHON_OK")',
          "-ex", "help set startup-with-shell", "-ex", "help set print frame-arguments",
          "-ex", "help set print entry-values", "-ex", "help set debuginfod enabled",
          "-ex", "help set disable-randomization"],
         ("OPENUSD_GDB_PYTHON_OK", "startup-with-shell", "frame-arguments", "entry-values",
          "debuginfod", "disable-randomization")),
    ):
        with subprocess.Popen([gdb, *arguments], stdout=subprocess.PIPE, stderr=subprocess.STDOUT) as process:
            identity = process_identity(process.pid)
            raw, truncated, expired = collect_bounded(process, 2)
            if expired and same_process(identity):
                os.kill(process.pid, signal.SIGKILL)
            process.wait(timeout=2)
            text = raw.decode("utf-8", errors="replace")
            if expired or truncated or process.returncode != 0 or any(value not in text for value in required):
                return False
    return True


def fingerprint(executable, stage=None, plugin=None, working_directory=None):
    root = executable.parent
    groups = {"executable": [executable], "runtimeAndShaderContainers": [
        path for path in root.iterdir() if path.is_file() and
        (path.suffix in (".dll", ".so", ".spv", ".metallib") or ".so." in path.name)]}
    for name, folder in (("nativeBin", root / "bin"), ("nativeLib", root / "lib"),
                         ("builtinResources", root / "usd")):
        groups[name] = sorted(path for path in folder.rglob("*") if path.is_file()) if folder.is_dir() else []
    if stage is not None and plugin is not None:
        stage = stage.resolve(strict=True)
        plugin = plugin.resolve(strict=True)
        if not stage.is_relative_to(root) or not plugin.is_relative_to(root):
            raise ValueError("Only the owned staged input/resource paths may be observed.")
        groups["stage"] = [stage]
        groups["pluginResources"] = sorted(path for path in plugin.rglob("*") if path.is_file())
    result = {}
    for name, paths in groups.items():
        digest = hashlib.sha256()
        for path in sorted(paths):
            if not path.resolve(strict=True).is_relative_to(root):
                raise ValueError("An observed resource escaped the owned staged runtime.")
            with path.open("rb") as stream:
                value = hashlib.file_digest(stream, "sha256").hexdigest()
            digest.update(str(path.relative_to(root)).encode() + b"\0" + value.encode() + b"\n")
        result[name] = {"fileCount": len(paths), "pathAndContentSha256": digest.hexdigest()}
    result["workingDirectory"] = str(working_directory.resolve(strict=True)) if working_directory else None
    result["approvedDisplayContext"] = {
        name: os.environ.get(name) for name in (
            "DISPLAY", "WAYLAND_DISPLAY", "XDG_SESSION_TYPE", "LIBGL_ALWAYS_SOFTWARE",
            "MESA_GL_VERSION_OVERRIDE", "MESA_GLSL_VERSION_OVERRIDE",
            "OPENUSD_RENDERER", "OPENUSD_VIEWER_PLATFORM",
        )
    }
    return result


def cleanup_owned(debugger, debugger_identity, inferior_record, executable):
    candidates = []
    if inferior_record.is_file():
        try:
            candidate = json.loads(inferior_record.read_text())
            if candidate["exe"] == str(executable) and candidate.get("parentPid") == debugger.pid:
                candidates.append(candidate)
        except (ValueError, KeyError):
            print(PREFIX + '{"status":"UNKNOWN","reason":"owned-inferior-record-incomplete"}')
    if same_process(debugger_identity):
        children = Path(f"/proc/{debugger.pid}/task/{debugger.pid}/children")
        try:
            for value in children.read_text().split():
                candidate = process_identity(int(value))
                if candidate["exe"] == str(executable) and candidate["parentPid"] == debugger.pid:
                    candidates.append(candidate)
        except (FileNotFoundError, ProcessLookupError):
            pass
    for candidate in candidates:
        if same_process(candidate):
            os.kill(candidate["pid"], signal.SIGKILL)
    if same_process(debugger_identity):
        os.kill(debugger.pid, signal.SIGKILL)
    debugger.wait(timeout=2)


def observe(executable, working_directory, output, original_exit, original_arguments,
            original_inputs=None, stage=None, plugin=None):
    result = {"status": "UNKNOWN", "originalExit": original_exit, "authoritativeVerdict": "original-failure",
              "evidenceKind": "separate-owned-startup-REPLAY; not-original-abort-stack-or-qualified-execution"}
    if original_exit != 134:
        result["reason"] = "not-the-approved-original-abort"
        return result
    if sys.platform != "linux":
        result["reason"] = "not-a-linux-replay-host"
        return result
    import resource

    executable = executable.resolve(strict=True)
    if output.parent.resolve(strict=True) != executable.parent:
        raise ValueError("Only the owned original executable directory may contain replay evidence.")
    if original_inputs is None or stage is None or plugin is None:
        result["reason"] = "original-input-snapshot-missing"
        return result
    original = json.loads(original_inputs.read_text())
    before = fingerprint(executable, stage, plugin, working_directory)
    if original.get("status") != "recorded-original-inputs" or original.get("inputs") != before:
        result["reason"] = "original-executable-runtime-plugin-shader-stage-or-display-context-mismatch"
        return result
    gdb = shutil.which("gdb")
    if gdb is None:
        gdb = next((path for path in GDB_LOCATIONS if os.path.isfile(path) and os.access(path, os.X_OK)), None)
        result["explicitInstalledDebuggerLocationsChecked"] = list(GDB_LOCATIONS)
    if gdb is None:
        result["reason"] = "already-installed-gdb-absent"
        return result
    gdb = os.path.realpath(gdb)
    if not supported_debugger(gdb):
        result["reason"] = "installed-gdb-help-or-command-support-unknown"
        return result
    output.mkdir(parents=True, exist_ok=False)
    inferior_record = output / "owned-inferior.json"
    fault_record = output / "owned-fault.json"
    script = output / "observer.gdb"
    script.write_text(
        "set pagination off\nset confirm off\nset startup-with-shell off\n"
        "set print frame-arguments none\nset print entry-values no\n"
        "set debuginfod enabled off\nset disable-randomization off\n"
        "handle SIGABRT stop print pass\npython\n"
        "import gdb, json, os\n"
        "approved_abort = False\n"
        "def record_owned(event):\n"
        "    pid = gdb.selected_inferior().pid\n"
        "    if not pid: return\n"
        "    try:\n"
        "        stat = open('/proc/%d/stat' % pid).read()\n"
        "        fields = stat.rsplit(')', 1)[1].split()\n"
        "        identity = {'pid': pid, 'startTicks': fields[19], 'parentPid': int(fields[1]), "
        "'exe': os.path.realpath('/proc/%d/exe' % pid)}\n"
        f"        if identity['exe'] == {str(executable)!r} and identity['parentPid'] == os.getpid():\n"
        f"            with open({str(inferior_record) + '.tmp'!r}, 'w') as stream: json.dump(identity, stream)\n"
        f"            os.replace({str(inferior_record) + '.tmp'!r}, {str(inferior_record)!r})\n"
        "    except (FileNotFoundError, ProcessLookupError): pass\n"
        "def stopped(event):\n"
        "    global approved_abort\n"
        "    approved_abort = isinstance(event, gdb.SignalEvent) and event.stop_signal == 'SIGABRT'\n"
        "gdb.events.new_thread.connect(record_owned)\ngdb.events.stop.connect(stopped)\nend\n"
        "run\npython\n"
        "if approved_abort:\n"
        "    frames = gdb.execute('bt 32', to_string=True)\n"
        "    record_owned(None)\n"
        "    bounded = frames.encode('utf-8')[:60000].decode('utf-8', errors='ignore')\n"
        "    proof = {'signal': 'SIGABRT', 'pid': gdb.selected_inferior().pid, "
        "'frames': bounded, 'truncated': len(frames.encode('utf-8')) > 60000}\n"
        "    encoded = json.dumps(proof, ensure_ascii=False)\n"
        "    if len(encoded.encode('utf-8')) > 65536:\n"
        "        encoded = json.dumps({'signal': 'SIGABRT', 'pid': gdb.selected_inferior().pid, "
        "'frames': '', 'truncated': True})\n"
        f"    with open({str(fault_record)!r}, 'w') as stream:\n"
        "        stream.write(encoded)\n"
        "    gdb.execute('continue')\n"
        "end\n",
        encoding="utf-8",
    )
    environment = os.environ.copy()
    environment["OPENUSD_STATUS_FILE"] = str(output / "replay-status.txt")
    environment["OPENUSD_LOG_FILE"] = str(output / "replay-viewer.log")
    environment["OPENUSD_SOAK_ARTIFACT"] = str(output / "replay-soak.json")
    environment["DOTNET_DbgEnableMiniDump"] = "0"
    environment["COMPlus_DbgEnableMiniDump"] = "0"
    arguments = list(original_arguments)
    if "--soak-artifact" in arguments:
        arguments[arguments.index("--soak-artifact") + 1] = environment["OPENUSD_SOAK_ARTIFACT"]
    started = time.monotonic()
    result["replayStartedAtUtc"] = datetime.now(timezone.utc).isoformat()
    debugger = subprocess.Popen(
        [gdb, "--nx", "--batch", f"--command={script}", "--args", str(executable), *arguments],
        cwd=working_directory, env=environment, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
        preexec_fn=lambda: resource.setrlimit(resource.RLIMIT_CORE, (0, 0)),
    )
    try:
        identity = process_identity(debugger.pid)
        result["ownedDebugger"] = identity
        raw, truncated, expired = collect_bounded(debugger, max(0, 5 - (time.monotonic() - started)))
        record = json.loads(fault_record.read_text()) if fault_record.is_file() and fault_record.stat().st_size <= TEXT_LIMIT else None
        if not expired and not truncated and record is not None and not record["truncated"] and record["signal"] == "SIGABRT" and inferior_record.is_file():
            owned = json.loads(inferior_record.read_text())
            if owned["exe"] == str(executable) and owned["pid"] == record["pid"] and owned.get("parentPid") == debugger.pid:
                result["ownedInferior"] = owned
                (output / "faulting-thread.txt").write_text(record["frames"], encoding="utf-8")
                result.update({"status": "observed-owned-replay-abort", "stackFramesMaximum": 32})
        result.update({"replayDeadlineExpired": expired, "textTruncated": truncated})
    finally:
        try:
            identity
        except UnboundLocalError:
            if debugger.poll() is None:
                raise RuntimeError("Owned debugger identity could not be established.")
        else:
            cleanup_owned(debugger, identity, inferior_record, executable)
    after = fingerprint(executable, stage, plugin, working_directory)
    result.update({"filesUnchanged": before == after, "fileHashes": before,
                   "identityMeaning": "owned replay PID/start/exe; file hashes are not mapped image hashes"})
    if before != after:
        result.update({"status": "UNKNOWN", "reason": "owned-input-bytes-changed"})
    (output / "observation.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--executable", type=Path, required=True)
    parser.add_argument("--working-directory", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--original-exit", type=int, required=True)
    parser.add_argument("--arguments-json", required=True)
    parser.add_argument("--original-inputs", type=Path)
    parser.add_argument("--stage", type=Path)
    parser.add_argument("--plugin", type=Path)
    parser.add_argument("--record-original", action="store_true")
    args = parser.parse_args()
    try:
        if args.record_original:
            if args.stage is None or args.plugin is None or args.output.parent.resolve() != args.executable.parent.resolve():
                raise ValueError("Original snapshot paths are not owned.")
            result = {"status": "recorded-original-inputs",
                      "recordedAtUtc": datetime.now(timezone.utc).isoformat(),
                      "inputs": fingerprint(args.executable.resolve(strict=True), args.stage, args.plugin, args.working_directory)}
            args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        else:
            result = observe(args.executable, args.working_directory, args.output, args.original_exit,
                             json.loads(args.arguments_json), args.original_inputs, args.stage, args.plugin)
    except (OSError, ValueError, KeyError, subprocess.SubprocessError):
        result = {"status": "UNKNOWN", "originalExit": args.original_exit,
                  "reason": "diagnostic-observation-error", "authoritativeVerdict": "original-failure"}
    print(PREFIX + json.dumps(result))


if __name__ == "__main__":
    main()
