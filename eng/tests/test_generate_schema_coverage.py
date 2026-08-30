# Copyright (c) marcschier. Licensed under the MIT License.
"""Regression tests for the schema inventory generator's usda parser.

Run with:
    python -m unittest discover eng/tests

These need no OpenUSD install: the parser is exercised against synthetic
``generatedSchema.usda`` text that reproduces the shapes the real resources use --
documentation strings that contain class and property declarations, escaped quotes,
default values with brackets and parentheses, and flattened inherited properties.
"""

from __future__ import annotations

import importlib.machinery
import importlib.util
import json
import pathlib
import sys
import tempfile
import unittest

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
GENERATOR = REPO_ROOT / "eng" / "generate-schema-coverage.py"


def load_generator():
    loader = importlib.machinery.SourceFileLoader("generate_schema_coverage",
                                                  str(GENERATOR))
    spec = importlib.util.spec_from_loader(loader.name, loader)
    module = importlib.util.module_from_spec(spec)
    sys.modules[loader.name] = module
    loader.exec_module(module)
    return module


gsc = load_generator()


class BlankStringsTests(unittest.TestCase):
    def assert_offsets_preserved(self, text: str) -> str:
        blanked = gsc.blank_strings(text)
        self.assertEqual(len(blanked), len(text))
        self.assertEqual(blanked.count("\n"), text.count("\n"))
        return blanked

    def test_blanks_documentation_without_moving_offsets(self):
        text = 'class Sphere "Sphere" (\n    doc = "class Fake { }"\n)\n{\n}\n'
        blanked = self.assert_offsets_preserved(text)

        self.assertNotIn("Fake", blanked)
        index = blanked.index("class Sphere ")
        self.assertEqual(text[index:index + 21], 'class Sphere "Sphere"')

    def test_escaped_quote_does_not_end_the_string(self):
        text = 'a = "say \\"hi\\"" b = "second"\nc\n'
        blanked = self.assert_offsets_preserved(text)

        self.assertNotIn("hi", blanked)
        self.assertNotIn("second", blanked)
        # Everything outside the two literals survives verbatim.
        self.assertEqual(blanked, 'a = "          " b = "      "\nc\n')

    def test_trailing_escaped_backslash_does_not_leak_into_code(self):
        text = 'a = "escaped backslash \\\\"\nb = 1\n'
        blanked = self.assert_offsets_preserved(text)

        self.assertNotIn("escaped", blanked)
        self.assertTrue(blanked.endswith('"\nb = 1\n'))

    def test_unterminated_string_consumes_the_remainder(self):
        text = 'a = "never closed\nstill inside\n'
        blanked = self.assert_offsets_preserved(text)

        self.assertNotIn("closed", blanked)
        self.assertNotIn("inside", blanked)

    def test_apostrophe_inside_a_double_quoted_string(self):
        text = '"a prim\'s doc" outside\n'
        blanked = self.assert_offsets_preserved(text)

        self.assertTrue(blanked.endswith(" outside\n"))

    def test_triple_quoted_documentation_is_blanked_as_one_literal(self):
        text = '"""line one "quoted" still inside\nline two"""\nafter\n'
        blanked = self.assert_offsets_preserved(text)

        self.assertNotIn("inside", blanked)
        self.assertNotIn("line two", blanked)
        self.assertTrue(blanked.endswith('"""\nafter\n'))


