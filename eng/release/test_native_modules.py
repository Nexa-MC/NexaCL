import json
import tempfile
import unittest
from unittest.mock import patch
from pathlib import Path

from package import copy_runtime_payload, validate_payload
from publish_launcher import module_names, verify_host_module_isolation, verify_ui_engine_isolation


class NativeModulePayloadTests(unittest.TestCase):
    def test_isolated_contract_caller_accepts_the_eight_engine_payload(self):
        from publish_launcher import ENGINES
        from verify_native_engine_contracts import test
        with tempfile.TemporaryDirectory() as temporary:
            payload, output = Path(temporary) / "engines", Path(temporary) / "caller"
            payload.mkdir(); output.mkdir()
            for engine in ENGINES:
                (payload / ("nexa-" + engine.lower() + ".so")).write_bytes(engine.encode())
            with patch("verify_native_engine_contracts.subprocess.run") as run:
                test("linux-x64", payload, output)
            self.assertEqual(2, run.call_count)
            self.assertEqual({file.name: file.read_bytes() for file in payload.iterdir()},
                             {file.name: file.read_bytes() for file in output.iterdir()})

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

    def test_native_payload_rejects_managed_runtime_instead_of_silently_removing_it(self):
        for name in ("coreclr.dll", "clrjit.dll", "hostfxr.dll", "hostpolicy.dll",
                     "libcoreclr.so", "libclrjit.so", "libhostfxr.so", "libhostpolicy.so",
                     "libcoreclr.dylib", "libclrjit.dylib", "libhostfxr.dylib", "libhostpolicy.dylib",
                     "System.Private.CoreLib.dll"):
            with self.subTest(name=name), tempfile.TemporaryDirectory() as temp:
                source = Path(temp) / "source"
                source.mkdir()
                nested = source / "stale-runtime"
                nested.mkdir()
                (nested / name.upper()).write_bytes(b"managed runtime")
                with self.assertRaisesRegex(ValueError, "CoreCLR"):
                    validate_payload(source, "win")
                with self.assertRaisesRegex(ValueError, "CoreCLR"):
                    copy_runtime_payload(source, Path(temp) / "staged")

    def test_publish_rejects_reembedded_engine_dependencies(self):
        with tempfile.TemporaryDirectory() as temp:
            assets = Path(temp) / "project.assets.json"
            assets.write_text(json.dumps({"libraries": {"Nexa.Services.Network.Contracts/2.0.0": {}}}))
            verify_ui_engine_isolation(assets)
            for name in ("Nexa.Services.Updates/1.0.0", "Nexa.Services.Rollouts/1.0.0", "Nexa.Services.Network/1.0.0", "Nexa.Services.Accounts/1.0.0", "Nexa.Native.Accounts/1.0.0", "Jint/4.17.0", "BouncyCastle.Cryptography/2.6.2", "Nexa.Native.Pac/1.0.0", "Nexa.Native.Crypto/1.0.0", "Nexa.Native.Network/1.0.0", "Nexa.Native.Updates/1.0.0", "Nexa.Native.Media/1.0.0", "Nexa.Native.Minecraft/1.0.0", "Nexa.Native.Markup/1.0.0", "Nexa.Native.Artwork/1.0.0", "ZstdSharp.Port/0.8.6"):
                assets.write_text(json.dumps({"libraries": {name: {}}}))
                with self.assertRaisesRegex(RuntimeError, "leaked back"):
                    verify_ui_engine_isolation(assets)

    def test_host_cannot_embed_ui_contracts_or_service_implementations(self):
        with tempfile.TemporaryDirectory() as temp:
            assets = Path(temp) / "project.assets.json"
            assets.write_text(json.dumps({"libraries": {"Nexa.Xsr.Patch.Compiler/1.0": {}}}))
            verify_host_module_isolation(assets)
            for name in ("Nexa.Desktop", "Nexa.Services", "Nexa.Services.Accounts", "Nexa.Xsr.Renderer",
                         "Nexa.Xsr.Renderer.Backend.Avalonia", "Avalonia", "SkiaSharp"):
                with self.subTest(name=name):
                    assets.write_text(json.dumps({"libraries": {name + "/1.0": {}}}))
                    with self.assertRaisesRegex(RuntimeError, "leaked back"):
                        verify_host_module_isolation(assets)

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
            self.assertEqual(len(module_names("linux-x64")) - 1, len(engines))
            self.assertTrue(all(m["identical"] and m["newDataBytes"] == 0 for m in engines))

    def test_first_content_modules_are_carried_by_a_verified_delta(self):
        from measure_module_delta import measure
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            before, after = root / "before", root / "after"
            before.mkdir(); after.mkdir()
            added = {"nexa-media.so", "nexa-minecraft.so", "nexa-markup.so", "nexa-artwork.so"}
            for name in module_names("linux-x64"):
                data = (name + " code").encode() * 100
                (after / name).write_bytes(data)
                if name not in added:
                    (before / name).write_bytes(data)
            result = measure(before, after, root / "report", "linux-x64", "2.0.0.alpha.18", "2.0.0.alpha.19")
            for name in added:
                module = next(item for item in result["modules"] if item["name"] == name)
                self.assertFalse(module["identical"])
                self.assertEqual(module["bytes"], module["newDataBytes"])
            self.assertTrue(result["roundTripVerified"])


if __name__ == "__main__":
    unittest.main()
