#!/usr/bin/env python3
"""Private installed-debugger fallback; a replacement CI action needs separate approval."""
import hashlib
import errno
import json
import os
from pathlib import Path, PurePosixPath
import resource
import selectors
import shutil
import signal
import stat
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from datetime import datetime, timezone
from contextlib import contextmanager

SOURCE = "b905ec0a04f75de6eb4cfd198707ddf224d1b492"
ARTIFACT = 12100369571
RUN = 39030881472
ZIP_BYTES = 283059814
ZIP_SHA = "207036d60ffae99a299fa0d82f849da64d7b6c75062e09f7f036518a625a1c6f"
PREFIX = "artifacts/platform-smoke/linux-x11/viewer/"
STAGE_SHA = "842943d60c14ddb0cd4552f888520125317be626a2b45d17844787f321da3d83"
ARCHIVE_MEMBERS, VIEWER_MEMBERS, VIEWER_BYTES = 1440, 736, 634372440
LIMIT = 65536
SECONDS = 5
INNER_SECONDS = 4
LLDB_LOCATIONS = (
    ("lldb-18", "/usr/bin/lldb-18", "/usr/lib/llvm-18/bin/lldb-server"),
    ("lldb-17", "/usr/bin/lldb-17", "/usr/lib/llvm-17/bin/lldb-server"),
    ("lldb-16", "/usr/bin/lldb-16", "/usr/lib/llvm-16/bin/lldb-server"),
    ("swift-lldb", "/usr/share/swift/usr/bin/lldb", "/usr/share/swift/usr/bin/lldb-server"),
)
LLDB_SETTINGS = (
    "settings set target.load-script-from-symbol-file false",
    "settings set symbols.enable-external-lookup false",
    "settings set target.disable-aslr false",
)
EXPECTED_PACKAGES = {"libgl1-mesa-dri": "25.2.8-0ubuntu0.24.04.4", "xvfb": "2:21.1.12-1ubuntu1.8"}
HISTORICAL = {"image": "ubuntu-24.04", "imageVersion": "20261012.334.1",
              "installedRuntime": "10.0.13", "packages": EXPECTED_PACKAGES,
              "mappedHostHashesAndInheritedEnvironment": "NOT RECORDED"}
SAFE_OPERATIONS = {
    "preflight": {"preflight-validate", "read-os-release", "find-required-tools", "find-debugger-candidates",
                  "package-version", "runtime-list", "runtime-layout", "hash-selected-debugger",
                  "debugger-capability", "installed-tool-command"},
    "owned-cleanup": {"proc-enumerate", "proc-session-stat", "proc-owned-identity",
                      "proc-owned-identity-mismatch", "proc-owned-signal", "proc-owned-wait"},
}
CLEANUP_TOTALS = {"calls": 0, "unrelatedSessionSkipped": 0, "unrelatedExeNotRead": 0,
                  "races": 0, "ownedObserved": 0, "ownedSignalled": 0,
                  "unknownSessionErrors": 0, "ownedVerificationErrors": 0, "ownedSignalErrors": 0}


class Unavailable(Exception):
    def __init__(self, reason, details=None):
        super().__init__(reason)
        self.details = details or {}


def safe_failure(phase, operation, error):
    if phase not in SAFE_OPERATIONS or operation not in SAFE_OPERATIONS[phase]:
        raise ValueError("Unreviewed diagnostic operation label.")
    allowed_types = {"PermissionError", "FileNotFoundError", "ProcessLookupError", "OSError", "ValueError",
                     "KeyError", "IndexError", "TimeoutExpired", "SubprocessError"}
    kind = type(error).__name__
    number = getattr(error, "errno", None)
    number = number if type(number) is int and 0 <= number <= 4096 else None
    return {"phase": phase, "operation": operation,
            "errorType": kind if kind in allowed_types else "OtherDiagnosticError",
            "errno": number, "errorCode": errno.errorcode.get(number, "NO_ERRNO")}


class DiagnosticOperationError(Exception):
    def __init__(self, failure, cleanup=None):
        super().__init__("Known diagnostic operation failed; unsafe exception details omitted.")
        self.failure, self.cleanup = failure, cleanup


@contextmanager
def safe_operation(phase, operation):
    try:
        yield
    except DiagnosticOperationError:
        raise
    except (OSError, ValueError, KeyError, IndexError, subprocess.SubprocessError) as error:
        raise DiagnosticOperationError(safe_failure(phase, operation, error)) from None


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def identity(pid):
    fields = Path(f"/proc/{pid}/stat").read_text().rsplit(")", 1)[1].split()
    return {"pid": pid, "start": fields[19], "session": int(fields[3]),
            "exe": os.path.realpath(f"/proc/{pid}/exe")}


def same_process(expected):
    try:
        return identity(expected["pid"]) == expected
    except (FileNotFoundError, ProcessLookupError):
        return False


def process_session_stat(pid):
    fields = Path(f"/proc/{pid}/stat").read_text().rsplit(")", 1)[1].split()
    return {"pid": pid, "start": fields[19], "session": int(fields[3])}


