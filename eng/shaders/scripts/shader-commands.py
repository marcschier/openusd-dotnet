# Copyright (c) marcschier. Licensed under the MIT License.

import argparse
import json
import pathlib

from shader_model import (
    ARTIFACT_SCOPES,
    build_lock_model,
    generate_plan,
    validate_manifest,
)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--lock", required=True, type=pathlib.Path)
    parser.add_argument("--manifest", required=True, type=pathlib.Path)
    parser.add_argument("--output-root")
    parser.add_argument("--output", type=pathlib.Path)
    parser.add_argument(
        "--artifact-scope",
        choices=ARTIFACT_SCOPES,
        default="full",
    )
    parser.add_argument("--model-only", action="store_true")
    args = parser.parse_args()

    lock = json.loads(args.lock.read_text(encoding="utf-8"))
    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    if args.model_only:
        model = build_lock_model(lock)
        validate_manifest(manifest, model)
        print(json.dumps(model))
        return 0
    if args.output_root is None or args.output is None:
        parser.error("--output-root and --output are required unless --model-only is used")

    plan = generate_plan(
        lock,
        manifest,
        args.output_root,
        args.artifact_scope,
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(plan, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
