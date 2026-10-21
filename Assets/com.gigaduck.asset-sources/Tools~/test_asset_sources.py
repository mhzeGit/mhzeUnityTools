import contextlib
import io
import json
import os
from pathlib import Path
import tempfile
import unittest

from asset_sources import Registry, main


class RegistryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.base = Path(self.temp.name)
        self.project = self.base / "Project"
        (self.project / "Assets").mkdir(parents=True)
        (self.project / "ProjectSettings").mkdir()
        self.root = self.base / "Resources"
        (self.root / "Chair").mkdir(parents=True)
        for name in ("chair.blend", "chair.gmat"):
            (self.root / "Chair" / name).write_text("fixture")
        self.guid = "1234567890abcdef1234567890abcdef"
        (self.project / "Assets/chair.fbx").write_text("fixture")
        (self.project / "Assets/chair.fbx.meta").write_text("fileFormatVersion: 2\nguid: " + self.guid + "\n")
        self.registry = Registry(self.project)
        self.registry.define_library("art")
        self.registry.set_local("libraries", "art", self.root)

    def tearDown(self):
        self.temp.cleanup()

    def test_link_by_asset_and_guid_is_idempotent_and_preserves_sources(self):
        self.registry.link("Assets/chair.fbx", "art", "Chair/chair.blend")
        self.registry.link(self.guid, "art", "Chair/chair.gmat", source_id="textures")
        data = self.registry.link(self.guid, "art", "Chair/chair.blend")
        self.assertEqual(2, len(data["sources"]))
        self.assertEqual("blender", data["sources"][0]["applicationId"])
        self.assertNotIn(str(self.root), json.dumps(data))

    def test_move_library_only_changes_local_settings(self):
        self.registry.link(self.guid, "art", "Chair/chair.blend")
        record = (self.registry.settings / "Links" / (self.guid + ".json"))
        before = record.read_bytes()
        moved = self.base / "Elsewhere"
        self.root.rename(moved)
        self.registry.set_local("libraries", "art", moved)
        self.assertTrue(self.registry.inspect(self.guid)["sources"][0]["exists"])
        self.assertEqual(before, record.read_bytes())

    def test_rejects_paths_and_preserves_existing_link_on_failure(self):
        self.registry.link(self.guid, "art", "Chair/chair.blend")
        for path in ("../bad.blend", "C:/outside.blend", "Chair//chair.blend", "Chair/missing.blend"):
            with self.subTest(path=path), self.assertRaises((ValueError, OSError)):
                self.registry.link(self.guid, "art", path)
        self.assertEqual(1, len(self.registry.record(self.guid)["sources"]))

    def test_unlink_keeps_source_file(self):
        self.registry.link(self.guid, "art", "Chair/chair.blend")
        self.registry.unlink(self.guid, "model")
        self.assertTrue((self.root / "Chair/chair.blend").exists())
        self.assertEqual([], self.registry.record(self.guid)["sources"])

    def test_missing_source_reports_validation_error(self):
        self.registry.link(self.guid, "art", "Chair/chair.blend")
        (self.root / "Chair/chair.blend").unlink()
        self.assertEqual(1, len(self.registry.validate()["errors"]))

    def test_environment_overrides_local_root(self):
        key = "GIGADUCK_ASSET_SOURCES_PYTHON_TEST"
        self.registry.define_library("art", environment=key)
        replacement = self.base / "OtherRoot"
        replacement.mkdir()
        previous = os.environ.get(key)
        try:
            os.environ[key] = str(replacement)
            self.assertEqual(replacement, self.registry.root("art"))
        finally:
            if previous is None: os.environ.pop(key, None)
            else: os.environ[key] = previous

    def test_internal_library_rejected(self):
        with self.assertRaises(ValueError):
            self.registry.set_local("libraries", "art", self.project / "Assets")

    def test_bad_json_not_overwritten(self):
        (self.registry.settings / "Links").mkdir()
        path = self.registry.settings / "Links" / (self.guid + ".json")
        path.write_text("{broken")
        with self.assertRaises(ValueError):
            self.registry.link(self.guid, "art", "Chair/chair.blend")
        self.assertEqual("{broken", path.read_text())

    def test_cli_json_and_error_exit_codes(self):
        with contextlib.redirect_stdout(io.StringIO()) as output:
            result = main(["--project", str(self.project), "link", "--asset", "Assets/chair.fbx",
                           "--library", "art", "--source", "Chair/chair.blend"])
        self.assertEqual(0, result)
        self.assertTrue(json.loads(output.getvalue())["ok"])
        with contextlib.redirect_stdout(io.StringIO()) as output:
            result = main(["--project", str(self.project), "link", "--asset", self.guid,
                           "--library", "art", "--source", "missing.blend"])
        self.assertEqual(1, result)
        self.assertFalse(json.loads(output.getvalue())["ok"])


if __name__ == "__main__":
    unittest.main()
