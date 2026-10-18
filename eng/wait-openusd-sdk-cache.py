#!/usr/bin/env python3
"""Wait only on a cold SDK cache; the ordinary restore and integrity/coverage gates still decide acceptance."""
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

VERIFIED_STEPS = (
    "Verify native install metadata",
    "Verify complete native redistribution notices",
    "Verify complete SDK build-cache pair",
)
NATIVE_LINUX_JOB = "linux-x64 verified native archive"


class CacheReadinessError(RuntimeError):
    pass


def read_api(repository, token, path):
    if not path:
        operation = "repository-metadata"
    elif path.startswith("actions/workflows/native.yml/runs?"):
        operation = "native-runs"
    elif path.startswith("actions/caches?"):
        operation = "sdk-cache-list"
    elif path.startswith("actions/runs/") and "/jobs?" in path:
        operation = "native-jobs"
    elif re.fullmatch(r"actions/runs/[0-9]+", path):
        operation = "coverage-run"
    else:
        raise CacheReadinessError("Unexpected SDK-readiness metadata operation.")
    url = "https://api.github.com/repos/" + repository + ("/" + path if path else "")
    request = urllib.request.Request(url, headers={
        "Authorization": "Bearer " + token, "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28"})
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            raw = response.read(4194305)
    except urllib.error.HTTPError as error:
        raise CacheReadinessError(f"GitHub SDK-readiness metadata failed: {operation}; HTTP {error.code}.") from None
    except (urllib.error.URLError, TimeoutError):
        raise CacheReadinessError(f"GitHub SDK-readiness metadata failed: {operation}; transport unavailable.") from None
    if len(raw) > 4194304:
        raise CacheReadinessError("GitHub SDK-readiness metadata exceeded its bound.")
    try:
        return json.loads(raw)
    except (ValueError, UnicodeError):
        raise CacheReadinessError("GitHub SDK-readiness metadata is not valid JSON.") from None


def jobs_for(api, run_id):
    result = []
    for page in range(1, 4):
        value = api(f"actions/runs/{run_id}/jobs?per_page=100&page={page}")
        total = value.get("total_count")
        jobs = value.get("jobs")
        if type(total) is not int or not isinstance(jobs, list) or total > 300:
            raise CacheReadinessError("Native producer jobs are incomplete or exceed the metadata bound.")
        result.extend(jobs)
        if len(result) == total:
            return result
        if len(result) > total or not jobs:
            break
    raise CacheReadinessError("Native producer job pagination was incomplete.")


def verified_prefix(job):
    steps = job.get("steps", [])
    for name in VERIFIED_STEPS:
        matching = [step for step in steps if step.get("name") == name]
        if len(matching) != 1 or matching[0].get("conclusion") != "success":
            return False
    return True


def eligible_runs(api, repository, source, current_run):
    current = api(f"actions/runs/{current_run}")
    if current.get("head_sha") != source:
        raise CacheReadinessError("The coverage workflow source does not match the expected source.")
    candidates = [current]
    query = urllib.parse.urlencode({"head_sha": source, "per_page": 100})
    listed = api("actions/workflows/native.yml/runs?" + query)
    if type(listed.get("total_count")) is not int or listed["total_count"] > 100:
        raise CacheReadinessError("Exact-source native producer metadata exceeds the bound.")
    for run in listed.get("workflow_runs", []):
        if run.get("head_sha") != source:
            raise CacheReadinessError("Native producer query returned a different source.")
        candidates.append(run)
    seen = set()
    result = []
    for run in candidates:
        identity = run.get("id")
        if type(identity) is not int or identity in seen:
            continue
        seen.add(identity)
        if run.get("head_repository", {}).get("full_name") != repository:
            continue
        if run.get("head_sha") == source:
            result.append(run)
    return result


