"""45-second outer owner for the SDK-free Win32/DbgHelp helper and its single subject."""
import json
import hashlib
import os
from pathlib import Path
import re
import subprocess
import time

from windows_owned_job import OwnedJob, allowlisted_loaded_images, exit_code, exited, release

EXPECTED_REPLAY_IMAGES = {
    "openusd_property_admission_probe.exe":
        "native/build/shim/win-x64-physx/tests/openusd_property_admission_probe.exe",
    "openusd_dotnet.dll": "native/build/shim/win-x64-physx/openusd_dotnet/openusd_dotnet.dll",
    "usd_ms.dll": "native/install/win-x64/lib/usd_ms.dll",
}


def bind_loaded_images(images, staged_hashes, stage_root, observed):
    boundary = stage_root.resolve()
    for image in images:
        expected = EXPECTED_REPLAY_IMAGES.get(image["name"].casefold())
        if expected is None:
            raise RuntimeError("An unexpected module entered the replay allowlist.")
        path = Path(image["path"]).resolve()
        if not path.is_relative_to(boundary):
            raise RuntimeError("An allowlisted runtime module loaded outside the assigned staged closure.")
        relative = path.relative_to(boundary).as_posix()
        if relative.casefold() != expected.casefold() or expected not in staged_hashes:
            raise RuntimeError("An allowlisted runtime module loaded from a different build variant/path.")
        if expected not in observed:
            with path.open("rb") as stream:
                digest = hashlib.file_digest(stream, "sha256").hexdigest()
            if digest != staged_hashes[expected]:
                raise RuntimeError("An actual loaded module backing-file hash differs from the staged closure.")
            observed[expected] = {
                "name": image["name"], "relativePath": expected, "sha256": digest,
                "observedWhileOwnedSubjectAlive": True}


def finish_owned(job, process, identities, deadline, assigned):
    errors = []
    all_exited = False
    owned = []

    def failed(phase, error):
        errors.append({"phase": phase, "errorType": type(error).__name__})

    try:
        try:
            members = job.members()
        except (OSError, RuntimeError) as error:
            failed("cleanup-membership", error)
            members = []
        known = {identity["pid"] for identity in identities}
        for pid in members:
            if pid not in known:
                try:
                    identities.append(job.identity(pid))
                    known.add(pid)
                except (OSError, RuntimeError) as error:
                    failed("cleanup-member-identity", error)
    finally:
        try:
            try:
                all_exited = job.stop_and_wait(deadline)
            except (OSError, RuntimeError) as error:
                failed("cleanup-job-termination", error)
        finally:
            try:
                job.close()
            except (OSError, RuntimeError) as error:
                failed("cleanup-job-close", error)
            finally:
                if process is not None:
                    try:
                        if not assigned and process.poll() is None:
                            process.kill()
                        process.wait(timeout=max(0.01, deadline - time.monotonic()))
                    except (OSError, subprocess.TimeoutExpired) as error:
                        failed("cleanup-helper-wait", error)
                        all_exited = False
                for identity in identities:
                    entry = {"pid": identity["pid"], "startFileTime": identity["startFileTime"],
                             "executable": Path(identity["image"]).name, "exited": False,
                             "numericExitCode": None}
                    try:
                        done = exited(identity, max(0, int((deadline - time.monotonic()) * 1000)))
                        entry.update(exited=done, numericExitCode=exit_code(identity) if done else None)
                        all_exited = all_exited and done
                    except (OSError, RuntimeError) as error:
                        failed("cleanup-owned-handle-wait", error)
                        all_exited = False
                    finally:
                        try:
                            release(identity)
                        except OSError as error:
                            failed("cleanup-owned-handle-close", error)
                    owned.append(entry)
    return all_exited, owned, errors


def finalize_runtime_binding(result, observed, requested):
    missing = sorted(set(EXPECTED_REPLAY_IMAGES.values()) - observed.keys())
    result["runtimeBindingRequested"] = requested
    result["runtimeBindingComplete"] = requested and not missing
    result["runtimeBindingState"] = ("not-requested-sdk-free" if not requested
                                     else "verified" if not missing else "incomplete-unverified")
    result["missingRuntimeImages"] = missing if requested else []
    result["diagnosticAccepted"] = (
        result.get("kind") in ("exited-before-stall", "original-main-stall")
        and result["allOwnedExitedWithin45Seconds"] and not result["cleanupErrors"]
        and (not requested or result["runtimeBindingComplete"]))


