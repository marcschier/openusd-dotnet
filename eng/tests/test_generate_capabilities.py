# Copyright (c) marcschier. Licensed under the MIT License.

import importlib.machinery
import importlib.util
import pathlib
import sys
import unittest


GENERATOR = pathlib.Path(__file__).resolve().parents[1] / "generate-capabilities.py"
loader = importlib.machinery.SourceFileLoader("generate_capabilities", str(GENERATOR))
spec = importlib.util.spec_from_loader(loader.name, loader)
generator = importlib.util.module_from_spec(spec)
sys.modules[loader.name] = generator
loader.exec_module(generator)


class GenerateContractTests(unittest.TestCase):
    def setUp(self):
        self.capabilities = [
            generator.Capability("OPENUSD_CAPABILITY_STRING_LIST_V2", 0),
            generator.Capability("OPENUSD_CAPABILITY_SCHEMA_FACADES_TEST", 15),
            generator.Capability("OPENUSD_CAPABILITY_REVIEW_STAGE_ADOPTION", 35),
            generator.Capability("OPENUSD_CAPABILITY_REVIEW_SOURCE_PUBLICATION", 36),
        ]
        self.current = (
            "public static class OpenUsdNativeContract\n"
            "{\n"
            "    private const ulong CoreCapabilities = 0x1;\n"
            "    private const ulong SchemaFacadeCapabilities = 1UL << 15;\n"
            "    public const uint AbiVersion = 26;\n"
            "    public const ulong RequiredCapabilities =\n"
            "        CoreCapabilities | SchemaFacadeCapabilities | ReviewSourcePublicationCapability;\n"
            "    public const ulong ReviewSourcePublicationCapability = 1UL << 36;\n"
            "}\n"
        )

    def test_multiline_required_capabilities_is_regenerated_without_losing_named_constants(self):
        actual = generator.generate_contract(self.current, 26, self.capabilities)

        self.assertIn("CoreCapabilities = 0x1800000001;", actual)
        self.assertIn("SchemaFacadeCapabilities = 1UL << 15;", actual)
        self.assertIn("RequiredCapabilities = CoreCapabilities | SchemaFacadeCapabilities;", actual)
        self.assertIn("ReviewSourcePublicationCapability = 1UL << 36;", actual)

    def test_regeneration_is_idempotent_after_a_multiline_declaration(self):
        first = generator.generate_contract(self.current, 26, self.capabilities)

        self.assertEqual(generator.generate_contract(first, 26, self.capabilities), first)


if __name__ == "__main__":
    unittest.main()
