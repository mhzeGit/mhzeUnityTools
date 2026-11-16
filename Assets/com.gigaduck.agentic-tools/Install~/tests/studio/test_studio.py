"""Content Studio tests without Unity: a synthetic take (made with ffmpeg) goes through takes, sheet, export, pieces,
the Blender edit project and publish. Blender steps are skipped when Blender is not installed.

    python Install~/tests/studio/test_studio.py            (needs ffmpeg: pip install imageio-ffmpeg)
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

PKG = Path(__file__).resolve().parents[2]
TOOLS = PKG / "files" / "tools"


def ffmpeg():
    found = shutil.which("ffmpeg")
    if not found:
        try:
            import imageio_ffmpeg
            found = imageio_ffmpeg.get_ffmpeg_exe()
        except Exception:
            found = None
    return found


class StudioTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.ff = ffmpeg()
        if not cls.ff:
            raise unittest.SkipTest("no ffmpeg")
        cls.tmp = Path(tempfile.mkdtemp(prefix="studio_test_"))
        proj = cls.tmp / "Proj"
        (proj / "ProjectSettings").mkdir(parents=True)
        (proj / "ProjectSettings" / "ProjectVersion.txt").write_text("m_EditorVersion: 6000.0.0f1\n")
        shutil.copytree(TOOLS / "agentplay", proj / "tools" / "agentplay")
        shutil.copytree(TOOLS / "studio", proj / "tools" / "studio")
        cls.proj, cls.out = proj, cls.tmp / "Out"
        cfg = {"outputDir": str(cls.out), "contentDir": "Marketing", "ffmpeg": cls.ff,
               "brand": {"gameName": "Test Game", "cta": "Wishlist now on Steam"},
               "defaults": {"format": "vertical", "fps": 30, "outro": "outro-test"}}
        (proj / "tools" / "studio" / "studio.json").write_text(json.dumps(cfg))
        cls.studio("init")
        cls.make_take()

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.tmp, ignore_errors=True)

    @classmethod
    def studio(cls, *args, ok=True, timeout=600):
        p = subprocess.run([sys.executable, str(cls.proj / "tools" / "studio" / "studio.py")] + list(args),
                           capture_output=True, text=True, timeout=timeout, cwd=str(cls.proj))
        if ok and p.returncode != 0:
            raise AssertionError("studio %s failed:\n%s\n%s" % (" ".join(args), p.stdout[-2000:], p.stderr[-3000:]))
        return p

    @classmethod
    def make_take(cls):
        """What the player writes: clean.mp4, ui/ui_00000.png... (alpha), audio.wav, events.jsonl, take.json."""
        folder = cls.out / "takes" / "demo" / "demo_vertical_20260101-000000"
        (folder / "ui").mkdir(parents=True)
        w, h, fps, dur = 540, 960, 30, 3
        run = lambda *a: subprocess.run([cls.ff, "-hide_banner", "-loglevel", "error", "-y"] + list(a), check=True, timeout=120)
        run("-f", "lavfi", "-i", "testsrc2=size=%dx%d:rate=%d:duration=%d" % (w, h, fps, dur), "-pix_fmt", "yuv420p", str(folder / "clean.mp4"))
        run("-f", "lavfi", "-i", "color=c=yellow:size=%dx%d:rate=%d:duration=%d,format=rgba,geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='if(between(X,40,500)*between(Y,700,820),230,0)'"
            % (w, h, fps, dur), "-start_number", "0", str(folder / "ui" / "ui_%05d.png"))
        run("-f", "lavfi", "-i", "sine=frequency=440:duration=%d:sample_rate=48000" % dur, "-ac", "2", "-c:a", "pcm_f32le", str(folder / "audio.wav"))
        (folder / "stems").mkdir()
        run("-f", "lavfi", "-i", "sine=frequency=880:duration=%d:sample_rate=48000" % dur, "-ac", "2", "-c:a", "pcm_f32le", str(folder / "stems" / "voice.wav"))
        events = [{"t": 0.0, "frame": 0, "kind": "label", "text": "03 Tea - kettle"},
                  {"t": 0.5, "frame": 15, "kind": "text", "text": "Blah blah blahblah..."},
                  {"t": 2.0, "frame": 60, "kind": "text", "text": "(none)"},
                  {"t": 1.0, "frame": 30, "kind": "mark", "text": "she smiles"}]
        (folder / "events.jsonl").write_text("\n".join(json.dumps(e) for e in events) + "\n")
        take = {"id": folder.name, "width": w, "height": h, "fps": fps, "frames": fps * dur, "duration": dur, "mode": "offline",
                "clean": "clean.mp4", "ui": "ui/ui_%05d.png", "audio": "audio.wav", "audioRate": 48000, "audioChannels": 2,
                "shot": "demo", "format": "vertical", "stemFiles": ["stems/voice.wav"], "finished": True}
        (folder / "take.json").write_text(json.dumps(take))
        cls.take = folder

    def test_1_takes_and_sheet(self):
        self.assertIn("demo_vertical", self.studio("takes").stdout)
        self.studio("sheet", "demo")
        self.assertTrue((self.take / "sheet.jpg").stat().st_size > 1000)

    def test_2_export_layers(self):
        a = json.loads(self.studio("export", "demo", "--outro", "none").stdout)
        self.assertTrue(a["audio"])
        self.assertAlmostEqual(a["duration"], 3.0, delta=0.15)
        self.assertEqual(a["size"], [540, 960])
        b = json.loads(self.studio("export", "demo", "--ui", "off", "--audio", "none", "--outro", "none").stdout)
        self.assertFalse(b["audio"])
        self.assertIn("_noui", b["export"])
        c = json.loads(self.studio("export", "demo", "--audio", "voice", "--outro", "none", "--in", "0.5", "--out", "2.5", "--platform", "tiktok").stdout)
        self.assertEqual(c["size"], [1080, 1920])
        self.assertAlmostEqual(c["duration"], 2.0, delta=0.15)

    def test_3_ui_is_in_full_and_not_in_clean(self):
        full = json.loads(self.studio("export", "demo", "--outro", "none", "-o", str(self.tmp / "full.mp4")).stdout)["export"]
        clean = json.loads(self.studio("export", "demo", "--ui", "off", "--outro", "none", "-o", str(self.tmp / "clean.mp4")).stdout)["export"]

        def pixel(path):
            raw = subprocess.run([self.ff, "-hide_banner", "-loglevel", "error", "-ss", "1", "-i", path, "-frames:v", "1",
                                  "-vf", "crop=10:10:270:760", "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], capture_output=True, timeout=60).stdout
            return raw[0], raw[1], raw[2]

        r, g, b = pixel(full)
        self.assertTrue(r > 150 and g > 150 and b < 120, "UI box (yellow) missing in full export: %s" % ((r, g, b),))
        self.assertNotEqual(pixel(clean), (r, g, b))

    def test_4_pieces_edit_publish(self):
        import importlib.util
        spec = importlib.util.spec_from_file_location("studio", self.proj / "tools" / "studio" / "studio.py")
        if not shutil.which("blender") and not os.environ.get("BLENDER_EXE"):
            mod = importlib.util.module_from_spec(spec)
            sys.argv = ["studio.py"]
            try:
                spec.loader.exec_module(mod)
                if not mod.blender(False):
                    self.skipTest("no Blender")
            except SystemExit:
                self.skipTest("no Blender")
        self.studio("piece", "new", "outro-test", "--role", "outro")
        self.assertIn("outro-test", self.studio("piece", "ls").stdout)
        pf = self.studio("piece", "render", "outro-test", "--format", "540x960").stdout.strip()
        self.assertTrue(Path(pf).exists(), pf)
        e = json.loads(self.studio("export", "demo").stdout)
        self.assertAlmostEqual(e["duration"], 3.0 + 3.0 - 0.25, delta=0.2)
        self.studio("edit", "new", "demo-edit", "--take", "demo")
        edit_file = self.proj / "Marketing" / "Edits" / "demo-edit.edit.json"
        d = json.loads(edit_file.read_text())
        d["format"] = "540x960"
        d["timeline"][0]["audio"] = "voice"
        d["post"] = {"caption": "She is so happy to see you.", "hashtags": ["#indiegame"]}
        edit_file.write_text(json.dumps(d))
        b = json.loads(self.studio("edit", "build", "demo-edit").stdout)
        self.assertTrue(Path(b["blend"]).exists())
        self.assertEqual(b["frames"], 90 + 90 - 8 + 1 - 1)  # outro fades in over the last 8 frames of the take
        # a hand-edited project is never replaced silently
        os.utime(b["blend"], None)
        later = Path(b["blend"]).stat().st_mtime + 10
        os.utime(b["blend"], (later, later))
        self.assertNotEqual(self.studio("edit", "build", "demo-edit", ok=False).returncode, 0)
        r = json.loads(self.studio("edit", "render", "demo-edit", timeout=1200).stdout)
        self.assertTrue(Path(r["render"]).exists())
        p = json.loads(self.studio("publish", "demo-edit", "--platform", "tiktok", timeout=1200).stdout)
        self.assertTrue(Path(p["files"][0]["file"]).exists())
        self.assertTrue(Path(p["thumbnail"]).exists())
        self.assertIn("Blah blah", Path(p["captions"]).read_text())


if __name__ == "__main__":
    unittest.main(verbosity=2)
