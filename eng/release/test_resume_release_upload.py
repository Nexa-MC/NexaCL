import copy
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import resume_release_upload as recovery


class ResumeReleaseUploadTests(unittest.TestCase):
    def test_metadata_read_retries_transport_errors_without_accepting_invalid_data(self):
        with patch.object(recovery.subprocess, "check_output", side_effect=[
                subprocess.CalledProcessError(1, ["gh", "api"]), b'{"id": 1}']) as read, \
                patch.object(recovery.time, "sleep"):
            self.assertEqual({"id": 1}, recovery.api("releases/1"))
            self.assertEqual(2, read.call_count)
        with patch.object(recovery.subprocess, "check_output", side_effect=
                          subprocess.CalledProcessError(1, ["gh", "api"])) as read, \
                patch.object(recovery.time, "sleep"):
            with self.assertRaises(RuntimeError):
                recovery.api("releases/1")
            self.assertEqual(4, read.call_count)
        with patch.object(recovery.subprocess, "check_output", return_value=b'not JSON') as read:
            with self.assertRaises(ValueError):
                recovery.api("releases/1")
            self.assertEqual(1, read.call_count)

    def test_partial_upload_retries_missing_bytes_without_replacing_existing_assets(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            for name in ("existing.zip", "missing.zip"):
                (directory / name).write_bytes(name.encode())
            expected = recovery.expected_files(directory)
            assets = [{"name": name, "size": size, "digest": digest, "state": "uploaded"}
                      for name, (size, digest) in sorted(expected.items())]
            partial, complete = {"assets": assets[:1]}, {"assets": assets}
            with patch.object(recovery, "read_draft", side_effect=[partial, partial, complete, complete]), \
                    patch.object(recovery.subprocess, "run", side_effect=[
                        subprocess.CompletedProcess([], 1), subprocess.CompletedProcess([], 0)]) as run, \
                    patch.object(recovery.time, "sleep"):
                recovery.upload(directory, 1, "v2.0.0.alpha.28", "a" * 40, expected)
                self.assertEqual(2, run.call_count)
                self.assertEqual(run.call_args_list[0], run.call_args_list[1])
                self.assertIn(str(directory / "missing.zip"), run.call_args.args[0])
                self.assertNotIn(str(directory / "existing.zip"), run.call_args.args[0])
                self.assertNotIn("--clobber", run.call_args.args[0])

    def test_conflicting_or_unknown_existing_assets_fail_closed(self):
        expected = {"expected.zip": (4, "sha256:" + "a" * 64)}
        for name, size, digest in (("unknown.zip", 4, "sha256:" + "a" * 64),
                                   ("expected.zip", 5, "sha256:" + "a" * 64),
                                   ("expected.zip", 4, "sha256:" + "b" * 64)):
            with self.subTest(name=name, size=size, digest=digest):
                with self.assertRaises(ValueError):
                    recovery.existing_files({"assets": [{"name": name, "size": size, "digest": digest,
                                                         "state": "uploaded"}]}, expected)

    def test_only_exact_unpublished_draft_can_receive_assets(self):
        release = {"tag_name": "v2.0.0.alpha.28", "target_commitish": "a" * 40,
                   "draft": True, "body": "manual release notes"}
        for field, invalid in (("tag_name", "v2.0.0.alpha.29"), ("target_commitish", "b" * 40),
                               ("draft", False), ("body", " ")):
            value = {**release, field: invalid}
            with self.subTest(field=field), patch.object(recovery, "api", return_value=value):
                with self.assertRaises(ValueError):
                    recovery.read_draft(1, "v2.0.0.alpha.28", "a" * 40)

    def test_source_must_pass_all_platforms_and_fail_only_after_signing(self):
        run = {"head_sha": "a" * 40, "head_branch": "v2.0.0.alpha.28",
               "path": ".github/workflows/launcher-build.yml", "status": "completed", "conclusion": "failure"}
        jobs = [{"name": "metadata", "conclusion": "success"}] + [
            {"name": "Launcher " + rid, "conclusion": "success"}
            for rid in ("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")]
        jobs.append({"name": "distribution", "conclusion": "failure", "steps": [
            {"name": "Sign and independently verify the complete distribution", "conclusion": "success"},
            {"name": "Verify embedded Sidecar signatures and provenance in all six portable packages", "conclusion": "success"},
            {"name": "Publish tagged release", "conclusion": "failure"}]})
        saved = {"artifacts": [{"name": "Complete-Nexa-distribution", "expired": False}]}
        variants = [(run, jobs, True), ({**run, "head_sha": "b" * 40}, jobs, False)]
        failed_platform = copy.deepcopy(jobs)
        failed_platform[1]["conclusion"] = "failure"
        variants.append((run, failed_platform, False))
        failed_signing = copy.deepcopy(jobs)
        failed_signing[-1]["steps"][0]["conclusion"] = "failure"
        variants.append((run, failed_signing, False))
        for source, source_jobs, valid in variants:
            with self.subTest(valid=valid, source=source), \
                    patch.object(recovery.subprocess, "check_output", return_value="a" * 40), \
                    patch.object(recovery, "api", side_effect=[source, {"jobs": source_jobs}, saved]):
                if valid:
                    self.assertEqual("a" * 40, recovery.source("v2.0.0.alpha.28", 1))
                else:
                    with self.assertRaises(ValueError):
                        recovery.source("v2.0.0.alpha.28", 1)
