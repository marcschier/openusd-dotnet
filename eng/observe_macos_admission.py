"""Observe one owned original CTest child; never change its selector, deadline or verdict."""
import argparse
import hashlib
import json
import os
import re
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
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


def bounded_line(text, limit=240):
    for line in (text or "").splitlines():
        line = "".join(char for char in line.strip() if char.isprintable())
        if line:
            return line[:limit]
    return ""


def main_thread_path(report):
    lines = report.splitlines()
    try:
        first = next(index for index, line in enumerate(lines)
                     if "Thread_" in line and "com.apple.main-thread" in line)
    except StopIteration as error:
        raise RuntimeError("The sampler report has no identified main thread.") from error
    frames = []
    depth = -1
    for line in lines[first + 1:]:
        match = re.match(r"^([\s+!:|]*)(\d+)\s+(.*?)\s+\(in ([^)]+)\)", line)
        if match is None:
            break
        current = len(match.group(1))
        if current <= depth:
            break
        depth = current
        frames.append({"module": match.group(4)[:256], "function": match.group(3)[:768]})
        if len(frames) == 32:
            break
    if not frames:
        raise RuntimeError("The original main-thread heaviest path is empty.")
    return frames


def sample(config, deadline):
    if os.getsid(config["pid"]) != config["ownerSession"]:
        raise RuntimeError("The selected process no longer belongs to the original owned CTest session.")
    started = subprocess.check_output(
        ["/bin/ps", "-p", str(config["pid"]), "-o", "lstart="], text=True, timeout=2).strip()
    if started != config["processStarted"]:
        raise RuntimeError("The selected owned process identity changed before sampling.")
    with tempfile.TemporaryDirectory() as scratch:
        report_path = Path(scratch) / "sample.txt"
        command = ["/usr/bin/sample", str(config["pid"]), "1", "-mayDie", "-file", str(report_path)]
        run = subprocess.run(command, capture_output=True, text=True,
                             timeout=max(1, deadline - time.monotonic()))
        refusal = None
        if run.returncode != 0 or not report_path.is_file():
            refusal = bounded_line(run.stderr) or bounded_line(run.stdout)
            if not re.search(r"task_for_pid|permission|not permitted|privilege|denied|cannot examine", refusal, re.IGNORECASE):
                raise RuntimeError("sample failed: " + refusal)
            if os.getsid(config["pid"]) != config["ownerSession"]:
                raise RuntimeError("The selected process left the owned session before elevated sampling.")
            run = subprocess.run(["/usr/bin/sudo", "-n"] + command, capture_output=True, text=True,
                                 timeout=max(1, deadline - time.monotonic()))
            if run.returncode != 0 or not report_path.is_file():
                raise RuntimeError("sample refused (" + refusal + "); sudo sample failed: "
                                   + (bounded_line(run.stderr) or bounded_line(run.stdout)))
        report = report_path.read_text(errors="replace")
    frames = main_thread_path(report)
    images = report[report.find("Binary Images:"):] if "Binary Images:" in report else ""
    loaded = {}
    for path, expected in config["files"].items():
        if path in images:
            if digest(Path(path)) != expected:
                raise RuntimeError("An actual loaded owned module differs from its bound file.")
            loaded[path] = expected
    return {
        "diagnosticOnly": True, "pid": config["pid"], "sampler": "/usr/bin/sample 1s",
        "elevatedSampler": refusal is not None, "samplerRefusal": refusal,
        "mainThreadHeaviestPath": frames, "loadedOwnedFiles": loaded,
        "loadedClosureComplete": set(loaded) == set(config["files"]),
        "noArgumentsLocalsRegistersAddressesOtherThreadsRawReportOrDump": True,
    }


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
        "observer": "/usr/bin/sample 1s main-thread heaviest path",
        "observerDeadlineSeconds": 20, "maximumFrames": 32, "maximumReceiptBytes": 65536,
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
                            "files": identity["files"],
                            "ownerSession": process.pid,
                            "processStarted": subprocess.check_output(
                                ["/bin/ps", "-p", str(subject), "-o", "lstart="],
                                text=True, timeout=2).strip(),
                        }
                        config_path = output / "capture-config.json"
                        write_receipt(config_path, config)
                        write_receipt(output / "main-thread.json",
                                      sample(config, time.monotonic() + 20))
                    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
                        observation_error = type(error).__name__ + ": " + bounded_line(str(error), 600)
                        print("MACOS_ADMISSION_OBSERVATION_UNAVAILABLE: " + observation_error, file=sys.stderr)
                    finally:
                        try:
                            if owned_subject(process.pid, executable.resolve(), test["command"]) == subject:
                                os.kill(subject, signal.SIGCONT)
                        except (OSError, RuntimeError, subprocess.SubprocessError) as error:
                            observation_error = (observation_error or "") + "; resume " + type(error).__name__
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
