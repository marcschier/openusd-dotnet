# Omniverse Kit companion -- specification examples (not a shipped extension)

This directory holds **checked, non-executable specification examples** for
[`docs/omniverse-kit-companion.md`](../../omniverse-kit-companion.md), the specification for a
separately owned and separately distributed Omniverse Kit extension. No such extension exists in,
or is authorized for, this repository.

- **[`extension.toml`](extension.toml)**
  - What it is: a structural sketch of a Kit `extension.toml`, illustrating the dependency and
    versioning shape [`docs/omniverse-kit-companion.md`](../../omniverse-kit-companion.md)
    describes.
  - What it is not: not installable. Every version field is the literal string `"pending"`, which
    Kit's own TOML/semver parsing does not accept as a version, so this file cannot be loaded by
    Kit's Extension Manager or an extension search path.
- **[`companion_service.py`](companion_service.py)**
  - What it is: checked Python pseudocode shaped like the server behavior in
    [`docs/omniverse-kit-companion.md`](../../omniverse-kit-companion.md).
  - What it is not: not runnable. It imports `openusd_bridge_v1_pb2` and
    `openusd_bridge_v1_pb2_grpc`, placeholder module names for the generated stubs the
    specification's Python gRPC workflow describes producing from this repository's own `.proto`
    files -- no such modules are vendored or generated here.

Both files exist so a future, independently authorized companion implementation has a checked
starting shape to diverge from, and so this specification's claims about dependency names,
capability negotiation, and server behavior are pinned to something more concrete than prose alone.
Neither file is referenced by any build, package, or test in this repository, and neither is a
substitute for the real extension's own repository, license, and version pins once those exist.

See [`eng/kit-companion-spec.json`](../../../eng/kit-companion-spec.json) and
[`eng/kit-companion-spec.schema.json`](../../../eng/kit-companion-spec.schema.json) for the
machine-readable facts behind this specification, and
[`docs/omniverse-kit-companion-reference.g.md`](../../omniverse-kit-companion-reference.g.md) for
their generated reference tables.
