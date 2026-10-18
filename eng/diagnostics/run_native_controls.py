"""Two owned SDK-free executables; no OpenUSD DLL, probe, source fixture or runtime."""
import json
import os
from pathlib import Path

from native_stall_outer import run

root = Path(__file__).resolve().parent
output = root / "control-results"
assert not output.exists()
output.mkdir()
helper = root / "windows_main_stall.exe"
control = root / "owned_native_control.exe"
quick = run(helper, control, ["exit7"], root, output / "numeric-exit")
assert quick["kind"] == "exited-before-stall" and quick["nativeResult"]["numericExitCode"] == 7
assert quick["subjectCount"] == 1 and quick["allOwnedExitedWithin45Seconds"]
stalled = run(helper, control, ["stall"], root, output / "nested-one-mib-main")
assert stalled["kind"] == "original-main-stall" and stalled["subjectCount"] == 1
assert stalled["allOwnedExitedWithin45Seconds"]
native = stalled["nativeResult"]
assert native["mainThreadId"] == stalled["originalMainThreadId"]
assert 0 < len(native["frames"]) <= 32
assert all(set(frame) == {"pc", "module", "moduleOffset", "function"} for frame in native["frames"])
assert native["noArgumentsLocalsRegistersRawStackOtherThreadsOrDump"]
assert 30000 <= native["ownedSubjectExitMilliseconds"] <= 45000
result = {
    "scope": "SDK-free Win32 numeric-exit and nested-original-sized1MiB-job main-thread stall controls",
    "passed": 2, "failed": 0, "actualImageOS": os.environ.get("ImageOS"),
    "actualImageVersion": os.environ.get("ImageVersion"),
    "quickControl": quick, "oneMiBMainThreadControl": stalled,
    "noActualOpenUsdReplayOrQualification": True,
}
(output / "summary.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print("WINDOWS_NATIVE_MAIN_CONTROLLER_SDK_FREE_CONTROLS passed=2 failed=0; NO_OPENUSD_EXECUTION")
