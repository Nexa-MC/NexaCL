"""Integrity boundaries for the SDK feed (no network or .NET required)."""
import copy
import io
import json
import unittest
import zipfile

import prepare


class FeedIntegrityTests(unittest.TestCase):
    def setUp(self):
        self.lock = {'commit': 'fixture-commit', 'version': 'fixture-version'}
        self.files = {name: b'package bytes' for name in prepare.package_names(self.lock)}
        manifest = {'commit': self.lock['commit'], 'packages': [
            {'file': name, 'sha256': prepare.digest(data)} for name, data in self.files.items()]}
        self.files['feed-manifest.json'] = json.dumps(manifest).encode()

    def bundle(self, extra=()):
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, 'w') as archive:
            for name, data in [*self.files.items(), *extra]:
                archive.writestr(name, data)
        payload = stream.getvalue()
        self.lock['distribution'] = {'size': len(payload), 'sha256': prepare.digest(payload)}
        return payload

    def test_valid_complete_feed(self):
        self.assertEqual(self.files, prepare.verify_bundle(self.bundle(), self.lock))

    def test_tampered_or_truncated_archive(self):
        payload = self.bundle()
        for bad in (payload[:-1], payload + b'x', b'x' + payload[1:]):
            with self.assertRaises(ValueError):
                prepare.verify_bundle(bad, self.lock)

    def test_path_traversal_or_duplicate_entry(self):
        for name in ('../outside.nupkg', 'feed-manifest.json'):
            with self.subTest(name=name), self.assertRaises(ValueError):
                prepare.verify_bundle(self.bundle([(name, b'bad')]), self.lock)

    def test_wrong_package_hash(self):
        self.files[next(iter(self.files))] = b'changed package'
        with self.assertRaises(ValueError):
            prepare.verify_bundle(self.bundle(), self.lock)

    def test_missing_package_and_empty_manifest(self):
        del self.files[next(iter(self.files))]
        with self.assertRaises(ValueError):
            prepare.verify_bundle(self.bundle(), self.lock)
        with self.assertRaises(ValueError):
            prepare.validate_manifest({'commit': self.lock['commit'], 'packages': []}, self.lock)

    def test_wrong_source_identity(self):
        payload = self.bundle()
        lock = copy.deepcopy(self.lock)
        lock['commit'] = 'another-commit'
        with self.assertRaises(ValueError):
            prepare.verify_bundle(payload, lock)


if __name__ == '__main__':
    unittest.main()
