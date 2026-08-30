# Copyright (c) marcschier. Licensed under the MIT License.
"""Generates the managed-facing inventory of the pinned OpenUSD schema registry.

The public facades under ``src/OpenUsd`` claim coverage of the standard OpenUSD
domain schemas. Nothing checked that claim against the schema registry the pinned
native install actually ships, so a schema or a property added by an OpenUSD bump
disappeared silently.

This script reads the registry the pinned install carries -- one
``plugInfo.json`` plus one ``generatedSchema.usda`` per schema library -- and emits
a deterministic inventory:

  schemas/openUsd/schema-registry.g.json   every schema type and declared property

``schemas/openUsd/managed-coverage.map.json`` is the hand-authored, reviewed
counterpart: it maps each inventory entry to the managed type or member that
represents it, or records an explicit exception. ``OpenUsdSchemaCoverageContractTests``
joins the two and fails when a schema or a declared property has neither.

The inventory is a pure function of the pinned install, so ``--verify`` fails when
the checked-in file drifts from it. That is the form to use in CI, in a job that
already stages ``native/install/<rid>``.

Inputs are discovered in this order:

  1. ``--install-root``
  2. ``$OPENUSD_ROOT``
  3. ``<repo>/native/install/<rid>`` for the first rid that carries a schema registry

The staged install must carry ``.openusd-install-metadata.json`` naming the pinned
OpenUSD commit, so an inventory can always be attributed to the revision it was read
from. ``--allow-unverified-install`` waives only the presence of that file, for the
parser tests that run against a synthetic registry.

Usage:
    python eng/generate-schema-coverage.py [--verify] [--install-root PATH]
"""

from __future__ import annotations

import argparse
import json
import os
import pathlib
import re
import sys
from dataclasses import dataclass, field

# The domain libraries the public facades claim coverage of. usd itself is parsed for
# base resolution only: UsdTyped, UsdAPISchemaBase and the core APIs are the roots the
# domain schemas inherit from, and their properties must not be attributed to a domain
# schema.
#
# usdPhysics here is the *standard* schema library that ships with OpenUSD, which the
# OpenUsd.Physics facades wrap. It is unrelated to schemas/openUsdPhysics, the codeless
# plugin this repository owns and generates from its own definition.
TARGET_LIBRARIES = (
    "usdGeom",
    "usdShade",
    "usdLux",
    "usdSkel",
    "usdVol",
    "usdRender",
    "usdMedia",
    "usdProc",
    "usdUI",
    "usdPhysics",
)

BASE_LIBRARIES = ("usd",)

# Plugin resource roots an OpenUSD install is known to use, most specific first.
PLUGIN_ROOTS = ("lib/usd", "plugin/usd", "share/usd")

RIDS = ("win-x64", "linux-x64", "osx-arm64")

CLASS_PATTERN = re.compile(
    r"^class\s+(?:(?P<type>[A-Za-z_][A-Za-z0-9_]*)\s+)?\"(?P<name>[^\"]+)\"",
    re.MULTILINE,
)

PROPERTY_PATTERN = re.compile(
    r"^(?:custom\s+)?"
    r"(?:(?P<variability>uniform|varying|config)\s+)?"
    r"(?P<type>rel|[A-Za-z][A-Za-z0-9_]*(?:\[\])?)\s+"
    r"(?P<name>[A-Za-z_][A-Za-z0-9_:]*)\s*(?:=|\(|$)"
)

APPLIED_KINDS = ("singleApplyAPI", "multipleApplyAPI")


@dataclass(frozen=True)
class Property:
    name: str
    type_name: str
    kind: str
    variability: str

    def to_json(self) -> dict:
        entry = {"name": self.name, "type": self.type_name, "kind": self.kind}
        if self.variability != "varying":
            entry["variability"] = self.variability
        return entry


@dataclass
class Schema:
    library: str
    type_name: str
    identifier: str
    kind: str
    bases: tuple = ()
    builtin_api_schemas: tuple = ()
    declared: dict = field(default_factory=dict)