def cleanup_session(owner):
    # Filter unrelated sessions before touching protected executable links.
    report = {key: 0 for key in CLEANUP_TOTALS}
    report["calls"] = 1
    failures, owned = [], []

    def failed(counter, operation, error):
        report[counter] += 1
        if len(failures) < 8:
            failures.append(safe_failure("owned-cleanup", operation, error))

    try:
        for entry in Path("/proc").iterdir():
            if not entry.name.isdigit():
                continue
            pid = int(entry.name)
            try:
                observed = process_session_stat(pid)
            except (FileNotFoundError, ProcessLookupError):
                report["races"] += 1
                continue
            except (OSError, ValueError, IndexError) as error:
                failed("unknownSessionErrors", "proc-session-stat", error)
                continue
            if observed["session"] != owner:
                report["unrelatedSessionSkipped"] += 1
                report["unrelatedExeNotRead"] += 1
                continue
            try:
                current = identity(pid)
            except (FileNotFoundError, ProcessLookupError):
                report["races"] += 1
                continue
            except (OSError, ValueError, IndexError) as error:
                failed("ownedVerificationErrors", "proc-owned-identity", error)
                continue
            if any(current[key] != observed[key] for key in ("pid", "start", "session")):
                report["races"] += 1
                continue
            report["ownedObserved"] += 1
            owned.append(current)
    except OSError as error:
        failed("unknownSessionErrors", "proc-enumerate", error)
    for current in sorted(owned, key=lambda item: item["pid"] == owner):
        try:
            observed = process_session_stat(current["pid"])
            if any(observed[key] != current[key] for key in ("start", "session")):
                report["races"] += 1
                continue
            if identity(current["pid"]) != current:
                failed("ownedVerificationErrors", "proc-owned-identity-mismatch", ValueError())
                continue
        except (FileNotFoundError, ProcessLookupError):
            report["races"] += 1
            continue
        except (OSError, ValueError, IndexError) as error:
            failed("ownedVerificationErrors", "proc-owned-identity", error)
            continue
        try:
            os.kill(current["pid"], signal.SIGKILL)
            report["ownedSignalled"] += 1
        except ProcessLookupError:
            report["races"] += 1
        except OSError as error:
            failed("ownedSignalErrors", "proc-owned-signal", error)
    for key in CLEANUP_TOTALS:
        CLEANUP_TOTALS[key] += report[key]
    if failures:
        raise DiagnosticOperationError(failures[0], dict(report, failures=failures))
    return report


def command_text(arguments, timeout=2, cap=8192, operation="installed-tool-command"):
    with safe_operation("preflight", operation):
        return _command_text(arguments, timeout, cap)


def _command_text(arguments, timeout, cap):
    process = subprocess.Popen(arguments, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                               start_new_session=True)
    result = bytearray()
    deadline = time.monotonic() + timeout
    try:
        with selectors.DefaultSelector() as selector:
            selector.register(process.stdout, selectors.EVENT_READ)
            while selector.get_map() and time.monotonic() < deadline:
                for key, _ in selector.select(0.02):
                    chunk = os.read(key.fd, 4096)
                    if not chunk:
                        selector.unregister(key.fileobj)
                    else:
                        result.extend(chunk[:max(0, cap + 1 - len(result))])
        if process.poll() is None:
            process.wait(timeout=max(0.001, deadline - time.monotonic()))
        if process.returncode or len(result) > cap:
            raise Unavailable("installed-tool-command-failed-or-output-limit")
        return result.decode("utf-8", errors="strict")
    except subprocess.TimeoutExpired:
        raise Unavailable("installed-tool-command-timeout", {"commandTimeoutSeconds": timeout, "operation": "no-target-capability" if timeout == 10 else "preflight-command"}) from None
    finally:
        cleanup_error = None
        try:
            cleanup_session(process.pid)
        except DiagnosticOperationError as error:
            cleanup_error = error
        finally:
            process.stdout.close()
            try:
                process.wait(timeout=2)
            except (OSError, subprocess.SubprocessError) as error:
                failure = safe_failure("owned-cleanup", "proc-owned-wait", error)
                if cleanup_error is None:
                    cleanup_error = DiagnosticOperationError(failure)
                else:
                    cleanup_error.cleanup = dict(cleanup_error.cleanup or {}, postCleanupWaitFailure=failure)
        if cleanup_error is not None:
            raise cleanup_error


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *arguments):
        return None


def acquire_original(destination, token):
    api = "https://api.github.com/repos/marcschier/openusd-dotnet/actions/artifacts/"
    headers = {"Authorization": "Bearer " + token, "Accept": "application/vnd.github+json"}
    with urllib.request.urlopen(urllib.request.Request(api + str(ARTIFACT), headers=headers), timeout=30) as response:
        metadata = json.loads(response.read(32769))
    if (metadata["expired"] or metadata["workflow_run"]["id"] != RUN or
            metadata["workflow_run"]["head_sha"] != SOURCE or metadata["size_in_bytes"] != ZIP_BYTES or
            metadata["name"] != f"render-linux-presentation-{RUN}" or metadata["digest"] != "sha256:" + ZIP_SHA):
        raise Unavailable("source-bound-original-artifact-metadata-mismatch")
    try:
        urllib.request.build_opener(NoRedirect()).open(
            urllib.request.Request(api + str(ARTIFACT) + "/zip", headers=headers), timeout=30)
    except urllib.error.HTTPError as error:
        if error.code != 302:
            raise Unavailable("original-artifact-download-unavailable") from None
        url = error.headers["Location"]
    else:
        raise Unavailable("original-artifact-redirect-missing")
    parsed = urllib.parse.urlsplit(url)
    if parsed.scheme != "https" or not parsed.hostname or not parsed.hostname.endswith(
            (".blob.core.windows.net", ".actions.githubusercontent.com")):
        raise Unavailable("original-artifact-redirect-host-refused")
    archive_path = destination / "original.zip"
    with urllib.request.urlopen(url, timeout=30) as response, archive_path.open("xb") as stream:
        remaining = ZIP_BYTES
        while remaining:
            chunk = response.read(min(1048576, remaining))
            if not chunk:
                raise Unavailable("original-artifact-short-download")
            stream.write(chunk)
            remaining -= len(chunk)
        if response.read(1):
            raise Unavailable("original-artifact-oversize")
    if digest(archive_path) != ZIP_SHA:
        raise Unavailable("original-artifact-outer-digest-mismatch")
    return archive_path


