"""Fetch, sign and verify reviewed independent images without compiling their source."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
from sidecar_catalog import catalog_names
from sidecar_package import ROOT, RIDS, stage, verify_archives
from manifest import release_channel

LOCKS = (('Nexa.Cloud.Market.Sidecar', 'market-sidecar.lock.json', True),
         ('Nexa.Plugin.Sidecar', 'plugin-sidecar.lock.json', False))


def reviewed_locks(root=ROOT):
    result = []
    for identity, filename, required in LOCKS:
        path = root / 'eng/release' / filename
        if not path.exists() and not required:
            continue
        if path.is_symlink():
            raise ValueError('Sidecar input lock must be a regular reviewed file')
        lock = json.loads(path.read_text(encoding='utf-8'))
        for key in ('commit', 'hostReference'):
            if not isinstance(lock.get(key), str) or not re.fullmatch('[0-9a-f]{40}', lock[key]):
                raise ValueError('Invalid Sidecar source identity')
        repository = lock.get('inputRepository', 'Nexa-MC/NexaCL')
        if not isinstance(repository, str) or not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9][A-Za-z0-9_.-]{0,99}', repository):
            raise ValueError('Invalid Sidecar input repository')
        if not isinstance(lock.get('inputRelease'), str) or not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_.-]{0,127}', lock['inputRelease']):
            raise ValueError('Invalid Sidecar input release')
        if type(lock.get('buildRun')) is not int or lock['buildRun'] < 1:
            raise ValueError('Invalid Sidecar build run')
        if not isinstance(lock.get('runtimeVersion'), str) or not 1 <= len(lock['runtimeVersion']) <= 64:
            raise ValueError('Invalid Sidecar runtime version')
        release_channel(lock['minimumHostVersion'])
        if set(lock.get('artifacts', {})) != set(RIDS):
            raise ValueError('Sidecar lock must contain all six runtimes')
        for item in lock['artifacts'].values():
            if type(item.get('size')) is not int or not 64 <= item['size'] <= 512 * 1024**2 or not re.fullmatch('[0-9a-f]{64}', item.get('sha256', '')):
                raise ValueError('Invalid Sidecar artifact identity')
        result.append((identity, lock))
    return result


def fetch(directory, rid, locks):
    directory.mkdir(parents=True, exist_ok=True)
    for identity, lock in locks:
        subprocess.run(['gh', 'release', 'download', lock['inputRelease'], '--repo',
            lock.get('inputRepository', 'Nexa-MC/NexaCL'), '--pattern', f'{identity}-{rid}.nsc',
            '--dir', str(directory)], check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('fetch', 'stage', 'verify', 'export', 'smoke'))
    parser.add_argument('directory', type=Path)
    parser.add_argument('--source', type=Path)
    parser.add_argument('--rid', choices=RIDS)
    parser.add_argument('--version')
    parser.add_argument('--host', type=Path)
    args = parser.parse_args()
    locks = reviewed_locks()
    if args.mode in ('fetch', 'stage') and args.rid is None:
        parser.error('--rid is required')
    if args.mode == 'stage' and args.source is None:
        parser.error('--source is required')
    if args.mode in ('verify', 'export') and args.version is None:
        parser.error('--version is required')
    if args.mode == 'smoke' and args.host is None:
        parser.error('--host is required')
    if args.mode == 'fetch':
        fetch(args.directory, args.rid, locks)
        return
    if args.mode == 'smoke':
        for identity, _ in locks:
            subprocess.run([str(args.host.resolve()), '--validate-shell', '--require-sidecar=' + identity], check=True)
        return
    public = (ROOT / 'GPG-PUBLIC-KEY.asc').read_bytes()
    for identity, lock in locks:
        if args.mode == 'stage':
            stage(args.source / f'{identity}-{args.rid}.nsc', args.directory, args.rid, lock, public,
                  os.environ.get('GPG_PRIVATE_KEY', ''), os.environ.get('GPG_PASSPHRASE', ''), package_id=identity)
        else:
            verify_archives(args.directory, args.version, lock, public, export=args.mode == 'export', package_id=identity)
    if args.mode in ('verify', 'export'):
        catalog_names(args.directory, args.version)
        expected = {identity for identity, _ in locks}
        for rid in RIDS:
            path = args.directory / f'Nexa-Sidecars-{rid}.json'
            if path.exists() and {entry['id'] for entry in json.loads(path.read_text())['packages']} != expected:
                raise ValueError('Exported catalog differs from the reviewed component set')
    if not any(identity == 'Nexa.Plugin.Sidecar' for identity, _ in locks):
        print('PluginSidecar is not bundled: a reviewed complete plugin-sidecar.lock.json is not present.')


if __name__ == '__main__':
    main()
