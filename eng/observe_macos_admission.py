"""Observe one owned original CTest child; never change its selector, deadline or verdict."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time


def write_receipt(path, value):
    data = (json.dumps(value, indent=2) + "\n").encode()
    if len(data) > 65536:
        raise ValueError("The diagnostic receipt exceeds its 64 KiB privacy bound.")
    with path.open("xb") as stream:
        stream.write(data)


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def capture(debugger, command, result, internal_dict):
    import lldb

    del command, result, internal_dict
    config = json.loads(Path(os.environ["OPENUSD_OWN_ADMISSION_CAPTURE"]).read_text())
    output = Path(config["output"])
    process = None
    try:
        if os.getsid(config["pid"]) != config["ownerSession"]:
            raise RuntimeError("The selected process no longer belongs to the original owned CTest session.")
        started = subprocess.check_output(
            ["/bin/ps", "-p", str(config["pid"]), "-o", "lstart="], text=True, timeout=2).strip()
        if started != config["processStarted"]:
            raise RuntimeError("The selected owned process identity changed before attachment.")
        target = debugger.CreateTarget(config["executable"])
        error = lldb.SBError()
        process = target.AttachToProcessWithID(debugger.GetListener(), config["pid"], error)
        if error.Fail() or not process.IsValid():
            raise RuntimeError("The owned subject could not be attached.")
        thread = process.GetThreadByIndexID(1)
        if not thread.IsValid():
            raise RuntimeError("The original main thread is unavailable.")
        modules = {}
        for module in target.modules:
            path = Path(str(module.GetFileSpec())).resolve()
            expected = config["files"].get(str(path))
            if expected is not None:
                if digest(path) != expected:
                    raise RuntimeError("An actual loaded owned module differs from its bound file.")
                modules[str(path)] = expected
        if set(modules) != set(config["files"]):
            raise RuntimeError("The exact probe/Core/USD loaded-module closure is incomplete.")
        frames = []
        for index in range(32):
            frame = thread.GetFrameAtIndex(index)
            if not frame.IsValid():
                break
            address = frame.GetPCAddress()
            module = frame.GetModule()
            frames.append({
                "pc": hex(frame.GetPC()),
                "module": module.GetFileSpec().GetFilename() if module.IsValid() else None,
                "moduleOffset": address.GetFileAddress(),
                "function": (frame.GetFunctionName() or "")[:768],
            })
        if not frames:
            raise RuntimeError("The original main-thread stack is empty.")
        captured = {
            "diagnosticOnly": True, "pid": config["pid"], "mainThreadId": thread.GetThreadID(),
            "frames": frames, "loadedOwnedFiles": modules,
            "noArgumentsLocalsRegistersRawStackOtherThreadsOrDump": True,
        }
    finally:
        if process is not None and process.IsValid():
            error = process.Detach()
            if error.Fail():
                raise RuntimeError("The owned subject could not be detached.")
    write_receipt(output, captured)


def children(parent):
    query = subprocess.run(
        ["/usr/bin/pgrep", "-P", str(parent)], capture_output=True, text=True, timeout=2)
    if query.returncode not in (0, 1):
        raise RuntimeError("Owned child process discovery failed.")
    return [int(value) for value in query.stdout.split()]


def owned_subject(root_pid, executable, original_command):
    pending = [root_pid]
    count = 0
    while pending:
        pid = pending.pop()
        count += 1
        if count > 64:
            raise RuntimeError("The owned process tree exceeds its observation bound.")
        try:
            if os.getsid(pid) != root_pid:
                continue
        except ProcessLookupError:
            continue
        query = subprocess.run(
            ["/bin/ps", "-ww", "-p", str(pid), "-o", "comm="],
            capture_output=True, text=True, timeout=2)
        if query.returncode == 0 and query.stdout.strip() == str(executable):
            arguments = subprocess.run(
                ["/bin/ps", "-ww", "-p", str(pid), "-o", "command="],
                capture_output=True, text=True, timeout=2)
            if arguments.returncode == 0 and arguments.stdout.strip() == " ".join(original_command):
                return pid
        pending.extend(children(pid))
    return None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    if sys.platform != "darwin":
        raise RuntimeError("This observer requires the actual macOS runner.")
    root = Path(__file__).resolve().parent.parent
    build = root / "native/build/shim/osx-arm64"
    executable = build / "tests/openusd_property_admission_probe"
    files = [
        executable, build / "openusd_dotnet/libopenusd_dotnet.dylib",
        root / "native/install/osx-arm64/lib/libusd_ms.dylib",
    ]
    for path in files:
        if not path.is_file() or not path.resolve().is_relative_to(root):
            raise RuntimeError("An exact owned probe/Core/USD input is missing.")
    command = args.command[1:] if args.command[:1] == ["--"] else args.command
    expected = [
        "ctest", "--test-dir", "native/build/shim/osx-arm64",
        "--no-tests=error", "--output-on-failure",
    ]
    if command != expected:
        raise ValueError("Only the unchanged original macOS CTest invocation may be observed.")
    tests = subprocess.run(
        ["ctest", "--test-dir", str(build), "--show-only=json-v1"],
        check=True, capture_output=True, text=True, timeout=30)
    selected = [test for test in json.loads(tests.stdout)["tests"]
                if test["name"] == "openusd_property_admission_layer-error-list"]
    if len(selected) != 1:
        raise RuntimeError("The actual failing CTest seam is missing or ambiguous.")
    test = selected[0]
    timeout = [item["value"] for item in test["properties"] if item["name"] == "TIMEOUT"]
    if (timeout != [120.0] or len(test["command"]) != 4
            or Path(test["command"][0]).resolve() != executable.resolve()
            or Path(test["command"][1]).resolve() != root / "native/install/osx-arm64/lib/usd"
            or Path(test["command"][2]).resolve() != build / "tests/property-probe/layer-error-list.usda"
            or test["command"][3] != "layer-error-list"):
        raise RuntimeError("The actual property case/command/120-second deadline differs.")
    output = args.output.resolve()
    if not output.is_relative_to(root / "artifacts") or output.exists():
        raise ValueError("The owned diagnostic destination is invalid or already used.")
    output.mkdir(parents=True)
    identity = {
        "diagnosticOnly": True, "checkoutSource": subprocess.check_output(
            ["git", "rev-parse", "HEAD"], cwd=root, text=True).strip(),
        "sourceHead": os.environ.get("OPENUSD_OBSERVATION_SOURCE_HEAD"),
        "producerRun": os.environ.get("GITHUB_RUN_ID"),
        "producerAttempt": os.environ.get("GITHUB_RUN_ATTEMPT"), "rid": "osx-arm64",
        "repository": os.environ.get("GITHUB_REPOSITORY"),
        "repositoryId": os.environ.get("GITHUB_REPOSITORY_ID"),
        "ref": os.environ.get("GITHUB_REF"),
        "originalCommand": command, "originalCase": test["name"],
        "originalDeadlineSeconds": 120, "observationAfterSeconds": 60,
        "observerDeadlineSeconds": 5, "maximumFrames": 32, "maximumReceiptBytes": 65536,
        "files": {str(path.resolve()): digest(path) for path in files},
    }
    write_receipt(output / "before.json", identity)
    process = subprocess.Popen(command, cwd=root, start_new_session=True)
    subject = None
    observed = False
    began = None
    observation_error = None
    try:
        while process.poll() is None:
            if not observed:
                try:
                    current = owned_subject(process.pid, executable.resolve(), test["command"])
                except (OSError, RuntimeError, subprocess.SubprocessError) as error:
                    observation_error = type(error).__name__
                    print("MACOS_ADMISSION_DISCOVERY_UNAVAILABLE: " + observation_error, file=sys.stderr)
                    observed = True
                    current = None
                if current is not None and subject is None:
                    subject, began = current, time.monotonic()
                if subject is not None and current == subject and time.monotonic() - began >= 60:
                    observed = True
                    try:
                        config = {
                            "pid": subject, "executable": str(executable.resolve()),
                            "output": str(output / "main-thread.json"), "files": identity["files"],
                            "ownerSession": process.pid,
                            "processStarted": subprocess.check_output(
                                ["/bin/ps", "-p", str(subject), "-o", "lstart="],
                                text=True, timeout=2).strip(),
                        }
                        config_path = output / "capture-config.json"
                        write_receipt(config_path, config)
                        env = os.environ.copy()
                        env["OPENUSD_OWN_ADMISSION_CAPTURE"] = str(config_path)
                        subprocess.run([
                            "/usr/bin/lldb", "--batch", "-o",
                            'command script import "' + str(Path(__file__).resolve()) + '"',
                            "-o", "command script add -f observe_macos_admission.capture openusd-capture",
                            "-o", "openusd-capture",
                        ], env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                            timeout=5, check=True)
                        if not (output / "main-thread.json").is_file():
                            raise RuntimeError("LLDB did not produce a bounded main-thread receipt.")
                    except (OSError, RuntimeError, subprocess.SubprocessError) as error:
                        observation_error = type(error).__name__
                        print("MACOS_ADMISSION_OBSERVATION_UNAVAILABLE: " + observation_error, file=sys.stderr)
                    finally:
                        try:
                            if owned_subject(process.pid, executable.resolve(), test["command"]) == subject:
                                os.kill(subject, signal.SIGCONT)
                        except (OSError, RuntimeError, subprocess.SubprocessError) as error:
                            observation_error = type(error).__name__
                            print("MACOS_ADMISSION_RESUME_UNVERIFIED: " + observation_error, file=sys.stderr)
            time.sleep(0.5)
        code = process.wait()
        write_receipt(output / "result.json", {
            "diagnosticOnly": True, "originalCtestExit": code,
            "observationAttempted": observed, "observationError": observation_error,
            "originalDeadlineAndSelectorsUnchanged": True,
        })
        return code
    finally:
        if process.poll() is None:
            os.killpg(process.pid, signal.SIGTERM)
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait()


if __name__ == "__main__":
    sys.exit(main())
