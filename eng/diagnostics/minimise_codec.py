"""Exact two original codec binaries only; bounded distinct controls, not qualification."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath
import selectors
import subprocess
import sys
import tempfile
import time
import zipfile

root = Path(os.environ["GITHUB_WORKSPACE"])
module_path = Path(__file__).with_name("diagnose_397a_linux_once.py")
spec = importlib.util.spec_from_file_location("original397_codec_transport", module_path)
transport = importlib.util.module_from_spec(spec)
spec.loader.exec_module(transport)
out = root / "artifacts/397a-codec-minimiser"
out.mkdir(parents=True, exist_ok=False)
records = []
with tempfile.TemporaryDirectory(prefix="397-codec-min-", dir=root) as temporary:
    temporary = Path(temporary)
    original = transport.acquire_original(temporary, os.environ["GH_TOKEN"])
    selected = {}
    with zipfile.ZipFile(original) as package:
        for name in ("libzstd.so.1", "libblosc.so.1", "libusd_ms.so"):
            path = transport.PREFIX + "lib/" + name
            item = package.getinfo(path)
            assert 0 < item.file_size < 128 * 1024 * 1024 and not item.is_dir()
            binary = temporary / name
            with package.open(item) as stream, binary.open("xb") as output:
                while chunk := stream.read(1048576):
                    output.write(chunk)
            selected[name] = {"bytes": binary.stat().st_size, "sha256": transport.digest(binary)}
    assert selected["libzstd.so.1"]["sha256"] == "933f9ee9b786f99a90db14ca383808f6a5dcef135a303cee907ce231ac0d3915"
    elf = {}
    static_paths = {name: temporary / name for name in selected}
    static_paths["host-libzstd.so.1"] = Path("/lib/x86_64-linux-gnu/libzstd.so.1")
    gallium = Path("/lib/x86_64-linux-gnu/libgallium-25.2.8-0ubuntu0.24.04.4.so")
    if not gallium.is_file():
        raise RuntimeError("Exact observed Gallium package is absent; no substituted host control.")
    static_paths["host-gallium"] = gallium
    for name, path in static_paths.items():
        text = subprocess.check_output(["readelf", "--dyn-syms", "--wide", str(path)],
                                       timeout=5, text=True)
        elf[name] = {"sha256": transport.digest(path),
                     "zstdDynamicSymbols": [line.split() for line in text.splitlines() if " ZSTD_" in line][:512]}
    for case in ("alone", "host-alone", "blosc", "blosc-deep", "system", "system-deep"):
        started = time.monotonic()
        process = subprocess.Popen(
            [sys.executable, str(Path(__file__).with_name("codec_child.py")), str(temporary), case],
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, start_new_session=True)
        metadata, symptom = [], False
        try:
            stdout, stderr = process.communicate(timeout=5)
            if len(stdout) > 16384 or len(stderr) > 16384:
                raise RuntimeError("Private codec control output exceeds its bound.")
            metadata = [json.loads(line) for line in stdout.splitlines()]
            symptom = b"free(): invalid pointer" in stderr
        finally:
            transport.cleanup_session(process.pid)
            process.wait(timeout=2)
        records.append({"case": case, "exitCode": process.returncode, "exactAllocatorSymptom": symptom,
                        "allOwnedExitSeconds": time.monotonic() - started,
                        "metadata": metadata, "noRawStderrRetained": True})
result = {"kind": "Bounded codec minimiser; no Viewer/OpenUSD/Mesa target and no qualification",
          "originalSource": transport.SOURCE, "originalRun": transport.RUN,
          "originalArtifactSha256": transport.ZIP_SHA, "actualOriginalCodecFiles": selected,
          "hostCodecSha256": transport.digest(Path("/lib/x86_64-linux-gnu/libzstd.so.1")),
          "actualImageVersion": os.environ.get("ImageVersion"),
          "exportedZstdSymbols": elf, "controls": records}
(out / "result.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print("CODEC_MINIMISER_COMPLETED", json.dumps([{key: item[key] for key in (
    "case", "exitCode", "exactAllocatorSymptom")} for item in records]))
