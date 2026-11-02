"""Exercise NativeAOT UI publication's repository inputs without invoking a compiler."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import verify_native_ui_contracts


class RepositoryPublicationPaths(unittest.TestCase):
    def test_ui_probe_publication_resolves_real_project_locations(self):
        read_text = Path.read_text
        publications = []

        def read(path, *args, **kwargs):
            if path.name == 'project.assets.json':
                return json.dumps({'libraries': {}})
            return read_text(path, *args, **kwargs)

        def invoke(command, **kwargs):
            self.assertEqual(command[:2], ['dotnet', 'publish'])
            project = Path(command[2])
            self.assertTrue(project.is_file(), f'NativeAOT publication names a missing project: {project}')
            publications.append(project.stem)

        with tempfile.TemporaryDirectory() as directory, patch.object(Path, 'read_text', read), \
                patch.object(verify_native_ui_contracts.subprocess, 'run', invoke):
            verify_native_ui_contracts.test('linux-x64', Path(directory), run_probe=False)
        self.assertCountEqual(publications, ['Nexa.Native.Ui.Peer', 'Nexa.Native.Ui.Tests'])

    def test_signed_sidecar_fixtures_retain_catalogued_bytes_on_all_rids(self):
        root = verify_native_ui_contracts.project_directory('Nexa.Native.Updates.Services.Tests') / 'Fixtures'
        for rid in ('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64'):
            with self.subTest(rid=rid):
                directory = root / rid
                catalog = json.loads((directory / f'Nexa-Sidecars-{rid}.json').read_text())
                for package in catalog['packages']:
                    payload = (directory / f"{package['id']}-{rid}.nsc").read_bytes()
                    self.assertEqual(len(payload), package['size'])
                    self.assertEqual(hashlib.sha256(payload).hexdigest(), package['sha256'])
