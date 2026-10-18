"""One exact reviewed SDK recipe transition in PR22; never a generic restore fallback."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path

OLD_BUILDER = "978a5b64fac7a8bf6e306c5c0f2d3a6084996007d336724f2db5ba9a269edec0"
NEW_BUILDER = "77983122e6983fc977407f81561abac899c11d7c68f26bc145bd1e2c5822eae0"
OLD_COMMON = "ae6479fc17740cb6a954c846e4a9f134b17a0c64967b4a3c6beefa34037fe9f9"
NEW_COMMON = "20c041af2f2fd1d777bc7eaaa988510baf8babe6b507a99c6397ddb8b11a4e3b"
PINS = {
    "win-x64": (OLD_COMMON, NEW_COMMON, 9117112659, 409369750, "20261007.264.1"),
    "linux-x64": (OLD_COMMON, NEW_COMMON, 9116549374, 242783835, "20261012.334.1"),
    "osx-arm64": (
        "68afeba1433040449dec5cfb7523db1805e96fa94bc5374ebb83566ed176b35b",
        "9bf2f422481fd09c7495bc8b26b1a1e847ab6bff45250ae22817184da840aa68",
        9115834304, 184364643, "20260907.0337.1"),
}
UNCHANGED = {
    "eng/openusd.install.lock.json": "c024697911799152594d6c457199fe63c5ff9bffc578bf6a1e35998f2935a961",
    "eng/fetch-native.ps1": "9147b518ceb693dd31887d7e4fb40fd65c1ef1d6f2966e5a5a65f0883176cfdc",
    "eng/build-vulkan-sdk.ps1": "b16ee6a1788aee6d2ec1a7bf0ecb326d0f9868d878d6056c21e919323899fc7e",
    "eng/generate-native-notices.py": "cd7b06054b07f346309836485733b7329c8a209c57430e0b4e4f05a4ad323e38",
    "eng/shaderc-source-patches.lock.json": "77b4ea1b430a291218a1a3fe8df230eae7978dc395e6e7ad470006092d5e220f",
    "eng/patches/shaderc/spirv-tools-explicit-case-breaks.patch": "ae4963541d9e12a9051f7f5d0e5a0bca88b0d766a3503e5889ca04cbf129c509",
    "eng/patches/shaderc/shaderc-zero-padded-string-copy.patch": "b475d7338356914c10efdef2bf234688dc783e7f410bf29b179a3027534d7440",
    "eng/generate-sbom.py": "dab6c036d5f7f10d01f96548781b4d8e9ff8a7a6a454be348c5222fd37eb334a",
    "LICENSE": "3ca5820f4e4ef7c9af0964693010df197f2665445fac35aeb73effeb39a2cb70",
    "NOTICE": "10d2c78d7c0e8cf0e891b9504fcb8f4da5deaf0a74dca5646943e8aac387c08b",
}


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def input_matches(root, name, expected):
    path = root
    for part in Path(name).parts:
        path /= part
        if path.is_symlink() or path.is_junction():
            return False
    return path.resolve().is_relative_to(root.resolve()) and path.is_file() and digest(path) == expected


def select(root, rid, requested_key, matched_key, environment, read_cache):
    old, new, identifier, length, image = PINS[rid]
    old_key, new_key = f"openusd-install-{rid}-{old}", f"openusd-install-{rid}-{new}"
    if (requested_key != new_key or environment.get("GITHUB_REPOSITORY") != "marcschier/openusd-dotnet"
            or environment.get("GITHUB_REPOSITORY_ID") != "1309934941"
            or environment.get("GITHUB_REF") != "refs/pull/22/merge"
            or environment.get("ImageVersion") != image):
        return {"eligible": False, "reason": "outside-exact-reviewed-transition"}
    expected = dict(UNCHANGED)
    expected["eng/build-native.ps1"] = NEW_BUILDER
    if rid == "osx-arm64":
        expected["eng/cmake/ShaderCompiler.cmake"] = "a85cf43c7a23b22efe2ce8e2db6b95eb0ab9d8d9e541b07da308620c069e013b"
    if any(not input_matches(root, name, value) for name, value in expected.items()):
        return {"eligible": False, "reason": "SDK-input-drift-disables-transition"}
    if matched_key:
        raise ValueError("An unexpected non-exact new-key restore cannot enter SDK recipe migration.")
    cache = read_cache(old_key)
    if cache["total_count"] != len(cache["actions_caches"]) or cache["total_count"] > 20:
        raise ValueError("Exact original SDK cache identity requires bounded complete metadata.")
    matches = [item for item in cache["actions_caches"] if item["key"] == old_key
               and item["ref"] == "refs/pull/22/merge"]
    if not matches:
        return {"eligible": False, "reason": "exact-original-cache-unavailable"}
    if len(matches) != 1 or matches[0]["id"] != identifier or matches[0]["size_in_bytes"] != length:
        raise ValueError("The exact old SDK cache object/provenance differs; migration refused.")
    return {
        "eligible": True, "oldKey": old_key, "newKey": new_key, "originalCacheId": identifier,
        "originalProducerRun": 39112660339,
        "originalProducerSource": "e2e8aecc6b40161d0b44b8716e25e1d7d4bc5f2c",
        "oldBuilderSha256": OLD_BUILDER, "newBuilderSha256": NEW_BUILDER,
        "strictCurrentVerificationStillRequired": True, "notNewlyCompiled": True,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--rid", choices=PINS, required=True)
    parser.add_argument("--new-key", required=True)
    parser.add_argument("--new-matched-key", default=os.environ.get("MATCHED_NEW_SDK_KEY", ""))
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    spec = importlib.util.spec_from_file_location("sdk_seed_metadata_only", root / "eng/cesium-sdk-artifact.py")
    metadata = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(metadata)
    result = select(root, args.rid, args.new_key, args.new_matched_key, os.environ,
                    lambda key: metadata.api("marcschier/openusd-dotnet", os.environ["GH_TOKEN"],
                                             "actions/caches?key=" + key + "&per_page=20"))
    if output := os.environ.get("GITHUB_OUTPUT"):
        with open(output, "a", encoding="utf-8") as stream:
            stream.write("eligible=" + str(result["eligible"]).lower() + "\n")
            if result["eligible"]:
                stream.write("old-key=" + result["oldKey"] + "\n")
    print("SDK_EXACT_RECIPE_SEED_SELECTION: " + json.dumps(result, sort_keys=True))


if __name__ == "__main__":
    main()