def run(helper, executable, arguments, working, output, staged_hashes=None, stage_root=None):
    if (staged_hashes is None) != (stage_root is None):
        raise ValueError("Staged runtime hashes and root must be supplied together.")
    if output.exists():
        raise ValueError("The diagnostic output directory is already occupied.")
    output.mkdir()
    job = OwnedJob()
    identities = []
    process = None
    began = time.monotonic()
    result = {"schemaVersion": 1, "diagnosticOnly": True, "subjectCount": 0,
              "backend": "native-original-primary-thread-Win32-DbgHelp",
              "noSdkBinaryIdentityOrQualificationClaimByControllerAlone": True}
    subject = None
    observed_images = {}
    assigned = False
    try:
        process = subprocess.Popen(
            [str(helper), str(output), str(working), str(executable), *arguments],
            stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
        job.assign(process.pid)
        assigned = True
        owner = job.identity(process.pid)
        identities.append(owner)
        if os.path.normcase(owner["image"]) != os.path.normcase(str(helper.resolve())):
            raise RuntimeError("Owned helper executable identity mismatch.")
        process.stdin.write(b"G\n")
        process.stdin.flush()
        observed = output / "subject-identity.json"
        while process.poll() is None and time.monotonic() < began + 44:
            if observed.exists() and result["subjectCount"] == 0:
                if observed.stat().st_size > 1024:
                    raise RuntimeError("Subject identity metadata exceeds its bound.")
                value = json.loads(observed.read_text(encoding="utf-8"))
                subject = job.identity(value["pid"])
                identities.append(subject)
                if (os.path.normcase(subject["image"]) != os.path.normcase(str(executable.resolve()))
                        or subject["startFileTime"] != value["startFileTime"]
                        or subject["startFileTime"] < owner["startFileTime"]):
                    raise RuntimeError("Original primary-thread subject identity mismatch.")
                result.update(subjectCount=1, originalMainThreadId=value["mainThreadId"])
                process.stdin.write(b"R\n")
                process.stdin.flush()
                process.stdin.close()
            if subject is not None and staged_hashes is not None and not exited(subject, 0):
                images = allowlisted_loaded_images(subject, set(EXPECTED_REPLAY_IMAGES))
                bind_loaded_images(images or (), staged_hashes, stage_root, observed_images)
            time.sleep(0.01)
        if process.poll() is None:
            result["kind"] = "outer-deadline"
        else:
            result["helperExitCode"] = process.returncode
            captured = output / "result.json"
            if captured.is_file() and captured.stat().st_size <= 65536:
                value = json.loads(captured.read_text(encoding="utf-8"))
                if value.get("mainThreadId") != result.get("originalMainThreadId"):
                    raise RuntimeError("Captured primary-thread identity mismatch.")
                frames = value.get("frames", [])
                if value["kind"] == "original-main-stall" and not 0 < len(frames) <= 32:
                    raise RuntimeError("Captured main-thread frame boundary mismatch.")
                result.update(kind=value["kind"], nativeResult=value)
            else:
                error = process.stderr.read(1024).decode("ascii", errors="replace")
                code = re.fullmatch(r"WINDOWS_MAIN_STALL_DIAGNOSTIC_UNAVAILABLE win32=([0-9]+)\s*", error)
                result.update(kind="unavailable", helperWin32Error=int(code.group(1)) if code else None)
    except (OSError, ValueError, RuntimeError) as error:
        result.update(kind="unavailable", errorType=type(error).__name__)
    finally:
        all_exited, owned, cleanup_errors = finish_owned(job, process, identities, began + 45, assigned)
        result["ownedProcesses"] = owned
        result["cleanupErrors"] = cleanup_errors
        if cleanup_errors:
            result["kind"] = "unavailable"
        result["allowlistedLoadedImages"] = list(observed_images.values())
        result["originalHistoricalCtestLoadedModulesUnobserved"] = True
        result["actualAllOwnedExitSeconds"] = time.monotonic() - began
        result["allOwnedExitedWithin45Seconds"] = bool(identities) and all_exited and \
            result["actualAllOwnedExitSeconds"] <= 45
        finalize_runtime_binding(result, observed_images, staged_hashes is not None)
        (output / "outer-result.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return result
