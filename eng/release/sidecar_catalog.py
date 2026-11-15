"""Validate the optional complete set of independent signed Sidecar update assets."""
import hashlib
import json

RIDS = ('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')
PACKAGE = 'Nexa.Cloud.Market.Sidecar'
PACKAGES = frozenset((PACKAGE, 'Nexa.Plugin.Sidecar'))


def catalog_names(directory, version):
    from manifest import release_channel
    if not any(directory.glob('Nexa-Sidecars-*.json')):
        return set()
    names = set()
    package_set = None
    for rid in RIDS:
        name = f'Nexa-Sidecars-{rid}.json'
        path = directory / name
        if path.is_symlink() or path.stat().st_size > 256 * 1024:
            raise ValueError('Invalid Sidecar catalog')
        data = json.loads(path.read_text(encoding='utf-8'))
        if data.get('schema') != 1 or data.get('rid') != rid or data.get('release') != version:
            raise ValueError('Sidecar catalog identity mismatch')
        entries = data.get('packages')
        if not isinstance(entries, list) or not 1 <= len(entries) <= len(PACKAGES) or any(not isinstance(item, dict) for item in entries):
            raise ValueError('Unexpected Sidecar package set')
        identities = [item.get('id') for item in entries]
        if any(not isinstance(identity, str) or identity not in PACKAGES for identity in identities) or len(set(identities)) != len(identities):
            raise ValueError('Unexpected or duplicate Sidecar identity')
        if package_set is None:
            package_set = set(identities)
        elif package_set != set(identities):
            raise ValueError('Sidecar package set differs between runtimes')
        names.add(name)
        for item in entries:
            release_channel(item['minimumHostVersion'])
            if not isinstance(item.get('version'), str) or not 1 <= len(item['version']) <= 64:
                raise ValueError('Invalid Sidecar version')
            image = f"{item['id']}-{rid}.nsc"
            payload = directory / image
            if payload.is_symlink() or payload.stat().st_size != item.get('size') or not 64 <= item['size'] <= 512 * 1024**2:
                raise ValueError('Sidecar size mismatch')
            with payload.open('rb') as stream:
                if hashlib.file_digest(stream, 'sha256').hexdigest() != item.get('sha256'):
                    raise ValueError('Sidecar digest mismatch')
            names.add(image)
    return names
