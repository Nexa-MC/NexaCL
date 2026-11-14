#!/usr/bin/env python3
"""Exercise the real NuGet restore parser and product identity independently of HEAD."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]


class VersionTests(unittest.TestCase):
    def test_product_identity_and_restore_accept_every_commit_shape(self):
        with tempfile.TemporaryDirectory(prefix="nexa-version-") as directory:
            root = Path(directory)
            # An isolated two-project graph requires no package feed. Copy the
            # repository SDK selection and import its actual version properties.
            (root / "global.json").write_bytes((ROOT / "global.json").read_bytes())
            (root / "NuGet.Config").write_text(
                '<configuration><packageSources><clear/></packageSources></configuration>', encoding="utf-8")
            (root / "Version.props").write_bytes((ROOT / "eng/xsr/Xsr.Version.props").read_bytes())
            for name in ("Consumer", "Library"):
                project = root / name
                project.mkdir()
                reference = ('<ItemGroup><ProjectReference Include="../Library/Library.csproj"/></ItemGroup>'
                             if name == "Consumer" else "")
                (project / f"{name}.csproj").write_text(
                    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
                    '</PropertyGroup><Import Project="../Version.props"/>' + reference + '</Project>', encoding="utf-8")
            cases = [("ci", commit, f"2.0.0.ci.{commit}", f"2.0.0-ci.g{commit}")
                     for commit in ("051029", "000001", "123456", "abcdef", "0abcde")]
            cases += [("stable", "abcdef", "2.0.0", "2.0.0"),
                      ("alpha", "abcdef", "2.0.0.alpha.7", "2.0.0-alpha.7"),
                      ("beta", "abcdef", "2.0.0.beta.7", "2.0.0-beta.7")]
            for channel, commit, product, package in cases:
                with self.subTest(channel=channel, commit=commit):
                    result = subprocess.run(
                        [os.environ.get("DOTNET_HOST_PATH", "dotnet"), "msbuild", "Consumer/Consumer.csproj",
                         "-t:Restore;PrintXsrVersion", "-nr:false", "-v:minimal",
                         f"-p:XsrVersionChannel={channel}", f"-p:XsrCommitShort={commit}",
                         "-p:XsrVersionSequence=7"], cwd=root, capture_output=True, text=True, timeout=60)
                    self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                    self.assertIn(f"XSR_PRODUCT_VERSION={product}", result.stdout)
                    self.assertIn(f"XSR_PACKAGE_VERSION={package}", result.stdout)
                    for name in ("Consumer", "Library"):
                        cache = json.loads((root / name / "obj/project.nuget.cache").read_text(encoding="utf-8"))
                        self.assertTrue(cache["success"], cache)


if __name__ == "__main__":
    unittest.main()