def extract_original(archive_path, viewer):
    entries = []
    excluded = {"viewer-status.txt", "viewer.log", "viewer.stdout.log", "viewer.stderr.log"}
    with zipfile.ZipFile(archive_path) as archive:
        members = archive.infolist()
        selected = [item for item in members if item.filename.startswith(PREFIX)]
        if (len(members) != ARCHIVE_MEMBERS or len(selected) != VIEWER_MEMBERS or
                sum(item.file_size for item in selected) != VIEWER_BYTES):
            raise Unavailable("original-archive-inventory-mismatch")
        seen = set()
        for item in selected:
            relative = PurePosixPath(item.filename[len(PREFIX):])
            mode = item.external_attr >> 16
            if (not relative.parts or relative.is_absolute() or ".." in relative.parts or
                    "\\" in item.filename or str(relative) in seen or item.is_dir() or
                    stat.S_ISLNK(mode) or item.flag_bits & 1):
                raise Unavailable("original-member-path-or-kind-refused")
            seen.add(str(relative))
            if str(relative) in excluded:
                continue
            target = viewer.joinpath(*relative.parts)
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(item) as source, target.open("xb") as stream:
                shutil.copyfileobj(source, stream, length=1048576)
            if target.stat().st_size != item.file_size:
                raise Unavailable("original-member-length-mismatch")
            entries.append((str(relative), digest(target)))
            if str(relative) == "OpenUsd.Viewer.App":
                stamp = datetime(*item.date_time, tzinfo=timezone.utc).timestamp()
                os.utime(target, (stamp, stamp))
    if digest(viewer / "minimal.usda") != STAGE_SHA or len(entries) != VIEWER_MEMBERS - len(excluded):
        raise Unavailable("original-stage-or-extracted-count-mismatch")
    (viewer / "OpenUsd.Viewer.App").chmod(0o700)
    return entries


def fingerprint(viewer, entries):
    hashes = [(relative, digest(viewer.joinpath(*PurePosixPath(relative).parts))) for relative, _ in entries]
    return hashlib.sha256(json.dumps(sorted(hashes), separators=(",", ":")).encode()).hexdigest()


def installed_debuggers():
    candidates, checked = [], ["gdb"]
    gdb = shutil.which("gdb")
    if gdb:
        candidates.append({"kind": "gdb", "path": gdb, "server": None})
    seen = set()
    for name, known_path, server in LLDB_LOCATIONS:
        checked.append(name)
        path = shutil.which(name) if name != "swift-lldb" else None
        if path is None and os.path.isfile(known_path) and os.access(known_path, os.X_OK):
            path = known_path
        if path and os.path.realpath(path) not in seen:
            seen.add(os.path.realpath(path))
            candidates.append({"kind": "lldb", "path": path, "server": server})
    return candidates, checked


def debugger_capability(candidate):
    if candidate["kind"] == "gdb":
        arguments = [candidate["path"], "--nx", "--nh", "--batch",
                     "-iex", "set auto-load off", "-iex", "set debuginfod enabled off",
                     "-ex", 'python print("B905_GDB_PYTHON_OK")', "-ex", "show version"]
        marker = "B905_GDB_PYTHON_OK"
    else:
        if not os.path.isfile(candidate["server"]) or not os.access(candidate["server"], os.X_OK):
            raise Unavailable("installed-lldb-server-unavailable", {"missingTools": [candidate["server"]]})
        arguments = [candidate["path"], "--no-lldbinit", "--batch"]
        for setting in LLDB_SETTINGS:
            arguments.extend(("--one-line-before-file", setting))
        arguments.extend(("--one-line", 'script import lldb; '
                          'assert hasattr(lldb.SBThread,"GetFrameAtIndex") and '
                          'hasattr(lldb.SBLaunchInfo,"AddSuppressFileAction"); '
                          'print("B905_LLDB_PYTHON_OK"); '
                          'print("B905_LLDB_VERSION="+lldb.SBDebugger.GetVersionString())'))
        marker = "B905_LLDB_PYTHON_OK"
    text = command_text(arguments, timeout=10, operation="debugger-capability")
    if marker not in text:
        raise Unavailable("installed-debugger-python-capability-unavailable")
    versions = [line.removeprefix("B905_LLDB_VERSION=") for line in text.splitlines()
                if line.startswith(("B905_LLDB_VERSION=", "GNU gdb"))]
    return {"pythonApiSupported": True, "versionText": versions[0] if versions else None,
            "versionEvidence": "installed-tool-capability-command" if versions else "not-reported"}


def preflight():
    with safe_operation("preflight", "preflight-validate"):
        return _preflight()


def _preflight():
    details = {"toolPreflight": {}, "versionPreflight": {"status": "not-run"}}
    with safe_operation("preflight", "read-os-release"):
        os_release = Path("/etc/os-release").read_text()
    if 'ID=ubuntu' not in os_release or 'VERSION_ID="24.04"' not in os_release:
        raise Unavailable("required-installed-ubuntu-24.04-unavailable", details)
    with safe_operation("preflight", "find-required-tools"):
        tools = {name: shutil.which(name) for name in ("dotnet", "Xvfb", "dpkg-query")}
    with safe_operation("preflight", "find-debugger-candidates"):
        candidates, checked = installed_debuggers()
    missing = [name for name, path in tools.items() if path is None]
    details["toolPreflight"] = {"paths": tools.copy(), "missingTools": missing,
                                "debuggerAlternativesChecked": checked,
                                "installedDebuggerCandidates": candidates}
    if missing or not candidates:
        reason = "required-already-installed-tools-unavailable" if missing else "no-approved-installed-debugger"
        raise Unavailable(reason, details)
    packages = {}
    for name, expected in EXPECTED_PACKAGES.items():
        details["versionPreflight"].update(status="checking-packages", package=name)
        try:
            actual = command_text([tools["dpkg-query"], "-W", "-f=${Version}", name],
                                  operation="package-version").strip()
        except Unavailable as error:
            raise Unavailable(str(error), details) from None
        details["versionPreflight"].update(status="checking-packages", package=name,
                                           expected=expected, actual=actual)
        if actual != expected:
            raise Unavailable("required-installed-package-version-unavailable:" + name, details)
        packages[name] = actual
    details["versionPreflight"]["status"] = "checking-runtime"
    try:
        runtimes = command_text([tools["dotnet"], "--list-runtimes"], operation="runtime-list")
    except Unavailable as error:
        raise Unavailable(str(error), details) from None
    matches = [line for line in runtimes.splitlines() if line.startswith("Microsoft.NETCore.App 10.0.")]
    details["versionPreflight"] = {"status": "checking-runtime", "packages": packages,
                                   "requiredRuntime": "10.0.13",
                                   "installedMatchingVersions": [line.split()[1] for line in matches]}
    if not matches or any(line.split()[1] != "10.0.13" for line in matches):
        raise Unavailable("required-installed-runtime-10.0.13-only-unavailable", details)
    with safe_operation("preflight", "runtime-layout"):
        runtime_root = Path(matches[0].split("[", 1)[1].rstrip("]")).parent.parent
        if not (runtime_root / "shared/Microsoft.NETCore.App/10.0.13/libcoreclr.so").is_file():
            raise Unavailable("installed-runtime-layout-unavailable", details)
    details["versionPreflight"]["status"] = "passed"
    failures = []
    selected = None
    for candidate in candidates:
        try:
            with safe_operation("preflight", "debugger-capability"):
                capability = debugger_capability(candidate)
        except Unavailable as error:
            failures.append(dict(candidate, reason=str(error), details=error.details))
            continue
        selected = dict(candidate, capability=capability)
        break
    if selected is None:
        details["debuggerCapabilityFailures"] = failures
        raise Unavailable("installed-debugger-capabilities-unavailable", details)
    tools.update(debugger=selected["path"], debuggerKind=selected["kind"], debuggerServer=selected["server"])
    with safe_operation("preflight", "hash-selected-debugger"):
        selected = dict(selected, sha256=digest(Path(selected["path"]).resolve()))
        if selected["server"]:
            selected["serverSha256"] = digest(Path(selected["server"]).resolve())
    return tools, runtime_root, {
        "kind": "RECONSTRUCTED HOST, NOT HISTORICAL IDENTITY",
        "imageOS": os.environ.get("ImageOS"), "imageVersion": os.environ.get("ImageVersion"),
        "runtimeRoot": str(runtime_root), "runtimeVersion": "10.0.13", "packages": packages,
        "selectedDebugger": selected, "preflightDetails": details,
        "earlierDebuggerCapabilityFailures": failures}


