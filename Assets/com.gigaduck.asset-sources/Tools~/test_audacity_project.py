"""python -B -m unittest discover -s "<package>/Tools~" -p test_audacity_project.py -v   (needs ffmpeg on PATH)"""
import array, math, os, shutil, sys, tempfile, unittest, wave

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "Exporters~"))
import audacity_project as ap


def write_wav(path, channels, rate=22050, frames=70000, width=2):
    samples = array.array("h", [int(20000 * math.sin(i * 0.05 * (1 + i % channels))) for i in range(frames * channels)])
    with wave.open(path, "wb") as w:
        w.setnchannels(channels), w.setsampwidth(width), w.setframerate(rate)
        w.writeframes(samples.tobytes())


@unittest.skipUnless(shutil.which("ffmpeg"), "ffmpeg is required")
class AudacityProjectTests(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.mkdtemp()

    def tearDown(self):
        shutil.rmtree(self.dir, ignore_errors=True)

    def test_document_encoding_is_lossless(self):
        self.assertEqual(ap.emit(ap.tokens(ap.HEAD)), ap.HEAD)
        doc = ap._doc("x", 44100.0, 2, 1000, [[1], [2]], ap.I16)
        self.assertEqual(ap.emit(ap.tokens(doc)), doc)

    def test_mono_and_stereo_round_trip_exactly(self):
        for channels in (1, 2):
            wav, project = (os.path.join(self.dir, n) for n in ("a%d.wav" % channels, "a%d.aup3" % channels))
            write_wav(wav, channels)
            info = ap.create(wav, project)
            self.assertEqual((info["channels"], info["rate"], info["format"]), (channels, 22050, "int16"))
            self.assertTrue(ap.verify(project, wav)["identical"])
            out = os.path.join(self.dir, "out%d.wav" % channels)
            ap.export(project, out, like=wav)
            self.assertEqual(ap.decode(out)[0], ap.decode(wav)[0])
            self.assertEqual([os.path.basename(f) for f in os.listdir(self.dir) if "-wal" in f or "-shm" in f], [])

    def test_blocks_split_past_the_block_size(self):
        wav, project = os.path.join(self.dir, "long.wav"), os.path.join(self.dir, "long.aup3")
        write_wav(wav, 2, frames=ap.BLOCK + 123)
        ap.create(wav, project)
        self.assertEqual(len(ap.read_project(project)["tracks"][0]["clips"][0]["blocks"]), 2)
        self.assertTrue(ap.verify(project, wav)["identical"])

    def test_never_overwrites_a_project(self):
        wav, project = os.path.join(self.dir, "a.wav"), os.path.join(self.dir, "a.aup3")
        write_wav(wav, 1, frames=100)
        ap.create(wav, project)
        with self.assertRaises(FileExistsError):
            ap.create(wav, project)

    def test_lossy_target_keeps_codec(self):
        wav, project, ogg = (os.path.join(self.dir, n) for n in ("a.wav", "a.aup3", "a.ogg"))
        write_wav(wav, 2, rate=44100, frames=44100)
        ap.create(wav, project)
        ap.export(project, ogg)
        self.assertEqual(ap.probe(ogg)["codec_name"], "vorbis")
        self.assertEqual(ap.probe(ogg)["channels"], 2)


if __name__ == "__main__":
    unittest.main()
