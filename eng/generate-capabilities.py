# Copyright (c) marcschier. Licensed under the MIT License.

import argparse
import difflib
import json
import pathlib
import re
import sys
from dataclasses import dataclass


ABI_PATTERN = re.compile(r"^#define[ \t]+OPENUSD_DATA_ABI_VERSION[ \t]+(?P<value>\d+)", re.MULTILINE)
CAPABILITY_PATTERN = re.compile(
    r"^#define[ \t]+(?P<name>OPENUSD_CAPABILITY_[A-Z0-9_]+)[ \t]+"
    r"\(UINT64_C\(1\)[ \t]*<<[ \t]*(?P<bit>\d+)\)",
    re.MULTILINE,
)
COMMON_PATTERN = re.compile(
    r"constexpr uint32_t DataAbiVersion = \d+;\n"
    r"constexpr uint64_t DataCapabilities =\n"
    r"(?P<body>.*?);",
    re.DOTALL,
)
CONTRACT_PATTERN = re.compile(
    r"    private const ulong CoreCapabilities = .*?;\n"
    r"    private const ulong SchemaFacadeCapabilities = .*?;\n"
    r".*?public const uint AbiVersion = \d+;\n"
    r".*?public const ulong RequiredCapabilities = .*?;",
    re.DOTALL,
)
PROBE_PATTERN = re.compile(
    r"    if \(openusd_get_abi_version\(\) != \d+ \|\|\n"
    r"        \(openusd_get_capabilities\(\) &\n"
    r"(?P<body>.*?)\)\)\n"
    r"    \{",
    re.DOTALL,
)


@dataclass(frozen=True)
class Capability:
    name: str
    bit: int


def read_text(path: pathlib.Path) -> str:
    return path.read_text(encoding="utf-8").replace("\r\n", "\n")


def write_text(path: pathlib.Path, value: str) -> None:
    path.write_text(value, encoding="utf-8", newline="\n")


def parse_header(header: str) -> tuple[int, list[Capability]]:
    abi_match = ABI_PATTERN.search(header)
    if abi_match is None:
        raise ValueError("openusd_dotnet.h must define OPENUSD_DATA_ABI_VERSION")

    capabilities = [
        Capability(match.group("name"), int(match.group("bit")))
        for match in CAPABILITY_PATTERN.finditer(header)
    ]
    if not capabilities:
        raise ValueError("openusd_dotnet.h must define at least one OPENUSD_CAPABILITY_* macro")

    seen_names: set[str] = set()
    seen_bits: set[int] = set()
    for capability in capabilities:
        if capability.name in seen_names:
            raise ValueError(f"Duplicate capability name: {capability.name}")
        if capability.bit in seen_bits:
            raise ValueError(f"Duplicate capability bit: {capability.bit}")
        if capability.bit < 0 or capability.bit > 63:
            raise ValueError(f"Capability bit out of uint64 range: {capability.bit}")
        seen_names.add(capability.name)
        seen_bits.add(capability.bit)

    capabilities.sort(key=lambda capability: capability.bit)
    expected_bits = list(range(capabilities[-1].bit + 1))
    actual_bits = [capability.bit for capability in capabilities]
    if actual_bits != expected_bits:
        raise ValueError(
            "Capability bits must be contiguous from zero; found "
            + ", ".join(str(bit) for bit in actual_bits)
        )

    return int(abi_match.group("value")), capabilities


def mask(capabilities: list[Capability]) -> int:
    value = 0
    for capability in capabilities:
        value |= 1 << capability.bit
    return value


def capability_expression(capabilities: list[Capability], indent: str) -> str:
    lines = []
    for index, capability in enumerate(capabilities):
        suffix = ";" if index == len(capabilities) - 1 else " |"
        lines.append(f"{indent}{capability.name}{suffix}")
    return "\n".join(lines)


def replace_one(pattern: re.Pattern[str], current: str, replacement: str, description: str) -> str:
    updated, count = pattern.subn(replacement, current, count=1)
    if count != 1:
        raise ValueError(f"Could not locate {description}")
    return updated


def generate_common(current: str, abi_version: int, capabilities: list[Capability]) -> str:
    replacement = (
        f"constexpr uint32_t DataAbiVersion = {abi_version};\n"
        "constexpr uint64_t DataCapabilities =\n"
        f"{capability_expression(capabilities, '    ')}"
    )
    return replace_one(COMMON_PATTERN, current, replacement, "common.h capability contract")


def format_mask(capabilities: list[Capability]) -> str:
    # Always hex. The previous form fell back to a chain of "(1UL << n)"
    # terms whenever the bits were not contiguous from zero, which happened
    # the moment a non-schema capability landed above the schema-facade bit:
    # the emitted line reached 255 characters and broke the 120-column gate,
    # and no longer matched the hex the ABI contract test asserts.
    mask = 0
    for capability in capabilities:
        mask |= 1 << capability.bit
    return f"0x{mask:X}"


