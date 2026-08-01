#!/usr/bin/env python3
"""Offline contract tests for the tutorial mirror's revision pinning."""

from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import unittest
from pathlib import Path
from unittest import mock


REPOSITORY_ROOT = Path(__file__).resolve().parent.parent
SCRIPT_PATH = REPOSITORY_ROOT / "Docs" / "_sync_tutorials.py"
SPEC = importlib.util.spec_from_file_location("ninjaslayer_tutorial_sync", SCRIPT_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Could not load {SCRIPT_PATH}")
SYNC = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = SYNC
SPEC.loader.exec_module(SYNC)


class TutorialSyncRevisionTests(unittest.TestCase):
    revision = "0123456789abcdef0123456789abcdef01234567"

    def test_resolve_git_revision_accepts_exact_sha(self) -> None:
        completed = subprocess.CompletedProcess(
            args=["git"],
            returncode=0,
            stdout=f"{self.revision}\trefs/heads/main\n",
            stderr="",
        )
        with mock.patch.object(SYNC.subprocess, "run", return_value=completed):
            self.assertEqual(SYNC.resolve_git_revision(), self.revision)

    def test_resolve_git_revision_requires_git(self) -> None:
        with mock.patch.object(SYNC.subprocess, "run", side_effect=FileNotFoundError("git")):
            with self.assertRaisesRegex(RuntimeError, "git is required"):
                SYNC.resolve_git_revision()

    def test_resolve_git_revision_rejects_timeout(self) -> None:
        error = subprocess.TimeoutExpired(cmd="git", timeout=10)
        with mock.patch.object(SYNC.subprocess, "run", side_effect=error):
            with self.assertRaisesRegex(RuntimeError, "timed out"):
                SYNC.resolve_git_revision()

    def test_resolve_git_revision_rejects_invalid_output(self) -> None:
        for output in ("", "main\trefs/heads/main\n", "a" * 39):
            with self.subTest(output=output):
                completed = subprocess.CompletedProcess(
                    args=["git"],
                    returncode=0,
                    stdout=output,
                    stderr="",
                )
                with mock.patch.object(SYNC.subprocess, "run", return_value=completed):
                    with self.assertRaisesRegex(RuntimeError, "invalid.*revision"):
                        SYNC.resolve_git_revision()

    def test_revision_failure_prevents_archive_download(self) -> None:
        with mock.patch.object(
            SYNC,
            "resolve_git_revision",
            side_effect=RuntimeError("revision unavailable"),
        ), mock.patch.object(SYNC, "fetch_repository_archive") as fetch_archive:
            with self.assertRaisesRegex(RuntimeError, "revision unavailable"):
                SYNC.load_ritsulib_markdown()
            fetch_archive.assert_not_called()

    def test_rendered_site_and_manifest_use_exact_revision(self) -> None:
        markdown = {"docs/pages/index.md": "# RitsuLib\n"}
        with mock.patch.object(
            SYNC,
            "load_ritsulib_markdown",
            return_value=(self.revision, markdown),
        ):
            result = SYNC.render_ritsulib_site()

        self.assertTrue(result.succeeded)
        self.assertEqual(result.source_revision, self.revision)
        self.assertEqual(len(result.pages), 1)
        self.assertIn(f"/blob/{self.revision}/docs/pages/index.md", result.pages[0].source_url)
        manifest = json.loads(SYNC.manifest_text(result, "2026-08-01T00:00:00+00:00"))
        self.assertEqual(manifest["sourceRevision"], self.revision)

    def test_rendered_site_strips_trailing_whitespace(self) -> None:
        markdown = {"docs/pages/index.md": "# RitsuLib  \n\n::: \t\n"}
        with mock.patch.object(
            SYNC,
            "load_ritsulib_markdown",
            return_value=(self.revision, markdown),
        ):
            result = SYNC.render_ritsulib_site()

        self.assertTrue(result.succeeded)
        self.assertEqual(next(iter(result.rendered.values())), "# RitsuLib\n\n:::\n")


if __name__ == "__main__":
    unittest.main()
