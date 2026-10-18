"""Distinct upstream-option controls against the exact original codec failure."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.request
import zipfile

root = Path(os.environ["GITHUB_WORKSPACE"])
spec = importlib.util.spec_from_file_location(
    "original397_transport", Path(__file__).with_name("diagnose_397a_linux_once.py"))
transport = importlib.util.module_from_spec(spec)
spec.loader.exec_module(transport)
out = root / "artifacts/397a-codec-repair"
out.mkdir(parents=True, exist_ok=False)
result = {
    "kind": "Private upstream codec-option experiment; not production or qualification",
    "originalSource": transport.SOURCE, "originalRun": transport.RUN,
    "originalArtifactSha256": transport.ZIP_SHA,
    "hostImage": os.environ.get("ImageVersion"), "controls": [], "builds": {},
}


def command(argv, log):
    with log.open("ab") as stream:
        subprocess.run(argv, stdout=stream, stderr=subprocess.STDOUT, check=True, timeout=150)


def download_source(name, temporary):
    matches = [item for item in json.loads((root / "eng/openusd.lock.json").read_text())["dependencies"]
               if item["name"] == name]
    assert matches and len({item["sha256"] for item in matches}) == 1
    item = matches[0]
    archive = temporary / (name + ".zip")
    with urllib.request.urlopen(item["url"], timeout=30) as response:
        payload = response.read(8 * 1024 * 1024 + 1)
    assert len(payload) <= 8 * 1024 * 1024
    assert hashlib.sha256(payload).hexdigest().upper() == item["sha256"]
    archive.write_bytes(payload)
    destination = temporary / (name + "-source")
    destination.mkdir()
    with zipfile.ZipFile(archive) as package:
        for member in package.infolist():
            target = (destination / member.filename).resolve()
            assert target.is_relative_to(destination.resolve())
        package.extractall(destination)
    directories = list(destination.iterdir())
    assert len(directories) == 1 and directories[0].is_dir()
    return directories[0], item["sha256"]


def symbols(path):
    text = subprocess.check_output(["readelf", "--dyn-syms", "--wide", str(path)],
                                   text=True, timeout=5)
    rows = [line.split() for line in text.splitlines() if " ZSTD_" in line]
    return {
        "sha256": transport.digest(path), "definedZstdCount": sum(row[6] != "UND" for row in rows),
        "api": [row for row in rows if row[-1] in (
            "ZSTD_compress", "ZSTD_compressCCtx", "ZSTD_createCCtx", "ZSTD_freeCCtx")],
    }


try:
    with tempfile.TemporaryDirectory(prefix="397-codec-repair-", dir=root) as workspace:
        temporary = Path(workspace)
        original_archive = transport.acquire_original(temporary, os.environ["GH_TOKEN"])
        original = temporary / "original"
        original.mkdir()
        with zipfile.ZipFile(original_archive) as archive:
            for name in ("libzstd.so.1", "libblosc.so.1"):
                payload = archive.read(transport.PREFIX + "lib/" + name)
                (original / name).write_bytes(payload)
        assert transport.digest(original / "libzstd.so.1") == (
            "933f9ee9b786f99a90db14ca383808f6a5dcef135a303cee907ce231ac0d3915")
        assert transport.digest(original / "libblosc.so.1") == (
            "c500270f4a8e5a76c565f69c69628d9537539c0511a080a2dc2fb540bf9852c8")
        zstd_source, zstd_sha = download_source("zstd", temporary)
        blosc_source, blosc_sha = download_source("Blosc", temporary)
        result["sourceArchives"] = {"zstd": zstd_sha, "Blosc": blosc_sha}
        prefixes = {}
        for policy in ("default", "protected"):
            build, prefix = temporary / ("zstd-build-" + policy), temporary / ("zstd-" + policy)
            args = ["cmake", "-S", str(zstd_source / "build/cmake"), "-B", str(build),
                    "-DCMAKE_BUILD_TYPE=Release", "-DCMAKE_INSTALL_PREFIX=" + str(prefix),
                    "-DCMAKE_INSTALL_LIBDIR=lib", "-DBUILD_SHARED_LIBS=ON",
                    "-DZSTD_BUILD_SHARED=ON", "-DZSTD_BUILD_STATIC=OFF",
                    "-DZSTD_BUILD_PROGRAMS=OFF", "-DZSTD_BUILD_TESTS=OFF"]
            if policy == "protected":
                args += ["-DCMAKE_C_VISIBILITY_PRESET=hidden", "-DZSTDLIB_VISIBLE=protected",
                         "-DZSTDERRORLIB_VISIBLE=protected", "-DZDICTLIB_VISIBLE=protected"]
            log = temporary / ("zstd-" + policy + ".log")
            started = time.monotonic()
            command(args, log)
            command(["cmake", "--build", str(build), "--parallel", "4"], log)
            command(["cmake", "--install", str(build)], log)
            prefixes[policy] = prefix
            result["builds"]["zstd-" + policy] = {
                "seconds": time.monotonic() - started, "options": args[5:],
                "symbols": symbols(prefix / "lib/libzstd.so.1"),
            }
            if policy == "protected":
                assert all(row[5] == "PROTECTED" for row in
                           result["builds"]["zstd-protected"]["symbols"]["api"])
        for policy, prefix in prefixes.items():
            build = temporary / ("blosc-build-" + policy)
            log = temporary / ("blosc-" + policy + ".log")
            args = ["cmake", "-S", str(blosc_source), "-B", str(build),
                    "-DCMAKE_POLICY_VERSION_MINIMUM=3.5", "-DCMAKE_BUILD_TYPE=Release",
                    "-DCMAKE_INSTALL_PREFIX=" + str(prefix), "-DCMAKE_INSTALL_LIBDIR=lib",
                    "-DBUILD_SHARED=ON", "-DBUILD_STATIC=OFF", "-DBUILD_TESTS=OFF",
                    "-DBUILD_FUZZERS=OFF", "-DBUILD_BENCHMARKS=OFF",
                    "-DPREFER_EXTERNAL_ZSTD=ON", "-DZSTD_INCLUDE_DIR=" + str(prefix / "include"),
                    "-DZSTD_LIBRARY=" + str(prefix / "lib/libzstd.so")]
            started = time.monotonic()
            command(args, log)
            command(["cmake", "--build", str(build), "--parallel", "4"], log)
            command(["cmake", "--install", str(build)], log)
            audit = symbols(prefix / "lib/libblosc.so.1")
            assert audit["definedZstdCount"] == 0
            result["builds"]["blosc-" + policy] = {
                "seconds": time.monotonic() - started, "symbols": audit,
                "lockedExternalZstd": True,
            }
        variants = {
            "original": (original / "libzstd.so.1", original / "libblosc.so.1"),
            "rebuilt-default-original-blosc": (
                prefixes["default"] / "lib/libzstd.so.1", original / "libblosc.so.1"),
            "protected-original-blosc": (
                prefixes["protected"] / "lib/libzstd.so.1", original / "libblosc.so.1"),
            "external-default": (
                prefixes["default"] / "lib/libzstd.so.1", prefixes["default"] / "lib/libblosc.so.1"),
            "external-protected": (
                prefixes["protected"] / "lib/libzstd.so.1", prefixes["protected"] / "lib/libblosc.so.1"),
        }
        for variant, (zstd, blosc) in variants.items():
            directory = temporary / ("control-" + variant)
            directory.mkdir()
            shutil.copyfile(zstd, directory / "libzstd.so.1")
            shutil.copyfile(blosc, directory / "libblosc.so.1")
            for case in ("alone", "blosc", "system"):
                started = time.monotonic()
                process = subprocess.Popen(
                    [sys.executable, str(Path(__file__).with_name("codec_child.py")),
                     str(directory), case],
                    stdout=subprocess.PIPE, stderr=subprocess.PIPE, start_new_session=True,
                    env={**os.environ, "LD_LIBRARY_PATH": str(directory)})
                try:
                    stdout, stderr = process.communicate(timeout=5)
                    assert len(stdout) <= 16384 and len(stderr) <= 16384
                finally:
                    transport.cleanup_session(process.pid)
                    process.wait(timeout=2)
                result["controls"].append({
                    "variant": variant, "case": case, "exitCode": process.returncode,
                    "exactAllocatorSymptom": b"free(): invalid pointer" in stderr,
                    "allOwnedExitSeconds": time.monotonic() - started,
                    "metadata": [json.loads(line) for line in stdout.splitlines()],
                    "noRawStderrRetained": True,
                })
        # These are diagnostic expectations, not an alteration of any production test.
        expected_red = [item for item in result["controls"] if item["variant"] in
                        ("original", "rebuilt-default-original-blosc") and item["case"] != "alone"]
        expected_green = [item for item in result["controls"] if item["variant"] == "external-protected"]
        result["originalAndRebuiltDefaultReproduce"] = all(
            item["exitCode"] == -6 and item["exactAllocatorSymptom"] for item in expected_red)
        result["supportedOptionsRemoveSymptom"] = all(
            item["exitCode"] == 0 and not item["exactAllocatorSymptom"] for item in expected_green)
        assert result["originalAndRebuiltDefaultReproduce"] and result["supportedOptionsRemoveSymptom"]
finally:
    (out / "result.json").write_text(json.dumps(result, indent=2) + "\n")
print(json.dumps({key: result[key] for key in (
    "originalAndRebuiltDefaultReproduce", "supportedOptionsRemoveSymptom")}))
