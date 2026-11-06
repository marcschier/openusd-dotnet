#!/usr/bin/env python3
# Copyright (c) marcschier. Licensed under the MIT License.

import copy
import fnmatch
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


CLASS = "SilkFlatOrdinalTransportConformanceTests"
METHOD = "SingleIndexedDrawCarriesFlatCompactOrdinalsWithoutProvingPtex"
SHADER_DIRECTORY = "tests/OpenUsd.Rendering.ConformanceTests/FlatOrdinalShaders/"
GENERATION = "0123456789abcdef0123456789abcdef"


def report_for(platform):
    backends = {
        "windows": ("D3D12", "Vulkan"),
        "linux": ("Vulkan",),
        "macos": ("Metal",),
    }[platform]
    tests = []
    for backend in backends:
        for ordinal in (255, 32767, 65534):
            for varying in ("False", "True"):
                tests.append({
                    "id": (
                        f"OpenUsd.Rendering.ConformanceTests.{CLASS}.1.1."
                        f"{METHOD}(Backend,Int32,Boolean).1.{len(tests)}.0"
                    ),
                    "displayName": f"{METHOD}({backend}, {ordinal}, {varying})",
                    "className": CLASS,
                    "methodName": METHOD,
                    "status": "passed",
                    "startTime": "2026-01-01T00:00:02Z",
                    "endTime": "2026-01-01T00:00:03Z",
                })
    summary = {
        "total": len(tests),
        "passed": len(tests),
        "failed": 0,
        "skipped": 0,
        "cancelled": 0,
        "timedOut": 0,
        "flaky": 0,
    }
    return {
        "schemaVersion": 1,
        "assemblyName": "OpenUsd.Rendering.ConformanceTests",
        "publicationGeneration": GENERATION,
        "summary": copy.deepcopy(summary),
        "groups": [{
            "namespace": "OpenUsd.Rendering.ConformanceTests",
            "className": CLASS,
            "summary": copy.deepcopy(summary),
            "tests": tests,
        }],
    }


def shader_receipt():
    inputs = [
        "eng/shaders/toolchain.lock.json",
        "eng/shaders/scripts/normalize-reflection.py",
        "eng/shaders/scripts/shader_model.py",
        f"{SHADER_DIRECTORY}build_flat_ordinal_shader.py",
        f"{SHADER_DIRECTORY}flat-ordinal.fragment.slang",
        f"{SHADER_DIRECTORY}flat-ordinal.vertex.slang",
        f"{SHADER_DIRECTORY}flat-ordinal.manifest.json",
    ]
    artifacts = [
        f"{SHADER_DIRECTORY}checked/flat-ordinal.{name}"
        for name in (
            "fragment.dxil",
            "fragment.dxil-reflection.json",
            "fragment.metal",
            "fragment.reflection.json",
            "fragment.spirv-reflection.json",
            "fragment.spv",
            "vertex.dxil",
            "vertex.dxil-reflection.json",
        )
    ]

    def record(path):
        return {"path": path, "size": 1, "sha256": "a" * 64}

    receipt = {
        "schemaVersion": 2,
        "inputs": [record(path) for path in inputs],
        "artifacts": [record(path) for path in artifacts],
    }
    files = [
        {"path": path, "size": 1, "sha256": "A" * 64}
        for path in (*inputs, *artifacts, f"{SHADER_DIRECTORY}checked/manifest.json")
    ]
    files.append({
        "path": f"{SHADER_DIRECTORY}verify_flat_ordinal_report.py",
        "size": 1,
        "sha256": "A" * 64,
    })
    return receipt, files


class ShaderWorkflowGateTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        root = Path(__file__).resolve().parents[2]
        cls.workflow = (root / ".github" / "workflows" / "shaders.yml").read_text(
            encoding="utf-8"
        )
        cls.ci = (root / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
        cls.gate = (root / "eng" / "run-compact-ordinal-conformance.ps1").read_text(
            encoding="utf-8"
        )
        cls.validators = "function Get-FlatOrdinalContract\n" + cls.gate.split(
            "function Get-FlatOrdinalContract\n", 1
        )[1].split("\nfunction Get-FlatOrdinalInputs\n", 1)[0]

    @staticmethod
    def step(workflow, name):
        return workflow.split(f"      - name: {name}\n", 1)[1].split(
            "\n      - name:", 1
        )[0]

    def validate_cases(self, cases):
        pwsh = shutil.which("pwsh")
        self.assertIsNotNone(pwsh, "PowerShell is required to exercise the gate validators.")
        harness = r"""
$ErrorActionPreference = 'Stop'
$cases = @(ConvertFrom-FlatOrdinalJson -Json ([Console]::In.ReadToEnd()))
$started = [DateTimeOffset]::Parse('2026-01-01T00:00:00Z').UtcDateTime
$finished = [DateTimeOffset]::Parse('2026-01-01T00:00:10Z').UtcDateTime
$results = @(foreach ($case in $cases)
{
    try
    {
        if ($case.kind -ceq 'report')
        {
            $null = Assert-FlatOrdinalReport -Report $case.report -Platform $case.platform `
                -StartedUtc $started -FinishedUtc $finished -PriorGeneration ([string]$case.priorGeneration)
        }
        elseif ($case.kind -ceq 'receipt')
        {
            Assert-FlatOrdinalShaderReceipt -Receipt $case.receipt -Files @($case.files)
        }
        elseif ($case.kind -ceq 'inputs')
        {
            Assert-FlatOrdinalInputsUnchanged -Before @($case.before) -After @($case.after)
        }
        else { throw 'Unknown validation fixture.' }
        [pscustomobject]@{ name = $case.name; accepted = $true; error = '' }
    }
    catch
    {
        [pscustomobject]@{ name = $case.name; accepted = $false; error = $_.Exception.Message }
    }
})
[Console]::Out.Write((ConvertTo-Json -InputObject $results -Depth 20 -Compress))
"""
        result = subprocess.run(
            [pwsh, "-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
             self.validators + "\n" + harness],
            input=json.dumps(cases),
            capture_output=True,
            text=True,
            timeout=30,
            check=False,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        results = {item["name"]: item for item in json.loads(result.stdout)}
        self.assertEqual(set(results), {case["name"] for case in cases})
        for case in cases:
            with self.subTest(case=case["name"]):
                actual = results[case["name"]]
                self.assertEqual(actual["accepted"], case["accepted"], actual["error"])
                if not case["accepted"]:
                    self.assertTrue(actual["error"])

    def test_metal_ordinal_gate_is_triggered_by_its_dependencies(self):
        push = self.workflow.split("  push:\n", 1)[1].split("  pull_request:\n", 1)[0]
        pull_request = self.workflow.split("  pull_request:\n", 1)[1].split(
            "\npermissions:", 1
        )[0]
        dependencies = (
            "Directory.Build.props",
            "Directory.Packages.props",
            "global.json",
            "eng/shaders/**",
            "eng/run-managed-tests.ps1",
            "eng/prepare-vulkan-test-runtime.ps1",
            "eng/vulkan-test-runtime-registry.ps1",
            "eng/vulkan-test-runtime.lock.json",
            "eng/tests/test_shader_workflow_gates.py",
            "src/OpenUsd.Rendering/**",
            "src/OpenUsd.Rendering.Silk/**",
            "src/OpenUsd.Rendering.Silk.D3D12/**",
            "src/OpenUsd.Rendering.Silk.Vulkan/**",
            "src/OpenUsd.Rendering.Silk.Metal/**",
            "src/OpenUsd.Rendering.Storm/**",
            "tests/OpenUsd.Rendering.ConformanceTests/**",
            "eng/run-compact-ordinal-conformance.ps1",
        )
        probe_files = (
            "tests/OpenUsd.Rendering.ConformanceTests/SilkFlatOrdinalTransportConformanceTests.cs",
            "tests/OpenUsd.Rendering.ConformanceTests/FlatOrdinalProbeShader.cs",
            f"{SHADER_DIRECTORY}flat-ordinal.vertex.slang",
            f"{SHADER_DIRECTORY}checked/manifest.json",
            f"{SHADER_DIRECTORY}checked/flat-ordinal.fragment.spv",
        )
        for event, block in (("push", push), ("pull_request", pull_request)):
            for dependency in dependencies:
                with self.subTest(event=event, dependency=dependency):
                    self.assertIn(f"      - '{dependency}'", block)
            for path in probe_files:
                with self.subTest(event=event, path=path):
                    self.assertTrue(fnmatch.fnmatchcase(
                        path, "tests/OpenUsd.Rendering.ConformanceTests/**"
                    ))

    def test_checked_shader_text_uses_canonical_lf(self):
        root = Path(__file__).resolve().parents[2] / SHADER_DIRECTORY / "checked"
        paths = (*root.glob("*.json"), *root.glob("*.metal"))
        self.assertEqual(
            {path.name for path in paths},
            {
                "flat-ordinal.fragment.dxil-reflection.json",
                "flat-ordinal.fragment.metal",
                "flat-ordinal.fragment.reflection.json",
                "flat-ordinal.fragment.spirv-reflection.json",
                "flat-ordinal.vertex.dxil-reflection.json",
                "manifest.json",
            },
        )
        for path in paths:
            with self.subTest(path=path.name):
                content = path.read_bytes()
                self.assertNotIn(b"\r", content)
                self.assertTrue(content.endswith(b"\n"))
                self.assertFalse(content.endswith(b"\n\n"))

    def test_shader_text_canonicalization_rejects_bare_carriage_returns(self):
        root = Path(__file__).resolve().parents[2] / SHADER_DIRECTORY
        spec = importlib.util.spec_from_file_location(
            "flat_ordinal_shader_builder", root / "build_flat_ordinal_shader.py"
        )
        self.assertIsNotNone(spec)
        builder = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(builder)
        with tempfile.TemporaryDirectory() as directory:
            original = builder.CHECKED
            builder.CHECKED = Path(directory)
            try:
                paths = [
                    builder.CHECKED / name
                    for name in builder.ARTIFACTS
                    if name.endswith((".json", ".metal"))
                ]
                for path in paths:
                    path.write_bytes(b"first\r\nsecond\r\n\r\n")
                builder.canonicalize_text_artifacts()
                for path in paths:
                    self.assertEqual(path.read_bytes(), b"first\nsecond\n")
                (builder.CHECKED / "manifest.json").write_bytes(b"{}\n")
                builder.verify_text_artifacts()
                paths[0].write_bytes(b"first\rsecond\n")
                with self.assertRaisesRegex(ValueError, "bare carriage return"):
                    builder.canonicalize_text_artifacts()
                with self.assertRaisesRegex(ValueError, "must use LF"):
                    builder.verify_text_artifacts()
            finally:
                builder.CHECKED = original

    def test_flat_ordinal_gate_routes_to_windows_linux_and_metal_jobs(self):
        ci_step = self.step(self.ci, "Execute sampled flat ordinal transport conformance")
        self.assertIn("if: runner.os != 'macOS'", ci_step)
        self.assertIn(
            "./eng/run-compact-ordinal-conformance.ps1 -Configuration Release", ci_step
        )
        vulkan_step = self.step(self.ci, "Execute original Vulkan platform-partition evidence")
        self.assertIn("if: runner.os != 'macOS'", vulkan_step)
        self.assertIn("-Partition Vulkan -Configuration Release", vulkan_step)

        metal_step = self.step(self.workflow, "Validate Metal staging, conformance, and package")
        self.assertIn("if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }", metal_step)
        self.assertIn(
            "./eng/run-compact-ordinal-conformance.ps1 -Configuration Release", metal_step
        )
        self.assertIn("-Partition Metal -Configuration Release", metal_step)
        self.assertLess(
            metal_step.index("run-compact-ordinal-conformance.ps1"),
            metal_step.index("run-platform-conformance.ps1"),
        )
        self.assertLess(
            metal_step.index("if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }"),
            metal_step.index("run-compact-ordinal-conformance.ps1"),
        )
        self.assertIn("artifacts/platform-conformance", self.workflow)
        self.assertIn("artifacts/platform-conformance", self.ci)

    def test_gate_selects_only_the_new_transport_probe(self):
        self.assertIn(f"'/*/*/{CLASS}/'", self.gate)
        self.assertIn(f"'{METHOD}'", self.gate)
        self.assertNotIn("CompactOrdinalsSurviveInterpolatedTextureLookup", self.gate)
        self.assertNotIn("SilkMaterialStreamingTests", self.gate)
        self.assertIn(
            "not Ptex, atlas filtering, UV transport, or exhaustive correctness.", self.gate
        )
        self.assertIn("Assert-FlatOrdinalCompiledSources", self.gate)
        self.assertIn("Assert-FlatOrdinalInputsUnchanged", self.gate)
        self.assertIn("MetadataReaderProvider", self.gate)
        self.assertIn("document.HashAlgorithm", self.gate)
        self.assertIn("$reportTime -lt $started -or $reportTime -gt $finished", self.gate)
        self.assertIn("$reportHash -ceq $previousReportHash", self.gate)

    def test_report_validator_requires_exact_fresh_platform_rows_without_skips(self):
        cases = []
        for platform in ("windows", "linux", "macos"):
            cases.append({
                "name": f"{platform} exact rows",
                "kind": "report",
                "platform": platform,
                "report": report_for(platform),
                "priorGeneration": "fedcba9876543210fedcba9876543210",
                "accepted": True,
            })

        def rejected(name, change):
            report = report_for("linux")
            case = {
                "name": name,
                "kind": "report",
                "platform": "linux",
                "report": report,
                "priorGeneration": "fedcba9876543210fedcba9876543210",
                "accepted": False,
            }
            change(case)
            cases.append(case)

        def first(case):
            return case["report"]["groups"][0]["tests"][0]

        def duplicate_row(case):
            tests = case["report"]["groups"][0]["tests"]
            tests[1]["displayName"] = tests[0]["displayName"]

        def duplicate_id(case):
            tests = case["report"]["groups"][0]["tests"]
            tests[0]["id"] += ".Case"
            tests[1]["id"] = tests[0]["id"][:-4] + "case"

        rejected("old zero row", lambda c: first(c).__setitem__(
            "displayName", f"{METHOD}(Vulkan, 0, False)"
        ))
        rejected("wrong backend", lambda c: first(c).__setitem__(
            "displayName", f"{METHOD}(Metal, 255, False)"
        ))
        rejected("wrong macOS backend", lambda c: c.__setitem__("platform", "macos"))
        rejected("wrong boolean spelling", lambda c: first(c).__setitem__(
            "displayName", f"{METHOD}(Vulkan, 255, false)"
        ))
        rejected("duplicate row", duplicate_row)
        rejected("duplicate identity", duplicate_id)
        rejected("wrong method", lambda c: first(c).__setitem__("methodName", "Other"))
        rejected("wrong class", lambda c: first(c).__setitem__("className", "Other"))
        rejected("skipped row", lambda c: first(c).__setitem__("status", "skipped"))
        rejected("missing row", lambda c: c["report"]["groups"][0]["tests"].pop())
        rejected("missing skip summary", lambda c: c["report"]["summary"].pop("skipped"))
        rejected("string total", lambda c: c["report"]["summary"].__setitem__("total", "6"))
        rejected("flaky summary", lambda c: c["report"]["groups"][0]["summary"].__setitem__(
            "flaky", 1
        ))
        for field in ("failed", "skipped", "cancelled", "timedOut"):
            rejected(f"nonzero {field}", lambda c, field=field: c["report"]["summary"].__setitem__(
                field, 1
            ))
        rejected("prior generation reused", lambda c: c.__setitem__(
            "priorGeneration", GENERATION.upper()
        ))
        rejected("empty generation", lambda c: c["report"].__setitem__(
            "publicationGeneration", "0" * 32
        ))
        rejected("stale case", lambda c: first(c).__setitem__(
            "startTime", "2025-12-31T23:59:59Z"
        ))
        rejected("future case", lambda c: first(c).__setitem__(
            "endTime", "2026-01-01T00:00:11Z"
        ))
        rejected("missing time zone", lambda c: first(c).__setitem__(
            "startTime", "2026-01-01T00:00:02"
        ))
        rejected("multiple groups", lambda c: c["report"]["groups"].append(
            copy.deepcopy(c["report"]["groups"][0])
        ))
        self.validate_cases(cases)

    def test_shader_receipt_rejects_missing_stale_and_unlisted_inputs(self):
        receipt, files = shader_receipt()
        cases = [{
            "name": "verified receipt",
            "kind": "receipt",
            "receipt": receipt,
            "files": files,
            "accepted": True,
        }]

        def rejected(name, change):
            receipt, files = shader_receipt()
            case = {
                "name": name,
                "kind": "receipt",
                "receipt": receipt,
                "files": files,
                "accepted": False,
            }
            change(case)
            cases.append(case)

        def file_named(case, suffix):
            return next(item for item in case["files"] if item["path"].endswith(suffix))

        rejected("stale shader source", lambda c: file_named(
            c, "flat-ordinal.vertex.slang"
        ).__setitem__("sha256", "B" * 64))
        rejected("stale generated payload", lambda c: file_named(
            c, "flat-ordinal.fragment.spv"
        ).__setitem__("size", 2))
        rejected("missing generated payload", lambda c: c["files"].remove(
            file_named(c, "flat-ordinal.fragment.dxil")
        ))
        def remove_required_artifact(case):
            case["files"].remove(file_named(case, "flat-ordinal.vertex.dxil"))
            case["receipt"]["artifacts"] = [
                artifact for artifact in case["receipt"]["artifacts"]
                if not artifact["path"].endswith("flat-ordinal.vertex.dxil")
            ]

        rejected("omitted required artifact and record", remove_required_artifact)
        rejected("missing receipt", lambda c: c["files"].remove(
            file_named(c, "checked/manifest.json")
        ))
        rejected("missing required input record", lambda c: c["receipt"]["inputs"].pop())
        rejected("duplicate manifest record", lambda c: c["receipt"]["artifacts"].append(
            copy.deepcopy(c["receipt"]["inputs"][0])
        ))
        rejected("unlisted generated payload", lambda c: c["files"].append({
            "path": f"{SHADER_DIRECTORY}checked/unlisted.spv", "size": 1, "sha256": "A" * 64,
        }))
        rejected("unlisted shader source", lambda c: c["files"].append({
            "path": f"{SHADER_DIRECTORY}unlisted.slang", "size": 1, "sha256": "A" * 64,
        }))
        rejected("path traversal", lambda c: c["receipt"]["inputs"][0].__setitem__(
            "path", "../toolchain.lock.json"
        ))
        rejected("invalid receipt schema", lambda c: c["receipt"].__setitem__("schemaVersion", 1))
        self.validate_cases(cases)

    def test_provenance_snapshot_rejects_added_removed_or_changed_files(self):
        before = [
            {"path": "source.cs", "size": 1, "sha256": "A" * 64},
            {"path": "checked/shader.spv", "size": 2, "sha256": "B" * 64},
        ]
        cases = [{
            "name": "unchanged inputs",
            "kind": "inputs",
            "before": before,
            "after": copy.deepcopy(before),
            "accepted": True,
        }]
        for name, change in (
            ("removed file", lambda files: files.pop()),
            ("added file", lambda files: files.append(
                {"path": "checked/extra.spv", "size": 1, "sha256": "C" * 64}
            )),
            ("changed path", lambda files: files[0].__setitem__("path", "other.cs")),
            ("changed size", lambda files: files[0].__setitem__("size", 2)),
            ("changed hash", lambda files: files[1].__setitem__("sha256", "C" * 64)),
        ):
            after = copy.deepcopy(before)
            change(after)
            cases.append({
                "name": name,
                "kind": "inputs",
                "before": before,
                "after": after,
                "accepted": False,
            })
        self.validate_cases(cases)


if __name__ == "__main__":
    unittest.main()
