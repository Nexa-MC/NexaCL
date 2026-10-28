import hashlib
import json
from pathlib import Path
import shutil
import tarfile
import tempfile
import unittest
import zipfile
import test_sign
from market_sidecar import INFO, NAME, RIDS, stage, verify_archives


@unittest.skipUnless(shutil.which('gpg'), 'GnuPG required')
class MarketSidecarTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        test_sign.SigningTests.setUpClass()
        cls.keys = test_sign.SigningTests

    def test_pinned_input_signed_bundle_and_tampering(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            lock = dict(runtimeVersion='1.0.0', commit='a' * 40, hostReference='b' * 40, artifacts={})
            for rid in RIDS:
                source = root / f'Nexa.Cloud.Market.Sidecar-{rid}.nsc'
                source.write_bytes(('native fixture ' + rid).encode())
                lock['artifacts'][rid] = dict(sha256=hashlib.sha256(source.read_bytes()).hexdigest(), size=source.stat().st_size)
                payload = root / rid
                stage(source, payload, rid, lock, self.keys.public, self.keys.private, '', self.keys.fingerprint)
                self.assertEqual(source.read_bytes(), (payload / NAME).read_bytes())
                self.assertEqual(rid, json.loads((payload / INFO).read_text())['runtimeIdentifier'])
                stem = root / f'Nexa-2.0.0.alpha.16-{rid}'
                if rid.startswith('win-'):
                    with zipfile.ZipFile(str(stem) + '.portable.zip', 'w') as archive:
                        for path in payload.iterdir(): archive.write(path, path.name)
                else:
                    with tarfile.open(str(stem) + '.portable.tar.gz', 'w:gz') as archive:
                        archive.add(payload, arcname='NexaCL')
            verify_archives(root, '2.0.0.alpha.16', lock, self.keys.public, self.keys.fingerprint)
            lock['artifacts']['linux-x64']['sha256'] = 'f' * 64
            with self.assertRaisesRegex(ValueError, 'provenance'):
                verify_archives(root, '2.0.0.alpha.16', lock, self.keys.public, self.keys.fingerprint)
            with self.assertRaisesRegex(ValueError, 'reviewed build'):
                stage(root / 'Nexa.Cloud.Market.Sidecar-linux-x64.nsc', root / 'rejected', 'linux-x64', lock,
                      self.keys.public, self.keys.private, '', self.keys.fingerprint)
            self.assertFalse((root / 'rejected' / NAME).exists())

    def test_missing_signing_key_does_not_leave_an_unsigned_package(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); source = root / 'Nexa.Cloud.Market.Sidecar-linux-x64.nsc'; source.write_bytes(b'fixture')
            lock = dict(artifacts={'linux-x64': dict(sha256=hashlib.sha256(b'fixture').hexdigest(), size=7)})
            with self.assertRaises(ValueError):
                stage(source, root / 'payload', 'linux-x64', lock, self.keys.public, '', '', self.keys.fingerprint)
            self.assertFalse(list((root / 'payload').iterdir()))