class ParsePropertiesTests(unittest.TestCase):
    def parse(self, body: str) -> dict:
        return gsc.parse_properties(gsc.blank_strings(body))

    def test_reads_attributes_relationships_and_variability(self):
        body = (
            '    rel proxyPrim\n'
            '    uniform token purpose = "default" (\n'
            '        allowedTokens = ["default", "render"]\n'
            '    )\n'
            '    double radius = 1\n'
            '    point3f[] points\n'
        )
        properties = self.parse(body)

        self.assertEqual(
            sorted(properties), ["points", "proxyPrim", "purpose", "radius"])
        self.assertEqual(properties["proxyPrim"].kind, "relationship")
        self.assertEqual(properties["proxyPrim"].type_name, "rel")
        self.assertEqual(properties["purpose"].variability, "uniform")
        self.assertEqual(properties["radius"].variability, "varying")
        self.assertEqual(properties["points"].type_name, "point3f[]")

    def test_ignores_metadata_and_documentation_that_looks_like_a_property(self):
        body = (
            '    double radius = 1 (\n'
            '        customData = {\n'
            '            string userDocBrief = """double fakeProperty = 2\n'
            '        rel fakeRelationship"""\n'
            '        }\n'
            '        displayGroup = "Sizing"\n'
            '    )\n'
        )
        properties = self.parse(body)

        self.assertEqual(sorted(properties), ["radius"])

    def test_multi_line_default_values_do_not_produce_properties(self):
        body = (
            '    matrix4d transform = ( (1, 0, 0, 0),\n'
            '        (0, 1, 0, 0),\n'
            '        (0, 0, 1, 0),\n'
            '        (0, 0, 0, 1) )\n'
            '    token[] order = ["a",\n'
            '        "b"]\n'
            '    int count\n'
        )
        properties = self.parse(body)

        self.assertEqual(sorted(properties), ["count", "order", "transform"])

    def test_namespaced_and_instanced_property_names(self):
        body = (
            '    float inputs:shaping:cone:angle = 90\n'
            '    float drive:__INSTANCE_NAME__:physics:damping\n'
        )
        properties = self.parse(body)

        self.assertEqual(
            sorted(properties),
            ["drive:__INSTANCE_NAME__:physics:damping",
             "inputs:shaping:cone:angle"])


class ParseGeneratedSchemaTests(unittest.TestCase):
    LAYER = '''#usda 1.0
(
    "WARNING: THIS FILE IS GENERATED BY usdGenSchema.  DO NOT EDIT."
)

class "Imageable" (
    customData = {
        string userDocBrief = """Documentation that says class Decoy "Decoy" (
        and even double decoyProperty = 1
        and an escaped \\" quote, plus a brace }"""
    }
)
{
    token visibility = "inherited"
}

class Sphere "Sphere" (
    apiSchemas = ["FakeAPI"]
)
{
    token visibility = "inherited"
    double radius = 1
}
'''

    def test_finds_only_real_classes_and_their_properties(self):
        classes = gsc.parse_generated_schema(self.LAYER)

        self.assertEqual(sorted(classes), ["Imageable", "Sphere"])
        self.assertNotIn("Decoy", classes)
        self.assertEqual(sorted(classes["Sphere"]["properties"]),
                         ["radius", "visibility"])
        self.assertEqual(sorted(classes["Imageable"]["properties"]),
                         ["visibility"])

    def test_reads_builtin_api_schemas_from_the_original_text(self):
        classes = gsc.parse_generated_schema(self.LAYER)

        self.assertEqual(classes["Sphere"]["builtinApiSchemas"], ("FakeAPI",))
        self.assertEqual(classes["Imageable"]["builtinApiSchemas"], ())


