"""Native packaging on the matching CI runner. No source or shell strings are evaluated."""
import argparse
import fnmatch
import os
import plistlib
import shutil
import subprocess
import tarfile
import tempfile
import time
from pathlib import Path
from macos_bundle import bundle_info
from publish_launcher import module_names

ROOT = Path(__file__).resolve().parents[2]

# Match individual path components, case-insensitively on every build runner.
# Do not blanket-exclude DLL/JSON/XML: they can be runtime libraries or assets.
DEVELOPMENT_ARTIFACTS = (
    "*.pdb", "*.dbg", "*.debug", "*.dsym", "*.ilk", "*.ipdb", "*.iobj",
    "*.tests", "*.tests.*", "testhost", "testhost.*",
    "microsoft.testplatform.*", "microsoft.visualstudio.testplatform.*",
    "testresults", "coverage", "coverage.cobertura.xml", "coverage.opencover.xml",
)

# A native shell smoke can initialize portable update state in the publish directory.
# Installation-local journals, transactions and slots must never become publisher payloads.
UPDATE_STATE_ARTIFACTS = (
    "portable-baseline.journal", "active-update.journal", "update-status.journal",
    "pending-update.journal", "highest-accepted-version.journal", "update-transaction.lock",
    ".nexa-tx-*", ".nexa-slot-*",
)


def is_development_artifact(name):
    return any(fnmatch.fnmatchcase(name.lower(), pattern) for pattern in DEVELOPMENT_ARTIFACTS)


def is_payload_artifact(name):
    return is_development_artifact(name) or any(
        fnmatch.fnmatchcase(name.lower(), pattern) for pattern in UPDATE_STATE_ARTIFACTS)


def validate_native_runtime(payload):
    managed_runtime = {"system.private.corelib.dll"}
    managed_runtime.update(stem + ".dll" for stem in ("coreclr", "clrjit", "hostfxr", "hostpolicy"))
    managed_runtime.update("lib" + stem + suffix for stem in ("coreclr", "clrjit", "hostfxr", "hostpolicy")
                           for suffix in (".so", ".dylib"))
    mixed = sorted(str(path.relative_to(payload)) for path in payload.rglob("*")
                   if path.is_file() and path.name.lower() in managed_runtime)
    if mixed:
        raise ValueError(f"CoreCLR runtime files in NativeAOT payload: {mixed}")


def validate_runtime_contents(payload):
    validate_native_runtime(payload)
    unwanted = [str(path.relative_to(payload)) for path in payload.rglob("*")
                if is_payload_artifact(path.name)]
    if unwanted:
        raise ValueError(f"Development or update-state artifacts in runtime payload: {sorted(unwanted)}")


def required_tool(variable):
    path = Path(os.environ[variable])
    if not path.is_absolute() or not path.is_file():
        raise ValueError(f"Missing isolated build tool: {variable}")
    return path


def copy_runtime_payload(source, destination):
    # NativeAOT emits large debugging sidecars; they are not runtime dependencies.
    # Keep the publish directory intact for separate diagnostic artifact retention.
    shutil.copytree(source, destination,
                    ignore=lambda _directory, names: [name for name in names if is_payload_artifact(name)])
    validate_runtime_contents(destination)


def run(*args, **kwargs):
    subprocess.run([str(arg) for arg in args], check=True, **kwargs)


def archive(source, output, name):
    validate_runtime_contents(source)
    if output.suffix == ".zip":
        shutil.make_archive(str(output.with_suffix("")), "zip", source)
    else:
        with tarfile.open(output, "w:gz") as tar:
            tar.add(source, arcname=name)


def windows(payload, output, work, base, version, prefix, arch):
    iscc = required_tool("NEXA_ISCC")
    icon = ROOT / "Nexa.Desktop/Assets/icon.ico"
    run(iscc, f"/DPayload={payload}", f"/DProductVersion={version}", f"/DNumericVersion={prefix}",
        f"/DInstallArch={'arm64' if arch == 'arm64' else 'x64compatible'}", f"/DOutputDir={output}",
        f"/DOutputName={base}.setup", f"/DIconPath={icon}", ROOT / "eng/release/windows.iss")
    run("wix", "build", ROOT / "eng/release/windows.wxs", "-arch", arch,
        "-d", f"Payload={payload}", "-d", f"NumericVersion={prefix}", "-d", f"IconPath={icon}",
                "-o", work / "launcher.msi")
    shutil.copy2(work / "launcher.msi", output / f"{base}.msi")
    archive(payload, output / f"{base}.portable.zip", "NexaCL")


