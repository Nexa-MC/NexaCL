"""Mirror a verified public source release into the independent update repository."""
import argparse
import json
import re
import subprocess
import tempfile
from pathlib import Path

from manifest import release_channel
from release_notes import get_release, require_notes
from sign import verify_signatures

SOURCE = "Nexa-MC/NexaMC"
DESTINATION = "Nexa-MC/NexaMC-Updates"
MAXIMUM_ASSET_BYTES = 2 * 1024**3


def gh(*arguments):
    result = subprocess.run(["gh", *map(str, arguments)], capture_output=True, text=True, check=False)
    if result.returncode:
        raise RuntimeError("GitHub release operation failed; check repository access and workflow permissions")
    return result.stdout


def api(path):
    raw = gh("api", path)
    if len(raw) > 4 * 1024**2:
        raise ValueError("Release metadata exceeds its budget")
    return json.loads(raw)


def identity(tag):
    if not isinstance(tag, str):
        raise ValueError("Release tag must be text")
    version = tag.removeprefix("v")
    channel = release_channel(version)
    if channel == "ci":
        raise ValueError("CI builds cannot become public update offers")
    return version, channel


def assets_for(release, tag):
    assets = release.get("assets")
    if not isinstance(assets, list) or not 1 <= len(assets) <= 128:
        raise ValueError("Release asset set is missing or too large")
    sizes = {}
    for asset in assets:
        name = asset.get("name")
        size = asset.get("size")
        if (not isinstance(name, str) or not re.fullmatch(r"[A-Za-z0-9._-]{1,240}", name)
                or name in (".", "..") or name in sizes
                or type(size) is not int or not 0 < size <= MAXIMUM_ASSET_BYTES
                or asset.get("browser_download_url") != f"https://github.com/{SOURCE}/releases/download/{tag}/{name}"):
            raise ValueError("Release contains an invalid asset identity")
        sizes[name] = size
    if sum(sizes.values()) > 16 * 1024**3:
        raise ValueError("Release assets exceed the total byte budget")
    return sizes


def publish(tag, public_key, destination=DESTINATION):
    if destination != DESTINATION:
        raise ValueError("Only the pinned independent update repository may receive releases")
    version, channel = identity(tag)
    release = api(f"repos/{SOURCE}/releases/tags/{tag}")
    if (not isinstance(release, dict) or release.get("tag_name") != tag
            or release.get("draft") is not False
            or release.get("prerelease") is not (channel != "stable")
            or release.get("html_url") != f"https://github.com/{SOURCE}/releases/tag/{tag}"):
        raise ValueError("Source must be a public release with the expected identity")
    notes = require_notes(release.get("body"))
    sizes = assets_for(release, tag)
    existing = get_release(destination, tag)
    if existing is not None and not existing["draft"]:
        print(f"Already published: {destination} {tag}")
        return False
    if existing is not None:
        require_notes(existing.get("body"))
    with tempfile.TemporaryDirectory(prefix="nexa-update-publish-") as temporary:
        root = Path(temporary)
        packages = root / "packages"
        packages.mkdir()
        gh("release", "download", tag, "--repo", SOURCE, "--dir", packages)
        if ({p.name for p in packages.iterdir()} != set(sizes)
                or any(not p.is_file() or p.is_symlink() or p.stat().st_size != sizes[p.name] for p in packages.iterdir())):
            raise ValueError("Downloaded assets do not match the source release")
        # This does not regenerate hashes/manifests or sign any bytes. It verifies the
        # original complete eighteen-package distribution with the pinned publisher key.
        verify_signatures(packages, version, public_key)
        if existing is None:
            notes_file = root / "RELEASE.md"
            notes_file.write_text(notes + f"\n\n源码与构建记录：https://github.com/{SOURCE}/releases/tag/{tag}\n", encoding="utf-8")
            flags = [] if channel == "stable" else ["--prerelease"]
            gh("release", "create", tag, "--repo", destination, "--target", "main", "--draft",
               "--title", f"NexaCL Firefly {version}", "--notes-file", notes_file, *flags)
        files = sorted(packages.iterdir())
        for offset in range(0, len(files), 20):
            gh("release", "upload", tag, "--repo", destination, "--clobber", *files[offset:offset + 20])
        uploaded = json.loads(gh("release", "view", tag, "--repo", destination, "--json", "assets"))
        uploaded_assets = uploaded.get("assets", [])
        if (len(uploaded_assets) != len(sizes)
                or {asset.get("name"): asset.get("size") for asset in uploaded_assets} != sizes):
            raise ValueError("Destination draft is incomplete or contains unexpected assets")
        final_draft = get_release(destination, tag)
        if final_draft is None or not final_draft["draft"]:
            raise ValueError("Destination draft changed during publication")
        require_notes(final_draft.get("body"))
        gh("release", "edit", tag, "--repo", destination, "--draft=false",
           "--prerelease=" + str(channel != "stable").lower(),
           "--latest=" + str(channel == "stable").lower())
    print(f"Published verified update distribution: {destination} {tag}")
    return True


def recent_tags():
    releases = api(f"repos/{SOURCE}/releases?per_page=30")
    if not isinstance(releases, list) or len(releases) > 30:
        raise ValueError("Invalid public source release list")
    selected = {}
    for release in releases:
        if not isinstance(release, dict) or release.get("draft") is not False:
            continue
        try:
            _, channel = identity(release.get("tag_name"))
        except (ValueError, TypeError):
            continue
        selected.setdefault(channel, release["tag_name"])
    return list(selected.values())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--destination", default=DESTINATION)
    parser.add_argument("--tag", default="")
    parser.add_argument("--public-key", type=Path, required=True)
    args = parser.parse_args()
    if args.destination != DESTINATION:
        raise ValueError("Unexpected update repository")
    for tag in [args.tag] if args.tag else recent_tags():
        publish(tag, args.public_key.read_bytes(), args.destination)


if __name__ == "__main__":
    main()