class InventoryTests(unittest.TestCase):
    """Drives the whole generator against a synthetic install."""

    PLUG_INFO = """# comment lines are legal here
{
    "Plugins": [
        {
            "Info": {
                "Types": {
                    "%(prefix)sImageable": {
                        "alias": { "UsdSchemaBase": "Imageable" },
                        "bases": [ "UsdTyped" ],
                        "schemaIdentifier": "Imageable",
                        "schemaKind": "abstractTyped"
                    },
                    "%(prefix)sSphere": {
                        "alias": { "UsdSchemaBase": "Sphere" },
                        "bases": [ "%(prefix)sImageable" ],
                        "schemaIdentifier": "Sphere",
                        "schemaKind": "concreteTyped"
                    }
                }
            }
        }
    ]
}
"""

    CORE_PLUG_INFO = """{
    "Plugins": [
        {
            "Info": {
                "Types": {
                    "UsdTyped": {
                        "alias": { "UsdSchemaBase": "Typed" },
                        "schemaIdentifier": "Typed",
                        "schemaKind": "abstractBase"
                    }
                }
            }
        }
    ]
}
"""

    CORE_LAYER = '#usda 1.0\n\nclass "Typed" (\n)\n{\n}\n'

    # The parser fixture declares a built-in apiSchemas entry no library defines,
    # which build_inventory rightly rejects; the inventory fixture drops it.
    LAYER = ParseGeneratedSchemaTests.LAYER.replace(
        '    apiSchemas = ["FakeAPI"]\n', '')

    @staticmethod
    def prefix(library: str) -> str:
        return library[0].upper() + library[1:]

    def build_install(self, root: pathlib.Path, layer: str | None = None) -> None:
        for library in gsc.TARGET_LIBRARIES:
            resources = root / "lib" / "usd" / library / "resources"
            resources.mkdir(parents=True)
            (resources / "generatedSchema.usda").write_text(
                layer or self.LAYER, encoding="utf-8")
            (resources / "plugInfo.json").write_text(
                self.PLUG_INFO % {"prefix": self.prefix(library)},
                encoding="utf-8")
        for library in gsc.BASE_LIBRARIES:
            resources = root / "lib" / "usd" / library / "resources"
            resources.mkdir(parents=True)
            (resources / "generatedSchema.usda").write_text(
                self.CORE_LAYER, encoding="utf-8")
            (resources / "plugInfo.json").write_text(
                self.CORE_PLUG_INFO, encoding="utf-8")

    def test_inherited_properties_are_attributed_to_the_declaring_schema(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self.build_install(root)
            inventory = gsc.build_inventory(root)

        schemas = {
            schema["typeName"]: schema
            for library in inventory["libraries"]
            for schema in library["schemas"]
        }
        sphere = schemas["UsdGeomSphere"]
        imageable = schemas["UsdGeomImageable"]

        self.assertEqual([prop["name"] for prop in sphere["properties"]],
                         ["radius"])
        self.assertEqual(sphere["inheritedPropertyCount"], 1)
        self.assertEqual([prop["name"] for prop in imageable["properties"]],
                         ["visibility"])
        self.assertEqual(
            inventory["counts"]["schemas"], 2 * len(gsc.TARGET_LIBRARIES))
        self.assertEqual(
            inventory["counts"]["ownProperties"], 2 * len(gsc.TARGET_LIBRARIES))
        self.assertEqual(
            inventory["counts"]["inheritedProperties"], len(gsc.TARGET_LIBRARIES))

    def test_a_registered_type_with_no_class_fails_loudly(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self.build_install(root)
            resources = root / "lib" / "usd" / "usdGeom" / "resources"
            (resources / "generatedSchema.usda").write_text(
                '#usda 1.0\n\nclass "Imageable" (\n)\n{\n}\n', encoding="utf-8")

            with self.assertRaises(ValueError) as raised:
                gsc.build_inventory(root)

        self.assertIn("UsdGeomSphere", str(raised.exception))

    def test_an_unresolvable_base_fails_loudly(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self.build_install(root)
            resources = root / "lib" / "usd" / "usd" / "resources"
            (resources / "plugInfo.json").write_text(
                '{ "Plugins": [ { "Info": { "Types": { } } } ] }',
                encoding="utf-8")

            with self.assertRaises(ValueError) as raised:
                gsc.build_inventory(root)

        self.assertIn("UsdTyped", str(raised.exception))

    def test_an_unresolvable_builtin_api_schema_fails_loudly(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self.build_install(root, ParseGeneratedSchemaTests.LAYER)

            with self.assertRaises(ValueError) as raised:
                gsc.build_inventory(root)

        self.assertIn("FakeAPI", str(raised.exception))


class InstallIdentityTests(unittest.TestCase):
    PIN = {"version": "26.05", "tag": "v26.05", "commit": "a" * 40}

    def write_metadata(self, root: pathlib.Path, body: dict) -> None:
        (root / ".openusd-install-metadata.json").write_text(
            json.dumps(body), encoding="utf-8")

    def test_matching_metadata_is_accepted(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self.write_metadata(root, {"openUsdCommit": self.PIN["commit"]})
            gsc.verify_install_matches_pin(root, self.PIN, False)

    def test_missing_metadata_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            with self.assertRaises(ValueError):
                gsc.verify_install_matches_pin(root, self.PIN, False)

    def test_metadata_without_a_commit_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self.write_metadata(root, {"rid": "linux-x64"})
            with self.assertRaises(ValueError):
                gsc.verify_install_matches_pin(root, self.PIN, False)

    def test_mismatched_commit_is_rejected_even_with_the_override(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self.write_metadata(root, {"openUsdCommit": "b" * 40})
            with self.assertRaises(ValueError):
                gsc.verify_install_matches_pin(root, self.PIN, True)

    def test_the_override_waives_only_a_missing_file(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            gsc.verify_install_matches_pin(root, self.PIN, True)


if __name__ == "__main__":
    unittest.main()
