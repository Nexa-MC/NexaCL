#!/usr/bin/env python3
"""Prepare pinned public SDK packages outside the Host source graph."""
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import urllib.request
import zipfile

NAMES = ('Nexa.Plugin.Bootstrap.V1', 'Nexa.Plugin.Sdk.Abi100',
         'Nexa.Plugin.Hosting', 'Nexa.Plugin.MarketDelivery',
         'PluginSidecar.WasmRuntime', 'PluginSidecar.Packages')


def digest(data):
    return hashlib.sha256(data).hexdigest()


def package_names(lock):
    return {f"{name}.{'0.1.0-alpha.1' if name == 'Nexa.Plugin.MarketDelivery' else lock['version']}.nupkg"
            for name in NAMES}


def validate_manifest(data, lock):
    packages = data['packages']
    if (data['commit'] != lock['commit'] or len(packages) != len(NAMES)
            or {p['file'] for p in packages} != package_names(lock)):
        raise ValueError('SDK feed does not match its immutable lock.')
    return packages


def verify_bundle(payload, lock):
    distribution = lock['distribution']
    if len(payload) != distribution['size'] or digest(payload) != distribution['sha256']:
        raise ValueError('SDK bundle size or digest mismatch.')
    with zipfile.ZipFile(io.BytesIO(payload)) as archive:
        expected = package_names(lock) | {'feed-manifest.json'}
        entries = archive.infolist()
        if (len(entries) != len(expected) or {e.filename for e in entries} != expected
                or any(e.file_size > 16 * 1024 * 1024 for e in entries)):
            raise ValueError('Invalid SDK bundle entries.')
        files = {e.filename: archive.read(e) for e in entries}
    manifest = json.loads(files['feed-manifest.json'])
    for package in validate_manifest(manifest, lock):
        if digest(files[package['file']]) != package['sha256']:
            raise ValueError('SDK package digest mismatch.')
    return files


def atomic_write(path, data):
    with tempfile.NamedTemporaryFile(dir=path.parent, delete=False) as temporary:
        temp_path = Path(temporary.name)
        temporary.write(data)
    try:
        os.replace(temp_path, path)
    finally:
        temp_path.unlink(missing_ok=True)


def main():
    root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser()
    parser.add_argument('--source', type=Path, help='Rebuild from a local SDK Git checkout')
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    lock = json.loads((root / 'eng/plugins/sdk.lock.json').read_text())
    feed = root / 'eng/plugins/feed'
    manifest = feed / 'feed-manifest.json'
    if not args.source and manifest.exists():
        try:
            packages = validate_manifest(json.loads(manifest.read_text()), lock)
            if all(digest((feed / p['file']).read_bytes()) == p['sha256'] for p in packages):
                print('Pinned plugin SDK feed verified.')
                return
        except (OSError, ValueError, KeyError, TypeError):
            pass
    feed.mkdir(parents=True, exist_ok=True)
    if not args.source:
        distribution = lock['distribution']
        url = (f"https://github.com/{distribution['repository']}/releases/download/"
               f"{distribution['release']}/{distribution['file']}")
        with urllib.request.urlopen(url, timeout=60) as response:
            payload = response.read(distribution['size'] + 1)
        files = verify_bundle(payload, lock)
        # Publish the manifest last so partial downloads cannot become valid caches.
        for name in sorted(package_names(lock)):
            atomic_write(feed / name, files[name])
        atomic_write(manifest, files['feed-manifest.json'])
    else:
        checkout = Path.home() / '.cache/nexa-plugin-sdk' / lock['commit']
        checkout.parent.mkdir(parents=True, exist_ok=True)
        if not (checkout / '.git').exists():
            subprocess.run(['git', 'clone', '--no-checkout', str(args.source.resolve()), str(checkout)], check=True)
        subprocess.run(['git', '-C', str(checkout), 'checkout', '--detach', lock['commit']], check=True)
        actual = subprocess.check_output(['git', '-C', str(checkout), 'rev-parse', 'HEAD'], text=True).strip()
        if actual != lock['commit'] or subprocess.check_output(['git', '-C', str(checkout), 'status', '--porcelain']):
            raise ValueError('SDK source must be clean and match its immutable lock.')
        for name in NAMES:
            subprocess.run([args.dotnet, 'pack', str(checkout / 'src' / name), '-c', 'Release',
                            '-m:1', '-o', str(feed), '-p:RestoreSources=https://api.nuget.org/v3/index.json'],
                           cwd=checkout, check=True)
        packages = [dict(file=name, sha256=digest((feed / name).read_bytes()))
                    for name in sorted(package_names(lock))]
        atomic_write(manifest, (json.dumps(dict(commit=lock['commit'], packages=packages), indent=2) + '\n').encode())
    print('Pinned plugin SDK feed prepared.')


if __name__ == '__main__':
    main()
