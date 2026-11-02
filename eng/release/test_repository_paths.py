"""Exercise NativeAOT UI publication's repository inputs without invoking a compiler."""
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