def create_dmg(source, destination):
    """Retry only transient hdiutil contention; publish only a verified image."""
    destination = Path(destination)
    # hdiutil's automatic APFS sizing can leave too little room for filesystem
    # metadata. Reserve explicit headroom independently of UDZO compression.
    payload_bytes = sum(path.stat().st_size for path in Path(source).rglob("*") if path.is_file())
    capacity_mib = max(128, (payload_bytes * 5 // 4 + (1 << 20) - 1) // (1 << 20) + 64)
    print(f"DMG payload={payload_bytes} bytes capacity={capacity_mib} MiB "
          f"host_free={shutil.disk_usage(destination.parent).free} bytes", flush=True)
    for attempt in range(3):
        with tempfile.TemporaryDirectory(prefix="nexacl-dmg-", dir=destination.parent) as staging:
            image = Path(staging) / "image.dmg"
            try:
                result = subprocess.run(
                    ["hdiutil", "create", "-volname", "NexaCL", "-srcfolder", str(source),
                     "-size", f"{capacity_mib}m", "-format", "UDZO", str(image)],
                    check=True, capture_output=True, text=True,
                    env={**os.environ, "LC_ALL": "C"})
            except subprocess.CalledProcessError as error:
                # Never retry permission, disk-space, input or verification failures.
                if attempt == 2 or "Resource busy" not in (error.stderr or ""):
                    print(error.stderr or "", flush=True)
                    raise
                print(f"hdiutil resource busy; retrying ({attempt + 1}/2)", flush=True)
            else:
                print(result.stdout or "", end="", flush=True)
                run("hdiutil", "verify", image)
                os.replace(image, destination)
                return
        time.sleep(2 ** (attempt + 1))


def seal_macos_bundle(app, work):
    # Preserve independently publisher-signed Sidecar bytes. Deep force-signing
    # would rewrite its Mach-O signature and invalidate the detached GPG signature.
    sidecar = app / "Contents/MacOS/Nexa.Cloud.Market.Sidecar.nsc"
    held = work / "publisher-signed-sidecar.nsc"
    if sidecar.exists():
        sidecar.rename(held)
    try:
        run("codesign", "--force", "--deep", "--sign", "-", app)
    finally:
        if held.exists():
            held.rename(sidecar)
    if sidecar.exists():
        # The Sidecar already has its NativeAOT ad-hoc signature. Reseal only
        # the outer application after restoring it, without rewriting nested code.
        run("codesign", "--force", "--sign", "-", app)
    run("codesign", "--verify", "--deep", "--strict", app)


def macos(payload, output, work, base, version, prefix, arch):
    app = work / "Nexa.app"
    contents = app / "Contents"
    copy_runtime_payload(payload, contents / "MacOS")
    native_actions = contents / "MacOS/NativeActions"
    if not (native_actions / "NexaCL Actions.app/Contents/MacOS/NexaCL.Actions").is_file():
        raise ValueError("Missing macOS native actions companion")
    native_actions.rename(contents / "Helpers")
    resources = contents / "Resources"
    resources.mkdir()
    iconset = work / "Launcher.iconset"
    iconset.mkdir()
    for size in (16, 32, 128, 256, 512):
        for scale in (1, 2):
            suffix = "@2x" if scale == 2 else ""
            run("sips", "-z", size * scale, size * scale, ROOT / "Nexa.Desktop/Assets/icon.png",
                "--out", iconset / f"icon_{size}x{size}{suffix}.png", stdout=subprocess.DEVNULL)
    run("iconutil", "-c", "icns", iconset, "-o", resources / "Launcher.icns")
    with (contents / "Info.plist").open("wb") as stream:
        plistlib.dump(bundle_info(version, prefix), stream)
    seal_macos_bundle(app, work)
    run(contents / "MacOS/Nexa.Desktop", "--validate-shell")
    dmg_source = work / "dmg"
    dmg_source.mkdir()
    package_root = work / "macos-root"
    package_root.mkdir()
    installed = package_root / "Library/Application Support/NexaCL"
    installed.mkdir(parents=True)
    shutil.copytree(app, installed / app.name)
    shutil.copy2(payload / "Nexa.Update.Helper", installed / "Nexa.Update.Helper")
    (installed / "Nexa.Update.Helper").chmod(0o755)
    scripts = work / "macos-scripts"
    scripts.mkdir()
    postinstall = scripts / "postinstall"
    postinstall.write_text('''#!/bin/sh
set -eu
target='/Library/Application Support/NexaCL/Nexa.app'
link='/Applications/Nexa.app'
if [ -L "$link" ] && [ "$(/usr/bin/readlink "$link")" = "$target" ]; then exit 0; fi
if [ -e "$link" ] || [ -L "$link" ]; then
  echo 'Please remove the previous /Applications/Nexa.app before installing the protected system edition.' >&2
  exit 1
fi
/bin/ln -s "$target" "$link"
''', encoding="utf-8")
    postinstall.chmod(0o755)
    component_plist = work / "components.plist"
    with component_plist.open("wb") as stream:
        plistlib.dump([dict(RootRelativeBundlePath="Library/Application Support/NexaCL/" + app.name, BundleIsRelocatable=False,
                           BundleIsVersionChecked=False, BundleHasStrictIdentifier=True,
                           BundleOverwriteAction="upgrade")], stream)
    run("pkgbuild", "--root", package_root, "--component-plist", component_plist,
        "--identifier", "org.nexacl.launcher", "--version", prefix,
        "--install-location", "/", "--ownership", "recommended", "--scripts", scripts, work / "Nexa-component.pkg")
    run("productbuild", "--distribution", ROOT / "eng/release/macos-distribution.xml",
        "--package-path", work, dmg_source / "NexaCL.pkg")
    create_dmg(dmg_source, output / f"{base}.dmg")
    archive(app, output / f"{base}.portable.tar.gz", app.name)


def linux(payload, output, work, base, version, prefix, arch):
    appdir = work / "Nexa.AppDir"
    shutil.copytree(payload, appdir / "usr/lib/nexacl")
    binary = appdir / "usr/lib/nexacl/Nexa.Desktop"
    binary.chmod(0o755)
    desktop = "[Desktop Entry]\nType=Application\nName=Nexa\nExec=nexacl %U\nIcon=nexacl\nTerminal=false\nCategories=Game;\nStartupWMClass=Nexa.Desktop\n"
    launcher = appdir / "usr/bin/nexacl"
    launcher.parent.mkdir(parents=True)
    launcher.write_text('#!/bin/sh\nexec "$(dirname "$(readlink -f "$0")")/../lib/nexacl/Nexa.Desktop" "$@"\n', encoding="utf-8")
    launcher.chmod(0o755)
    for relative in ("nexacl.desktop", "usr/share/applications/nexacl.desktop"):
        path = appdir / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(desktop, encoding="utf-8")
    for relative in ("nexacl.png", "usr/share/icons/hicolor/256x256/apps/nexacl.png"):
        path = appdir / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / "Nexa.Desktop/Assets/icon.png", path)
    (appdir / "AppRun").write_text('#!/bin/sh\nHERE="$(dirname "$(readlink -f "$0")")"\nexec "$HERE/usr/bin/nexacl" "$@"\n', encoding="utf-8")
    (appdir / "AppRun").chmod(0o755)
    run("desktop-file-validate", appdir / "nexacl.desktop")
    for package_type, package_arch in (("deb", "arm64" if arch == "arm64" else "amd64"), ("rpm", "aarch64" if arch == "arm64" else "x86_64")):
        native_version = version.replace(".alpha.", "~alpha.").replace(".beta.", "~beta.").replace(".ci.", "~ci.")
        dependencies = ("libc6", "libgcc-s1", "libstdc++6", "zlib1g", "libfontconfig1", "libx11-6", "libice6", "libsm6", "libsecret-tools", "pkexec") if package_type == "deb" else ("glibc", "libgcc", "libstdc++", "zlib", "fontconfig", "libX11", "libICE", "libSM", "/usr/bin/secret-tool", "/usr/bin/pkexec")
        options = [item for dependency in dependencies for item in ("--depends", dependency)]
        options += [f"--{package_type}-user", "root", f"--{package_type}-group", "root"]
        run("fpm", "-s", "dir", "-t", package_type, "-n", "nexacl", "-v", native_version, "--iteration", "1",
            "-a", package_arch, "--maintainer", "Nexa", "--description", "Nexa Minecraft Launcher",
            "--url", "https://github.com/Nexa-MC/NexaCL", "--license", "Apache-2.0", *options,
            "-C", appdir, "-p", output / f"{base}.{package_type}", "usr")
    environment = dict(os.environ, ARCH="aarch64" if arch == "arm64" else "x86_64", APPIMAGE_EXTRACT_AND_RUN="1")
    run(required_tool("APPIMAGETOOL"), "--runtime-file", required_tool("APPIMAGE_RUNTIME"),
        appdir, output / f"{base}.AppImage", env=environment)
    archive(payload, output / f"{base}.portable.tar.gz", "Nexa")
    run("dpkg-deb", "--info", output / f"{base}.deb")
    run("rpm", "-qip", output / f"{base}.rpm")


def validate_payload(payload, platform):
    validate_native_runtime(payload)
    suffix = ".exe" if platform == "win" else ""
    required = ["Nexa.Desktop" + suffix, "Nexa.Jvm.Host" + suffix, "Nexa.Update.Helper" + suffix]
    required.extend(module_names(platform + "-x64"))
    for name in required:
        path = payload / name
        if not path.is_file() or path.stat().st_size == 0:
            raise ValueError(f"Release payload is missing {name}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--payload", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--rid", required=True)
    parser.add_argument("--version", required=True)
    args = parser.parse_args()
    from metadata import identity
    data = identity("refs/tags/" + args.version, args.version.rsplit(".", 1)[-1].ljust(40, "0") if ".ci." in args.version else "0" * 40)
    platform, arch = args.rid.split("-")
    if platform not in ("win", "linux", "osx") or arch not in ("x64", "arm64"):
        raise ValueError("Unsupported RID")
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    work = output.parent / f"package-{args.rid}"
    work.mkdir()  # A fresh staging directory prevents stale files entering packages.
    payload = work / "runtime"
    copy_runtime_payload(args.payload.resolve(), payload)
    validate_payload(payload, platform)
    globals()[{"win": "windows", "osx": "macos", "linux": "linux"}[platform]](
        payload, output, work, f"Nexa-{args.version}-{args.rid}", args.version, data["prefix"], arch)


if __name__ == "__main__":
    main()
