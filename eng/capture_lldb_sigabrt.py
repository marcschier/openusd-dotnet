"""Private LLDB API backend: one launched target, only its stopped SIGABRT thread, no values."""
import json
import os
from pathlib import Path
import signal
import time


def save(path, value):
    raw = json.dumps(value, ensure_ascii=True).encode()
    if len(raw) > 60000:
        raw = b'{"status":"truncated","frames":[]}'
    with Path(str(path) + ".tmp").open("xb") as stream:
        stream.write(raw)
    os.replace(str(path) + ".tmp", path)


def process_identity(pid):
    fields = Path(f"/proc/{pid}/stat").read_text().rsplit(")", 1)[1].split()
    return {"pid": pid, "start": fields[19], "session": int(fields[3]),
            "exe": os.path.realpath(f"/proc/{pid}/exe")}, int(fields[1])


def record_owned(process, config):
    owned, parent = process_identity(process.GetProcessID())
    save(config["owned"], owned)
    helper_path = config["owned"] + ".debugger-helper"
    if parent == os.getpid() and owned["session"] == os.getpid():
        save(helper_path, {"kind": "direct"})
        return True
    helper, helper_parent = process_identity(parent)
    verified = (helper_parent == os.getpid() and helper["exe"] == os.path.realpath(config["server"]) and
                owned["exe"] == os.path.realpath(config["executable"]))
    save(helper_path, {"kind": "tracked" if verified else "unverified", "identity": helper,
                       "debuggerPid": os.getpid(), "helperParent": helper_parent, "inferiorPid": owned["pid"]})
    return verified


def capture(debugger):
    import lldb

    config_path = Path(os.environ["B905_LLDB_CONFIG"])
    if config_path.stat().st_size > 32768:
        raise ValueError("Private LLDB launch configuration limit.")
    config = json.loads(config_path.read_bytes())
    executable = Path(config["executable"])
    expected_arguments = ["--shared-stage-soak", "--soak-seconds", "90",
                          "--soak-artifact", str(executable.parent / "shared-stage-soak.json")]
    if (config["source"] != "b905ec0a04f75de6eb4cfd198707ddf224d1b492" or
            executable.name != "OpenUsd.Viewer.App" or config["arguments"] != expected_arguments):
        raise ValueError("Private LLDB target identity mismatch.")
    debugger.SetAsync(True)
    target = debugger.CreateTarget(str(executable))
    if not target.IsValid():
        save(config["fault"], {"status": "lldb-target-unavailable"})
        return
    listener = lldb.SBListener("owned-b905-once")
    launch = lldb.SBLaunchInfo(config["arguments"])
    launch.SetWorkingDirectory(config["workingDirectory"])
    launch.SetLaunchFlags(lldb.eLaunchFlagDebug)
    launch.SetEnvironmentEntries([key + "=" + value for key, value in config["environment"].items()], False)
    launch.SetListener(listener)
    launch.SetDetachOnError(False)
    if not all(launch.AddSuppressFileAction(fd, fd == 0, fd != 0) for fd in (0, 1, 2)):
        save(config["fault"], {"status": "lldb-output-suppression-unavailable"})
        return
    error = lldb.SBError()
    process = target.Launch(launch, error)
    if error.Fail() or not process.IsValid() or process.GetProcessID() <= 0:
        save(config["fault"], {"status": "lldb-launch-error", "lldbErrorCode": error.GetError()})
        return
    if not record_owned(process, config):
        killed = process.Kill()
        save(config["fault"], {"status": "lldb-owned-lineage-unverified", "killErrorCode": killed.GetError()})
        return
    signals = process.GetUnixSignals()
    if not (signals.IsValid() and signals.SetShouldStop(signal.SIGABRT, True) and
            signals.SetShouldNotify(signal.SIGABRT, False) and
            signals.SetShouldSuppress(signal.SIGABRT, True)):
        killed = process.Kill()
        save(config["fault"], {"status": "lldb-signal-policy-unavailable", "killErrorCode": killed.GetError()})
        return
    event = lldb.SBEvent()
    while process.IsValid():
        if not listener.WaitForEvent(1, event):
            continue
        if not lldb.SBProcess.EventIsProcessEvent(event):
            continue
        published_process = lldb.SBProcess.GetProcessFromEvent(event)
        if not published_process.IsValid() or published_process.GetProcessID() != process.GetProcessID():
            continue
        if lldb.SBProcess.GetRestartedFromEvent(event):
            continue
        state = lldb.SBProcess.GetStateFromEvent(event)
        if state in (lldb.eStateExited, lldb.eStateDetached, lldb.eStateInvalid):
            save(config["fault"], {"status": "lldb-exited-without-approved-fault"})
            return
        if state in (lldb.eStateStopped, lldb.eStateCrashed):
            thread = process.GetSelectedThread()
            if not thread.IsValid():
                save(config["fault"], {"status": "lldb-selected-thread-unavailable-after-stop-event"})
                return
            reason = thread.GetStopReason()
            if (reason == lldb.eStopReasonSignal and thread.GetStopReasonDataCount() >= 1 and
                    thread.GetStopReasonDataAtIndex(0) == signal.SIGABRT):
                save(config["fault"] + ".started", {"signal": "SIGABRT"})
                frames = []
                for index in range(32):
                    frame = thread.GetFrameAtIndex(index)
                    if not frame.IsValid():
                        break
                    item = {"pc": hex(frame.GetPC()), "function": (frame.GetFunctionName() or "??")[:384]}
                    line = frame.GetLineEntry()
                    if line.IsValid():
                        file = line.GetFileSpec()
                        item.update(file=os.path.join(file.GetDirectory() or "", file.GetFilename() or "")[:384],
                                    line=line.GetLine())
                    frames.append(item)
                pid = process.GetProcessID()
                paths = sorted({line.split(None, 5)[5].strip()
                                for line in Path(f"/proc/{pid}/maps").read_text().splitlines()
                                if len(line.split(None, 5)) == 6 and line.split(None, 5)[5].startswith("/")})
                save(config["fault"], {"status": "fault", "signal": "SIGABRT", "pid": pid,
                                       "thread": thread.GetThreadID(), "frames": frames, "mappedPaths": paths,
                                       "capturedMonotonic": time.monotonic()})
                return
            if reason not in (lldb.eStopReasonNone, lldb.eStopReasonExec, lldb.eStopReasonTrace):
                save(config["fault"], {"status": "lldb-other-stop-without-approved-fault", "stopReason": reason})
                return
            continued = process.Continue()
            if continued.Fail():
                save(config["fault"], {"status": "lldb-continue-error", "lldbErrorCode": continued.GetError()})
                return
