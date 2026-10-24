"""Offline regression for stable-release pin updates."""
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("updater", Path(__file__).with_name("Update-MapsetVerifier.py"))
updater = importlib.util.module_from_spec(spec)
spec.loader.exec_module(updater)


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
