# Copyright (c) marcschier. Licensed under the MIT License.

import ast
import hashlib
import importlib.util
import math
from pathlib import Path
import re
import tempfile
import unittest

script = Path(__file__).resolve().parents[1] / "create-simready-box-scenario.py"
spec = importlib.util.spec_from_file_location("simready_scenario", script)
scenario = importlib.util.module_from_spec(spec)
spec.loader.exec_module(scenario)


class ScenarioTests(unittest.TestCase):
    def test_creates_only_a_removable_layer_with_explicit_units_and_one_body(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "root.usda"
            source.write_text("#usda 1.0\n", encoding="utf-8")
            before = source.read_bytes()
            output = Path(directory) / "scenario.usda"
            report = scenario.create_scenario(source, output)
            layer = output.read_text(encoding="utf-8")
            self.assertEqual(source.read_bytes(), before)
            self.assertEqual(report["scenarioSha256"], hashlib.sha256(output.read_bytes()).hexdigest())
            self.assertIn(f"@{source.resolve().as_posix()}@", layer)
            self.assertIn('upAxis = "Z"', layer)
            self.assertIn("metersPerUnit = 1", layer)
            self.assertIn("kilogramsPerUnit = 1", layer)
            self.assertIn("timeCodesPerSecond = 60", layer)
            self.assertEqual(layer.count("instanceable = false"), 1)
            self.assertEqual(layer.count('prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsMassAPI"]'), 1)
            self.assertIn("vector3f physics:velocity = (0, 0, 2)", layer)
            self.assertIn("!resetXformStack!", layer)
            self.assertNotIn("active = false", layer)
            self.assertNotIn("@LOCALIZED_ROOT@", layer)
            self.assertIsNone(report["cameraPath"])
            self.assertNotIn("def Camera", layer)
            expected = script.with_name("datasets").joinpath("simready-box-drop.usda.template").read_text(
                encoding="utf-8"
            ).replace("@LOCALIZED_ROOT@", f"@{source.resolve().as_posix()}@")
            self.assertEqual(layer, expected)

    def test_camera_looks_at_the_box_stack_without_filtering_the_source(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "root.usda"
            source.write_text('#usda 1.0\ndef Xform "World" {}\n', encoding="utf-8")
            before = source.read_bytes()
            output = Path(directory) / "scenario.usda"
            report = scenario.create_scenario(source, output, focused_camera=True)
            layer = output.read_text(encoding="utf-8")
            self.assertEqual(report["scenarioSha256"], hashlib.sha256(output.read_bytes()).hexdigest())
            self.assertEqual(report["cameraPath"], "/WarehouseDemoCamera")
            self.assertEqual(layer.count('def Camera "WarehouseDemoCamera"'), 1)
            matrix = ast.literal_eval(re.search(
                r"matrix4d xformOp:transform:warehouseCamera = (.+)", layer
            ).group(1))
            eye = (-20.1080493927, -39.1122589111, 4.9)
            target = (-24.6080493927, -43.1122589111, 1.9)
            right, up, back = [row[:3] for row in matrix[:3]]
            self.assertEqual(matrix[3], (*eye, 1))
            self.assertEqual([row[3] for row in matrix[:3]], [0, 0, 0])

            def dot(left, right):
                return sum(a * b for a, b in zip(left, right))

            for axis in (right, up, back):
                self.assertAlmostEqual(dot(axis, axis), 1, places=12)
            for left, right_axis in ((right, up), (right, back), (up, back)):
                self.assertAlmostEqual(dot(left, right_axis), 0, places=12)
            cross = (right[1] * up[2] - right[2] * up[1],
                     right[2] * up[0] - right[0] * up[2],
                     right[0] * up[1] - right[1] * up[0])
            self.assertAlmostEqual(dot(cross, back), 1, places=12)
            relative = tuple(b - a for a, b in zip(eye, target))
            self.assertAlmostEqual(dot(relative, right), 0, places=12)
            self.assertAlmostEqual(dot(relative, up), 0, places=12)
            self.assertLess(dot(relative, back), 0)
            self.assertGreater(up[2], 0)
            clipping = ast.literal_eval(re.search(r"float2 clippingRange = (.+)", layer).group(1))
            distance = math.sqrt(dot(relative, relative))
            self.assertEqual(clipping, (0.05, 20))
            self.assertLess(clipping[0], distance)
            self.assertGreater(clipping[1], distance)
            focal = float(re.search(r"float focalLength = (\S+)", layer).group(1))
            vertical = float(re.search(r"float verticalAperture = (\S+)", layer).group(1))
            horizontal = float(re.search(r"float horizontalAperture = (\S+)", layer).group(1))
            self.assertAlmostEqual(math.degrees(2 * math.atan(vertical / (2 * focal))), 45, places=12)
            self.assertAlmostEqual(horizontal / vertical, 1920 / 1080, places=12)
            self.assertIn('uniform token[] xformOpOrder = ["xformOp:transform:warehouseCamera"]', layer)
            self.assertNotIn("visibility =", layer)
            self.assertNotIn("active = false", layer)
            self.assertEqual(source.read_bytes(), before)

    def test_report_carries_the_explicit_mcp_root_settings(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "root.usda"
            source.write_text("#usda 1.0\n", encoding="utf-8")
            output = Path(directory) / "scenario.usda"
            report = scenario.create_scenario(source, output)
            self.assertEqual(report["stageSettings"], {
                "metersPerUnit": 1, "kilogramsPerUnit": 1, "upAxis": "Z",
                "startTimeCode": 0, "endTimeCode": 240,
                "timeCodesPerSecond": 60, "framesPerSecond": 60,
            })
            layer = output.read_text(encoding="utf-8")
            for name, value in report["stageSettings"].items():
                authored = f'"{value}"' if isinstance(value, str) else str(value)
                self.assertIn(f"{name} = {authored}", layer)

    def test_existing_outputs_and_unrepresentable_assets_are_refused(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "source@bad.usda"
            source.write_text("#usda 1.0\n", encoding="utf-8")
            output = Path(directory) / "scenario.usda"
            with self.assertRaises(ValueError):
                scenario.create_scenario(source, output)
            self.assertFalse(output.exists())
            source = Path(directory) / "source.usda"
            source.write_text("#usda 1.0\n", encoding="utf-8")
            output.write_text("preserve", encoding="utf-8")
            with self.assertRaises(ValueError):
                scenario.create_scenario(source, output)
            self.assertEqual(output.read_text(encoding="utf-8"), "preserve")


if __name__ == "__main__":
    unittest.main()
