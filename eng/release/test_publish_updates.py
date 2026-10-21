import json
import shutil
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import publish_updates
from sign import gpg, keyring, sign_distribution, verify_signatures
from verify import expected_names


@unittest.skipUnless(shutil.which("gpg"), "GnuPG is required for publisher verification")
class UpdateRepositoryPublicationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temporary = tempfile.TemporaryDirectory()
        cls.packages = Path(cls.temporary.name)
        with keyring() as home:
            gpg(home, "--pinentry-mode", "loopback", "--passphrase", "", "--quick-generate-key",
                "Update publisher test <publisher@example.invalid>", "ed25519", "sign", "0")
            listing = gpg(home, "--with-colons", "--fingerprint", "--list-keys")
            cls.fingerprint = next(line.split(b":")[9].decode() for line in listing.splitlines() if line.startswith(b"fpr:"))
            cls.public = gpg(home, "--armor", "--export", cls.fingerprint)
            private = gpg(home, "--armor", "--export-secret-keys", cls.fingerprint).decode()
        cls.version, cls.tag = "2.0.0.beta.1", "v2.0.0.beta.1"
        for name in expected_names(cls.version):
            (cls.packages / name).write_bytes(("package " + name).encode())
        sign_distribution(cls.packages, cls.version, cls.public, private, "", cls.fingerprint)

    @classmethod
    def tearDownClass(cls):
        cls.temporary.cleanup()

    def setUp(self):
        self.operations = []
        self.uploaded = {}
        self.draft = None
        self.corrupt = False
        self.extra_asset = False
        self.release = {
            "tag_name": self.tag, "draft": False, "prerelease": True,
            "html_url": f"https://github.com/{publish_updates.SOURCE}/releases/tag/{self.tag}",
            "body": "Human-written Beta 1 release notes.",
            "assets": [{"name": p.name, "size": p.stat().st_size,
                        "browser_download_url": f"https://github.com/{publish_updates.SOURCE}/releases/download/{self.tag}/{p.name}"}
                       for p in self.packages.iterdir()],
        }

    def command(self, *arguments):
        self.operations.append(arguments)
        action = arguments[1]
        if action == "download":
            target = Path(arguments[arguments.index("--dir") + 1])
            for p in self.packages.iterdir():
                shutil.copy2(p, target / p.name)
            if self.corrupt:
                p = next(target.glob("*.portable.zip"))
                data = p.read_bytes()
                p.write_bytes(bytes([data[0] ^ 1]) + data[1:])
        elif action == "create":
            self.assertIn("--draft", arguments)
            self.draft = {"draft": True, "body": "Human-written Beta 1 release notes."}
        elif action == "upload":
            self.assertTrue(self.draft["draft"])
            for p in arguments[arguments.index("--clobber") + 1:]:
                self.uploaded[Path(p).name] = Path(p).stat().st_size
        elif action == "view":
            rows = [{"name": name, "size": size} for name, size in self.uploaded.items()]
            if self.extra_asset:
                rows.append({"name": "unexpected.zip", "size": 1})
            return json.dumps({"assets": rows})
        elif action == "edit":
            self.assertIn("--draft=false", arguments)
            self.assertIn("--prerelease=true", arguments)
            self.assertIn("--latest=false", arguments)
            self.draft["draft"] = False
        else:
            self.fail("Unexpected GitHub operation")
        return ""

    def run_publication(self):
        def verify(directory, version, public):
            self.assertFalse(any(args[1] in ("create", "upload", "edit") for args in self.operations))
            verify_signatures(directory, version, public, self.fingerprint)
        with patch.object(publish_updates, "api", return_value=self.release), \
             patch.object(publish_updates, "gh", side_effect=self.command), \
             patch.object(publish_updates, "get_release", side_effect=lambda *_: self.draft), \
             patch.object(publish_updates, "verify_signatures", side_effect=verify):
            return publish_updates.publish(self.tag, self.public)

    def test_complete_signed_distribution_becomes_visible_only_after_upload_verification(self):
        self.assertTrue(self.run_publication())
        self.assertFalse(self.draft["draft"])
        self.assertEqual({p.name: p.stat().st_size for p in self.packages.iterdir()}, self.uploaded)
        self.assertEqual("edit", self.operations[-1][1])

    def test_tampering_blocks_all_destination_mutations(self):
        self.corrupt = True
        with self.assertRaises(ValueError):
            self.run_publication()
        self.assertIsNone(self.draft)
        self.assertEqual(["download"], [args[1] for args in self.operations])

    def test_unexpected_uploaded_asset_leaves_a_retryable_draft(self):
        self.extra_asset = True
        with self.assertRaises(ValueError):
            self.run_publication()
        self.assertTrue(self.draft["draft"])
        self.assertFalse(any(args[1] == "edit" for args in self.operations))

    def test_published_versions_are_not_overwritten(self):
        self.draft = {"draft": False, "body": "Published notes"}
        self.assertFalse(self.run_publication())
        self.assertEqual([], self.operations)

    def test_invalid_origin_destination_tag_or_asset_never_downloads(self):
        with patch.object(publish_updates, "api") as read:
            for tag, destination in [(self.tag, "other/repo"), ("v2.0.0.ci.abcdef", publish_updates.DESTINATION),
                                     ("v2.0.0;echo", publish_updates.DESTINATION)]:
                with self.assertRaises(ValueError):
                    publish_updates.publish(tag, self.public, destination)
            read.assert_not_called()
        for mutation in (lambda release: release.update(draft=True),
                         lambda release: release["assets"][0].update(name="../file"),
                         lambda release: release["assets"][0].update(browser_download_url="https://example.com/file")):
            release = json.loads(json.dumps(self.release))
            mutation(release)
            with patch.object(publish_updates, "api", return_value=release), patch.object(publish_updates, "gh") as execute:
                with self.assertRaises(ValueError):
                    publish_updates.publish(self.tag, self.public)
                execute.assert_not_called()

    def test_schedule_selects_one_public_release_per_channel(self):
        rows = [{"tag_name": tag, "draft": draft} for tag, draft in [
            ("v2.0.0.ci.abcdef", False), ("v2.0.0.beta.3", True), ("v2.0.0.beta.2", False),
            ("v2.0.0.alpha.9", False), ("v2.0.0.beta.1", False), ("v2.0.0", False)]]
        with patch.object(publish_updates, "api", return_value=rows):
            self.assertEqual(["v2.0.0.beta.2", "v2.0.0.alpha.9", "v2.0.0"], publish_updates.recent_tags())


if __name__ == "__main__":
    unittest.main()