def start_display(xvfb, environment):
    read_fd, write_fd = os.pipe()
    process = None
    try:
        process = subprocess.Popen(
            [xvfb, "-displayfd", str(write_fd), "-screen", "0", "1280x720x24", "-ac", "-nolisten", "tcp"],
            env=environment, pass_fds=(write_fd,), stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
            start_new_session=True)
        os.close(write_fd)
        write_fd = None
        deadline = time.monotonic() + 3
        raw = bytearray()
        with selectors.DefaultSelector() as selector:
            selector.register(read_fd, selectors.EVENT_READ)
            while time.monotonic() < deadline:
                if process.poll() is not None:
                    raise Unavailable("owned-xvfb-exited-before-displayfd-readiness")
                for _, _ in selector.select(min(0.05, max(0, deadline - time.monotonic()))):
                    chunk = os.read(read_fd, 65 - len(raw))
                    if not chunk:
                        raise Unavailable("owned-xvfb-displayfd-eof-before-readiness")
                    raw.extend(chunk)
                    if len(raw) > 64:
                        raise Unavailable("owned-xvfb-displayfd-output-limit")
                    if raw.endswith(b"\n"):
                        value = bytes(raw[:-1])
                        if not value.isdigit() or not 0 <= int(value) <= 65535:
                            raise Unavailable("owned-xvfb-displayfd-invalid-number")
                        return process, ":" + value.decode("ascii")
        raise Unavailable("owned-xvfb-displayfd-readiness-timeout")
    except (OSError, Unavailable):
        if process is not None:
            cleanup_session(process.pid)
            process.wait(timeout=2)
        raise
    finally:
        os.close(read_fd)
        if write_fd is not None:
            os.close(write_fd)


def debugger_script(viewer, owned, fault):
    # Walk ONLY the stopped signal thread's frames. Never request arguments, locals, other threads or dumps.
    return (
        "set pagination off\nset confirm off\nset startup-with-shell off\n"
        "set auto-load off\nset debuginfod enabled off\nset print frame-arguments none\n"
        "set print entry-values no\nset disable-randomization off\n"
        "set detach-on-fork off\nhandle SIGABRT stop noprint nopass\n"
        "python\nimport gdb,json,os,time\n"
        f"owned_path={str(owned)!r}\nfault_path={str(fault)!r}\n"
        "def save(path, value):\n"
        " data=json.dumps(value,ensure_ascii=True).encode()\n"
        " if len(data)>60000: data=json.dumps({'status':'truncated','frames':[]}).encode()\n"
        " with open(path+'.tmp','xb') as stream: stream.write(data)\n"
        " os.replace(path+'.tmp',path)\n"
        "def record(event):\n"
        " pid=gdb.selected_inferior().pid\n"
        " if pid:\n"
        "  fields=open('/proc/%d/stat'%pid).read().rsplit(')',1)[1].split()\n"
        "  save(owned_path,{'pid':pid,'start':fields[19],'session':int(fields[3]),\n"
        "                   'exe':os.path.realpath('/proc/%d/exe'%pid)})\n"
        "def stopped(event):\n"
        " if not isinstance(event,gdb.SignalEvent) or event.stop_signal!='SIGABRT': return\n"
        " record(event)\n"
        " thread=gdb.selected_thread()\n"
        " if thread is None: return\n"
        " if getattr(event,'inferior_thread',None) not in (None,thread): return\n"
        " save(fault_path+'.started',{'signal':'SIGABRT'})\n"
        " frames=[]\n"
        " frame=gdb.newest_frame()\n"
        " while frame is not None and len(frames)<32:\n"
        "  item={'pc':hex(frame.pc()),'function':(frame.name() or '??')[:384]}\n"
        "  line=frame.find_sal()\n"
        "  if line.symtab: item.update(file=line.symtab.filename[:384],line=line.line)\n"
        "  frames.append(item)\n"
        "  frame=frame.older()\n"
        " pid=gdb.selected_inferior().pid\n"
        " paths=sorted({line.split(None,5)[5].strip() for line in open('/proc/%d/maps'%pid)\n"
        "               if len(line.split(None,5))==6 and line.split(None,5)[5].startswith('/')})\n"
        " save(fault_path,{'status':'fault','signal':'SIGABRT','pid':pid,\n"
        "                  'thread':thread.global_num,'frames':frames,'mappedPaths':paths,\n"
        "                  'capturedMonotonic':time.monotonic()})\n"
        "gdb.events.new_thread.connect(record)\ngdb.events.stop.connect(stopped)\nend\nrun\n")


