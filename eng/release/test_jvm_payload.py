import re
import subprocess
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest.mock import patch

from package import archive, copy_runtime_payload
from smoke_install import check_windows_portable


class JvmPortablePayloadTests(unittest.TestCase):
    WINDOWS_FILES = ("Nexa.Desktop.exe", "Nexa.Update.Helper.exe", "Nexa.Jvm.Host.exe", "nexa-host.dll", "nexa-pac.dll", "nexa-crypto.dll")

    def test_portable_staging_strips_symbols_and_smoke_executes_host(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            published = root / "published"
            published.mkdir()
            for name in self.WINDOWS_FILES:
                (published / name).write_bytes(b"native payload")
            (published / "Nexa.Jvm.Host.pdb").write_bytes(b"symbols")
            runtime = root / "runtime"
            copy_runtime_payload(published, runtime)
            archive(runtime, root / "fixture.portable.zip", "NexaCL")
            with patch("smoke_install.subprocess.run", return_value=subprocess.CompletedProcess([], 2)) as execute:
                check_windows_portable(root, "fixture")
            self.assertEqual(1, execute.call_count)
            self.assertEqual("Nexa.Jvm.Host.exe", Path(execute.call_args.args[0][0]).name)
            self.assertEqual(15, execute.call_args.kwargs["timeout"])
            self.assertFalse((runtime / "Nexa.Jvm.Host.pdb").exists())

    def test_portable_missing_or_empty_runtime_fails_before_launch(self):
        for missing in self.WINDOWS_FILES:
            for data in (None, b""):
                with self.subTest(name=missing, data=data), tempfile.TemporaryDirectory() as temporary:
                    root = Path(temporary)
                    with zipfile.ZipFile(root / "fixture.portable.zip", "w") as bundle:
                        for name in self.WINDOWS_FILES:
                            if name != missing or data is not None:
                                bundle.writestr(name, data if name == missing else b"native payload")
                    with patch("smoke_install.subprocess.run") as execute:
                        with self.assertRaisesRegex((RuntimeError, ValueError), re.escape(missing)):
                            check_windows_portable(root, "fixture")
                    execute.assert_not_called()

    def test_portable_broken_host_fails_smoke(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with zipfile.ZipFile(root / "fixture.portable.zip", "w") as bundle:
                for name in self.WINDOWS_FILES:
                    bundle.writestr(name, b"native payload")
            with patch("smoke_install.subprocess.run", return_value=subprocess.CompletedProcess([], 1)):
                with self.assertRaisesRegex(RuntimeError, "cannot execute: 1"):
                    check_windows_portable(root, "fixture")

    def test_portable_rejects_nested_development_artifacts_before_launch(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with zipfile.ZipFile(root / "fixture.portable.zip", "w") as bundle:
                for name in self.WINDOWS_FILES:
                    bundle.writestr(name, b"native payload")
                bundle.writestr("runtime/symbols.PDB", b"symbols")
            with patch("smoke_install.subprocess.run") as execute:
                with self.assertRaisesRegex(RuntimeError, "Development artifact"):
                    check_windows_portable(root, "fixture")
            execute.assert_not_called()

    def test_portable_repeated_runtime_fails_before_launch(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with zipfile.ZipFile(root / "fixture.portable.zip", "w") as bundle:
                for name in self.WINDOWS_FILES:
                    bundle.writestr(name, b"native payload")
                with self.assertWarns(UserWarning):
                    bundle.writestr("Nexa.Jvm.Host.exe", b"duplicate")
            with patch("smoke_install.subprocess.run") as execute:
                with self.assertRaisesRegex(RuntimeError, "repeats Nexa.Jvm.Host.exe"):
                    check_windows_portable(root, "fixture")
            execute.assert_not_called()


if __name__ == "__main__":
    unittest.main()
