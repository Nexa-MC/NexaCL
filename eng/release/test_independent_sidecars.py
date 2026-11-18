import copy
import json
from pathlib import Path
import tempfile
import unittest
from independent_sidecars import ROOT, reviewed_locks
from market_ci import RIDS, built_lock, image_identity, provenance_path, source_lock


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


class MarketSourceInputsTests(unittest.TestCase):
    def test_current_run_provenance_binds_all_six_portable_inputs(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = source_lock()
            run = {'buildRun': 321, 'launcherCommit': 'c' * 40}
            expected = {}
            for rid in RIDS:
                image = root / f'{rid}.nsc'
                image.write_bytes(rid.encode() * 20)
                expected[rid] = image_identity(image)
                proof = {**source, **run, 'rid': rid, 'artifact': expected[rid]}
                provenance_path(root, rid).write_text(json.dumps(proof))
            self.assertEqual(expected, built_lock(root, RIDS, source, run)['artifacts'])
            self.assertEqual({'win-x64': expected['win-x64']},
                             built_lock(root, ('win-x64',), source, run)['artifacts'])
            with self.assertRaisesRegex(ValueError, 'does not match'):
                built_lock(root, RIDS, source, {**run, 'buildRun': 322})
            with self.assertRaisesRegex(ValueError, 'does not match'):
                built_lock(root, RIDS, {**source, 'runtimeVersion': '1.0.2'}, run)
            provenance_path(root, 'osx-arm64').unlink()
            with self.assertRaisesRegex(ValueError, 'Missing'):
                built_lock(root, RIDS, source, run)

    def test_source_build_does_not_read_the_historical_market_binary_lock(self):
        with tempfile.TemporaryDirectory() as temporary:
            self.assertEqual([], reviewed_locks(Path(temporary), market_from_source=True))
