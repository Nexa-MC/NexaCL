"""Package the independently built Market Sidecar; sign only pinned binary inputs."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import tarfile
import tempfile
import zipfile
from sign import FINGERPRINT, gpg, import_public, keyring, sign_files

ROOT = Path(__file__).resolve().parents[2]
NAME = 'Nexa.Cloud.Market.Sidecar.nsc'
INFO = 'Nexa.Cloud.Market.Sidecar.json'
RIDS = ('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')


def verify_pair(directory, public, fingerprint=FINGERPRINT):
    with keyring() as home:
        import_public(home, public, fingerprint)
        status = gpg(home, '--status-fd', '1', '--verify', str(directory / (NAME + '.asc')), str(directory / NAME))
        signers = [line.split()[2].decode('ascii') for line in status.splitlines() if line.startswith(b'[GNUPG:] VALIDSIG ')]
        if signers != [fingerprint]:
            raise ValueError('Market Sidecar signature does not match the pinned publisher')


def stage(source, payload, rid, lock, public, private, passphrase, fingerprint=FINGERPRINT):
    if rid not in RIDS:
        raise ValueError('Unsupported Sidecar runtime')
    item = lock['artifacts'][rid]
    if source.name != f'Nexa.Cloud.Market.Sidecar-{rid}.nsc' or source.is_symlink() or not source.is_file():
        raise ValueError('Unexpected Sidecar input')
    with source.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    if digest != item['sha256'] or source.stat().st_size != item['size']:
        raise ValueError('Market Sidecar input differs from reviewed build')
    payload.mkdir(parents=True, exist_ok=True)
    destination = payload / NAME
    shutil.copyfile(source, destination)
    destination.chmod(0o755)
    try:
        sign_files(payload, {NAME}, public, private, passphrase, fingerprint)
        verify_pair(payload, public, fingerprint)
        (payload / INFO).write_text(json.dumps(dict(runtimeVersion=lock['runtimeVersion'], runtimeIdentifier=rid,
            sourceCommit=lock['commit'], hostReference=lock['hostReference'], sha256=digest,
            signingFingerprint=fingerprint), indent=2) + '\n', encoding='utf-8')
    except BaseException:
        for name in (NAME, NAME + '.asc', INFO):
            (payload / name).unlink(missing_ok=True)
        raise


def verify_archives(directory, version, lock, public, fingerprint=FINGERPRINT, export=False):
    for rid in RIDS:
        extension = '.portable.zip' if rid.startswith('win-') else '.portable.tar.gz'
        archive = directory / f'Nexa-{version}-{rid}{extension}'
        with tempfile.TemporaryDirectory(prefix='nexa-market-verify-') as temporary:
            target = Path(temporary)
            if extension.endswith('.zip'):
                with zipfile.ZipFile(archive) as package:
                    for name in (NAME, NAME + '.asc', INFO):
                        entries = [i for i in package.infolist() if i.filename == name]
                        if len(entries) != 1 or entries[0].file_size > 100 * 1024 * 1024:
                            raise ValueError('Missing or duplicate bundled Market Sidecar')
                        (target / name).write_bytes(package.read(entries[0]))
            else:
                with tarfile.open(archive, 'r:gz') as package:
                    # macOS portable packages put the executable in the application bundle.
                    for name in (NAME, NAME + '.asc', INFO):
                        entries = [i for i in package.getmembers() if i.name.endswith('/' + name)]
                        if len(entries) != 1 or not entries[0].isfile() or entries[0].size > 100 * 1024 * 1024:
                            raise ValueError('Missing or invalid bundled Market Sidecar')
                        if name == NAME and not entries[0].mode & 0o111:
                            raise ValueError('Bundled Sidecar is not executable')
                        (target / name).write_bytes(package.extractfile(entries[0]).read())
            info = json.loads((target / INFO).read_text())
            with (target / NAME).open('rb') as stream:
                digest = hashlib.file_digest(stream, 'sha256').hexdigest()
            if (digest != lock['artifacts'][rid]['sha256'] or info['sha256'] != digest
                    or info['runtimeIdentifier'] != rid or info['sourceCommit'] != lock['commit']
                    or info['signingFingerprint'] != fingerprint):
                raise ValueError('Bundled Market Sidecar provenance mismatch')
            verify_pair(target, public, fingerprint)
            if export:
                package_id = NAME.removesuffix('.nsc')
                shutil.copyfile(target / NAME, directory / f'{package_id}-{rid}.nsc')
                catalog = dict(schema=1, rid=rid, release=version, packages=[
                    dict(id=package_id, version=lock['runtimeVersion'], sha256=digest,
                         size=(target / NAME).stat().st_size, minimumHostVersion=lock.get('minimumHostVersion', version))])
                (directory / f'Nexa-Sidecars-{rid}.json').write_text(
                    json.dumps(catalog, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['stage', 'verify', 'export'])
    parser.add_argument('directory', type=Path)
    parser.add_argument('--source', type=Path)
    parser.add_argument('--rid', choices=RIDS)
    parser.add_argument('--version')
    args = parser.parse_args()
    lock = json.loads((ROOT / 'eng/release/market-sidecar.lock.json').read_text())
    public = (ROOT / 'GPG-PUBLIC-KEY.asc').read_bytes()
    if args.mode == 'stage':
        stage(args.source, args.directory, args.rid, lock, public,
              os.environ.get('GPG_PRIVATE_KEY', ''), os.environ.get('GPG_PASSPHRASE', ''))
    else:
        verify_archives(args.directory, args.version, lock, public, export=args.mode == "export")
