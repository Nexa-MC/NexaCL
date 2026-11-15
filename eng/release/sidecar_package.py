"""Package the independently built Sidecar; sign only pinned binary inputs."""
import hashlib
import json
from pathlib import Path
import shutil
import tarfile
import tempfile
import zipfile
from sidecar_catalog import PACKAGES
from sign import FINGERPRINT, gpg, import_public, keyring, sign_files

ROOT = Path(__file__).resolve().parents[2]
NAME = 'Nexa.Cloud.Market.Sidecar.nsc'
INFO = 'Nexa.Cloud.Market.Sidecar.json'
RIDS = ('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')


def verify_pair(directory, public, fingerprint=FINGERPRINT, package_id="Nexa.Cloud.Market.Sidecar"):
    if package_id not in PACKAGES:
        raise ValueError('Unreviewed Sidecar identity')
    name = package_id + '.nsc'
    with keyring() as home:
        import_public(home, public, fingerprint)
        status = gpg(home, '--status-fd', '1', '--verify', str(directory / (name + '.asc')), str(directory / name))
        signers = [line.split()[2].decode('ascii') for line in status.splitlines() if line.startswith(b'[GNUPG:] VALIDSIG ')]
        if signers != [fingerprint]:
            raise ValueError('Sidecar signature does not match the pinned publisher')


def stage(source, payload, rid, lock, public, private, passphrase, fingerprint=FINGERPRINT, package_id="Nexa.Cloud.Market.Sidecar"):
    if package_id not in PACKAGES:
        raise ValueError('Unreviewed Sidecar identity')
    name, info_name = package_id + '.nsc', package_id + '.json'
    if rid not in RIDS:
        raise ValueError('Unsupported Sidecar runtime')
    item = lock['artifacts'][rid]
    if source.name != f'{package_id}-{rid}.nsc' or source.is_symlink() or not source.is_file():
        raise ValueError('Unexpected Sidecar input')
    with source.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    if digest != item['sha256'] or source.stat().st_size != item['size']:
        raise ValueError('Sidecar input differs from reviewed build')
    payload.mkdir(parents=True, exist_ok=True)
    destination = payload / name
    shutil.copyfile(source, destination)
    destination.chmod(0o755)
    try:
        sign_files(payload, {name}, public, private, passphrase, fingerprint)
        verify_pair(payload, public, fingerprint, package_id)
        (payload / info_name).write_text(json.dumps(dict(runtimeVersion=lock['runtimeVersion'], runtimeIdentifier=rid,
            sourceCommit=lock['commit'], hostReference=lock['hostReference'], sha256=digest,
            signingFingerprint=fingerprint), indent=2) + '\n', encoding='utf-8')
    except BaseException:
        for name in (name, name + '.asc', info_name):
            (payload / name).unlink(missing_ok=True)
        raise


def verify_archives(directory, version, lock, public, fingerprint=FINGERPRINT, export=False, package_id="Nexa.Cloud.Market.Sidecar"):
    if package_id not in PACKAGES:
        raise ValueError('Unreviewed Sidecar identity')
    image_name, info_name = package_id + '.nsc', package_id + '.json'
    for rid in RIDS:
        extension = '.portable.zip' if rid.startswith('win-') else '.portable.tar.gz'
        archive = directory / f'Nexa-{version}-{rid}{extension}'
        with tempfile.TemporaryDirectory(prefix='nexa-sidecar-verify-') as temporary:
            target = Path(temporary)
            if extension.endswith('.zip'):
                with zipfile.ZipFile(archive) as package:
                    for name in (image_name, image_name + '.asc', info_name):
                        entries = [i for i in package.infolist() if i.filename == name]
                        if len(entries) != 1 or entries[0].file_size > (512 * 1024 * 1024 if name == image_name else 256 * 1024):
                            raise ValueError('Missing or duplicate bundled Sidecar')
                        (target / name).write_bytes(package.read(entries[0]))
            else:
                with tarfile.open(archive, 'r:gz') as package:
                    # macOS portable packages put the executable in the application bundle.
                    for name in (image_name, image_name + '.asc', info_name):
                        entries = [i for i in package.getmembers() if i.name.endswith('/' + name)]
                        if len(entries) != 1 or not entries[0].isfile() or entries[0].size > (512 * 1024 * 1024 if name == image_name else 256 * 1024):
                            raise ValueError('Missing or invalid bundled Sidecar')
                        if name == image_name and not entries[0].mode & 0o111:
                            raise ValueError('Bundled Sidecar is not executable')
                        (target / name).write_bytes(package.extractfile(entries[0]).read())
            info = json.loads((target / info_name).read_text())
            with (target / image_name).open('rb') as stream:
                digest = hashlib.file_digest(stream, 'sha256').hexdigest()
            if (digest != lock['artifacts'][rid]['sha256'] or info['sha256'] != digest
                    or info['runtimeIdentifier'] != rid or info['sourceCommit'] != lock['commit']
                    or info['signingFingerprint'] != fingerprint or info['runtimeVersion'] != lock['runtimeVersion']
                    or info['hostReference'] != lock['hostReference'] or (target / image_name).stat().st_size != lock['artifacts'][rid]['size']):
                raise ValueError('Bundled Sidecar provenance mismatch')
            verify_pair(target, public, fingerprint, package_id)
            if export:
                shutil.copyfile(target / image_name, directory / f'{package_id}-{rid}.nsc')
                catalog = dict(schema=1, rid=rid, release=version, packages=[
                    dict(id=package_id, version=lock['runtimeVersion'], sha256=digest,
                         size=(target / image_name).stat().st_size, minimumHostVersion=lock.get('minimumHostVersion', version))])
                catalog_path = directory / f'Nexa-Sidecars-{rid}.json'
                if catalog_path.exists():
                    previous = json.loads(catalog_path.read_text(encoding='utf-8'))
                    if any(previous.get(key) != catalog[key] for key in ('schema', 'rid', 'release')):
                        raise ValueError('Existing Sidecar catalog identity mismatch')
                    catalog['packages'] = [entry for entry in previous['packages'] if entry.get('id') != package_id] + catalog['packages']
                catalog['packages'].sort(key=lambda entry: entry['id'])
                catalog_path.write_text(json.dumps(catalog, indent=2) + '\n', encoding='utf-8')