def process_has_terminated(expected):
    try:
        fields = Path(f"/proc/{expected['pid']}/stat").read_text().rsplit(")", 1)[1].split()
    except (FileNotFoundError, ProcessLookupError):
        return True
    # An exec/image change is NOT termination. Require exit/death or proof this PID was reused.
    return fields[19] != expected["start"] or fields[0] in ("Z", "X")


def terminate_replay(debugger, debugger_identity, owned, viewer, started, helper_path=None):
    ended, errors = {}, []
    inferior = helper = None
    required = {"inferior", "debugger"}
    try:
        candidate = {}
        if owned.is_file() and owned.stat().st_size <= 4096:
            candidate = json.loads(owned.read_bytes())
            if (isinstance(candidate.get("pid"), int) and candidate["pid"] > 0 and
                    isinstance(candidate.get("start"), str) and candidate["start"].isdigit() and
                    candidate.get("session") == debugger.pid and
                    candidate.get("exe") == str(viewer / "OpenUsd.Viewer.App")):
                inferior = candidate
        if helper_path is not None:
            marker_path = Path(str(owned) + ".debugger-helper")
            marker = json.loads(marker_path.read_bytes()) if (
                marker_path.is_file() and marker_path.stat().st_size <= 4096) else {}
            if marker.get("kind") == "tracked":
                proxy = marker.get("identity", {})
                if (marker.get("debuggerPid") == marker.get("helperParent") == debugger.pid and
                        marker.get("inferiorPid") == candidate.get("pid") and
                        isinstance(proxy.get("pid"), int) and proxy["pid"] > 0 and
                        isinstance(proxy.get("start"), str) and proxy["start"].isdigit() and
                        proxy.get("exe") == os.path.realpath(helper_path) and
                        isinstance(candidate.get("pid"), int) and candidate["pid"] > 0 and
                        candidate.get("exe") == str(viewer / "OpenUsd.Viewer.App") and
                        isinstance(candidate.get("start"), str) and candidate["start"].isdigit()):
                    helper, inferior = proxy, candidate
                    required.add("debuggerHelper")
                else:
                    errors.append("owned-lldb-helper-lineage-unverified")
            elif marker.get("kind") != "direct":
                errors.append("owned-lldb-helper-identity-unavailable")
        if inferior is None:
            errors.append("owned-inferior-identity-unavailable")
        # Signal the already tracked inferior FIRST; do not scan /proc before stopping it.
        for expected in (inferior, helper, debugger_identity):
            try:
                if expected is not None and same_process(expected):
                    os.kill(expected["pid"], signal.SIGKILL)
            except ProcessLookupError:
                pass
            except OSError as error:
                errors.append("tracked-signal-error:" + type(error).__name__)
        if debugger_identity is None and debugger.poll() is None:
            errors.append("owned-debugger-identity-unavailable")
        while True:
            if inferior is not None and "inferior" not in ended and process_has_terminated(inferior):
                ended["inferior"] = time.monotonic() - started
            if "debugger" not in ended and debugger.poll() is not None:
                ended["debugger"] = time.monotonic() - started
            if helper is not None and "debuggerHelper" not in ended and process_has_terminated(helper):
                ended["debuggerHelper"] = time.monotonic() - started
            elapsed = time.monotonic() - started
            if required.issubset(ended) or (inferior is None and "debugger" in ended):
                break
            if elapsed >= SECONDS:
                errors.append("tracked-process-termination-not-observed-within-five-seconds")
                break
            time.sleep(min(0.005, SECONDS - elapsed))
    except (OSError, ValueError, KeyError) as error:
        errors.append("tracked-cleanup-error:" + type(error).__name__)
    measured = max(ended.values()) if set(ended) == required else None
    bounded = measured is not None and measured <= SECONDS and not errors
    if measured is not None and measured > SECONDS:
        errors.append("actual-tracked-process-termination-overrun")
    return {"innerReplaySeconds": INNER_SECONDS, "terminationElapsedSeconds": measured,
            "ownedInferiorVerified": inferior is not None,
            "inferiorTerminationElapsedSeconds": ended.get("inferior"),
            "debuggerTerminationElapsedSeconds": ended.get("debugger"),
            "debuggerHelperTerminationElapsedSeconds": ended.get("debuggerHelper"),
            "terminationWithinFiveSeconds": bounded, "constraintFailureReasons": errors}



def dotnet_timestamp(seconds):
    value = datetime.fromtimestamp(seconds, timezone.utc)
    return value.strftime("%Y-%m-%dT%H:%M:%S.") + f"{value.microsecond:06d}0+00:00"


def loader_listing(viewer, extra_root=None):
    # --list resolves DT_NEEDED only; it does not execute the library's constructors or the target.
    import re
    loader = "/lib64/ld-linux-x86-64.so.2"
    library = viewer / "lib/libopenusd_storm_child.so"
    if not Path(loader).is_file() or not library.is_file():
        raise Unavailable("owned-static-loader-input-unavailable")
    paths = [str(viewer / "lib"), str(viewer / "bin")]
    if extra_root is not None:
        paths.insert(0, str(extra_root / "usr/lib/x86_64-linux-gnu"))
    environment = {"PATH": os.environ["PATH"], "LD_LIBRARY_PATH": ":".join(paths)}
    process = subprocess.Popen([loader, "--list", str(library)], env=environment, stdout=subprocess.PIPE,
                               stderr=subprocess.STDOUT, start_new_session=True)
    raw = bytearray()
    deadline = time.monotonic() + 5
    try:
        with selectors.DefaultSelector() as selector:
            selector.register(process.stdout, selectors.EVENT_READ)
            while selector.get_map() and time.monotonic() < deadline:
                for key, _ in selector.select(0.02):
                    data = os.read(key.fd, 4096)
                    if not data:
                        selector.unregister(key.fileobj)
                    else:
                        raw.extend(data[:max(0, 32769 - len(raw))])
        process.wait(timeout=max(0.001, deadline - time.monotonic()))
        if len(raw) > 32768:
            raise Unavailable("static-loader-output-limit")
        output = raw.decode("utf-8", errors="replace")
        missing = sorted(set(re.findall(r"([A-Za-z0-9_+.-]+\.so(?:\.[0-9]+)*): cannot open shared object file", output)))
        if process.returncode and not missing:
            raise Unavailable("static-loader-resolution-error", {"exitCode": process.returncode})
        resolved = sorted(set(re.findall(r"^\s*([A-Za-z0-9_+.-]+\.so(?:\.[0-9]+)*)\s+=>", output, re.MULTILINE)))
        return {"status": "missing" if missing else "resolved", "missingLibraries": missing,
                "resolvedLibraryBasenames": resolved, "targetConstructorsOrRendererExecuted": False}
    except subprocess.TimeoutExpired:
        raise Unavailable("static-loader-preflight-timeout") from None
    finally:
        cleanup_session(process.pid)
        process.stdout.close()
        process.wait(timeout=2)


