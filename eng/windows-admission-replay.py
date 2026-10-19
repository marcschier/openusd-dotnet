"""Diagnostic-only exact Windows probe/PhysX-Core/runtime transport; never qualified native input."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import tarfile
import tempfile
import zipfile

SDK = "native/install/win-x64"
VARIANT = "native/build/shim/win-x64-physx"
PROBE = VARIANT + "/tests/openusd_property_admission_probe.exe"
CORE = VARIANT + "/openusd_dotnet/openusd_dotnet.dll"
CASE = "openusd_property_admission_targets"
MODE = "targets"
SCENE = VARIANT + "/tests/property-probe/targets.usda"
PLUGINS = SDK + "/lib/usd"
WORKING = VARIANT + "/tests"
PRESET = "win-x64-physx"
EXPORT_STEP = "Export exact Windows admission replay inputs (diagnostic only)"
ARTIFACT_PREFIX = "openusd-windows-admission-replay-"
INPUTS = (
    "eng/openusd.lock.json", "eng/openusd.install.lock.json", "eng/openusd-runtime-patches.lock.json",
    "eng/openusd-storage-admission.lock.json", "eng/native-install-metadata.ps1",
    "eng/sdk-runtime-patch-metadata.ps1", "eng/sdk-storage-admission-metadata.ps1",
    "eng/generate-native-notices.py", "eng/cesium-sdk-artifact.py",
    "eng/windows-admission-replay.py",
    "native/CMakePresets.json", "native/openusd_dotnet/tests/property_admission_probe.cpp",
    "native/openusd_dotnet/tests/property_inspection_probe.cmake",
    "LICENSE", "NOTICE",
)


def select_base_render_profile():
    global VARIANT, PROBE, CORE, CASE, SCENE, WORKING, PRESET, EXPORT_STEP, ARTIFACT_PREFIX, INPUTS
    VARIANT = "native/build/shim/win-x64"
    PROBE = VARIANT + "/tests/openusd_render_admission_probe.exe"
    CORE = VARIANT + "/openusd_dotnet/openusd_dotnet.dll"
    CASE = "openusd_render_admission_targets"
    SCENE = VARIANT + "/tests/render-admission-targets.usda"
    WORKING = VARIANT + "/tests"
    PRESET = "win-x64"
    EXPORT_STEP = "Export exact Windows base render admission inputs (diagnostic only)"
    ARTIFACT_PREFIX = "openusd-windows-base-render-admission-replay-"
    INPUTS = tuple(name for name in INPUTS if name not in (
        "native/openusd_dotnet/tests/property_admission_probe.cpp",
        "native/openusd_dotnet/tests/property_inspection_probe.cmake")) + (
            "native/openusd_dotnet/tests/render_admission_probe.cpp", "native/tests/CMakeLists.txt")


def require(value, message):
    if not value:
        raise ValueError(message)


def bootstrap_path(root, relative):
    require(not relative.is_absolute() and ".." not in relative.parts, "Invalid replay bootstrap path.")
    target = root / relative
    current = root
    for part in relative.parts:
        current /= part
        require(not current.is_symlink() and not current.is_junction(),
                "Replay bootstrap input has a linked ancestor or file.")
    require(target.resolve().is_relative_to(root.resolve()), "Replay bootstrap input escapes its assigned root.")
    return target


def boundaries(root):
    path = bootstrap_path(root, Path("eng/cesium-sdk-artifact.py"))
    spec = importlib.util.spec_from_file_location("owned_replay_archive_boundaries", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def inputs(root, b):
    return {name: b.digest(b.confined_destination(root, Path(name))) for name in INPUTS}


def guarded_tree(root, b, relative):
    pending = [b.confined_destination(root, relative)]
    while pending:
        directory = pending.pop()
        require(directory.is_dir(), "Replay verification directory is missing.")
        for child in directory.iterdir():
            child = b.confined_destination(root, child.relative_to(root))
            if child.is_dir():
                pending.append(child)
            elif child.is_file():
                yield child


def producer_preflight(root, b):
    for name in INPUTS:
        require(b.confined_destination(root, Path(name)).is_file(), "Replay verification source input is missing.")
    for relative in (Path("eng"), Path(SDK)):
        for _ in guarded_tree(root, b, relative):
            pass
    lock = json.loads(b.confined_destination(root, Path("eng/openusd.lock.json")).read_bytes())
    compiler = Path("native/install") / ("vulkan-sdk-" + lock["vulkanSdk"]["version"])
    if b.confined_destination(root, compiler).exists():
        for _ in guarded_tree(root, b, compiler):
            pass
    for name in (VARIANT, str(PurePosixPath(PROBE).parent), str(PurePosixPath(CORE).parent)):
        require(b.confined_destination(root, Path(name)).is_dir(), "Original replay build directory is missing.")
    for name in (PROBE, CORE, str(PurePosixPath(PROBE).with_suffix(".pdb")),
                 str(PurePosixPath(CORE).with_suffix(".pdb")),
                 VARIANT + "/Testing/Temporary/LastTest.log",
                 VARIANT + "/Testing/Temporary/LastTestsFailed.log"):
        b.confined_destination(root, Path(name))
    for name in ("native/openusd_dotnet/include", "native/openusd_hydra/include",
                 "native/hdSilk/include", "native/openusd_storm_child/include", "native/openusd_physx/include"):
        path = b.confined_destination(root, Path(name))
        if path.exists():
            for _ in guarded_tree(root, b, Path(name)):
                pass


def verify_original_sdk(root):
    for script, arguments in (
        ("sdk-runtime-patch-metadata.ps1", ["-Operation", "Verify", "-SdkRoot", SDK, "-Rid", "win-x64"]),
        ("sdk-storage-admission-metadata.ps1", ["-Operation", "Verify", "-SdkRoot", SDK, "-Rid", "win-x64"]),
        ("native-install-metadata.ps1", ["-Operation", "Verify", "-Rid", "win-x64", "-RequireVulkanRuntime"]),
    ):
        subprocess.run(["pwsh", "-NoProfile", "-File", str(root / "eng" / script), *arguments],
                       cwd=root, check=True, timeout=120)
    subprocess.run(["python", str(root / "eng/generate-native-notices.py"), "verify",
                    "--rid", "win-x64", "--install-root", SDK], cwd=root, check=True, timeout=120)


def collect(root, b):
    sdk = b.confined_destination(root, Path(SDK))
    variant = b.confined_destination(root, Path(VARIANT))
    require(sdk.is_dir() and variant.is_dir(), "The actual SDK/PhysX build variant is missing.")
    files, names = {}, {}

    def add(path, boundary):
        physical = b.system_path(path)
        require(physical.resolve().is_relative_to(b.system_path(boundary).resolve()),
                "Replay input link escapes its actual input root.")
        if not physical.is_file():
            return
        logical_root = b.system_path(root) if str(path).startswith("\\\\?\\") else root
        name = path.relative_to(logical_root).as_posix()
        if physical.suffix.casefold() in (".dll", ".pdb") and physical.stem.casefold().startswith("openusd_"):
            selected = {PROBE, CORE, str(PurePosixPath(PROBE).with_suffix(".pdb")),
                        str(PurePosixPath(CORE).with_suffix(".pdb"))}
            if name not in selected:
                return
        folded = b.portable_member(name)
        require(folded not in names or names[folded] == name, "Replay input has a case-fold alias.")
        require(not re.search(r"(?i)(physxgpu|physxdevice|nvidia|\.dmp$|\.core$)", name),
                "Proprietary GPU modules or dumps may not enter a replay artifact.")
        names[folded], files[name] = name, physical

    for directory in ("bin", "lib"):
        base = b.confined_destination(root, Path(SDK) / directory)
        for path in base.glob("*") if base.is_dir() else ():
            path = b.confined_destination(root, path.relative_to(root))
            if path.suffix.lower() in (".dll", ".pdb"):
                if path.stem.casefold().startswith("openusd_"):
                    continue
                add(path, sdk)
    plugins = b.confined_destination(root, Path(SDK) / "lib/usd")
    require(plugins.is_dir(), "Original SDK plugin/resource closure is missing.")
    for path in guarded_tree(root, b, Path(SDK) / "lib/usd"):
        add(path, sdk)
    for name in (".openusd-install-metadata.json", ".openusd-runtime-patches.json",
                 ".openusd-storage-admission.json", ".openusd-native-notices.json", "THIRD-PARTY.md"):
        path = b.confined_destination(root, Path(SDK) / name)
        require(path.is_file(), "Original SDK verification/notice evidence is missing.")
        add(path, sdk)
    public = set()
    for lock_name in ("eng/openusd-runtime-patches.lock.json", "eng/openusd-storage-admission.lock.json"):
        lock = json.loads(b.confined_destination(root, Path(lock_name)).read_bytes())
        for patch in lock["patches"]:
            for entry in patch["files"]:
                if entry["path"].endswith(".h") and entry.get("installedHeader") is not False:
                    name = SDK + "/include/" + entry["path"]
                    path = b.confined_destination(root, Path(name))
                    require(path.is_file() and b.digest(path).upper() == entry["afterSha256"].upper(),
                            "Original installed patched header identity differs.")
                    public.add(name)
                    add(path, sdk)
    for name in (PROBE, CORE):
        path = b.confined_destination(root, Path(name))
        require(path.is_file(), "The exact original probe/selected-variant Core module is missing.")
        add(path, variant)
        pdb = path.with_suffix(".pdb")
        if pdb.is_file():
            add(pdb, variant)
    require(any(name.startswith(SDK + "/lib/usd/") for name in files), "Original SDK plugins were not captured.")
    require(SDK + "/lib/usd_ms.dll" in files, "Original verified SDK monolith is missing.")
    require(not any("/property-probe/" in name for name in files), "Generated authored scenes must not be captured.")
    return files, sorted(public)


def create(root, destination, source, host, original_suite_outcome="unobserved"):
    require(re.fullmatch("[a-f0-9]{40}", source), "Invalid producer source identity.")
    b = boundaries(root)
    destination = b.confined_destination(root, destination.relative_to(root))
    require(not destination.exists(), "Replay destination already exists.")
    producer_preflight(root, b)
    verify_original_sdk(root)
    files, headers = collect(root, b)
    require(original_suite_outcome in ("success", "failure", "unobserved"), "Invalid original suite outcome.")
    case_result = "unobserved"
    original_log = b.confined_destination(root, Path(VARIANT) / "Testing/Temporary/LastTest.log")
    failed_log = b.confined_destination(root, Path(VARIANT) / "Testing/Temporary/LastTestsFailed.log")
    if original_log.is_file() and original_log.stat().st_size <= 8388608:
        if ("Testing: " + CASE + "\n").encode() in original_log.read_bytes().replace(b"\r\n", b"\n"):
            require(not failed_log.exists() or failed_log.stat().st_size <= 65536,
                    "Original CTest failure metadata exceeds its bound.")
            failed = failed_log.read_text().splitlines() if failed_log.is_file() else []
            case_result = "failure" if any(line.partition(":")[2] == CASE
                                           for line in failed) else "not-listed-as-failed"
    destination.mkdir(parents=True)
    archive = destination / "windows-admission-replay.tar.gz"
    manifest = {
        "schemaVersion": 1, "kind": "diagnostic-only-windows-admission-replay-not-qualified-native",
        "sourceHead": source, "producerRun": os.environ.get("GITHUB_RUN_ID"),
        "producerAttempt": os.environ.get("GITHUB_RUN_ATTEMPT"), "rid": "win-x64",
        "producerRepository": os.environ.get("GITHUB_REPOSITORY"),
        "producerRepositoryId": os.environ.get("GITHUB_REPOSITORY_ID"),
        "producerRef": os.environ.get("GITHUB_REF"),
        "originalSuiteOutcome": original_suite_outcome, "originalCaseRecordedOutcome": case_result,
        "actualProducerHost": host, "sourceInputs": inputs(root, b),
        "probe": PROBE, "coreVariant": CORE, "preset": PRESET,
        "originalCase": CASE, "mode": MODE, "freshScene": SCENE, "pluginRoot": PLUGINS,
        "originalWorkingDirectory": WORKING, "originalQuotaBytes": 1048576, "originalCtestDeadlineSeconds": 120,
        "installedPatchedHeaders": headers, "exactOriginalFilesNotRebuilt": True,
        "filesProveBuiltPhysxVariantNotHistoricalLoadedModuleIdentity": PRESET == "win-x64-physx",
        "selectedBuiltVariant": PRESET,
        "originalHistoricalCtestLoadedModulesUnobserved": True,
        "files": {name: {"bytes": path.stat().st_size, "sha256": b.digest(path)}
                  for name, path in sorted(files.items())},
    }
    with tarfile.open(archive, "w:gz", compresslevel=1, dereference=True) as package:
        for name, path in sorted(files.items()):
            package.add(b.system_path(path), arcname=name, recursive=False)
    manifest.update(archiveBytes=archive.stat().st_size, archiveSha256=b.digest(archive))
    (destination / "windows-admission-replay.json").write_text(
        json.dumps(manifest, indent=2) + "\n", encoding="utf-8", newline="\n")
    return manifest


def import_replay(root, output, archive, manifest, source, run_id, attempt, envelope):
    b = boundaries(root)
    output = b.confined_destination(root, output.relative_to(root))
    require(not output.exists(), "Replay destination already exists.")
    require(manifest.get("schemaVersion") == 1
            and manifest.get("kind") == "diagnostic-only-windows-admission-replay-not-qualified-native"
            and manifest.get("sourceHead") == source and manifest.get("rid") == "win-x64"
            and manifest.get("producerRun") == str(run_id) and manifest.get("producerAttempt") == str(attempt),
            "Replay source/run/attempt/kind identity differs.")
    require(manifest.get("producerRepository") == envelope["repository"]
            and manifest.get("producerRepositoryId") == str(envelope["repositoryId"])
            and manifest.get("producerRef") == envelope["ref"]
            and envelope["source"] == source and envelope["run"] == run_id
            and envelope["attempt"] == attempt,
            "Replay manifest repository/ref differs from the authenticated producer envelope.")
    require(manifest.get("sourceInputs") == inputs(root, b), "Replay source recipe differs.")
    require(manifest.get("probe") == PROBE and manifest.get("coreVariant") == CORE
            and manifest.get("preset") == PRESET and manifest.get("mode") == MODE
            and manifest.get("originalCase") == CASE and manifest.get("freshScene") == SCENE
            and manifest.get("pluginRoot") == PLUGINS and manifest.get("originalWorkingDirectory") == WORKING
            and manifest.get("originalQuotaBytes") == 1048576 and manifest.get("originalCtestDeadlineSeconds") == 120
            and manifest.get("originalHistoricalCtestLoadedModulesUnobserved") is True,
            "Replay original variant/case/qualification distinction differs.")
    require(archive.stat().st_size == manifest.get("archiveBytes")
            and b.digest(archive) == manifest.get("archiveSha256"), "Replay archive integrity differs.")
    expected = manifest.get("files")
    require(isinstance(expected, dict) and 0 < len(expected) <= 10000, "Invalid replay payload inventory.")
    required = {PROBE, CORE, SDK + "/lib/usd_ms.dll", SDK + "/.openusd-runtime-patches.json"}
    require(required <= set(expected), "Replay omits an original required binary/profile.")
    names = set()
    maximum = 0
    for name, entry in expected.items():
        canonical = b.portable_member(name)
        require(canonical not in names, "Replay archive has a path alias.")
        names.add(canonical)
        path = PurePosixPath(name)
        optional = {PROBE, CORE, str(PurePosixPath(PROBE).with_suffix(".pdb")),
                    str(PurePosixPath(CORE).with_suffix(".pdb"))}
        require(name.startswith(SDK + "/") or name in optional, "Replay has an unapproved module/input root.")
        require(not (path.stem.casefold().startswith("openusd_") and path.suffix.casefold() in (".dll", ".pdb"))
                or name in optional, "Replay contains an unselected default project module.")
        require("/property-probe/" not in name
                and not re.search(r"(?i)(physxgpu|physxdevice|nvidia|\.dmp$|\.core$)", name),
                "Replay contains an authored fixture/proprietary GPU module/dump.")
        require(isinstance(entry, dict) and type(entry.get("bytes")) is int
                and 0 <= entry["bytes"] <= 1073741824
                and re.fullmatch("[a-f0-9]{64}", entry.get("sha256", "")),
                "Replay file binding is invalid.")
        maximum += entry["bytes"]
    require(maximum <= 2147483648, "Replay expanded payload exceeds the bound.")
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="owned-replay-stage-", dir=b.system_path(output.parent)) as temporary:
        stage = Path(temporary)
        seen = set()
        with tarfile.open(archive, "r:gz") as package:
            for member in package:
                require(member.isfile() and member.name in expected and member.name not in seen,
                        "Replay has a nonregular, duplicate or unbound archive member.")
                entry = expected[member.name]
                require(member.size == entry["bytes"], "Replay archive member length differs.")
                destination = b.confined_destination(stage, Path(member.name))
                destination.parent.mkdir(parents=True, exist_ok=True)
                content = package.extractfile(member)
                require(content is not None, "Replay archive member is unreadable.")
                digest = hashlib.sha256()
                length = 0
                with content, b.system_path(destination).open("xb") as stream:
                    for chunk in iter(lambda: content.read(1024 * 1024), b""):
                        length += len(chunk)
                        require(length <= entry["bytes"], "Replay member exceeds its bound.")
                        digest.update(chunk)
                        stream.write(chunk)
                require(length == entry["bytes"] and digest.hexdigest() == entry["sha256"],
                        "Replay file bytes differ from the actual original producer binding.")
                seen.add(member.name)
        require(seen == set(expected), "Replay payload is incomplete.")
        runtime = json.loads((stage / SDK / ".openusd-runtime-patches.json").read_bytes())
        require(runtime["patchLockSha256"] == inputs(root, b)["eng/openusd-runtime-patches.lock.json"].upper()
                and runtime["librarySha256"] == b.digest(stage / SDK / "lib/usd_ms.dll").upper(),
                "Replay SDK runtime/profile binding differs.")
        b.confined_destination(root, output.relative_to(root))
        require(not output.exists(), "Replay destination became occupied.")
        shutil.move(str(stage), str(output))


def verify_producer_metadata(run, job, artifact, source, repository_id, expected_ref):
    require(run.get("head_sha") == source and run.get("path") == ".github/workflows/native.yml"
            and run.get("repository", {}).get("id") == repository_id
            and run.get("head_repository", {}).get("id") == repository_id
            and run.get("status") == "completed" and run.get("conclusion") in ("success", "failure"),
            "Diagnostic replay producer source/repository/workflow identity differs.")
    repository = run.get("repository", {}).get("full_name")
    require(isinstance(repository, str) and re.fullmatch("[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository),
            "Diagnostic producer repository name is invalid.")
    if run.get("event") == "pull_request":
        requests = run.get("pull_requests", [])
        require(len(requests) == 1 and expected_ref == f"refs/pull/{requests[0]['number']}/merge",
                "Diagnostic producer pull-request ref differs.")
    else:
        require(expected_ref in ("refs/heads/" + run["head_branch"], "refs/tags/" + run["head_branch"]),
                "Diagnostic producer branch/tag ref differs.")
    require(job.get("run_id") == run["id"] and job.get("head_sha") == source
            and job.get("run_attempt") == run["run_attempt"]
            and job.get("name", "").endswith("win-x64 verified native archive")
            and job.get("status") == "completed" and job.get("conclusion") in ("success", "failure"),
            "Diagnostic replay original Windows producer job identity differs.")
    for name in ("Verify native install metadata", "Verify complete native redistribution notices",
                 "Verify complete SDK build-cache pair",
                 EXPORT_STEP):
        steps = [step for step in job.get("steps", []) if step.get("name") == name]
        require(len(steps) == 1 and steps[0].get("conclusion") == "success",
                "Diagnostic replay lacks an actual successful original SDK/export verification step.")
    binding = artifact.get("workflow_run", {})
    require(binding.get("id") == run["id"] and binding.get("head_sha") == source
            and binding.get("repository_id") == repository_id
            and binding.get("head_repository_id") == repository_id
            and binding.get("head_branch") == run.get("head_branch")
            and artifact.get("name") == ARTIFACT_PREFIX + str(run["run_attempt"])
            and type(artifact.get("id")) is int and artifact["id"] > 0
            and artifact.get("expired") is False
            and type(artifact.get("size_in_bytes")) is int
            and 0 < artifact["size_in_bytes"] <= 1073741824
            and re.fullmatch("sha256:[a-f0-9]{64}", artifact.get("digest", "")),
            "Listed diagnostic artifact source/run/repository/digest identity differs.")
    return {"repository": repository, "repositoryId": repository_id, "ref": expected_ref,
            "source": source, "run": run["id"], "attempt": run["run_attempt"]}


def import_downloaded_artifact(root, output, archive_zip, run, job, artifact, source, repository_id, expected_ref):
    envelope = verify_producer_metadata(run, job, artifact, source, repository_id, expected_ref)
    b = boundaries(root)
    require(archive_zip.stat().st_size == artifact["size_in_bytes"]
            and "sha256:" + b.digest(archive_zip) == artifact["digest"],
            "Downloaded diagnostic outer ZIP bytes do not match the authenticated artifact digest/length.")
    output = b.confined_destination(root, output.relative_to(root))
    require(not output.exists(), "Diagnostic replay destination already exists.")
    with zipfile.ZipFile(archive_zip) as package:
        members = package.infolist()
        expected = {"windows-admission-replay.json", "windows-admission-replay.tar.gz"}
        require(len(members) == 2 and set(package.namelist()) == expected,
                "Downloaded replay artifact has unexpected/duplicate ZIP members.")
        require(all(not item.is_dir() and not item.flag_bits & 1 and item.file_size <= 1073741824
                    and (item.external_attr >> 16) & 0o170000 in (0, 0o100000) for item in members),
                "Downloaded replay artifact has an unsafe ZIP member.")
        metadata = package.getinfo("windows-admission-replay.json")
        require(metadata.file_size <= 4194304, "Replay manifest exceeds its bound.")
        manifest = json.loads(package.read(metadata))
        output.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="owned-replay-inner-", dir=b.system_path(output.parent)) as temporary:
            archive = Path(temporary) / "windows-admission-replay.tar.gz"
            with package.open("windows-admission-replay.tar.gz") as stream, archive.open("xb") as destination:
                shutil.copyfileobj(stream, destination, 1048576)
            import_replay(root, output, archive, manifest, source, run["id"], run["run_attempt"], envelope)
    return manifest


def fetch_replay(root, output, source, run_id, repository, repository_id, expected_ref, token):
    b = boundaries(root)
    run = b.api(repository, token, f"actions/runs/{run_id}")
    jobs = b.api(repository, token, f"actions/runs/{run_id}/jobs?filter=latest&per_page=100")
    artifacts = b.api(repository, token, f"actions/runs/{run_id}/artifacts?per_page=100")
    require(jobs["total_count"] == len(jobs["jobs"]) <= 100
            and artifacts["total_count"] == len(artifacts["artifacts"]) <= 100,
            "Replay producer metadata requires explicit pagination.")
    selected_jobs = [job for job in jobs["jobs"] if job.get("name", "").endswith("win-x64 verified native archive")]
    selected_artifacts = [item for item in artifacts["artifacts"] if item.get("name") ==
                          ARTIFACT_PREFIX + str(run["run_attempt"])]
    require(len(selected_jobs) == len(selected_artifacts) == 1, "Exact diagnostic producer/artifact is unavailable.")
    job, artifact = selected_jobs[0], selected_artifacts[0]
    envelope = verify_producer_metadata(run, job, artifact, source, repository_id, expected_ref)
    require(envelope["repository"] == repository, "Replay API repository differs from its run metadata.")
    output = b.confined_destination(root, output.relative_to(root))
    require(not output.exists(), "Replay destination already exists.")
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="owned-replay-download-", dir=b.system_path(output.parent)) as temporary:
        archive_zip = Path(temporary) / "producer.zip"
        b.download_artifact(repository, token, artifact, archive_zip)
        return import_downloaded_artifact(root, output, archive_zip, run, job, artifact,
                                          source, repository_id, expected_ref)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--destination", type=Path, required=True)
    parser.add_argument("--source", required=True)
    parser.add_argument("--original-suite-outcome", choices=("success", "failure"), required=True)
    parser.add_argument("--base-render-targets", action="store_true")
    args = parser.parse_args()
    if args.base_render_targets:
        select_base_render_profile()
    require(os.name == "nt", "The exact replay capture is Windows-only.")
    require(all(re.fullmatch("[0-9]+", os.environ.get(name, "")) for name in (
        "GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT", "GITHUB_REPOSITORY_ID"))
        and os.environ.get("GITHUB_REPOSITORY") and os.environ.get("GITHUB_REF"),
        "Actual producer run/attempt/repository/ref identity is missing.")
    host = {name: os.environ.get(name) for name in ("ImageOS", "ImageVersion", "VCToolsVersion")}
    require(host["ImageVersion"] and host["VCToolsVersion"], "Actual Windows producer host/compiler metadata is missing.")
    value = create(args.root.resolve(), args.destination.resolve(), args.source, host, args.original_suite_outcome)
    print(f"DIAGNOSTIC_REPLAY_ONLY files={len(value['files'])}; NOT_NATIVE_QUALIFICATION_OR_RELEASE_INPUT")


if __name__ == "__main__":
    main()
