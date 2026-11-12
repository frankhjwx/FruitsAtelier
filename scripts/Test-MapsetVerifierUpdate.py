"""Offline regression for stable-release pin updates."""
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("updater", Path(__file__).with_name("Update-MapsetVerifier.py"))
updater = importlib.util.module_from_spec(spec)
spec.loader.exec_module(updater)


class AuthenticationTests(unittest.TestCase):
    def test_environment_tokens_take_precedence_over_cli(self):
        for environment, expected in (({"GH_TOKEN": "gh-token", "GITHUB_TOKEN": "github-token"}, "gh-token"),
                                      ({"GITHUB_TOKEN": "github-token"}, "github-token")):
            with patch.dict(updater.os.environ, environment, clear=True), patch.object(updater, "github_cli_authenticated") as cli, \
                    patch.object(updater.urllib.request, "urlopen", return_value=io.BytesIO(b'{"tag_name":"v1"}')) as request:
                self.assertEqual(updater.github("repos/example/upstream/releases/latest")["tag_name"], "v1")
                self.assertEqual(request.call_args.args[0].get_header("Authorization"), "Bearer " + expected)
                cli.assert_not_called()

    def test_authenticated_cli_keeps_credentials_inside_cli(self):
        result = subprocess.CompletedProcess([], 0, '{"tag_name":"v1"}', "")
        with patch.dict(updater.os.environ, {}, clear=True), patch.object(updater, "github_cli_authenticated", return_value=True), \
                patch.object(updater.subprocess, "run", return_value=result) as run, patch.object(updater.urllib.request, "urlopen") as request:
            self.assertEqual(updater.github("repos/example/upstream/releases/latest")["tag_name"], "v1")
            self.assertEqual(run.call_args.args[0], ["gh", "api", "--hostname", "github.com", "repos/example/upstream/releases/latest"])
            request.assert_not_called()

    def test_cli_failure_does_not_retry_anonymously(self):
        with patch.dict(updater.os.environ, {}, clear=True), patch.object(updater, "github_cli_authenticated", return_value=True), \
                patch.object(updater.subprocess, "run", return_value=subprocess.CompletedProcess([], 1, "", "API rate limit exceeded")), \
                patch.object(updater.urllib.request, "urlopen") as request:
            with self.assertRaisesRegex(RuntimeError, "Authenticated GitHub CLI request failed"):
                updater.github("repos/example/upstream/releases/latest")
            request.assert_not_called()

    def test_anonymous_quota_error_explains_authentication_and_reset(self):
        error = updater.urllib.error.HTTPError("https://api.github.com/test", 403, "Forbidden",
            {"X-RateLimit-Remaining": "0", "X-RateLimit-Limit": "60", "X-RateLimit-Reset": "1794453888"}, None)
        with patch.dict(updater.os.environ, {}, clear=True), patch.object(updater, "github_cli_authenticated", return_value=False), \
                patch.object(updater.urllib.request, "urlopen", side_effect=error):
            with self.assertRaisesRegex(RuntimeError, "unauthenticated.*HTTP 403.*limit 60.*Resets at.*gh auth login"):
                updater.github("repos/example/upstream/releases/latest")


class ReleasePinTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        (self.root / "eng").mkdir()
        self.path = self.root / "eng/mapset-verifier.json"
        self.pin = dict(repository="example/upstream", tag="v1", commit="old", sourceSha256="old-hash")
        self.path.write_text(json.dumps(self.pin), encoding="utf-8")
        self.root_patch = patch.object(updater, "ROOT", self.root)
        self.root_patch.start()

    def tearDown(self):
        self.root_patch.stop()
        self.temporary.cleanup()

    def test_unchanged_release_needs_no_download(self):
        with patch.object(updater, "github", side_effect=[dict(tag_name="v1", draft=False, prerelease=False), dict(object=dict(type="commit", sha="old"))]), patch.object(updater.urllib.request, "urlopen") as download:
            updater.main()
            download.assert_not_called()
        self.assertEqual(json.loads(self.path.read_text()), self.pin)

    def test_annotated_tag_pins_commit_and_archive_hash(self):
        replies = [dict(tag_name="v2", draft=False, prerelease=False), dict(object=dict(type="tag", sha="tag-object")), dict(object=dict(type="commit", sha="immutable"))]
        with patch.object(updater, "github", side_effect=replies), patch.object(updater.urllib.request, "urlopen", return_value=io.BytesIO(b"source archive")) as download:
            updater.main()
        download.assert_called_once_with("https://codeload.github.com/example/upstream/zip/immutable", timeout=120)
        self.assertEqual(json.loads(self.path.read_text()), dict(repository="example/upstream", tag="v2", commit="immutable", sourceSha256=hashlib.sha256(b"source archive").hexdigest()))

    def test_prerelease_and_network_failure_preserve_pin(self):
        original = self.path.read_bytes()
        for response in (dict(tag_name="v2-rc", draft=False, prerelease=True), dict(tag_name="v2", draft=True, prerelease=False)):
            with patch.object(updater, "github", return_value=response), self.assertRaises(RuntimeError):
                updater.main()
        with patch.object(updater, "github", side_effect=OSError("offline")), self.assertRaises(OSError):
            updater.main()
        self.assertEqual(self.path.read_bytes(), original)


if __name__ == "__main__":
    unittest.main()
