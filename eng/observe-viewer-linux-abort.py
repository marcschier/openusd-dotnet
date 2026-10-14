#!/usr/bin/env python3
# Copyright (c) marcschier. Licensed under the MIT License.
"""Temporary, single owned post-failure replay; never changes the original verdict."""

import argparse
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


def process_identity(pid):
    stat = Path(f"/proc/{pid}/stat").read_text()
    return {
        "pid": pid,
        "startTicks": stat.rsplit(")", 1)[1].split()[19],
        "exe": os.path.realpath(f"/proc/{pid}/exe"),
    }


def same_process(expected):
    try:
        return process_identity(expected["pid"]) == expected
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


def fingerprint(executable):
    root = executable.parent
    paths = [executable]
    for relative in (
        "OpenUsd.Viewer.App.dll", "OpenUsd.Viewer.dll",
        "OpenUsd.Interop.dll", "OpenUsd.Rendering.dll", "OpenUsd.Rendering.Storm.dll",
        "OpenUsd.Rendering.Silk.dll", "OpenUsd.Rendering.Silk.Vulkan.dll",
        "libvulkan.so", "libvk_swiftshader.so",
        "bin/libopenusd_dotnet.so", "bin/libopenusd_storm_child.so", "bin/libopenusd_hydra.so",
        "lib/libopenusd_dotnet.so", "lib/libopenusd_storm_child.so", "lib/libopenusd_hydra.so",
        "lib/libusd_ms.so", "plugin/usd/plugInfo.json", "usd/plugInfo.json",
    ):
        candidate = root / relative
        if candidate.is_file():
            paths.append(candidate)
    shader_root = root / "OpenUsd.Rendering.Silk"
    if shader_root.is_dir():
        paths.extend(sorted(shader_root.glob("*.spv")))
    records = []
    for path in paths:
        with path.open("rb") as stream:
            digest = hashlib.file_digest(stream, "sha256").hexdigest()
        records.append({"path": str(path.relative_to(root)), "sha256": digest})
    return records


def cleanup_owned(debugger, debugger_identity, inferior_record, executable):
    candidates = []
    if inferior_record.is_file():
        try:
            candidate = json.loads(inferior_record.read_text())
            if candidate["exe"] == str(executable):
                candidates.append(candidate)
        except (ValueError, KeyError):
            print(PREFIX + '{"status":"UNKNOWN","reason":"owned-inferior-record-incomplete"}')
    if same_process(debugger_identity):
        children = Path(f"/proc/{debugger.pid}/task/{debugger.pid}/children")
        try:
            for value in children.read_text().split():
                candidate = process_identity(int(value))
                if candidate["exe"] == str(executable):
                    candidates.append(candidate)
        except (FileNotFoundError, ProcessLookupError):
            pass
    for candidate in candidates:
        if same_process(candidate):
            os.kill(candidate["pid"], signal.SIGKILL)
    if same_process(debugger_identity):
        os.kill(debugger.pid, signal.SIGKILL)
    debugger.wait(timeout=2)


def observe(executable, working_directory, output, original_exit, original_arguments):
    result = {"status": "UNKNOWN", "originalExit": original_exit, "authoritativeVerdict": "original-failure"}
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
    gdb = shutil.which("gdb")
    if gdb is None:
        result["reason"] = "already-installed-gdb-absent"
        return result
    gdb = os.path.realpath(gdb)
    if not supported_debugger(gdb):
        result["reason"] = "installed-gdb-help-or-command-support-unknown"
        return result
    output.mkdir(parents=True, exist_ok=False)
    before = fingerprint(executable)
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
        "        identity = {'pid': pid, 'startTicks': stat.rsplit(')', 1)[1].split()[19], "
        "'exe': os.path.realpath('/proc/%d/exe' % pid)}\n"
        f"        if identity['exe'] == {str(executable)!r}:\n"
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
    debugger = subprocess.Popen(
        [gdb, "--nx", "--batch", f"--command={script}", "--args", str(executable), *arguments],
        cwd=working_directory, env=environment, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
        preexec_fn=lambda: resource.setrlimit(resource.RLIMIT_CORE, (0, 0)),
    )
    try:
        identity = process_identity(debugger.pid)
        result["ownedDebugger"] = identity
        raw, truncated, expired = collect_bounded(debugger, 5)
        record = json.loads(fault_record.read_text()) if fault_record.is_file() and fault_record.stat().st_size <= TEXT_LIMIT else None
        if record is not None and not record["truncated"] and record["signal"] == "SIGABRT" and inferior_record.is_file():
            owned = json.loads(inferior_record.read_text())
            if owned["exe"] == str(executable) and owned["pid"] == record["pid"]:
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
    after = fingerprint(executable)
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
    args = parser.parse_args()
    try:
        result = observe(args.executable, args.working_directory, args.output, args.original_exit,
                         json.loads(args.arguments_json))
    except (OSError, ValueError, KeyError, subprocess.SubprocessError):
        result = {"status": "UNKNOWN", "originalExit": args.original_exit,
                  "reason": "diagnostic-observation-error", "authoritativeVerdict": "original-failure"}
    print(PREFIX + json.dumps(result))


if __name__ == "__main__":
    main()