def prepare_loader_closure(viewer, temporary):
    result = loader_listing(viewer)
    if result["status"] == "resolved":
        return result
    if result["missingLibraries"] != ["libOpenGL.so.0"]:
        raise Unavailable("original-host-library-dependencies-unavailable", result)
    # The original failed run installed this exact Ubuntu Noble GLVND package.
    url = "https://archive.ubuntu.com/ubuntu/pool/main/libg/libglvnd/libopengl0_1.7.0-1build1_amd64.deb"
    expected = "da36bd4d30c13b40b697fa8b72cc09d309ff36a5bd9750341ef78492bf3f3966"
    extractor = shutil.which("dpkg-deb")
    if extractor is None:
        raise Unavailable("already-installed-dpkg-deb-unavailable")
    archive = temporary / "original-libopengl0.deb"
    with urllib.request.urlopen(url, timeout=30) as response:
        if response.geturl() != url:
            raise Unavailable("private-original-host-library-redirect-refused")
        data = response.read(1048577)
    if len(data) > 1048576 or hashlib.sha256(data).hexdigest() != expected:
        raise Unavailable("exact-original-host-library-integrity-mismatch")
    archive.write_bytes(data)
    private = temporary / "private-original-host-libraries"
    command_text([extractor, "--extract", str(archive), str(private)])
    library = private / "usr/lib/x86_64-linux-gnu/libOpenGL.so.0"
    if not library.is_file() or not library.resolve().is_relative_to(private.resolve()):
        raise Unavailable("exact-private-host-library-layout-invalid")
    after = loader_listing(viewer, private)
    if after["status"] != "resolved":
        raise Unavailable("remaining-original-host-dependencies-unavailable", after)
    after.update(privateHostLibraryRoot=str(private), library="libOpenGL.so.0", packageVersion="1.7.0-1build1",
                 packageSha256=expected, librarySha256=digest(library), acquisition="Private data extraction; no global installation/scripts")
    return after

