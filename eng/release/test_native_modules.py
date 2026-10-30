import tempfile
import unittest
from pathlib import Path

from package import validate_payload
from publish_launcher import module_name


class NativeModulePayloadTests(unittest.TestCase):
    def test_each_platform_requires_nonempty_matching_host_module(self):
        for platform in ("win", "linux", "osx"):
            for arch in ("x64", "arm64"):
                with self.subTest(platform=platform, arch=arch), tempfile.TemporaryDirectory() as temp:
                    root = Path(temp)
                    suffix = ".exe" if platform == "win" else ""
                    for name in ("Nexa.Desktop", "Nexa.Jvm.Host", "Nexa.Update.Helper"):
                        (root / (name + suffix)).write_bytes(b"executable")
                    module = root / module_name(platform + "-" + arch)
                    self.assertEqual(module.name, module.name.lower())
                    with self.assertRaisesRegex(ValueError, "nexa-host"):
                        validate_payload(root, platform)
                    module.touch()
                    with self.assertRaisesRegex(ValueError, "nexa-host"):
                        validate_payload(root, platform)
                    module.write_bytes(b"native library")
                    validate_payload(root, platform)


if __name__ == "__main__":
    unittest.main()