def format_schema_mask(capabilities: list[Capability]) -> str:
    if len(capabilities) == 1:
        return f"1UL << {capabilities[0].bit}"
    return format_mask(capabilities)


def ordinal(value: int) -> str:
    names = {
        1: "first",
        2: "second",
        3: "third",
        4: "fourth",
        5: "fifth",
        6: "sixth",
        7: "seventh",
        8: "eighth",
        9: "ninth",
        10: "tenth",
        11: "eleventh",
        12: "twelfth",
        13: "thirteenth",
        14: "fourteenth",
        15: "fifteenth",
        16: "sixteenth",
    }
    return names.get(value, f"version {value}")


def generate_contract(current: str, abi_version: int, capabilities: list[Capability]) -> str:
    schema_prefix = "OPENUSD_CAPABILITY_SCHEMA_FACADES_"
    core = [capability for capability in capabilities if not capability.name.startswith(schema_prefix)]
    schema = [capability for capability in capabilities if capability.name.startswith(schema_prefix)]
    replacement = (
        f"    private const ulong CoreCapabilities = {format_mask(core)};\n"
        f"    private const ulong SchemaFacadeCapabilities = {format_schema_mask(schema)};\n\n"
        "    /// <summary>Gets the platform-neutral native import name.</summary>\n"
        "    public const string LibraryName = \"openusd_dotnet\";\n\n"
        f"    /// <summary>Gets the {ordinal(abi_version)} version of the project-owned native ABI.</summary>\n"
        f"    public const uint AbiVersion = {abi_version};\n\n"
        "    /// <summary>Gets the capabilities required by this managed contract.</summary>\n"
        "    public const ulong RequiredCapabilities = CoreCapabilities | SchemaFacadeCapabilities;"
    )
    return replace_one(CONTRACT_PATTERN, current, replacement, "managed capability contract")


def generate_probe(current: str, abi_version: int, capabilities: list[Capability]) -> str:
    expression = capability_expression(capabilities, "          ").removesuffix(";")
    expected = capability_expression(capabilities, "             ").removesuffix(";")
    replacement = (
        f"    if (openusd_get_abi_version() != {abi_version} ||\n"
        "        (openusd_get_capabilities() &\n"
        f"         ({expression.removeprefix('          ')})) !=\n"
        f"            ({expected.removeprefix('             ')}))\n"
        "    {"
    )
    return replace_one(PROBE_PATTERN, current, replacement, "native probe capability check")


def unified_diff(path: pathlib.Path, current: str, generated: str) -> str:
    return "".join(difflib.unified_diff(
        current.splitlines(keepends=True),
        generated.splitlines(keepends=True),
        fromfile=str(path),
        tofile=f"{path} (generated)",
        lineterm="\n",
    ))


def verify_file(path: pathlib.Path, generated: str) -> bool:
    current = read_text(path)
    if current == generated:
        return True
    print(f"Generated capability output is out of date: {path}", file=sys.stderr)
    print(unified_diff(path, current, generated), file=sys.stderr)
    return False


def verify_lock(path: pathlib.Path, abi_version: int, capabilities: list[Capability]) -> bool:
    lock = json.loads(read_text(path))
    expected_mask = mask(capabilities)
    ok = True
    actual_abi = int(lock["abi"]["data"])
    actual_mask = int(lock["abi"]["dataCapabilities"])
    if actual_abi != abi_version:
        print(
            f"Lock data ABI is out of date: {path} records {actual_abi}, "
            f"source declares {abi_version}",
            file=sys.stderr,
        )
        ok = False
    if actual_mask != expected_mask:
        print(
            f"Lock data capability mask is out of date: {path} records "
            f"0x{actual_mask:X}, source declares 0x{expected_mask:X}",
            file=sys.stderr,
        )
        ok = False
    return ok


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()

    root = pathlib.Path(__file__).resolve().parents[1]
    header_path = root / "native" / "openusd_dotnet" / "include" / "openusd_dotnet.h"
    common_path = root / "native" / "openusd_dotnet" / "src" / "internal" / "common.h"
    contract_path = root / "src" / "OpenUsd.Interop" / "OpenUsdNativeContract.cs"
    probe_path = root / "native" / "tests" / "native_probe.cpp"
    lock_path = root / "eng" / "openusd.lock.json"

    try:
        abi_version, capabilities = parse_header(read_text(header_path))
        outputs = {
            common_path: generate_common(read_text(common_path), abi_version, capabilities),
            contract_path: generate_contract(read_text(contract_path), abi_version, capabilities),
            probe_path: generate_probe(read_text(probe_path), abi_version, capabilities),
        }
    except ValueError as error:
        print(f"Capability generation failed: {error}", file=sys.stderr)
        return 1

    if args.verify:
        ok = verify_lock(lock_path, abi_version, capabilities)
        for path, generated in outputs.items():
            ok = verify_file(path, generated) and ok
        return 0 if ok else 1

    for path, generated in outputs.items():
        write_text(path, generated)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