def replay(viewer, workspace, temporary, tools, runtime_root, result):
    display_process = None
    debugger = None
    result.update(status="unavailable", replayCount=0, debuggerLaunchRequests=0)
    environment = {key: os.environ[key] for key in (
        "PATH", "HOME", "LANG", "LC_ALL", "TMPDIR", "XDG_RUNTIME_DIR",
        "DBUS_SESSION_BUS_ADDRESS", "SESSION_MANAGER") if key in os.environ}
    environment.update({
        "PATH": str(viewer / "bin") + ":" + str(viewer / "lib") + ":" + str(viewer) + ":" + os.environ["PATH"],
        "LD_LIBRARY_PATH": str(viewer / "lib") + ":" + str(viewer / "bin"),
        "DOTNET_ROOT": str(runtime_root), "DOTNET_ROOT_X64": str(runtime_root),
        "OPENUSD_PLUGIN_PATH": str(viewer / "plugin/usd"), "OPENUSD_STAGE_PATH": str(viewer / "minimal.usda"),
        "OPENUSD_STATUS_FILE": "/dev/null", "OPENUSD_LOG_FILE": "/dev/null",
        "OPENUSD_SHARED_STAGE_SOAK": "1", "OPENUSD_SOAK_SECONDS": "90",
        "OPENUSD_SOAK_ARTIFACT": str(viewer / "shared-stage-soak.json"),
        "OPENUSD_SOAK_SOURCE_HASH": ZIP_SHA.upper(),
        "OPENUSD_SOAK_EXECUTABLE_HASH": digest(viewer / "OpenUsd.Viewer.App").upper(),
        "OPENUSD_SOAK_EXECUTABLE_TIMESTAMP_UTC": dotnet_timestamp(
            (viewer / "OpenUsd.Viewer.App").stat().st_mtime),
        "OPENUSD_RENDERER": "Storm", "OPENUSD_VIEWER_PLATFORM": "linux-x11",
        "OPENUSD_STORM_NATIVE_WAYLAND": "0", "OPENUSD_DOTNET_OWN_SDK_PHASES": "1",
        "XDG_SESSION_TYPE": "x11", "LIBGL_ALWAYS_SOFTWARE": "1",
        "MESA_GL_VERSION_OVERRIDE": "4.5COMPAT", "MESA_GLSL_VERSION_OVERRIDE": "450",
        "DOTNET_DbgEnableMiniDump": "0", "COMPlus_DbgEnableMiniDump": "0"})
    if "privateHostLibraryRoot" in tools:
        environment["LD_LIBRARY_PATH"] = str(Path(tools["privateHostLibraryRoot"]) / "usr/lib/x86_64-linux-gnu") + ":" + environment["LD_LIBRARY_PATH"]
    environment.pop("WAYLAND_DISPLAY", None)
    for name in ("DOTNET_ROLL_FORWARD", "DOTNET_ROLL_FORWARD_TO_PRERELEASE", "DOTNET_STARTUP_HOOKS",
                 "LD_PRELOAD", "OPENUSD_VIEWER_LINUX_ABORT_OBSERVATION", "OPENUSD_VIEWER_VIEWPORT_OBSERVATION"):
        environment.pop(name, None)
    environment["DEBUGINFOD_URLS"] = ""
    try:
        display_process, environment["DISPLAY"] = start_display(tools["Xvfb"], environment)
        owned, fault = temporary / "owned.json", temporary / "fault.json"
        arguments = ["--shared-stage-soak", "--soak-seconds", "90",
                     "--soak-artifact", str(viewer / "shared-stage-soak.json")]
        debugger_environment = environment
        if tools["debuggerKind"] == "gdb":
            script = temporary / "once.gdb"
            script.write_text(debugger_script(viewer, owned, fault), encoding="utf-8")
            launch = [tools["debugger"], "--nx", "--nh", "--batch", "-iex", "set auto-load off",
                      "-iex", "set debuginfod enabled off", "--command=" + str(script), "--args",
                      str(viewer / "OpenUsd.Viewer.App"), *arguments]
        elif tools["debuggerKind"] == "lldb":
            config = temporary / "lldb-launch.json"
            with config.open("x", encoding="utf-8") as stream:
                json.dump({"source": SOURCE, "executable": str(viewer / "OpenUsd.Viewer.App"),
                           "arguments": arguments, "workingDirectory": str(workspace),
                           "environment": environment, "owned": str(owned), "fault": str(fault),
                           "server": tools["debuggerServer"]}, stream)
            module = Path(__file__).with_name("capture_lldb_sigabrt.py").resolve()
            launch = [tools["debugger"], "--no-lldbinit", "--batch"]
            for setting in LLDB_SETTINGS:
                launch.extend(("--one-line-before-file", setting))
            launch.extend(("--one-line", "script import lldb,runpy; runpy.run_path(" +
                           json.dumps(str(module)) + ')["capture"](lldb.debugger)'))
            debugger_environment = {key: value for key, value in environment.items() if key != "LD_LIBRARY_PATH"}
            debugger_environment.update(PATH=os.environ["PATH"], B905_LLDB_CONFIG=str(config),
                                        LLDB_DEBUGSERVER_PATH=tools["debuggerServer"])
        else:
            raise Unavailable("unreviewed-debugger-backend")
        started = time.monotonic()
        result["debuggerLaunchRequests"] = 1
        debugger = subprocess.Popen(launch, cwd=workspace, env=debugger_environment, stdout=subprocess.DEVNULL,
                                    stderr=subprocess.DEVNULL, start_new_session=True)
        result.update(status="no-fault", replayCount=1, debuggerKind=tools["debuggerKind"])
        try:
            debugger_identity = identity(debugger.pid)
        except (FileNotFoundError, ProcessLookupError):
            debugger_identity = None
        while debugger.poll() is None and not fault.exists():
            remaining = INNER_SECONDS - (time.monotonic() - started)
            if remaining <= 0:
                break
            time.sleep(min(0.005, remaining))
        result["loopElapsedSeconds"] = time.monotonic() - started
        result.update(terminate_replay(debugger, debugger_identity, owned, viewer, started,
                                      tools["debuggerServer"] if tools["debuggerKind"] == "lldb" else None))
        result["innerDeadlineExpired"] = result["loopElapsedSeconds"] >= INNER_SECONDS
        result["debuggerExit"] = debugger.returncode
        result["inferiorStartRecorded"] = owned.is_file()
        if fault.is_file() and fault.stat().st_size <= 60000:
            backend = json.loads(fault.read_bytes())
            result["backendOutcome"] = {key: backend[key] for key in (
                "status", "lldbErrorCode", "killErrorCode", "stopReason") if key in backend}
        if not result["terminationWithinFiveSeconds"]:
            result.update(status="constraint-failure", faultDiscarded=True)
        elif not fault.exists() and Path(str(fault) + ".started").exists():
            result["status"] = "fault-capture-incomplete"
        elif not fault.exists() and debugger.returncode not in (0, -signal.SIGKILL):
            result["status"] = "debugger-error-without-approved-fault"
        elif not fault.exists() and tools["debuggerKind"] == "lldb" and debugger.returncode == 0:
            result["status"] = "lldb-backend-ended-without-outcome"
        elif not fault.exists() and not owned.is_file():
            result["status"] = "inferior-start-unobserved"
        if result["terminationWithinFiveSeconds"] and fault.exists() and fault.stat().st_size <= 60000:
            record = json.loads(fault.read_bytes())
            owned_record = json.loads(owned.read_bytes()) if owned.is_file() else {}
            if (record.get("status") == "fault" and record.get("signal") == "SIGABRT" and
                    len(record.get("frames", [])) <= 32 and owned_record.get("pid") == record["pid"] and
                    result["ownedInferiorVerified"] and
                    owned_record.get("exe") == str(viewer / "OpenUsd.Viewer.App") and
                    0 <= record.get("capturedMonotonic", 0) - started <= SECONDS):
                modules = []
                for value in record.pop("mappedPaths", []):
                    path = Path(value)
                    if path.is_file():
                        modules.append({"path": value, "sha256": digest(path)})
                selected_core = [item for item in modules if item["path"].endswith("/libcoreclr.so")]
                if len(selected_core) != 1 or "/10.0.13/libcoreclr.so" not in selected_core[0]["path"]:
                    result.update(status="selected-runtime-mismatch", faultDiscarded=True)
                else:
                    result.update(status="fault-on-reconstructed-host", fault=record, selectedModules=modules)
            elif record.get("status") == "lldb-exited-without-approved-fault":
                result["status"] = "no-fault"
            else:
                result.update(status="truncated-or-invalid-fault-record")
        elif result["terminationWithinFiveSeconds"] and fault.exists():
            result.update(status="fault-record-output-limit")
        result["reconstructedDisplay"] = {"display": environment["DISPLAY"], "xvfbPid": display_process.pid,
                                          "historicalDisplayIdentity": "NOT RECORDED; NEW INSTANCE"}
        result["newEnvironment"] = {key: environment.get(key) for key in (
            "DOTNET_ROOT", "LD_LIBRARY_PATH", "DISPLAY", "XDG_SESSION_TYPE", "LIBGL_ALWAYS_SOFTWARE",
            "MESA_GL_VERSION_OVERRIDE", "MESA_GLSL_VERSION_OVERRIDE", "OPENUSD_RENDERER",
            "OPENUSD_VIEWER_PLATFORM", "OPENUSD_DOTNET_OWN_SDK_PHASES")}
        result["newInheritedEnvironmentHashes"] = {
            key: hashlib.sha256(environment[key].encode()).hexdigest() if key in environment else None
            for key in ("HOME", "PATH", "LANG", "LC_ALL", "TMPDIR", "XDG_RUNTIME_DIR",
                        "DBUS_SESSION_BUS_ADDRESS", "SESSION_MANAGER")}
        result["diagnosticSoakIdentityMetadata"] = {
            "sourceHash": ZIP_SHA.upper(), "meaning": "NEW artifact-derived diagnostic provenance, NOT original sourceHash",
            "executableHash": environment["OPENUSD_SOAK_EXECUTABLE_HASH"],
            "executableTimestamp": environment["OPENUSD_SOAK_EXECUTABLE_TIMESTAMP_UTC"],
            "timestampMeaning": "Fresh staged file metadata restored to original ZIP timestamp; NOT historical subsecond proof"}
        return result
    finally:
        for process in (debugger, display_process):
            if process is not None:
                try:
                    cleanup_session(process.pid)
                    process.wait(timeout=2)
                except DiagnosticOperationError as error:
                    result.update(status="constraint-failure", faultDiscarded=True,
                                  finalCleanupFailure=error.failure)
                    if error.cleanup is not None:
                        result["cleanupFailure"] = error.cleanup
                    result.pop("fault", None)
                    result.pop("selectedModules", None)
                except (OSError, subprocess.SubprocessError) as error:
                    result.update(status="constraint-failure", faultDiscarded=True,
                                  finalCleanupError=type(error).__name__)
                    result.pop("fault", None)
                    result.pop("selectedModules", None)


