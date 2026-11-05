"""Resume an upload-only failure using the original, independently verified signed artifact."""
import argparse
import hashlib
import json
import subprocess
import time
from pathlib import Path

from manifest import release_channel
from sign import verify_signatures

REPOSITORY = "Nexa-MC/NexaCL"


def api(path):
    for attempt in range(4):
        try:
            raw = subprocess.check_output(["gh", "api", f"repos/{REPOSITORY}/{path}"],
                                          stderr=subprocess.PIPE, timeout=60)
            break
        except (subprocess.CalledProcessError, subprocess.TimeoutExpired):
            if attempt == 3:
                raise RuntimeError("GitHub metadata could not be read after bounded retries") from None
            time.sleep(2 ** attempt)
    # A transport retry must never turn invalid metadata into an absent release.
    return json.loads(raw)


def source(tag, run_id):
    if not tag.startswith("v") or release_channel(tag[1:]) == "ci" or run_id <= 0:
        raise ValueError("A numbered release tag and a positive source run are required")
    commit = subprocess.check_output(["git", "rev-parse", "--verify", tag + "^{commit}"], text=True).strip()
    run = api(f"actions/runs/{run_id}")
    if (run.get("head_sha") != commit or run.get("head_branch") != tag
            or run.get("path") != ".github/workflows/launcher-build.yml"
            or run.get("status") != "completed" or run.get("conclusion") != "failure"):
        raise ValueError("Source must be the completed failed launcher run for the immutable tag")
    jobs = api(f"actions/runs/{run_id}/jobs?per_page=100")["jobs"]
    expected = {"metadata", "distribution"} | {"Launcher " + rid for rid in
                ("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")}
    if len(jobs) != len(expected) or {job["name"] for job in jobs} != expected:
        raise ValueError("Source run does not contain the complete six-platform distribution")
    for job in jobs:
        if job["name"] != "distribution":
            if job.get("conclusion") != "success":
                raise ValueError("Every source platform and metadata gate must have passed")
        else:
            failed = [step["name"] for step in job["steps"] if step.get("conclusion") == "failure"]
            required = {"Sign and independently verify the complete distribution",
                        "Verify embedded Sidecar signatures and provenance in all six portable packages"}
            passed = {step["name"] for step in job["steps"] if step.get("conclusion") == "success"}
            if job.get("conclusion") != "failure" or failed != ["Publish tagged release"] or not required <= passed:
                raise ValueError("Only a final upload failure after successful signing can be resumed")
    artifacts = api(f"actions/runs/{run_id}/artifacts?per_page=100")["artifacts"]
    saved = [item for item in artifacts if item["name"] == "Complete-Nexa-distribution" and not item["expired"]]
    if len(saved) != 1:
        raise ValueError("The original complete signed artifact is missing or ambiguous")
    return commit


def read_draft(release_id, tag, commit):
    release = api(f"releases/{release_id}")
    if (release.get("tag_name") != tag or release.get("target_commitish") != commit
            or release.get("draft") is not True or not isinstance(release.get("body"), str)
            or not release["body"].strip()):
        raise ValueError("Only the existing, nonempty draft for the exact source commit can be resumed")
    return release


def expected_files(directory):
    files = list(directory.iterdir())
    if not files or any(path.is_symlink() or not path.is_file() for path in files):
        raise ValueError("Signed artifacts must contain only regular files")
    result = {}
    for path in files:
        hasher = hashlib.sha256()
        with path.open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                hasher.update(block)
        result[path.name] = (path.stat().st_size, "sha256:" + hasher.hexdigest())
    return result


def existing_files(release, expected):
    found = set()
    for asset in release["assets"]:
        name = asset["name"]
        if (name in found or name not in expected or asset.get("state") != "uploaded"
                or (asset.get("size"), asset.get("digest")) != expected[name]):
            raise ValueError("Existing draft assets differ from the verified signed artifact")
        found.add(name)
    return found


def upload(directory, release_id, tag, commit, expected):
    present = existing_files(read_draft(release_id, tag, commit), expected)
    for name in sorted(expected.keys() - present):
        for attempt in range(4):
            result = subprocess.run(["gh", "release", "upload", tag, "--repo", REPOSITORY,
                                     str(directory / name)], capture_output=True, check=False)
            current = existing_files(read_draft(release_id, tag, commit), expected)
            if name in current:
                break
            if attempt == 3:
                raise RuntimeError(f"Upload could not be verified for {name} (exit {result.returncode})")
            time.sleep(2 ** attempt)
        print("Verified uploaded asset:", name, flush=True)
    if existing_files(read_draft(release_id, tag, commit), expected) != set(expected):
        raise ValueError("Resumed draft inventory is incomplete")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tag", required=True)
    parser.add_argument("--run-id", type=int, required=True)
    parser.add_argument("--release-id", type=int)
    parser.add_argument("--directory", type=Path)
    args = parser.parse_args()
    commit = source(args.tag, args.run_id)
    if args.directory is None:
        print("Verified upload-only source failure:", commit)
        return
    if args.release_id is None or args.release_id <= 0:
        raise ValueError("A destination draft ID is required")
    # Read the trust anchor from the immutable source tag, not a later publisher checkout.
    key = subprocess.check_output(["git", "show", args.tag + ":GPG-PUBLIC-KEY.asc"])
    verify_signatures(args.directory, args.tag[1:], key)
    upload(args.directory, args.release_id, args.tag, commit, expected_files(args.directory))
    print("Complete signed draft verified; publication still requires a separate action.")


if __name__ == "__main__":
    main()
