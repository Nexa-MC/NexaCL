import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import package


class DmgTests(unittest.TestCase):
    def test_busy_retries_with_fresh_image_and_publishes_after_verification(self):
        with tempfile.TemporaryDirectory() as root:
            target = Path(root) / "result.dmg"
            target.write_bytes(b"previous")
            images = []

            def hdiutil(args, **kwargs):
                if args[1] == "verify":
                    self.assertEqual(b"complete", Path(args[2]).read_bytes())
                    self.assertEqual(b"previous", target.read_bytes())
                    return subprocess.CompletedProcess(args, 0, stdout="")
                self.assertEqual("128m", args[args.index("-size") + 1])
                image = Path(args[-1])
                images.append(image)
                image.write_bytes(b"partial" if len(images) == 1 else b"complete")
                if len(images) == 1:
                    raise subprocess.CalledProcessError(1, args, stderr="hdiutil: create failed - Resource busy")
                return subprocess.CompletedProcess(args, 0, stdout="")

            with patch.object(package.subprocess, "run", side_effect=hdiutil), \
                    patch.object(package.time, "sleep") as sleep:
                package.create_dmg(root, target)
            self.assertEqual(b"complete", target.read_bytes())
            self.assertNotEqual(images[0], images[1])
            self.assertTrue(all(not path.exists() for path in images))
            sleep.assert_called_once_with(2)

    def test_nonbusy_and_exhausted_errors_preserve_previous_artifact(self):
        for message, attempts in [("Permission denied", 1), ("No space left on device", 1), ("Resource busy", 3)]:
            with self.subTest(message=message), tempfile.TemporaryDirectory() as root:
                target = Path(root) / "result.dmg"
                target.write_bytes(b"previous")
                error = subprocess.CalledProcessError(1, ["hdiutil", "create"], stderr=message)
                with patch.object(package.subprocess, "run", side_effect=error) as create, \
                        patch.object(package.time, "sleep"):
                    with self.assertRaises(subprocess.CalledProcessError):
                        package.create_dmg(root, target)
                self.assertEqual(attempts, create.call_count)
                self.assertEqual(b"previous", target.read_bytes())
                self.assertEqual([target], list(Path(root).iterdir()))

    def test_failed_verification_is_not_retried_or_published(self):
        with tempfile.TemporaryDirectory() as root:
            target = Path(root) / "result.dmg"
            target.write_bytes(b"previous")
            def hdiutil(args, **kwargs):
                if args[1] == "verify":
                    raise subprocess.CalledProcessError(1, args, stderr="Checksum mismatch")
                Path(args[-1]).write_bytes(b"invalid")
                return subprocess.CompletedProcess(args, 0, stdout="")
            with patch.object(package.subprocess, "run", side_effect=hdiutil) as command, \
                    patch.object(package.time, "sleep") as sleep:
                with self.assertRaises(subprocess.CalledProcessError):
                    package.create_dmg(root, target)
            self.assertEqual(["create", "verify"], [call.args[0][1] for call in command.call_args_list])
            sleep.assert_not_called()
            self.assertEqual(b"previous", target.read_bytes())

    def test_temporarily_unavailable_verification_retries_before_replacing_image(self):
        with tempfile.TemporaryDirectory() as root:
            target = Path(root) / "result.dmg"
            target.write_bytes(b"previous")
            images = []

            def hdiutil(args, **kwargs):
                image = Path(args[-1])
                if args[1] == "create":
                    images.append(image)
                    image.write_bytes(b"complete")
                else:
                    self.assertEqual(b"previous", target.read_bytes())
                    if len(images) == 1:
                        raise subprocess.CalledProcessError(
                            1, args, stderr="hdiutil: verify failed - Resource temporarily unavailable")
                return subprocess.CompletedProcess(args, 0, stdout="")

            with patch.object(package.subprocess, "run", side_effect=hdiutil), \
                    patch.object(package.time, "sleep") as sleep:
                package.create_dmg(root, target)
            sleep.assert_called_once_with(2)
            self.assertEqual(2, len(set(images)))
            self.assertEqual(b"complete", target.read_bytes())
            self.assertEqual([target], list(Path(root).iterdir()))

    def test_image_capacity_includes_payload_and_filesystem_headroom(self):
        with tempfile.TemporaryDirectory() as root:
            source = Path(root) / "source"; source.mkdir()
            with (source / "NexaCL.pkg").open("wb") as stream:
                stream.truncate(100 * (1 << 20))
            target = Path(root) / "result.dmg"
            def hdiutil(args, **kwargs):
                if args[1] == "create":
                    self.assertEqual("189m", args[args.index("-size") + 1])
                    Path(args[-1]).write_bytes(b"image")
                return subprocess.CompletedProcess(args, 0, stdout="")
            with patch.object(package.subprocess, "run", side_effect=hdiutil):
                package.create_dmg(source, target)