def write_result(output, result):
    raw = (json.dumps(result, indent=2, ensure_ascii=True) + "\n").encode()
    if len(raw) > LIMIT:
        result = {key: result[key] for key in ("kind", "qualification", "originalSource", "originalRun",
                                              "originalJob", "originalExit", "replayCount")}
        result.update(status="public-output-truncated", faultDiscarded=True, publicByteLimit=LIMIT)
        raw = (json.dumps(result, indent=2) + "\n").encode()
    with output.open("xb") as stream:
        stream.write(raw)
    return result["status"]


def main():
    workspace = Path(os.environ["GITHUB_WORKSPACE"]).resolve()
    output = workspace / "artifacts/b905-replacement-diagnostic-only/result.json"
    if (sys.platform != "linux" or os.environ.get("GITHUB_ACTIONS") != "true" or
            os.environ.get("GITHUB_RUN_ATTEMPT") != "1" or os.environ.get("B905_ONCE_APPROVED") != "true" or
            os.environ.get("B905_FALLBACK_REPLACEMENT_APPROVED") != "true"):
        raise RuntimeError("Private preparation refusal: a separately approved replacement CI action is required.")
    output.parent.mkdir(parents=True, exist_ok=False)
    with (output.parent / "replay-owner").open("xb") as stream:
        stream.write(b"ONE diagnostic invocation; never retry this directory or Actions run.\n")
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    result = {"kind": "DIAGNOSTIC ON RECONSTRUCTED HOST/DISPLAY", "qualification": "NEVER QUALIFICATION",
              "originalSource": SOURCE, "originalRun": RUN, "originalJob": 117390909935,
              "originalExit": 134, "originalVerdict": "FAIL UNCHANGED", "replayCount": 0,
              "historicalFacts": HISTORICAL}
    try:
        tools, runtime_root, host = preflight()
        result["newHost"] = host
        with tempfile.TemporaryDirectory(prefix="b905-once-", dir=workspace) as temporary:
            temporary = Path(temporary)
            archive = acquire_original(temporary, os.environ["GH_TOKEN"])
            viewer = workspace.joinpath(*PurePosixPath(PREFIX).parts)
            if viewer.exists():
                raise Unavailable("original-layout-target-already-occupied")
            entries = extract_original(archive, viewer)
            before = fingerprint(viewer, entries)
            result.update(originalZipSha256=ZIP_SHA, originalStageSha256=STAGE_SHA,
                          extractedFiles=len(entries), artifactDerivedInputFingerprint=before)
            result["loaderClosure"] = prepare_loader_closure(viewer, temporary)
            if "privateHostLibraryRoot" in result["loaderClosure"]:
                tools["privateHostLibraryRoot"] = result["loaderClosure"]["privateHostLibraryRoot"]
            replay(viewer, workspace, temporary, tools, runtime_root, result)
            result["inputBytesUnchanged"] = before == fingerprint(viewer, entries)
            if not result["inputBytesUnchanged"]:
                result.update(status="input-identity-changed", faultDiscarded=True)
                result.pop("fault", None)
    except DiagnosticOperationError as error:
        result.update(status="diagnostic-error", safeFailure=error.failure,
                      reason="Allowlisted phase/operation/error code only; unsafe exception fields omitted.")
        if result["replayCount"] > 0:
            result.update(status="constraint-failure", faultDiscarded=True)
            result.pop("fault", None)
            result.pop("selectedModules", None)
        if error.cleanup is not None:
            result["cleanupFailure"] = error.cleanup
    except Unavailable as error:
        result.update(status="unavailable", reason=str(error))
        if error.details:
            result["preflightDetails"] = error.details
    except (OSError, ValueError, KeyError, subprocess.SubprocessError, urllib.error.URLError, zipfile.BadZipFile) as error:
        result.update(status="diagnostic-error", errorType=type(error).__name__,
                      reason="Error retained without URL, token, command arguments or locals.")
    if CLEANUP_TOTALS["calls"]:
        result["cleanupDiscovery"] = CLEANUP_TOTALS.copy()
    persisted_status = write_result(output, result)
    return 0 if persisted_status in ("fault-on-reconstructed-host", "no-fault", "unavailable") else 1


if __name__ == "__main__":
    raise SystemExit(main())