def read_text(path: pathlib.Path) -> str:
    return path.read_text(encoding="utf-8")


def write_text(path: pathlib.Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8", newline="\n")
    print(f"wrote {path}")


def blank_strings(text: str) -> str:
    """Blanks the content of every string literal, preserving offsets.

    ``generatedSchema.usda`` carries documentation strings that contain braces,
    parentheses and lines that read exactly like a class or property declaration.
    Blanking their content first keeps the structural scan below honest.

    Every consumed character is replaced one for one -- a newline by a newline,
    anything else by a space -- so offsets and line numbers in the result address
    exactly the same characters as in the input. That is what lets the class scan
    match on the blanked text and read the class name back from the original.

    Backslash escapes are consumed as a unit. A literal such as ``"say \\"hi\\""``
    does not end at its embedded quote, and treating it as if it did would shift
    every subsequent string by one, turning code into string and string into code
    for the rest of the file.
    """
    out: list = []
    index = 0
    length = len(text)
    while index < length:
        char = text[index]
        if char not in "\"'":
            out.append(char)
            index += 1
            continue

        triple = text[index:index + 3]
        quote = triple if triple in ('"""', "'''") else char
        out.append(quote)
        index += len(quote)
        while index < length:
            if text[index] == "\\" and index + 1 < length:
                out.append(" ")
                out.append("\n" if text[index + 1] == "\n" else " ")
                index += 2
                continue
            if text.startswith(quote, index):
                break
            out.append("\n" if text[index] == "\n" else " ")
            index += 1
        if index < length:
            out.append(quote)
            index += len(quote)
    return "".join(out)


def match_delimited(text: str, start: int, opening: str, closing: str) -> int:
    """Returns the index just past the balanced block that starts at ``start``."""
    depth = 0
    index = start
    while index < len(text):
        char = text[index]
        if char == opening:
            depth += 1
        elif char == closing:
            depth -= 1
            if depth == 0:
                return index + 1
        index += 1
    raise ValueError(f"unbalanced '{opening}' at offset {start}")


def parse_metadata_list(blanked: str, original: str, key: str) -> tuple:
    """Reads a list-valued class metadatum, for example ``apiSchemas``.

    The list is located in the blanked text so a documentation string cannot
    masquerade as metadata, and read back from the original text because the
    entries themselves are string literals.
    """
    match = re.search(rf"{key}\s*=\s*\[(?P<body>[^\]]*)\]", blanked)
    if match is None:
        return ()
    body = original[match.start("body"):match.end("body")]
    return tuple(
        value.strip().strip('"').strip()
        for value in body.split(",")
        if value.strip().strip('"').strip()
    )


def parse_properties(body: str) -> dict:
    """Parses the property declarations of one class body.

    Only lines outside a nested metadata block declare a property, so parenthesis
    and bracket depth is tracked across the body rather than matched per line.
    """
    properties: dict = {}
    depth = 0
    for line in body.splitlines():
        stripped = line.strip()
        if depth == 0 and stripped and not stripped.startswith(("#", "}", ")")):
            match = PROPERTY_PATTERN.match(stripped)
            if match is not None:
                type_name = match.group("type")
                kind = "relationship" if type_name == "rel" else "attribute"
                name = match.group("name")
                properties[name] = Property(
                    name, type_name, kind, match.group("variability") or "varying")
        depth += (
            line.count("(") - line.count(")") + line.count("[") - line.count("]")
        )
    return properties


def parse_generated_schema(text: str) -> dict:
    """Maps every class identifier in a ``generatedSchema.usda`` to its properties."""
    source = blank_strings(text)
    classes: dict = {}
    for match in CLASS_PATTERN.finditer(source):
        # blank_strings preserves offsets, so the class name reads back from the
        # original text at the same span the structural scan matched.
        name = text[match.start("name"):match.end("name")]
        cursor = match.end()
        metadata = ""
        metadata_source = ""
        while cursor < len(source) and source[cursor] in " \t\r\n":
            cursor += 1
        if cursor < len(source) and source[cursor] == "(":
            end = match_delimited(source, cursor, "(", ")")
            metadata = source[cursor:end]
            metadata_source = text[cursor:end]
            cursor = end
        while cursor < len(source) and source[cursor] in " \t\r\n":
            cursor += 1
        if cursor >= len(source) or source[cursor] != "{":
            raise ValueError(f"class {name} has no body")
        end = match_delimited(source, cursor, "{", "}")
        body = source[cursor + 1:end - 1]
        classes[name] = {
            "properties": parse_properties(body),
            "builtinApiSchemas": parse_metadata_list(
                metadata, metadata_source, "apiSchemas"),
        }
    return classes


def parse_plug_info(text: str) -> dict:
    """Reads the schema type table of a ``plugInfo.json``, which allows comments."""
    body = "\n".join(
        line for line in text.splitlines() if not line.lstrip().startswith("#")
    )
    document = json.loads(body)
    types: dict = {}
    for plugin in document.get("Plugins", []):
        for name, entry in plugin.get("Info", {}).get("Types", {}).items():
            if "schemaKind" not in entry:
                continue
            types[name] = entry
    return types


def library_resource_dir(install_root: pathlib.Path, library: str) -> pathlib.Path:
    for plugin_root in PLUGIN_ROOTS:
        candidate = install_root / plugin_root / library / "resources"
        if (candidate / "generatedSchema.usda").is_file():
            return candidate
    raise FileNotFoundError(
        f"'{library}' carries no generatedSchema.usda under {install_root}. "
        f"Looked in {', '.join(PLUGIN_ROOTS)}."
    )


def load_library(install_root: pathlib.Path, library: str, strict: bool) -> dict:
    resources = library_resource_dir(install_root, library)
    classes = parse_generated_schema(read_text(resources / "generatedSchema.usda"))
    types = parse_plug_info(read_text(resources / "plugInfo.json"))
    schemas: dict = {}
    for type_name, entry in types.items():
        identifier = entry.get("schemaIdentifier") or entry.get("alias", {}).get(
            "UsdSchemaBase", type_name)
        # A registered type with no class in generatedSchema.usda means the two
        # resources disagree, or the structural scan lost the file. Either way the
        # inventory would silently record the type as property-free, which is the one
        # failure this gate must never produce.
        if strict and identifier not in classes:
            raise ValueError(
                f"{library}: '{type_name}' is registered as '{identifier}' but no such "
                f"class exists in {resources / 'generatedSchema.usda'}.")
        declared = classes.get(identifier, {})
        schemas[type_name] = Schema(
            library=library,
            type_name=type_name,
            identifier=identifier,
            kind=entry["schemaKind"],
            bases=tuple(entry.get("bases", [])),
            builtin_api_schemas=tuple(declared.get("builtinApiSchemas", ())),
            declared=declared.get("properties", {}),
        )
    return schemas


def resolve_parent(name: str, index: dict):
    """Resolves a base or built-in API schema to its inventoried schema."""
    parent = index.get(name)
    if parent is not None:
        return parent
    # Built-in API schemas are recorded by identifier, not by C++ type name.
    return next(
        (candidate for candidate in index.values() if candidate.identifier == name),
        None)


def validate_parents(schemas: dict, index: dict) -> None:
    """Fails when a schema inherits from something outside the parsed libraries.

    An unresolved base would silently promote every property it declares into the
    descendant's own set, which then has to be mapped or excepted on the wrong
    schema. Instance-qualified built-ins such as ``CollectionAPI:lightLink`` are
    exempt: they contribute instance-named properties that the descendant declares
    itself and genuinely owns.
    """
    for schema in schemas.values():
        for parent_name in schema.bases + schema.builtin_api_schemas:
            if ":" in parent_name or resolve_parent(parent_name, index) is not None:
                continue
            raise ValueError(
                f"{schema.type_name} inherits from '{parent_name}', which is not in "
                "any parsed schema library. Add its library to TARGET_LIBRARIES or "
                "BASE_LIBRARIES.")


def inherited_names(schema: Schema, index: dict, seen: set) -> set:
    """Collects every property name a schema inherits.

    ``generatedSchema.usda`` is already flattened, so an inherited property is
    repeated verbatim on every descendant. Attributing a property to the single
    schema that introduces it is what keeps the inventory free of duplicates and
    keeps the coverage map from having to restate ``visibility`` thirty times.
    """
    names: set = set()
    for parent_name in schema.bases + schema.builtin_api_schemas:
        parent = resolve_parent(parent_name, index)
        if parent is None or parent.type_name in seen:
            continue
        seen.add(parent.type_name)
        names |= set(parent.declared)
        names |= inherited_names(parent, index, seen)
    return names


def build_inventory(install_root: pathlib.Path) -> dict:
    index: dict = {}
    per_library: dict = {}
    for library in TARGET_LIBRARIES + BASE_LIBRARIES:
        schemas = load_library(install_root, library, library in TARGET_LIBRARIES)
        per_library[library] = schemas
        index.update(schemas)

    libraries = []
    counts = {
        "libraries": len(TARGET_LIBRARIES),
        "schemas": 0,
        "concreteTyped": 0,
        "abstractTyped": 0,
        "appliedApi": 0,
        "nonAppliedApi": 0,
        "ownProperties": 0,
        "inheritedProperties": 0,
    }

    for library in TARGET_LIBRARIES:
        validate_parents(per_library[library], index)

    for library in TARGET_LIBRARIES:
        entries = []
        for type_name in sorted(per_library[library]):
            schema = per_library[library][type_name]
            inherited = inherited_names(schema, index, set())
            own = [
                schema.declared[name]
                for name in sorted(schema.declared)
                if name not in inherited
            ]
            counts["schemas"] += 1
            counts["ownProperties"] += len(own)
            counts["inheritedProperties"] += len(schema.declared) - len(own)
            if schema.kind == "concreteTyped":
                counts["concreteTyped"] += 1
            elif schema.kind == "abstractTyped":
                counts["abstractTyped"] += 1
            elif schema.kind in APPLIED_KINDS:
                counts["appliedApi"] += 1
            else:
                counts["nonAppliedApi"] += 1
            entries.append({
                "typeName": schema.type_name,
                "schemaIdentifier": schema.identifier,
                "schemaKind": schema.kind,
                "bases": list(schema.bases),
                "builtinApiSchemas": list(schema.builtin_api_schemas),
                "inheritedPropertyCount": len(schema.declared) - len(own),
                "properties": [prop.to_json() for prop in own],
            })
        libraries.append({"name": library, "schemas": entries})

    return {"libraries": libraries, "counts": counts}


def read_pin(repo_root: pathlib.Path) -> dict:
    lock = json.loads(read_text(repo_root / "eng" / "openusd.install.lock.json"))
    open_usd = lock["openUsd"]
    return {
        "version": open_usd["version"],
        "tag": open_usd["tag"],
        "commit": open_usd["commit"],
    }


def verify_install_matches_pin(
        install_root: pathlib.Path, pin: dict, allow_unverified: bool) -> None:
    """Requires the staged install to identify itself as the pinned OpenUSD build.

    The inventory only means anything if it was read from the OpenUSD revision the
    repository pins. ``.openusd-install-metadata.json`` is written by
    ``eng/build-native.ps1`` and verified by the native pipeline, so its presence is
    the install's own statement of which commit it was built from. An install
    without it is unidentified, not merely undocumented, and silently accepting one
    would let an inventory generated from an arbitrary OpenUSD build be checked in
    under the pin.

    ``--allow-unverified-install`` waives the *presence* requirement, and nothing
    else: it exists for the parser tests, which point the generator at a synthetic
    registry that no native build produced. A metadata file that names a different
    commit always fails.
    """
    metadata_path = install_root / ".openusd-install-metadata.json"
    if not metadata_path.is_file():
        if allow_unverified:
            print(
                f"warning: {install_root} carries no install metadata; the "
                "inventory is not attributable to the pinned OpenUSD commit.",
                file=sys.stderr)
            return
        raise ValueError(
            f"{metadata_path} is missing, so the staged install cannot be shown to "
            f"be OpenUSD {pin['commit']}. Stage the install with "
            "eng/fetch-native.ps1 and eng/build-native.ps1, which write it.")

    metadata = json.loads(read_text(metadata_path))
    installed = metadata.get("openUsdCommit")
    if not installed:
        if allow_unverified:
            print(
                f"warning: {metadata_path} records no openUsdCommit.",
                file=sys.stderr)
            return
        raise ValueError(
            f"{metadata_path} records no openUsdCommit, so the staged install "
            f"cannot be shown to be OpenUSD {pin['commit']}.")

    if installed != pin["commit"]:
        raise ValueError(
            f"{install_root} was built from OpenUSD {installed}, but "
            f"eng/openusd.install.lock.json pins {pin['commit']}. "
            "Refresh the native install before regenerating the inventory.")


def discover_install_root(
        repo_root: pathlib.Path, explicit: str | None) -> pathlib.Path:
    candidates = []
    if explicit:
        candidates.append(pathlib.Path(explicit))
    elif os.environ.get("OPENUSD_ROOT"):
        candidates.append(pathlib.Path(os.environ["OPENUSD_ROOT"]))
    else:
        candidates.extend(repo_root / "native" / "install" / rid for rid in RIDS)

    for candidate in candidates:
        root = candidate.expanduser()
        if not root.is_absolute():
            root = (repo_root / root).resolve()
        if any((root / plugin_root / "usdGeom" / "resources"
                / "generatedSchema.usda").is_file()
               for plugin_root in PLUGIN_ROOTS):
            return root

    searched = ", ".join(str(candidate) for candidate in candidates)
    raise FileNotFoundError(
        "No OpenUSD schema registry found. Stage the pinned native install "
        "(eng/fetch-native.ps1) or pass --install-root. Searched: " + searched)


def render_inventory(pin: dict, inventory: dict) -> str:
    document = {
        "$schemaVersion": 1,
        "$generator": "eng/generate-schema-coverage.py",
        "$comment": (
            "Generated from the pinned OpenUSD schema registry. Do not hand edit; "
            "run python eng/generate-schema-coverage.py."),
        "openUsd": pin,
        "counts": inventory["counts"],
        "libraries": inventory["libraries"],
    }
    return json.dumps(document, indent=2, sort_keys=False) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--verify",
        action="store_true",
        help="Fail instead of writing when the inventory is out of date.")
    parser.add_argument(
        "--install-root",
        help="OpenUSD install root to read the schema registry from.")
    parser.add_argument(
        "--allow-unverified-install",
        action="store_true",
        help="Accept an install that carries no .openusd-install-metadata.json. "
             "For the parser tests only: it waives the presence of the metadata, "
             "never a commit mismatch.")
    args = parser.parse_args()

    repo_root = pathlib.Path(__file__).resolve().parents[1]
    output_path = repo_root / "schemas" / "openUsd" / "schema-registry.g.json"

    try:
        pin = read_pin(repo_root)
        install_root = discover_install_root(repo_root, args.install_root)
        verify_install_matches_pin(install_root, pin, args.allow_unverified_install)
        generated = render_inventory(pin, build_inventory(install_root))
    except (FileNotFoundError, ValueError, KeyError) as error:
        print(f"Schema inventory generation failed: {error}", file=sys.stderr)
        return 1

    if args.verify:
        existing = read_text(output_path) if output_path.exists() else None
        if existing == generated:
            print(f"{output_path} matches the pinned OpenUSD schema registry.")
            return 0
        print(
            f"{output_path} is out of date for OpenUSD {pin['version']} "
            f"({install_root}). Run python eng/generate-schema-coverage.py.",
            file=sys.stderr)
        return 1

    write_text(output_path, generated)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
