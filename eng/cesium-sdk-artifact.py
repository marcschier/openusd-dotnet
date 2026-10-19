#!/usr/bin/env python3
"""Produce/import exact-source Cesium SDK artifacts; missing artifacts build normally, invalid artifacts fail closed."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile

INPUTS = (
    "eng/cesium.lock.json", "eng/fetch-cesium-native.ps1", "eng/build-cesium-native.ps1",
    "eng/generate-native-notices.py", "eng/sbom/cesium-vcpkg-components.lock.json",
    "eng/cesium-sdk-artifact.py", "LICENSE", "NOTICE",
)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(path):
    with system_path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def system_path(path):
    absolute = str(path.resolve())
    return Path("\\\\?\\" + absolute) if os.name == "nt" and not absolute.startswith("\\\\?\\") else path


def portable_member(name):
    require(isinstance(name, str) and name and "\\" not in name and ":" not in name
            and not any(ord(char) < 32 or char in '<>"|?*' for char in name),
            "Cesium archive member is not a portable file name.")
    path = PurePosixPath(name)
    require(not path.is_absolute() and ".." not in path.parts and path.as_posix() == name,
            "Cesium archive member has an absolute/traversal/path alias.")
    reserved = {"CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$"}
    reserved.update(prefix + str(index) for prefix in ("COM", "LPT") for index in range(10))
    require(all(part.rstrip(" .") == part and part.split(".", 1)[0].upper() not in reserved
                and not re.search(r"~[0-9]+(?:\.|$)", part) for part in path.parts),
            "Cesium archive member has a Windows device/short-name/trailing alias.")
    return name.casefold()


def confined_destination(root, relative):
    require(not relative.is_absolute() and ".." not in relative.parts, "Invalid Cesium SDK destination.")
    actual_root = root.resolve()
    target = root / relative
    require(target.resolve().is_relative_to(actual_root) and target.resolve() != actual_root,
            "Cesium SDK destination escapes the assigned root.")
    current = root
    for part in relative.parts:
        current /= part
        require(not current.is_symlink() and not getattr(current, "is_junction", lambda: False)(),
                "Cesium SDK destination has a linked ancestor or destination.")
    return target


def source_inputs(root):
    paths = [confined_destination(root, Path(value)) for value in INPUTS]
    require(all(path.is_file() for path in paths), "Cesium provenance source input is missing.")
    probe = []
    pending = [confined_destination(root, Path("native/cesium_probe"))]
    while pending:
        for path in pending.pop().iterdir():
            path = confined_destination(root, path.relative_to(root))
            if path.is_dir():
                pending.append(path)
            elif path.is_file():
                probe.append(path)
    probe.sort()
    require(bool(probe), "Cesium probe source input is missing.")
    paths.extend(probe)
    return {path.relative_to(root).as_posix(): digest(path) for path in paths if path.is_file()}


def toolchain_identity():
    compiler = os.environ.get("CXX") or ("cl.exe" if os.name == "nt" else "c++")
    executable = shutil.which(compiler)
    if sys.platform == "darwin" and not os.environ.get("CXX"):
        executable = subprocess.check_output(["xcrun", "--find", "clang++"], timeout=10).decode().strip()
    require(executable is not None, "Cesium C++ compiler is unavailable.")
    image = os.environ.get("ImageVersion")
    require(bool(image), "Cesium artifact reuse requires the runner image identity.")
    require(not os.environ.get("CXXFLAGS") and not os.environ.get("CFLAGS") and not os.environ.get("CPPFLAGS"),
            "Cesium artifact toolchain identity does not permit unbound compiler flags.")
    c_compiler = shutil.which(os.environ.get("CC") or ("cl.exe" if os.name == "nt" else "cc"))
    if sys.platform == "darwin" and not os.environ.get("CC"):
        c_compiler = subprocess.check_output(["xcrun", "--find", "clang"], timeout=10).decode().strip()
    require(c_compiler is not None, "Cesium C compiler is unavailable.")
    result = {"imageVersion": image, "imageOS": os.environ.get("ImageOS"),
              "compiler": Path(executable).resolve().name,
              "compilerSha256": digest(Path(executable).resolve()),
              "cCompilerSha256": digest(Path(c_compiler).resolve()),
              "msvcToolsVersion": os.environ.get("VCToolsVersion")}
    for name in ("cmake", "ninja"):
        output = subprocess.check_output([name, "--version"], stderr=subprocess.STDOUT, timeout=10)
        require(len(output) <= 4096, "Cesium build tool version output exceeds the bound.")
        result[name] = output.decode("utf-8").splitlines()[0]
    return result


def identity_key(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def triplet_for(root, rid):
    lock = json.loads(confined_destination(root, Path("eng/cesium.lock.json")).read_bytes())
    field = {"win-x64": "windowsTriplet", "linux-x64": "linuxTriplet", "osx-arm64": "macosTriplet"}[rid]
    return lock["vcpkg"][field]


def roots_for(root, rid):
    triplet = triplet_for(root, rid)
    install = confined_destination(root, Path("native/install/cesium") / rid)
    dependency = confined_destination(root, Path("native/build/cesium") / rid / "vcpkg_installed" / triplet)
    if not dependency.is_dir():
        lock = json.loads(confined_destination(root, Path("eng/cesium.lock.json")).read_bytes())
        dependency = confined_destination(root, Path("native/src") / lock["cesiumNative"]["extractDirectory"] / "vcpkg_installed" / triplet)
    require(install.is_dir() and dependency.is_dir(), "Complete Cesium SDK/dependency install is missing.")
    return system_path(install), system_path(dependency), triplet


def payload_files(root, rid):
    install, dependency, triplet = roots_for(root, rid)
    files = {}
    case_names = {}
    def add(physical, logical, boundary):
        require(physical.resolve().is_relative_to(boundary.resolve()), "Cesium artifact link escapes its input tree.")
        require(not physical.is_symlink() or physical.is_file(), "Cesium artifact contains a broken link.")
        if physical.is_file():
            canonical = portable_member(logical)
            require(canonical not in case_names or case_names[canonical] == logical,
                    "Cesium payload contains case-fold-colliding paths.")
            case_names[canonical] = logical
            files[logical] = physical
    for path in install.rglob("*"):
        add(path, (PurePosixPath("native/install/cesium") / rid / path.relative_to(install).as_posix()).as_posix(), install)
    prefix = PurePosixPath("native/build/cesium") / rid / "vcpkg_installed" / triplet
    for folder in ("include", "lib", "share", "bin"):
        directory = dependency / folder
        for path in directory.rglob("*") if directory.is_dir() else ():
            add(path, (prefix / path.relative_to(dependency).as_posix()).as_posix(), dependency)
    # Some exported CMake targets validate debug-library paths even for Release consumers.
    for config in (dependency / "share").rglob("*.cmake"):
        for match in re.findall(r"(debug/(?:lib|bin)/[^\"$;()\s]+)", config.read_text(errors="strict")):
            relative = PurePosixPath(match)
            require(".." not in relative.parts, "Cesium dependency export has an unsafe debug path.")
            physical = dependency.joinpath(*relative.parts)
            if physical.is_file():
                add(physical, (prefix / relative).as_posix(), dependency)
    require(files and any(name.startswith(f"native/install/cesium/{rid}/include/") for name in files)
            and any(name.startswith(f"native/install/cesium/{rid}/lib/") for name in files),
            "Cesium artifact is missing installed headers or libraries.")
    require(f"native/install/cesium/{rid}/.openusd-native-notices.json" in files
            and f"native/install/cesium/{rid}/THIRD-PARTY-CESIUM.md" in files,
            "Cesium SDK redistribution notice metadata is missing.")
    return files


def write_artifact(root, rid, destination, source, host):
    require(re.fullmatch(r"[0-9a-f]{40}", source) is not None, "Invalid Cesium source identity.")
    destination = confined_destination(root, destination.relative_to(root))
    require(not destination.exists(), "Cesium artifact destination is already occupied.")
    destination.mkdir(parents=True)
    files = payload_files(root, rid)
    archive_path = destination / f"cesium-sdk-{rid}.tar.gz"
    manifest = {"schemaVersion": 1, "family": "cesium-sdk", "rid": rid, "sourceHead": source,
                "producerRun": os.environ.get("GITHUB_RUN_ID"), "producerAttempt": os.environ.get("GITHUB_RUN_ATTEMPT"),
                "triplet": triplet_for(root, rid), "sourceInputs": source_inputs(root), "toolchain": host,
                "files": {name: {"bytes": path.stat().st_size, "sha256": digest(path)}
                          for name, path in sorted(files.items())}}
    with tarfile.open(archive_path, "w:gz", compresslevel=1, dereference=True) as archive:
        for name, path in sorted(files.items()):
            archive.add(path, arcname=name, recursive=False)
    manifest["archiveSha256"] = digest(archive_path)
    manifest["archiveBytes"] = archive_path.stat().st_size
    (destination / f"cesium-sdk-{rid}.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return manifest


def verify_and_import(root, rid, archive_path, manifest, source, host, run_id, attempt):
    require(manifest.get("schemaVersion") == 1 and manifest.get("family") == "cesium-sdk"
            and manifest.get("rid") == rid and manifest.get("sourceHead") == source
            and manifest.get("producerRun") == str(run_id) and manifest.get("producerAttempt") == str(attempt),
            "Cesium artifact source/RID/run/attempt provenance differs.")
    require(manifest.get("triplet") == triplet_for(root, rid)
            and manifest.get("sourceInputs") == source_inputs(root), "Cesium artifact locked source inputs differ.")
    require(manifest.get("archiveBytes") == archive_path.stat().st_size
            and manifest.get("archiveSha256") == digest(archive_path), "Cesium SDK archive integrity differs.")
    expected = manifest.get("files")
    require(isinstance(expected, dict) and expected, "Cesium SDK payload manifest is empty or invalid.")
    require(len(expected) <= 50000 and all(isinstance(value, dict) and type(value.get("bytes")) is int
            and 0 <= value["bytes"] <= 2147483648 and isinstance(value.get("sha256"), str)
            and re.fullmatch(r"[a-f0-9]{64}", value["sha256"])
            for value in expected.values()), "Cesium SDK payload manifest entries are invalid.")
    require(sum(value["bytes"] for value in expected.values()) <= 8589934592,
            "Cesium SDK extracted payload exceeds the bound.")
    triplet = triplet_for(root, rid)
    prefixes = (f"native/install/cesium/{rid}/", f"native/build/cesium/{rid}/vcpkg_installed/{triplet}/")
    destinations = (Path("native/install/cesium") / rid,
                    Path("native/build/cesium") / rid / "vcpkg_installed" / triplet)
    for relative in destinations:
        require(not confined_destination(root, relative).exists(),
                "Cesium SDK import would overwrite an existing install.")
    canonical = [portable_member(name) for name in expected]
    require(len(canonical) == len(set(canonical)), "Cesium manifest has case-fold-colliding members.")
    with tempfile.TemporaryDirectory(prefix=".cesium-sdk-import-", dir=system_path(root)) as temporary:
        staging = Path(temporary)
        with tarfile.open(archive_path) as archive:
            members = archive.getmembers()
            require(len(members) == len(expected) and len({item.name for item in members}) == len(members),
                    "Cesium SDK archive has missing/duplicate/extra members.")
            for item in members:
                name = PurePosixPath(item.name)
                require(item.isfile() and item.name in expected and item.name.startswith(prefixes)
                        and not name.is_absolute() and ".." not in name.parts and "\\" not in item.name,
                        "Cesium SDK archive contains an unsafe/unexpected member.")
                require(type(expected[item.name].get("bytes")) is int and item.size == expected[item.name]["bytes"],
                        "Cesium SDK member length differs.")
            archive.extractall(system_path(staging), filter="data")
        for name, entry in expected.items():
            require(digest(staging.joinpath(*PurePosixPath(name).parts)) == entry["sha256"],
                    "Cesium SDK header/library/config payload hash differs.")
        require(manifest.get("toolchain") == host, "Cesium artifact compiler/toolchain/runner identity differs.")
        for relative in destinations:
            target = system_path(confined_destination(root, relative))
            require(not target.exists(), "Cesium SDK import would overwrite an existing install.")
        for relative in destinations:
            target = system_path(confined_destination(root, relative))
            target.parent.mkdir(parents=True, exist_ok=True)
            require(not confined_destination(root, relative).exists(), "Cesium SDK destination changed during import.")
            os.replace(system_path(staging / relative), target)


def api(repository, token, path):
    request = urllib.request.Request("https://api.github.com/repos/" + repository + "/" + path,
                                    headers={"Authorization": "Bearer " + token,
                                             "Accept": "application/vnd.github+json"})
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            raw = response.read(4194305)
    except (urllib.error.URLError, TimeoutError):
        raise RuntimeError("Cesium producer metadata is unavailable; no successful fallback is inferred.") from None
    require(len(raw) <= 4194304, "Cesium producer metadata exceeds its bound.")
    return json.loads(raw)


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args):
        return None


def validate_artifact_identity(artifact, run, rid):
    require(type(artifact.get("id")) is int and artifact["id"] > 0
            and artifact.get("name") == f"openusd-cesium-sdk-{rid}-{run['run_attempt']}"
            and artifact.get("expired") is False and isinstance(artifact.get("digest"), str)
            and re.fullmatch(r"sha256:[a-f0-9]{64}", artifact["digest"]),
            "Listed Cesium artifact ID/name/digest/expiry differs.")
    binding = artifact.get("workflow_run")
    repository = run.get("repository", {}).get("id")
    head_repository = run.get("head_repository", {}).get("id")
    require(type(repository) is int and type(head_repository) is int and isinstance(binding, dict)
            and binding.get("id") == run["id"] and binding.get("head_sha") == run["head_sha"]
            and binding.get("repository_id") == repository and binding.get("head_repository_id") == head_repository
            and binding.get("head_branch") == run.get("head_branch"),
            "Listed Cesium artifact producer source/run/repository/ref differs.")


def download_artifact(repository, token, artifact, path):
    require(artifact.get("digest", "").startswith("sha256:") and not artifact["expired"],
            "Cesium artifact digest/expiry is invalid.")
    size = artifact["size_in_bytes"]
    require(type(size) is int and 0 < size <= 2147483648, "Cesium artifact length is invalid.")
    request = urllib.request.Request(
        f"https://api.github.com/repos/{repository}/actions/artifacts/{artifact['id']}/zip",
        headers={"Authorization": "Bearer " + token, "Accept": "application/vnd.github+json"})
    try:
        urllib.request.build_opener(NoRedirect()).open(request, timeout=30)
    except urllib.error.HTTPError as error:
        require(error.code == 302, "Cesium artifact redirect is unavailable.")
        location = error.headers["Location"]
    else:
        raise ValueError("Cesium artifact redirect is missing.")
    host = urllib.parse.urlsplit(location)
    require(host.scheme == "https" and host.hostname is not None and host.hostname.endswith(
        (".blob.core.windows.net", ".actions.githubusercontent.com")), "Cesium artifact redirect host refused.")
    with urllib.request.urlopen(location, timeout=60) as response, path.open("xb") as stream:
        remaining = size
        while remaining:
            chunk = response.read(min(1048576, remaining))
            require(bool(chunk), "Cesium artifact download ended early.")
            stream.write(chunk)
            remaining -= len(chunk)
        require(not response.read(1), "Cesium artifact exceeds its declared length.")
    require("sha256:" + digest(path) == artifact["digest"], "Cesium artifact outer digest differs.")


def consume_once(root, rid, source, host, explicit_run=None, fallback_on_toolchain_mismatch=False):
    repository, token = os.environ["GITHUB_REPOSITORY"], os.environ["GH_TOKEN"]
    require(re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository) is not None, "Invalid repository.")
    if explicit_run:
        require(str(explicit_run).isdigit(), "Invalid same-release Cesium producer run.")
        runs = [api(repository, token, f"actions/runs/{explicit_run}")]
    else:
        query = urllib.parse.urlencode({"head_sha": source, "per_page": 100})
        listing = api(repository, token, "actions/workflows/cesium-native.yml/runs?" + query)
        require(listing["total_count"] <= 100, "Cesium producer query exceeds the bound.")
        runs = listing["workflow_runs"]
    pending = False
    for run in runs:
        require(run["head_sha"] == source, "Cesium producer source differs.")
        require(run.get("head_repository", {}).get("full_name") == repository, "Foreign Cesium producer is refused.")
        require(run["path"] in (".github/workflows/cesium-native.yml", ".github/workflows/release.yml"),
                "Unexpected Cesium producer workflow.")
        if explicit_run:
            require(run["path"] in (".github/workflows/release.yml", ".github/workflows/cesium-native.yml")
                    and str(run["id"]) == os.environ["GITHUB_RUN_ID"],
                    "Explicit Cesium artifacts must come from the current qualified producer workflow run.")
        jobs = api(repository, token, f"actions/runs/{run['id']}/jobs?per_page=100")
        require(jobs["total_count"] <= 100, "Cesium producer jobs exceed the bound.")
        matching = [job for job in jobs["jobs"] if job["name"].split(" / ")[-1] == f"{rid} locked cesium-native"
                    and job["run_attempt"] == run["run_attempt"]]
        require(len(matching) <= 1, "Cesium RID producer jobs are ambiguous.")
        if not matching:
            if not jobs["jobs"] and run.get("status") in ("queued", "in_progress", "requested", "waiting", "pending"):
                pending = True
            continue
        job = matching[0]
        require(job["head_sha"] == source and job["run_id"] == run["id"], "Cesium producer job source/run differs.")
        if job["status"] != "completed":
            pending = True
            continue
        require(job["conclusion"] == "success", "The exact-source Cesium RID producer failed; no successful fallback is inferred.")
        listed = api(repository, token, f"actions/runs/{run['id']}/artifacts?per_page=100")
        require(listed["total_count"] <= 100, "Cesium artifacts exceed the bound.")
        name = f"openusd-cesium-sdk-{rid}-{run['run_attempt']}"
        artifacts = [item for item in listed["artifacts"] if item["name"] == name]
        require(len(artifacts) <= 1, "Cesium producer artifacts are ambiguous.")
        if not artifacts:
            continue
        validate_artifact_identity(artifacts[0], run, rid)
        with tempfile.TemporaryDirectory(prefix=".cesium-sdk-download-", dir=system_path(root)) as temporary:
            temporary = Path(temporary)
            archive_zip = temporary / "producer.zip"
            download_artifact(repository, token, artifacts[0], archive_zip)
            with zipfile.ZipFile(archive_zip) as archive:
                expected = {f"cesium-sdk-{rid}.tar.gz", f"cesium-sdk-{rid}.json"}
                require(len(archive.infolist()) == 2 and set(archive.namelist()) == expected,
                        "Cesium producer artifact members differ.")
                manifest_item = archive.getinfo(f"cesium-sdk-{rid}.json")
                require(manifest_item.file_size <= 8388608, "Cesium payload manifest exceeds its bound.")
                manifest = json.loads(archive.read(manifest_item))
                archive_path = temporary / f"cesium-sdk-{rid}.tar.gz"
                payload = archive.getinfo(archive_path.name)
                require(type(manifest.get("archiveBytes")) is int
                        and payload.file_size == manifest["archiveBytes"] and 0 < payload.file_size <= 2147483648,
                        "Cesium inner archive length differs or exceeds its bound.")
                with archive.open(archive_path.name) as source_stream, archive_path.open("xb") as stream:
                    shutil.copyfileobj(source_stream, stream, length=1048576)
            try:
                verify_and_import(root, rid, archive_path, manifest, source, host, run["id"], run["run_attempt"])
            except ValueError as error:
                if (fallback_on_toolchain_mismatch and
                        str(error) == "Cesium artifact compiler/toolchain/runner identity differs."):
                    decision = {
                        "reasonCode": "artifact_identity_mismatch_nonreusable",
                        "producerImageIdentity": {
                            "imageOS": manifest.get("toolchain", {}).get("imageOS"),
                            "imageVersion": manifest.get("toolchain", {}).get("imageVersion"),
                        },
                        "consumerImageIdentity": {
                            "imageOS": host.get("imageOS"),
                            "imageVersion": host.get("imageVersion"),
                        },
                        "selectedAction": "rebuilt_from_pinned_source",
                    }
                    text = "CESIUM_SDK_DECISION: " + json.dumps(
                        decision, sort_keys=True, separators=(",", ":"))
                    require(len(text.encode("utf-8")) <= 2048,
                            "Cesium fallback decision log exceeds the bound.")
                    print(text)
                    continue
                raise
        print(f"CESIUM_SDK_REUSED: verified source={source} producer={run['id']} rid={rid}; current shim/gates still required.")
        return True
    return None if pending else False


def consume(root, rid, source, host, explicit_run=None, wait_seconds=5400,
            fallback_on_toolchain_mismatch=False,
            clock=time.monotonic, sleep=time.sleep):
    started = clock()
    announced = False
    while True:
        result = consume_once(root, rid, source, host, explicit_run,
                              fallback_on_toolchain_mismatch=fallback_on_toolchain_mismatch)
        if result is True:
            return True
        if result is False:
            print(f"CESIUM_SDK_SOURCE_BUILD: no eligible immutable SDK artifact for {source}/{rid}; building normally.")
            return False
        remaining = wait_seconds - (clock() - started)
        require(remaining > 0, "Timed out waiting for the already-active exact-source Cesium RID producer.")
        if not announced:
            print(f"CESIUM_SDK_WAIT: exact-source {rid} producer is already active; avoiding a duplicate SDK compilation.")
            announced = True
        sleep(min(30, remaining))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("identity", "create", "consume"))
    parser.add_argument("--rid", required=True, choices=("win-x64", "linux-x64", "osx-arm64"))
    parser.add_argument("--source")
    parser.add_argument("--run-id")
    parser.add_argument("--fallback-on-toolchain-mismatch", action="store_true",
                        help="Treat a verified artifact with toolchain/runner identity mismatch as non-reusable.")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    host = toolchain_identity()
    if args.operation == "identity":
        value = "toolchain=" + identity_key(host)
    else:
        require(args.source is not None and re.fullmatch(r"[0-9a-f]{40}", args.source) is not None,
                "Cesium producer source is required.")
        if args.operation == "create":
            destination = root / "artifacts/cesium-sdk" / args.rid
            write_artifact(root, args.rid, destination, args.source, host)
            value = "created=true"
        else:
            reused = consume(root, args.rid, args.source, host, args.run_id,
                             fallback_on_toolchain_mismatch=args.fallback_on_toolchain_mismatch)
            value = "reused=" + str(reused).lower()
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as stream:
        stream.write(value + "\n")


if __name__ == "__main__":
    main()
