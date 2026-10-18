"""One owned exact-built-input replay, not original loaded history or qualification."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import re

from native_stall_outer import run


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", required=True, type=Path)
    parser.add_argument("--helper", required=True, type=Path)
    parser.add_argument("--producer-source", required=True)
    parser.add_argument("--producer-run", required=True, type=int)
    parser.add_argument("--producer-ref", required=True)
    parser.add_argument("--session", required=True, type=Path)
    args = parser.parse_args()
    if not re.fullmatch("[a-f0-9]{40}", args.producer_source) or args.producer_run <= 0:
        raise ValueError("Invalid selected replay source/run.")
    source = args.source_root.resolve()
    spec = importlib.util.spec_from_file_location("exact_built_windows_replay", source / "eng/windows-admission-replay.py")
    transport = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(transport)
    b = transport.boundaries(source)
    session = b.confined_destination(source, args.session.resolve().relative_to(source))
    if session.exists():
        raise ValueError("This one-subject replay session already exists.")
    session.mkdir(parents=True)
    staged = session / "staged-original-build"
    manifest = transport.fetch_replay(
        source, staged, args.producer_source, args.producer_run,
        os.environ["GITHUB_REPOSITORY"], int(os.environ["GITHUB_REPOSITORY_ID"]),
        args.producer_ref, os.environ["GH_TOKEN"])
    if (manifest["originalCase"] != transport.CASE or manifest["mode"] != transport.MODE
            or manifest["freshScene"] != transport.SCENE or manifest["pluginRoot"] != transport.PLUGINS
            or manifest["originalQuotaBytes"] != 1048576 or manifest["originalCtestDeadlineSeconds"] != 120):
        raise ValueError("The exact original case/scene/quota contract differs.")
    scene = b.confined_destination(staged, Path(transport.SCENE))
    if scene.exists():
        raise ValueError("The original probe must recreate a fresh authored scene.")
    scene.parent.mkdir(parents=True, exist_ok=True)
    host = {name: os.environ.get(name) for name in ("ImageOS", "ImageVersion", "VCToolsVersion")}
    host_comparison = {
        "producer": manifest["actualProducerHost"], "replay": host,
        "differences": [name for name in host if host[name] != manifest["actualProducerHost"].get(name)],
        "sourceBuiltByteIdentityIsNotHistoricalHostOrLoadedModuleIdentity": True,
    }
    (session / "host-comparison.json").write_text(json.dumps(host_comparison, indent=2) + "\n", encoding="utf-8")
    executable = staged / transport.PROBE
    working = staged / transport.WORKING
    expected = {name: value["sha256"] for name, value in manifest["files"].items()}
    saved_path = os.environ.get("PATH", "")
    try:
        os.environ["PATH"] = os.pathsep.join((
            str((staged / transport.CORE).parent), str(staged / transport.SDK / "bin"),
            str(staged / transport.SDK / "lib"), saved_path))
        result = run(args.helper.resolve(), executable,
                     [str(staged / transport.PLUGINS), str(scene), transport.MODE],
                     working, session / "one-subject-result", expected, staged)
    finally:
        os.environ["PATH"] = saved_path
    result["originalCase"] = transport.CASE
    result["originalQuotaAndQualificationDeadlineUnchanged"] = True
    result["producerSource"] = args.producer_source
    result["producerRun"] = args.producer_run
    result["hostComparison"] = host_comparison
    result["noHistoricalLoadedModuleOrQualificationClaim"] = True
    (session / "replay-summary.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({name: result.get(name) for name in (
        "kind", "subjectCount", "runtimeBindingState", "diagnosticAccepted", "actualAllOwnedExitSeconds")}))
    return 0 if result["diagnosticAccepted"] and result["allOwnedExitedWithin45Seconds"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
