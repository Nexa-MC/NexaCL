import copy
import json
from pathlib import Path
import tempfile
import unittest
from independent_sidecars import ROOT, reviewed_locks


class ReviewedSidecarInputsTests(unittest.TestCase):
    def test_plugin_requires_complete_reviewed_provenance(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            release = root / 'eng/release'
            release.mkdir(parents=True)
            market = json.loads((ROOT / 'eng/release/market-sidecar.lock.json').read_text())
            (release / 'market-sidecar.lock.json').write_text(json.dumps(market))
            self.assertEqual(['Nexa.Cloud.Market.Sidecar'], [identity for identity, _ in reviewed_locks(root)])
            plugin = copy.deepcopy(market)
            plugin['repository'] = plugin['inputRepository'] = 'Nexa-MC/PluginSidecar'
            plugin['inputRelease'] = 'plugin-inputs-' + plugin['commit'][:12]
            path = release / 'plugin-sidecar.lock.json'
            path.write_text(json.dumps(plugin))
            self.assertEqual(['Nexa.Cloud.Market.Sidecar', 'Nexa.Plugin.Sidecar'], [identity for identity, _ in reviewed_locks(root)])
            invalid = []
            for field, value in [('commit', 'main'), ('hostReference', 'latest'), ('buildRun', True),
                                 ('inputRelease', '--latest'), ('inputRepository', '../outside'),
                                 ('minimumHostVersion', 'latest')]:
                changed = copy.deepcopy(plugin)
                changed[field] = value
                invalid.append(changed)
            changed = copy.deepcopy(plugin)
            del changed['artifacts']['win-arm64']
            invalid.append(changed)
            changed = copy.deepcopy(plugin)
            changed['artifacts']['linux-x64']['sha256'] = 'pending'
            invalid.append(changed)
            for changed in invalid:
                path.write_text(json.dumps(changed))
                with self.subTest(lock=changed), self.assertRaises((ValueError, KeyError)):
                    reviewed_locks(root)
