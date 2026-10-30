import json
import tempfile
import unittest
from pathlib import Path

from package import validate_payload
from publish_launcher import module_names, verify_host_engine_isolation


class NativeModulePayloadTests(unittest.TestCase):
    def test_each_platform_requires_all_nonempty_matching_native_modules(self):
        for platform in ("win", "linux", "osx"):
            for arch in ("x64", "arm64"):
                with self.subTest(platform=platform, arch=arch), tempfile.TemporaryDirectory() as temp:
                    root = Path(temp)
                    suffix = ".exe" if platform == "win" else ""
                    for name in ("Nexa.Desktop", "Nexa.Jvm.Host", "Nexa.Update.Helper"):
                        (root / (name + suffix)).write_bytes(b"executable")
                    for name in module_names(platform + "-" + arch):
                        (root / name).write_bytes(b"engine")
                    for name in module_names(platform + "-" + arch):
                        module = root / name
                        with self.subTest(module=name):
                            module.unlink()
                            self.assertEqual(module.name, module.name.lower())
                            with self.assertRaisesRegex(ValueError, module.stem):
                                validate_payload(root, platform)
                            module.touch()
                            with self.assertRaisesRegex(ValueError, module.stem):
                                validate_payload(root, platform)
                            module.write_bytes(b"native library")
                            validate_payload(root, platform)

    def test_publish_rejects_reembedded_engine_dependencies(self):
        with tempfile.TemporaryDirectory() as temp:
            assets = Path(temp) / "project.assets.json"
            assets.write_text(json.dumps({"libraries": {"Nexa.Services.Network/2.0.0": {}}}))
            verify_host_engine_isolation(assets)
            for name in ("Jint/4.17.0", "BouncyCastle.Cryptography/2.6.2", "Nexa.Native.Pac/1.0.0", "Nexa.Native.Crypto/1.0.0", "Nexa.Native.Network/1.0.0", "Nexa.Native.Updates/1.0.0", "ZstdSharp.Port/0.8.6"):
                assets.write_text(json.dumps({"libraries": {name: {}}}))
                with self.assertRaisesRegex(RuntimeError, "leaked back"):
                    verify_host_engine_isolation(assets)

    def test_host_change_reuses_stable_engines_without_new_data(self):
        from measure_module_delta import measure
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            before, after = root / "before", root / "after"
            before.mkdir(); after.mkdir()
            for directory in (before, after):
                for name in module_names("linux-x64"):
                    (directory / name).write_bytes((name + " stable machine code").encode() * 100)
                (directory / "Nexa.Desktop").write_bytes(b"bootstrap")
                (directory / "discard.dbg").write_bytes(b"debug symbols")
            (after / "nexa-host.so").write_bytes(b"changed host code")
            result = measure(before, after, root / "report", "linux-x64", "2.0.0.alpha.18", "2.0.0.alpha.19")
            engines = [m for m in result["modules"] if m["name"] != "nexa-host.so"]
            self.assertTrue(result["roundTripVerified"])
            self.assertEqual(4, len(engines))
            self.assertTrue(all(m["identical"] and m["newDataBytes"] == 0 for m in engines))

    def test_first_network_module_is_carried_by_a_verified_delta(self):
        from measure_module_delta import measure
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            before, after = root / "before", root / "after"
            before.mkdir(); after.mkdir()
            for name in module_names("linux-x64"):
                data = (name + " code").encode() * 100
                (after / name).write_bytes(data)
                if name != "nexa-network.so":
                    (before / name).write_bytes(data)
            result = measure(before, after, root / "report", "linux-x64", "2.0.0.alpha.18", "2.0.0.alpha.19")
            network = next(item for item in result["modules"] if item["name"] == "nexa-network.so")
            self.assertFalse(network["identical"])
            self.assertEqual(network["bytes"], network["newDataBytes"])
            self.assertTrue(result["roundTripVerified"])


if __name__ == "__main__":
    unittest.main()
