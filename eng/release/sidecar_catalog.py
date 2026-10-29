"""Validate the optional complete set of independent signed Sidecar update assets."""
import hashlib
import json

RIDS = ('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')
PACKAGE = 'Nexa.Cloud.Market.Sidecar'


def catalog_names(directory, version):
    from manifest import release_channel
    if not any(directory.glob('Nexa-Sidecars-*.json')):
        return set()
    names = set()
    for rid in RIDS:
        name = f'Nexa-Sidecars-{rid}.json'
        path = directory / name
        if path.is_symlink() or path.stat().st_size > 256 * 1024:
            raise ValueError('Invalid Sidecar catalog')
        data = json.loads(path.read_text(encoding='utf-8'))
        if data.get('schema') != 1 or data.get('rid') != rid or data.get('release') != version:
            raise ValueError('Sidecar catalog identity mismatch')
        entries = data.get('packages')
        if not isinstance(entries, list) or len(entries) != 1:
            raise ValueError('Unexpected Sidecar package set')
        item = entries[0]
        if item.get('id') != PACKAGE:
            raise ValueError('Unexpected Sidecar identity')
        release_channel(item['minimumHostVersion'])
        if not isinstance(item.get('version'), str) or not 1 <= len(item['version']) <= 64:
            raise ValueError('Invalid Sidecar version')
        image = f'{PACKAGE}-{rid}.nsc'
        payload = directory / image
        if payload.is_symlink() or payload.stat().st_size != item.get('size') or not 64 <= item['size'] <= 512 * 1024**2:
            raise ValueError('Sidecar size mismatch')
        with payload.open('rb') as stream:
            if hashlib.file_digest(stream, 'sha256').hexdigest() != item.get('sha256'):
                raise ValueError('Sidecar digest mismatch')
        names.update((name, image))
    return names
