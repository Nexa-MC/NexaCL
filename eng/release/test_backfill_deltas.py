import copy
import hashlib
import io
import json
import random
import shutil
import tarfile
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest.mock import patch

import backfill_deltas as backfill
import publish_updates
import sign
from delta import INDEX_NAME, RIDS, package_name
from verify import expected_names


@unittest.skipUnless(shutil.which('gpg'), 'GnuPG is required for publisher verification')
class DifferentialBackfillTests(unittest.TestCase):
    version, previous = '2.0.0.alpha.8', '2.0.0.alpha.7'
    tag = 'v' + version

    @classmethod
    def portables(cls, root, version):
        shared = random.Random(42).randbytes(160000)
        for rid in RIDS:
            prefix = 'Nexa.app/Contents/MacOS/' if rid.startswith('osx-') else ''
            suffix = '.exe' if rid.startswith('win-') else ''
            files = {prefix + 'Nexa.Desktop' + suffix: shared,
                     prefix + 'Nexa.Jvm.Host' + suffix: b'JVM host fixture',
                     prefix + 'version.txt': version.encode()}
            output = root / package_name(version, rid)
            if rid.startswith('win-'):
                with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as archive:
                    for name, raw in files.items():
                        archive.writestr(name, raw)
            else:
                with tarfile.open(output, 'w:gz') as archive:
                    for name, raw in files.items():
                        entry = tarfile.TarInfo(('Nexa/' if rid.startswith('linux-') else '') + name)
                        entry.mode = 0o755
                        entry.size = len(raw)
                        archive.addfile(entry, io.BytesIO(raw))

    @classmethod
    def setUpClass(cls):
        cls.temporary = tempfile.TemporaryDirectory()
        root = Path(cls.temporary.name)
        cls.packages, cls.previous_packages = root / 'current', root / 'previous'
        cls.packages.mkdir(); cls.previous_packages.mkdir()
        for name in expected_names(cls.version):
            (cls.packages / name).write_bytes(('installer fixture ' + name).encode())
        cls.portables(cls.packages, cls.version)
        cls.portables(cls.previous_packages, cls.previous)
        with sign.keyring() as home:
            sign.gpg(home, '--pinentry-mode', 'loopback', '--passphrase', '', '--quick-generate-key',
                     'Delta test <delta@example.invalid>', 'ed25519', 'sign', '0')
            listing = sign.gpg(home, '--with-colons', '--fingerprint', '--list-keys')
            cls.fingerprint = next(line.split(b':')[9].decode() for line in listing.splitlines() if line.startswith(b'fpr:'))
            cls.public = sign.gpg(home, '--armor', '--export', cls.fingerprint)
            cls.private = sign.gpg(home, '--armor', '--export-secret-keys', cls.fingerprint).decode()
        sign.sign_distribution(cls.packages, cls.version, cls.public, cls.private, '', cls.fingerprint)

    @classmethod
    def tearDownClass(cls):
        cls.temporary.cleanup()

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.downloads = Path(self.temporary.name) / 'download'
        shutil.copytree(self.packages, self.downloads)
        self.remote = {p.name: self.asset(p) for p in self.downloads.iterdir()}
        self.original = copy.deepcopy(self.remote)
        self.uploads = []
        self.interrupt = False
        self.index = None

    def asset(self, path):
        return dict(name=path.name, size=path.stat().st_size,
                    digest='sha256:' + hashlib.sha256(path.read_bytes()).hexdigest(),
                    browser_download_url=f'https://github.com/{backfill.SOURCE}/releases/download/{self.tag}/{path.name}')

    def api(self, path):
        self.assertEqual(f'repos/{backfill.SOURCE}/releases/tags/{self.tag}', path)
        return dict(tag_name=self.tag, draft=False, prerelease=True, assets=list(self.remote.values()))

    def download(self, *args):
        self.assertEqual(('release', 'download'), args[:2])
        output = Path(args[args.index('--dir') + 1])
        for path in self.downloads.iterdir():
            shutil.copy2(path, output / path.name)

    def upload(self, *args):
        self.assertEqual(('release', 'upload'), args[:2])
        self.assertNotIn('--clobber', args)
        for value in args[args.index('--repo') + 2:]:
            path = Path(value)
            self.assertNotIn(path.name, self.remote, 'Published files must never be uploaded again')
            self.remote[path.name] = self.asset(path)
            shutil.copy2(path, self.downloads / path.name)
            self.uploads.append(path.name)
            if path.name == INDEX_NAME:
                self.index = json.loads(path.read_text())
        if self.interrupt:
            self.interrupt = False
            raise RuntimeError('simulated interrupted upload')

    def fetch(self, directory, version):
        self.assertEqual(self.version, version)
        shutil.copytree(self.previous_packages, directory / self.previous)

    def run_backfill(self):
        with patch.object(backfill, 'api', side_effect=self.api), \
             patch.object(backfill, 'gh', side_effect=self.download), \
             patch.object(backfill, 'fetch', side_effect=self.fetch), \
             patch.object(backfill, 'verify_signatures', side_effect=lambda d, v, p: sign.verify_signatures(d, v, p, self.fingerprint)), \
             patch.object(backfill, 'sign_delta_extension', side_effect=lambda d, v, p, k, phrase: sign.sign_delta_extension(d, v, p, k, phrase, self.fingerprint)), \
             patch.object(publish_updates, 'api', side_effect=self.api), \
             patch.object(publish_updates, 'gh', side_effect=self.upload):
            return backfill.backfill(self.tag, self.public, self.private, '')

    def run_mirror(self, destination):
        release = dict(self.api(f'repos/{backfill.SOURCE}/releases/tags/{self.tag}'),
                       html_url=f'https://github.com/{backfill.SOURCE}/releases/tag/{self.tag}',
                       body='Verified Alpha 8 release notes')
        operations = []
        def api(path):
            if path == f'repos/{backfill.SOURCE}/releases/tags/{self.tag}':
                return release
            self.assertEqual(f'repos/{publish_updates.DESTINATION}/releases/tags/{self.tag}', path)
            return dict(tag_name=self.tag, draft=False, prerelease=True, assets=list(destination.values()))
        def command(*args):
            operations.append(args)
            if args[1] == 'download':
                output = Path(args[args.index('--dir') + 1])
                for path in self.downloads.iterdir():
                    shutil.copy2(path, output / path.name)
            else:
                self.assertEqual('upload', args[1])
                self.assertNotIn('--clobber', args)
                for value in args[args.index('--repo') + 2:]:
                    path = Path(value)
                    self.assertNotIn(path.name, destination)
                    asset = self.asset(path)
                    asset['browser_download_url'] = asset['browser_download_url'].replace(backfill.SOURCE, publish_updates.DESTINATION)
                    destination[path.name] = asset
        with patch.object(publish_updates, 'api', side_effect=api), \
             patch.object(publish_updates, 'gh', side_effect=command), \
             patch.object(publish_updates, 'get_release', return_value={'draft':False, 'body':'Existing public notes'}), \
             patch.object(publish_updates, 'verify_signatures', side_effect=lambda d,v,p: sign.verify_signatures(d,v,p,self.fingerprint)):
            changed = publish_updates.publish(self.tag, self.public)
        return changed, operations

    def destination(self):
        values = copy.deepcopy(self.original)
        for asset in values.values():
            asset['browser_download_url'] = asset['browser_download_url'].replace(backfill.SOURCE, publish_updates.DESTINATION)
        return values

    def test_mirror_appends_only_verified_deltas_and_skips_an_identical_public_distribution(self):
        self.run_backfill()
        destination = self.destination()
        original = copy.deepcopy(destination)
        changed, operations = self.run_mirror(destination)
        self.assertTrue(changed)
        self.assertTrue(all(destination[name] == value for name, value in original.items()))
        self.assertEqual(INDEX_NAME, Path(operations[-1][-1]).name)
        self.assertEqual(54, len(destination))
        self.assertEqual((False, []), self.run_mirror(destination))

    def test_mirror_refuses_conflicting_existing_bytes(self):
        self.run_backfill()
        destination = self.destination()
        destination['SHA256SUMS']['digest'] = 'sha256:' + '0' * 64
        before = copy.deepcopy(destination)
        with self.assertRaisesRegex(ValueError, 'refusing overwrite'):
            self.run_mirror(destination)
        self.assertEqual(before, destination)

    def test_six_signed_deltas_preserve_all_published_bytes_and_commit_index_last(self):
        self.assertTrue(self.run_backfill())
        self.assertEqual([INDEX_NAME + '.asc', INDEX_NAME], self.uploads[-2:])
        self.assertEqual(6, len(self.index['patches']))
        self.assertEqual(set(RIDS), {p['rid'] for p in self.index['patches']})
        self.assertTrue(all(self.remote[name] == value for name, value in self.original.items()))
        self.assertFalse(self.run_backfill())
        self.assertEqual(14, len(self.uploads))

    def test_tampered_full_payload_never_reaches_publication(self):
        path = self.downloads / package_name(self.version, 'win-x64')
        raw = path.read_bytes(); path.write_bytes(bytes([raw[0] ^ 1]) + raw[1:])
        with self.assertRaises(ValueError):
            self.run_backfill()
        self.assertEqual([], self.uploads)

    def test_conflicting_published_asset_cannot_be_overwritten(self):
        self.remote['SHA256SUMS']['digest'] = 'sha256:' + '0' * 64
        with self.assertRaisesRegex(ValueError, 'refusing overwrite'):
            self.run_backfill()
        self.assertEqual([], self.uploads)

    def test_interrupted_extension_resumes_without_replacing_previous_signatures(self):
        self.interrupt = True
        with self.assertRaisesRegex(RuntimeError, 'interrupted'):
            self.run_backfill()
        self.assertNotIn(INDEX_NAME, self.remote)
        previous = copy.deepcopy(self.remote)
        self.assertTrue(self.run_backfill())
        self.assertTrue(all(self.remote[name] == value for name, value in previous.items()))
        self.assertEqual([INDEX_NAME + '.asc', INDEX_NAME], self.uploads[-2:])
        self.assertEqual(14, len(self.uploads))


if __name__ == '__main__':
    unittest.main()
