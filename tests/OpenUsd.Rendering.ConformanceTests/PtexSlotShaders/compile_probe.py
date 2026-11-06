# Copyright (c) marcschier. Licensed under the MIT License.

"""Explicit, admission-gated candidate compiler; never run by production builds."""

import argparse
import hashlib
import json
import pathlib
import re
import subprocess


def resource_index(parameter, context):
    if "binding" in parameter and "bindings" not in parameter:
        bindings = [parameter["binding"]]
    elif "bindings" in parameter and "binding" not in parameter:
        bindings = parameter["bindings"]
    else:
        raise RuntimeError(f"{context}: missing or ambiguous reflected binding")
    active = []
    for binding in bindings:
        count = binding.get("count", 1)
        if count not in (0, 1):
            raise RuntimeError(f"{context}: unexpected binding count {count}")
        if count == 1:
            active.append(binding)
    if len(active) != 1 or "index" not in active[0]:
        raise RuntimeError(f"{context}: expected exactly one active reflected binding")
    return active[0]["index"]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--slangc", type=pathlib.Path, required=True)
    parser.add_argument("--spirv-val", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    parser.add_argument("--admitted", action="store_true", required=True)
    parser.add_argument("--sampling", action="store_true")
    parser.add_argument("--mixed", action="store_true")
    args = parser.parse_args()
    root = pathlib.Path(__file__).resolve().parent
    lock = json.loads((root.parents[2] / "eng" / "shaders" / "toolchain.lock.json").read_text(
        encoding="utf-8"))
    version = subprocess.run(
        [str(args.slangc), "-version"], check=True, capture_output=True, text=True)
    if lock["slang"]["version"] not in version.stdout + version.stderr:
        raise RuntimeError("The candidate requires the repository-pinned Slang version")
    validator = subprocess.run(
        [str(args.spirv_val), "--version"], check=True, capture_output=True, text=True)
    if lock["spirvTools"]["commit"] not in validator.stdout + validator.stderr:
        raise RuntimeError("The candidate requires the repository-pinned SPIR-V validator")
    args.output.mkdir(parents=True, exist_ok=True)
    sampled = args.sampling or args.mixed
    stem = "ptex-slot-mixed" if args.mixed else "ptex-slot-sampling" if args.sampling else "ptex-slot-probe"
    receipt = args.output / f"{stem}-inputs.json" if sampled else args.output / "probe-inputs.json"
    receipt.unlink(missing_ok=True)
    inputs = [
        root / "ptex-slot-lookup.slang", root / "ptex-slot-probe.slang",
        pathlib.Path(__file__).resolve(),
        root.parents[2] / "eng" / "shaders" / "toolchain.lock.json",
    ]
    if sampled:
        inputs.append(root / "ptex-slot-sampling.slang")
    if args.mixed:
        inputs.extend([
            root / "ptex-slot-mixed.slang",
            root.parents[2] / "eng" / "shaders" / "sources" / "material.surface.slang",
        ])
    input_hashes = {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in inputs}
    artifacts = {}
    for stage, profile in (("vertex", "vs_6_0"), ("fragment", "ps_6_0")):
        entry = f"ptexSlotProbe{stage.title()}Main"
        if sampled and stage == "fragment":
            entry = "ptexSlotMixedFragmentMain" if args.mixed else "ptexSlotSamplingFragmentMain"
        for target, suffix in (("dxil", "dxil"), ("spirv", "spv"), ("metal", "metal")):
            output = args.output / f"{stem}.{stage}.{suffix}"
            reflection = args.output / f"{stem}.{stage}.{target}.reflection.json"
            command = [
                str(args.slangc), str(root / f"{stem}.slang"),
                "-entry", entry, "-target", target,
                "-profile", profile + (
                    "+spirv_1_5" if target == "spirv" else
                    "+METAL_2_4" if target == "metal" else ""),
                "-matrix-layout-row-major",
                "-warnings-as-errors", "all", "-g0",
                "-line-directive-mode", "none", "-source-embed-style", "none", "-O2",
                f"-DOPENUSD_TARGET_METAL={int(target == 'metal')}",
                "-o", str(output), "-reflection-json", str(reflection),
            ]
            if target == "spirv":
                command.append("-fvk-use-entrypoint-name")
            subprocess.run(command, cwd=root, check=True)
            if target == "spirv":
                subprocess.run(
                    [str(args.spirv_val), "--target-env", "vulkan1.2", str(output)],
                    check=True)
            parameters = json.loads(reflection.read_text(encoding="utf-8"))["parameters"]
            resources = [p for p in parameters if p.get("name") == "ptexSlotLookup"]
            if len(resources) != 1:
                raise RuntimeError(f"{target}/{stage}: expected one Ptex lookup resource")
            expected = 31 if target == "dxil" else 49 if target == "spirv" else 2
            if resource_index(resources[0], f"{target}/{stage}/lookup") != expected:
                raise RuntimeError(f"{target}/{stage}: unexpected lookup binding")
            if stage == "vertex":
                instances = [p for p in parameters if p.get("name") == "ptexProbeInstances"]
                if len(instances) != 1:
                    raise RuntimeError(f"{target}/{stage}: expected one instance resource")
                instance_type = instances[0]["type"]["resultType"]
                sizes = [size["value"] for size in instance_type["sizes"]
                         if size.get("kind") == "uniform"]
                metadata = [field for field in instance_type["fields"]
                            if field.get("name") == "lookupMetadata"]
                if sizes != [80] or len(metadata) != 1 or metadata[0]["binding"]["offset"] != 64:
                    raise RuntimeError(f"{target}/{stage}: unexpected probe instance layout")
            if sampled and stage == "fragment":
                for name, vk_index, register in (
                    ("ptexBaseColorTexture", 2, 0), ("ptexBaseColorSampler", 1, 0),
                    ("ptexRoughnessTexture", 4, 2), ("ptexRoughnessSampler", 11, 2),
                ):
                    matched = [p for p in parameters if p.get("name") == name]
                    expected_index = vk_index if target == "spirv" else register
                    if len(matched) != 1 or resource_index(
                            matched[0], f"{target}/{stage}/{name}") != expected_index:
                        raise RuntimeError(f"{target}/{stage}: unexpected sampled slot {name}")
            if target == "metal":
                source = output.read_text(encoding="utf-8")
                if not re.search(r"\bptexSlotLookup\w*\s*\[\[buffer\(2\)\]\]", source):
                    raise RuntimeError(f"{stage}: emitted Metal lookup is not buffer(2)")
                if re.search(r"\[\[buffer\((?:1|46|47|48|49)\)\]\]", source):
                    raise RuntimeError(f"{stage}: emitted Metal lookup uses a reserved index")
                # The generic Metal loader expects the host-visible entry "main".
                pattern = rf"(\[\[\s*{stage}\s*\]\])(?=[^\r\n]*\b{entry}\s*\()"
                source, count = re.subn(pattern, r'[[host_name("main")]] \1', source)
                if count != 1:
                    raise RuntimeError(f"{stage}: cannot identify exactly one Metal entry")
                output.write_text(source, encoding="utf-8", newline="\n")
            for path in (output, reflection):
                artifacts[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    if input_hashes != {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in inputs}:
        raise RuntimeError("Probe sources changed during compilation")
    receipt.write_text(json.dumps({
        "schemaVersion": 1, "inputs": input_hashes, "artifacts": artifacts,
        "tools": {
            "slangc": hashlib.sha256(args.slangc.read_bytes()).hexdigest(),
            "spirv-val": hashlib.sha256(args.spirv_val.read_bytes()).hexdigest(),
            "slangVersion": version.stdout + version.stderr,
            "validatorVersion": validator.stdout + validator.stderr,
        },
    }, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