def assess(api, repository, source, current_run, cache_key, allowed_refs):
    verified = []
    active = False
    producers = 0
    for run in eligible_runs(api, repository, source, current_run):
        linux = [job for job in jobs_for(api, run["id"])
                 if job.get("name", "").split(" / ")[-1] == NATIVE_LINUX_JOB]
        registered_producer = (run["id"] != current_run or
                               run.get("path") in (".github/workflows/native.yml", ".github/workflows/release.yml"))
        if not linux and registered_producer:
            producers += 1
            if run.get("status") != "completed":
                active = True
        for job in linux:
            producers += 1
            if job.get("status") == "completed" and job.get("conclusion") != "success":
                continue
            if job.get("status") != "completed":
                active = True
            if verified_prefix(job):
                verified.append((run["id"], job["id"]))
    query = urllib.parse.urlencode({"key": cache_key, "per_page": 100})
    value = api("actions/caches?" + query)
    if type(value.get("total_count")) is not int or value["total_count"] > 100:
        raise CacheReadinessError("Exact SDK cache metadata exceeds the bound.")
    caches = [item for item in value.get("actions_caches", [])
              if item.get("key") == cache_key and item.get("ref") in allowed_refs
              and type(item.get("size_in_bytes")) is int and item["size_in_bytes"] > 0]
    if verified and caches:
        return {"status": "ready", "producerRun": verified[0][0], "producerJob": verified[0][1],
                "cacheId": caches[0]["id"], "cacheRef": caches[0]["ref"],
                "meaning": "Verified SDK build-cache readiness only; not completed native or coverage qualification."}
    if producers and not active and not verified:
        raise CacheReadinessError("The exact-source Linux native producer failed before SDK readiness.")
    return {"status": "waiting", "producerSeen": producers > 0, "producerActive": active,
            "verifiedPrefixSeen": bool(verified), "exactVisibleCacheSeen": bool(caches)}


def wait_ready(api, repository, source, current_run, cache_key, allowed_refs, timeout=9000,
               clock=time.monotonic, sleep=time.sleep):
    started = clock()
    announced = False
    completed_without_cache_since = None
    while clock() - started < timeout:
        result = assess(api, repository, source, current_run, cache_key, allowed_refs)
        if result["status"] == "ready":
            result["waitedSeconds"] = round(clock() - started, 3)
            return result
        if not result["producerSeen"] and clock() - started >= 120:
            raise CacheReadinessError("No eligible exact-source Linux native producer appeared for the cold SDK cache.")
        if result["producerSeen"] and not result["producerActive"] and not result["exactVisibleCacheSeen"]:
            if completed_without_cache_since is None:
                completed_without_cache_since = clock()
            if clock() - completed_without_cache_since >= 60:
                raise CacheReadinessError("The verified native producer completed without publishing the required SDK cache.")
        else:
            completed_without_cache_since = None
        if not announced:
            print("Cold SDK cache: waiting for the exact-source verified native producer; no SDK rebuild or gate skip.")
            announced = True
        sleep(min(30, max(0, timeout - (clock() - started))))
    raise CacheReadinessError("Timed out waiting for the exact-source verified SDK cache.")


def main():
    repository = os.environ["GITHUB_REPOSITORY"]
    source, key = os.environ["OPENUSD_CACHE_SOURCE_SHA"], os.environ["OPENUSD_CACHE_KEY"]
    current = os.environ["GITHUB_RUN_ID"]
    ref = os.environ["GITHUB_REF"]
    if (not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository) or
            not re.fullmatch(r"[a-f0-9]{40}", source) or not current.isdigit() or
            not re.fullmatch(r"openusd-install-linux-x64-[a-f0-9]{64}", key) or
            not ref.startswith(("refs/heads/", "refs/tags/", "refs/pull/"))):
        raise CacheReadinessError("Invalid source/cache/ref readiness identity.")
    token = os.environ["GH_TOKEN"]
    api = lambda path: read_api(repository, token, path)
    info = api("")
    default = info.get("default_branch")
    if not isinstance(default, str) or not default or default.startswith("/") or ".." in default.split("/"):
        raise CacheReadinessError("Repository default cache scope is unavailable.")
    result = wait_ready(api, repository, source, int(current), key, {ref, "refs/heads/" + default})
    print(json.dumps(result, sort_keys=True))


if __name__ == "__main__":
    try:
        main()
    except (CacheReadinessError, KeyError) as error:
        message = str(error) if isinstance(error, CacheReadinessError) else "Required SDK readiness input is missing."
        print("::error::" + message, file=sys.stderr)
        raise SystemExit(1)
