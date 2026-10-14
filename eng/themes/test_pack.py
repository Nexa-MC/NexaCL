"""Container properties that a theme author can verify before runtime admission."""

import json
import pathlib
import tempfile
import unittest
import zipfile

import pack


class ThemePackTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = pathlib.Path(self.temporary.name)
        self.theme = self.root / "theme"
        self.theme.mkdir()
        (self.theme / "mimetype").write_bytes(pack.MIMETYPE)
        (self.theme / "theme.json").write_text(json.dumps({
            "format": "NTP/1", "styleApi": 1, "nss": "NSS/1", "styles": ["main.nss"],
        }), encoding="utf-8")
        (self.theme / "main.nss").write_text("/* deliberately empty */", encoding="utf-8")

    def test_package_is_reproducible_with_literal_uncompressed_mimetype_first(self):
        first, second = self.root / "first.ntp", self.root / "second.ntp"
        pack.pack(self.theme, first)
        pack.pack(self.theme, second)
        self.assertEqual(first.read_bytes(), second.read_bytes())
        with zipfile.ZipFile(first) as archive:
            self.assertEqual(archive.infolist()[0].filename, "mimetype")
            self.assertEqual(archive.infolist()[0].compress_type, zipfile.ZIP_STORED)
            self.assertEqual(archive.read("mimetype"), pack.MIMETYPE)

    def test_asset_cannot_escape_through_a_link(self):
        (self.root / "private.txt").write_text("private", encoding="utf-8")
        (self.theme / "linked.txt").symlink_to(self.root / "private.txt")
        with self.assertRaisesRegex(ValueError, "Symbolic links"):
            pack.source_files(self.theme)

    def test_ambiguous_portable_paths_and_presentation_replacements_are_rejected(self):
        for name in ("CON.png", "trailing. ", "page.nxml", "behavior.js"):
            with self.subTest(name=name):
                path = self.theme / name
                path.write_bytes(b"fixture")
                with self.assertRaises(ValueError):
                    pack.source_files(self.theme)
                path.unlink()

    def test_missing_style_and_case_colliding_assets_are_rejected(self):
        (self.theme / "main.nss").unlink()
        with self.assertRaisesRegex(ValueError, "declared style"):
            pack.source_files(self.theme)
        (self.theme / "main.nss").write_bytes(b"")
        (self.theme / "ICON.png").write_bytes(b"first")
        (self.theme / "icon.png").write_bytes(b"second")
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            pack.source_files(self.theme)

    def test_publication_check_detects_a_missing_or_changed_resource(self):
        published = self.root / "published"
        published.mkdir()
        with self.assertRaisesRegex(ValueError, "Missing or altered"):
            pack.verify_published(self.root, published)


if __name__ == "__main__":
    unittest.main()
