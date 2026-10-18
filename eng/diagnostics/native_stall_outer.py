"""45-second outer owner for the SDK-free Win32/DbgHelp helper and its single subject."""
import json
import os
from pathlib import Path
import re
import subprocess
import time

from windows_owned_job import OwnedJob, exit_code, exited, release


def run(helper, executable, arguments, working, output):
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
    try:
        process = subprocess.Popen(
            [str(helper), str(output), str(working), str(executable), *arguments],
            stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
        job.assign(process.pid)
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
        known = {identity["pid"] for identity in identities}
        for pid in job.members():
            if pid not in known:
                identities.append(job.identity(pid))
                known.add(pid)
        all_exited = job.stop_and_wait(began + 45)
        job.close()
        if process is not None:
            if not identities and process.poll() is None:
                process.kill()
            try:
                process.wait(timeout=max(0.01, began + 45 - time.monotonic()))
            except subprocess.TimeoutExpired:
                all_exited = False
        result["ownedProcesses"] = []
        for identity in identities:
            done = exited(identity, max(0, int((began + 45 - time.monotonic()) * 1000)))
            result["ownedProcesses"].append({
                "pid": identity["pid"], "startFileTime": identity["startFileTime"],
                "executable": Path(identity["image"]).name,
                "exited": done, "numericExitCode": exit_code(identity) if done else None})
            all_exited = all_exited and done
            release(identity)
        result["actualAllOwnedExitSeconds"] = time.monotonic() - began
        result["allOwnedExitedWithin45Seconds"] = bool(identities) and all_exited and \
            result["actualAllOwnedExitSeconds"] <= 45
        (output / "outer-result.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return result
