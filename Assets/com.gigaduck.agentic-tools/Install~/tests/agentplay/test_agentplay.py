"""Offline tests of agentplay's recording, learning and dashboard modules (no Unity needed).

A fake game answers the harness's file protocol (inbox/outbox) from a thread: a short cutscene per stage, stages that
need "use the hinted item, then walk to the hinted marker", a last stage with no hints (trial and error), a lamp that
does nothing and a trap that kills. The tests learn it to mastery, play it as a recorded playtest, check the report, the
video (ffmpeg if present, else the AVI writer) and the HTTP API.

  python Install~/tests/agentplay/test_agentplay.py      (Python 3.9+; Pillow and ffmpeg optional, more is tested with them)
"""
import importlib.util
import json
import os
import shutil
import sys
import tempfile
import threading
import time
import unittest
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
TOOL = HERE.parent.parent / "files" / "tools" / "agentplay"

try:
    from PIL import Image
except ImportError:
    Image = None


# ------------------------------------------------------------------ the fake game

class FakeGame:
    STAGES = 3
    INTRO = 0.3

    def __init__(self):
        self.deaths, self.stage, self.sub, self.held, self.finished, self.lamps = 0, 0, 0, None, False, 0
        self.t_stage = time.time()
        self.lock = threading.Lock()

    def running(self):
        if self.finished:
            return []
        if time.time() - self.t_stage < self.INTRO:
            return ["Intro%d" % self.stage]
        return ["Beat%d%s" % (self.stage, "ab"[self.sub])]

    def status(self):
        return {"scene": "Fake", "stage": self.stage, "running": self.running(), "deaths": self.deaths, "held": self.held,
                "finished": self.finished, "chapter": "Fake", "checkpoint": "c%d" % self.stage}

    def jump(self, n):
        self.stage, self.sub, self.held, self.t_stage = n, 0, None, time.time()

    def advance(self):
        if self.stage + 1 >= self.STAGES:
            self.finished = True
            self.stage += 1
        else:
            self.jump(self.stage + 1)

    def observe(self):
        objs = [{"name": "Trap", "path": "Room/Trap", "dist": 1.0, "usable": True, "inView": True},
                {"name": "Lamp", "path": "Room/Lamp", "dist": 2.0, "usable": True, "inView": True},
                {"name": "Item%d" % self.stage, "path": "Room/Item%d" % self.stage, "dist": 5.0, "usable": True}]
        beats, run = [], self.running()
        if run and run[0].startswith("Beat"):
            if self.stage == self.STAGES - 1:
                beats = [{"name": run[0], "type": "MysteryBeat", "fields": {}}]  # no hints: trial and error
            elif self.sub == 0:
                beats = [{"name": run[0], "type": "UseItemBeat", "fields": {"item": {"obj": "Item%d" % self.stage, "pos": [1, 0, 1], "active": True}}}]
            else:
                beats = [{"name": run[0], "type": "WaitForPlayerNearBeat", "fields": {"zone": {"obj": "Marker%d" % self.stage, "pos": [5, 0, 5], "active": True}}}]
        return {"status": self.status(), "objects": objs, "beats": beats, "texts": ["Stage %d" % self.stage], "crosshair": None}

    def interact(self, target):
        if target == "Trap":
            self.deaths += 1
            self.jump(self.stage)
            return {}
        if target == "Item%d" % self.stage and self.running() == ["Beat%da" % self.stage]:
            if self.stage == self.STAGES - 1:
                self.advance()
            else:
                self.sub = 1
            return {"targeted": True}
        if target == "Lamp":
            self.lamps += 1
            return {"targeted": True}
        if target.startswith("Item"):
            return {"targeted": True}
        raise KeyError("interact: no active object named '%s'" % target)

    def walk(self, pos):
        if self.sub == 1 and self.running() == ["Beat%db" % self.stage] and pos == [5, 0, 5]:
            self.advance()
        return {"reached": True}


def evaluate(cond, st, start, game):
    if isinstance(cond, list):
        return all(evaluate(c, st, start, game) for c in cond)
    ok = True
    for key, arg in cond.items():
        if key == "all":
            ok &= all(evaluate(c, st, start, game) for c in arg)
        elif key == "any":
            ok &= any(evaluate(c, st, start, game) for c in arg)
        elif key == "not":
            ok &= not evaluate(arg, st, start, game)
        elif key == "field":
            v = st.get(arg)
            if "eq" in cond:
                ok &= v == cond["eq"]
            if "gt" in cond:
                ok &= v is not None and v > cond["gt"]
            if "contains" in cond:
                ok &= v is not None and cond["contains"] in v
            if "changed" in cond:
                ok &= (v != start.get(arg)) == cond["changed"]
        elif key == "usable":
            ok &= True
        elif key in ("eq", "gt", "contains", "changed", "within"):
            pass
        else:
            raise KeyError("unknown condition key '%s'" % key)
    return ok


def fake_player(folder, game, stop):
    """Answers commands like the Unity harness: inbox/*.json -> outbox/<id>.json."""
    inbox, outbox = folder / "inbox", folder / "outbox"
    inbox.mkdir(parents=True, exist_ok=True)
    outbox.mkdir(parents=True, exist_ok=True)
    rec = {"id": None, "t0": 0, "keys": 0}
    while not stop.is_set():
        for path in sorted(inbox.glob("*.json")):
            try:
                cmd = json.loads(path.read_text(encoding="utf-8"))
                path.unlink()
            except (OSError, ValueError):
                continue
            r = {"id": cmd.get("id"), "ok": True}
            try:
                r.update(handle(cmd, game, folder, rec))
            except KeyError as e:
                r.update(ok=False, error=str(e).strip("'\""))
            tmp = outbox / (cmd["id"] + ".tmp")
            tmp.write_text(json.dumps(r), encoding="utf-8")
            os.replace(tmp, outbox / (cmd["id"] + ".json"))
        time.sleep(0.01)


def handle(cmd, game, folder, rec):
    op = cmd.get("op")
    with game.lock:
        if op == "status":
            return game.status()
        if op == "stage":
            game.jump(int(cmd["n"]))
            game.finished = False
            return game.status()
        if op == "observe":
            return game.observe()
        if op == "interact":
            return game.interact(cmd["target"])
        if op == "walk":
            return game.walk(cmd["pos"])
        if op in ("look", "wait", "key"):
            pass
        if op == "errors":  # a warning always, an error that grows with every lamp switched on
            return {"issues": ["WARN x3 @walk: fake noise"] + (["ERR x%d @interact: the lamp sparks" % game.lamps] if game.lamps else [])}
        if op == "perf":
            return {"fps": 60, "worstMs": 20, "p95Ms": 17, "frames": 100}
    if op == "wait":
        time.sleep(min(float(cmd.get("s", 1)), 0.2))
        return {}
    if op == "until":
        with game.lock:
            start = game.status()
        until = time.time() + min(float(cmd.get("timeout", 10)), 2.0)
        while True:
            with game.lock:
                st = game.status()
                met = evaluate(cmd["cond"], st, start, game)
            if met or time.time() >= until:
                break
            time.sleep(0.02)
        if not met and not cmd.get("soft"):
            raise KeyError("condition not met within %s s" % cmd.get("timeout"))
        return {"met": met, "status": st}
    if op == "record":
        return record(cmd, folder, rec, game)
    return {}


def record(cmd, folder, rec, game):
    action = cmd.get("action")
    if action == "start":
        rec.update(id=cmd["id"], t0=time.time(), frames=0)
        out = folder / "recordings" / rec["id"]
        (out / "keyframes").mkdir(parents=True, exist_ok=True)
        (out / "frames").mkdir(exist_ok=True)
        (out / "events.jsonl").write_text(json.dumps({"t": 0, "kind": "label", "text": cmd.get("label", "")}) + "\n", encoding="utf-8")
        snap(out, rec, game)
        return {"recording": True, "id": rec["id"], "sink": "jpeg"}
    out = folder / "recordings" / (rec["id"] or "none")
    if action == "mark":
        with open(out / "events.jsonl", "a", encoding="utf-8") as f:
            f.write(json.dumps({"t": round(time.time() - rec["t0"], 3), "kind": cmd.get("kind", "mark"), "text": cmd.get("text")}) + "\n")
        snap(out, rec, game)
        return {}
    if action == "status":
        return {"recording": rec["id"] is not None, "elapsed": round(time.time() - rec["t0"], 3) if rec["id"] else None}
    if action == "stop":
        snap(out, rec, game)
        meta = {"id": rec["id"], "sink": "jpeg", "fps": 10, "width": 64, "height": 48, "duration": round(time.time() - rec["t0"], 3),
                "frames": rec["frames"], "captured": rec["frames"], "dropped": 0, "started": time.strftime("%Y-%m-%d %H:%M:%S")}
        (out / "recording.json").write_text(json.dumps(meta), encoding="utf-8")
        rec["id"] = None
        return meta
    return {}


def snap(out, rec, game):
    if Image is None:
        return
    t = time.time() - rec["t0"]
    color = (40 + 60 * (game.stage % 4), 90, 160)
    img = Image.new("RGB", (64, 48), color)
    img.save(out / "keyframes" / ("k_%07d.jpg" % int(t * 1000)), quality=80)
    img.save(out / "frames" / ("f_%06d.jpg" % rec["frames"]), quality=80)
    with open(out / "frames.jsonl", "a", encoding="utf-8") as f:
        f.write(json.dumps({"i": rec["frames"], "t": round(t, 3)}) + "\n")
    rec["frames"] += 1


# ------------------------------------------------------------------ the tool in a temp project

def load_tool(root):
    """A throwaway project with this package's tools/agentplay; returns the loaded agentplay module."""
    (root / "ProjectSettings").mkdir(parents=True)
    (root / "ProjectSettings" / "ProjectVersion.txt").write_text("m_EditorVersion: 6000.0.0f1\n")
    shutil.copytree(TOOL, root / "tools" / "agentplay", ignore=shutil.ignore_patterns("__pycache__"))
    for name in [m for m in list(sys.modules) if m.startswith("agentplay")]:
        del sys.modules[name]
    sys.path.insert(0, str(root / "tools" / "agentplay"))
    spec = importlib.util.spec_from_file_location("agentplay_main", root / "tools" / "agentplay" / "agentplay.py")
    ap = importlib.util.module_from_spec(spec)
    sys.modules["agentplay_main"] = ap
    spec.loader.exec_module(ap)
    import argparse
    sub = argparse.ArgumentParser().add_subparsers(dest="command")
    ap.load_modules(sub)
    ap.EXE = "python"  # the fake player is this process
    return ap


SEGMENTS = [{"id": "fake/s%d" % k, "label": "%02d Fake - s%d" % (k, k), "order": k, "setup": [{"op": "stage", "n": k}],
             "at": {"field": "stage", "eq": k}, "goal": {"any": [{"field": "stage", "gt": k}, {"field": "finished", "eq": True}]},
             "budget": 20} for k in range(FakeGame.STAGES)]


class AgentPlayTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = Path(tempfile.mkdtemp(prefix="agentplay-test-"))
        cls.ap = load_tool(cls.tmp / "Proj")
        cls.ap.learn.set_segment_provider(lambda: SEGMENTS)
        cls.game = FakeGame()
        cls.stop = threading.Event()
        folder = cls.ap.run_dir("bot")
        folder.mkdir(parents=True)
        (folder / "pid.txt").write_text(str(os.getpid()))
        cls.thread = threading.Thread(target=fake_player, args=(folder, cls.game, cls.stop), daemon=True)
        cls.thread.start()

    @classmethod
    def tearDownClass(cls):
        cls.stop.set()
        cls.thread.join(2)
        sys.path[:] = [p for p in sys.path if "agentplay-test-" not in p]
        shutil.rmtree(cls.tmp, ignore_errors=True)

    # --------------------------------------------------------- pure functions
    def test_01_conditions_state_and_gate(self):
        learn = self.ap.learn
        self.assertEqual(learn.state_key({"stage": 2, "running": ["b", "a"], "scene": "S"}), "scene=S|stage=2|running=a+b")
        gate = learn.gate_for({"stage": 1, "running": ["A", "B", "C"]}, ["A"])
        self.assertEqual(gate, {"all": [{"field": "running", "contains": "B"}, {"field": "running", "contains": "C"}]})
        self.assertEqual(learn.gate_for({"stage": 4}, []), {"all": [{"field": "stage", "eq": 4}]})

    def test_02_candidates_prefer_hints_and_learned_values(self):
        learn = self.ap.learn
        obs = {"objects": [{"name": "Trap", "dist": 1, "usable": True}, {"name": "Kettle", "dist": 6, "usable": True, "path": "K/Kettle"}],
               "beats": [{"name": "B", "type": "UseBeat", "fields": {"target": {"obj": "Kettle", "pos": [1, 0, 2]}}}]}
        acts = learn.candidates(obs, rng=__import__("random").Random(0))
        self.assertEqual(acts[0].key, "interact:Kettle")
        passive = learn.candidates({"objects": [], "beats": [], "playerFrozen": True})
        self.assertEqual(passive[0].key, "wait")
        q = {"interact:Kettle": {"n": 3, "value": -1.0}}
        self.assertNotEqual(learn.candidates(obs, q)[0].key, "interact:Kettle")

    def test_03_routes_stats_mastery_and_ucb(self):
        learn = self.ap.learn
        rec = learn.KB.blank("x/y")
        self.assertEqual(learn.mastery_of(rec), "unknown")
        a = learn.add_route(rec, [{"op": "wait", "s": 1}], "test", "me")
        self.assertEqual(learn.add_route(rec, [{"op": "wait", "s": 1}], "dup", "me"), a)
        b = learn.add_route(rec, [{"op": "key", "key": "E"}], "test", "me")
        self.assertEqual(learn.pick_route(rec, __import__("random").Random(0))["runs"], 0)
        win = {"outcome": "win", "time": 5.0, "at": "t", "by": "me", "id": "e", "steps": []}
        fail = {"outcome": "fail", "time": 9.0, "at": "t", "by": "me", "id": "e", "failKind": "blocked", "failStep": 0, "failDetail": "x",
                "steps": [{"kind": "blocked"}]}
        learn.apply_episode(rec, a, fail)
        self.assertEqual(learn.mastery_of(rec), "learning")
        for _ in range(3):
            learn.apply_episode(rec, a, win)
        self.assertEqual(learn.mastery_of(rec), "mastered")
        self.assertEqual(rec["stats"]["firstWinAfter"], 1)
        learn.apply_episode(rec, a, fail)
        self.assertEqual(learn.mastery_of(rec), "stale")
        for _ in range(4):
            learn.apply_episode(rec, b, fail)
        self.assertTrue(next(r for r in rec["routes"] if r["id"] == b)["retired"])
        fast = learn.add_route(rec, [{"op": "key", "key": "F"}], "optimize:x", "me")
        for _ in range(3):
            learn.apply_episode(rec, fast, dict(win, time=2.0))
        learn.apply_episode(rec, a, win)
        learn.apply_episode(rec, a, win)
        learn.apply_episode(rec, a, win)
        self.assertEqual(learn.best_route(rec)["id"], fast, "a faster mastered route wins")
        self.assertGreater(learn.friction(rec), 0)

    def test_04_repairs(self):
        learn = self.ap.learn
        route = {"steps": [{"op": "walk", "pos": [10, 0, 0], "when": {"field": "stage", "eq": 1}}]}
        ep = {"failStep": 0, "failKind": "blocked", "steps": [{"result": {"stoppedAt": [4, 0, 0]}}]}
        obs = {"objects": [{"name": "Door_Hall", "path": "House/Door_Hall", "usable": True, "dist": 1.5}]}
        origins = [o for o, _, _ in learn.repairs(route, ep, obs)]
        self.assertIn("repair:blocked:detour-left", origins)
        self.assertIn("repair:blocked:door", origins)
        door = next(s for o, s, _ in learn.repairs(route, ep, obs) if o == "repair:blocked:door")
        self.assertEqual(door[0]["when"], {"field": "stage", "eq": 1}, "inserted steps keep the gate")
        route2 = {"steps": [{"op": "interact", "target": "Cup"}]}
        origins = [o for o, _, _ in learn.repairs(route2, {"failStep": 0, "failKind": "no-target", "steps": []},
                                                 {"objects": [{"name": "Cup", "usable": False}]})]
        self.assertEqual(origins[0], "repair:no-target:until-usable")
        self.assertEqual(learn.repairs(route2, {"failStep": 0, "failKind": "crash"}, {}), [])

    # --------------------------------------------------------- with the fake player
    def test_10_explore_learns_and_avoids_the_trap(self):
        learn = self.ap.learn
        seg = SEGMENTS[2]  # no hints: trial and error
        res = learn.explore("bot", seg, budget=40, rng=__import__("random").Random(3))
        self.assertEqual(res["outcome"], "win", res)
        self.assertEqual([s["target"] for s in res["steps"]], ["Item2"])
        trap = [u for u in res["q"] if u[1] == "interact:Trap"]
        if trap:  # it tried the trap: it must have learned it kills
            self.assertTrue(all(u[2] < 0 and u[3] for u in trap))
        learn.record_exploration(seg["id"], seg, res, "test")
        q = learn.KB.load(seg["id"])["q"]
        state = next(k for k in q if "Beat2a" in k)
        self.assertEqual(learn.candidates({"objects": [{"name": "Trap", "dist": 1, "usable": True}], "beats": []},
                                          {"interact:Trap": {"n": 1, "value": -1.0}})[0].key == "interact:Trap", False)
        self.assertTrue(state)

    def test_11_learn_to_mastery_then_playtest_with_video(self):
        learn = self.ap.learn
        for seg in SEGMENTS:
            res = learn.learn_segment("bot", seg, attempts=10, streak=3, explore_budget=40, by="test", rng=__import__("random").Random(1))
            self.assertEqual(res["mastery"], "mastered", res)
        rec = learn.KB.load("fake/s0")
        best = learn.best_route(rec)
        self.assertEqual([s["op"] for s in best["steps"]], ["interact", "walk"])
        self.assertIn("when", best["steps"][0])
        self.assertTrue((learn.KB.root / "README.md").exists())
        report = learn.playtest("bot", SEGMENTS, record=Image is not None, by="test")
        self.assertEqual(report["passed"], 3, report)
        self.assertTrue(all(s["chained"] for s in report["segments"][1:]), "segments chain without jumps")
        self.assertEqual(report["quality"]["completion"], 1.0)
        folder = learn.PLAYTESTS / report["id"]
        html = (folder / "report.html").read_text(encoding="utf-8")
        self.assertNotIn("/*__REPORT_JSON__*/null", html)
        if Image is not None:
            meta = self.ap.video.find(report["videoId"])
            self.assertIn(meta["videoKind"], ("mp4", "avi"))
            self.assertTrue((self.ap.video.folder_of(meta) / meta["video"]).stat().st_size > 0)
            self.assertTrue((self.ap.video.folder_of(meta) / "captions.vtt").read_text(encoding="utf-8").startswith("WEBVTT"))
            self.assertTrue(any(c["title"].startswith("00 Fake") for c in meta["chapters"]), meta["chapters"])
            self.assertTrue(report["video"]["keyframes"][0]["url"].startswith("../../videos/"))
            type(self).playtest_video = meta["id"]

    def test_12_draft_to_route(self):
        learn, ap = self.ap.learn, self.ap
        seg = SEGMENTS[0]
        learn.send("bot", {"op": "stage", "n": 0}, 10)
        time.sleep(0.4)
        import argparse
        for cmd in ({"op": "interact", "target": "Lamp"}, {"op": "interact", "target": "Item0"}, {"op": "walk", "pos": [5, 0, 5]}):
            learn.cmd_step(argparse.Namespace(name="bot", json=json.dumps(cmd), why="test", segment=seg["id"]))
        lines = learn.draft_file("bot").read_text(encoding="utf-8").splitlines()
        ep = learn.run_episode("bot", seg, [{"op": "interact", "target": "Lamp"}], final_wait=1)
        self.assertEqual(ep["errors"], ["ERR @interact: the lamp sparks"], "new errors of the run are reported")
        self.assertEqual(len(lines), 3)
        self.assertFalse(json.loads(lines[0])["progressed"])
        learn.learn_from_draft("bot", seg, "test")
        rec = learn.KB.load(seg["id"])
        self.assertTrue(any(r["origin"] == "agent-draft" for r in rec["routes"]))

    def test_13_until_perfect(self):
        learn = self.ap.learn
        row = {"id": "s", "outcome": "pass", "chained": True, "deaths": 0, "errors": []}
        self.assertEqual(learn.imperfections({"segments": [dict(row, chained=False), row]}), [], "the first may be set up")
        why = learn.imperfections({"segments": [row, dict(row, chained=False), dict(row, deaths=1), dict(row, explored=True)]})
        self.assertEqual(len(why), 3, why)
        perfect, reports = learn.until_perfect("bot", SEGMENTS, runs=2, record=False, label="fake", by="test")
        self.assertIsNotNone(perfect, [r.get("imperfect") for r in reports])
        self.assertTrue(perfect["verdict"].startswith("PERFECT"), perfect["verdict"])
        self.assertEqual(perfect["series"]["run"], len(reports))

    def test_20_video_finalize_and_avi(self):
        if Image is None:
            self.skipTest("Pillow missing")
        video = self.ap.video
        src = self.tmp / "rec" / "manual-rec"
        (src / "frames").mkdir(parents=True)
        (src / "keyframes").mkdir()
        times = [0.0, 0.1, 0.2, 0.9, 1.0]
        for i, t in enumerate(times):
            img = Image.new("RGB", (64, 48), (20 * i, 100, 200))
            img.save(src / "frames" / ("f_%06d.jpg" % i))
            img.save(src / "keyframes" / ("k_%07d.jpg" % int(t * 1000)))
        (src / "frames.jsonl").write_text("".join(json.dumps({"i": i, "t": t}) + "\n" for i, t in enumerate(times)))
        (src / "events.jsonl").write_text(json.dumps({"t": 0.1, "kind": "op", "text": "walk pos=[1,2,3]"}) + "\n" +
                                          json.dumps({"t": 0.5, "kind": "result", "text": "walk FAILED: blocked"}) + "\n")
        (src / "recording.json").write_text(json.dumps({"id": "manual-rec", "sink": "jpeg", "fps": 10, "width": 64, "height": 48, "duration": 1.1}))
        meta = video.finalize(src, player="t")
        self.assertIn(meta["videoKind"], ("mp4", "avi"))
        vtt = (video.VIDEOS / meta["id"] / "captions.vtt").read_text(encoding="utf-8")
        self.assertIn("✖ walk FAILED", vtt)
        self.assertIn("▶ walk", vtt)
        avi = self.tmp / "t.avi"
        frames = sorted((video.VIDEOS / meta["id"] / "keyframes").glob("*.jpg"))
        video.write_avi(avi, frames, times, 10, 64, 48)
        data = avi.read_bytes()
        self.assertEqual(data[:4], b"RIFF")
        self.assertEqual(data[8:12], b"AVI ")
        self.assertEqual(int.from_bytes(data[4:8], "little"), len(data) - 8)
        if video.ffmpeg():
            probe = video.run([video.ffmpeg(), "-v", "error", "-i", avi, "-f", "null", "-"], 60)
            self.assertEqual(probe.returncode, 0, probe.stderr)
            mp4 = video.VIDEOS / meta["id"] / "video.mp4"
            probe = video.run([video.ffmpeg(), "-v", "error", "-i", mp4, "-f", "null", "-"], 60)
            self.assertEqual(probe.returncode, 0, probe.stderr)

    def test_21_raw_pipe_like_the_recorder(self):
        """The exact ffmpeg arguments the Unity recorder uses, fed raw RGBA frames, then finalize's remux."""
        video = self.ap.video
        ff = video.ffmpeg()
        if not ff:
            self.skipTest("no ffmpeg")
        out = self.tmp / "pipe" / "pipe-rec"
        out.mkdir(parents=True)
        w, h, fps = 64, 48, 15
        args = [ff, "-hide_banner", "-loglevel", "error", "-nostats", "-y", "-f", "rawvideo", "-pix_fmt", "rgba", "-s", "%dx%d" % (w, h),
                "-r", str(fps), "-i", "-", "-c:v", video.encoder(), *(["-preset", "veryfast", "-crf", "26", "-tune", "zerolatency"]
                                                                    if video.encoder() == "libx264" else ["-q:v", "5"]),
                "-pix_fmt", "yuv420p", "-g", str(fps * 2), "-movflags", "+frag_keyframe+empty_moov+default_base_moof", str(out / "video.mp4")]
        import subprocess
        proc = subprocess.Popen(args, stdin=subprocess.PIPE, stderr=subprocess.PIPE)
        for i in range(30):
            proc.stdin.write(bytes([i * 8 % 256, 80, 160, 255]) * (w * h))
        proc.stdin.close()
        self.assertEqual(proc.wait(60), 0, proc.stderr.read())
        (out / "events.jsonl").write_text(json.dumps({"t": 0.5, "kind": "mark", "text": "hello"}) + "\n")
        (out / "recording.json").write_text(json.dumps({"id": "pipe-rec", "sink": "ffmpeg", "fps": fps, "width": w, "height": h, "duration": 2.0}))
        meta = video.finalize(out, player="t")
        self.assertEqual(meta["videoKind"], "mp4")
        probe = video.run([ff, "-hide_banner", "-i", video.VIDEOS / meta["id"] / "video.mp4"], 60)
        self.assertIn("Subtitle: mov_text", probe.stderr, "captions are embedded")
        self.assertFalse(meta["notes"], meta["notes"])

    def test_30_http_api(self):
        from http.server import ThreadingHTTPServer
        web = self.ap.web
        server = ThreadingHTTPServer(("127.0.0.1", 0), web.Handler)
        threading.Thread(target=server.serve_forever, daemon=True).start()
        base = "http://127.0.0.1:%d" % server.server_address[1]
        try:
            def get(path, headers=None):
                req = urllib.request.Request(base + path, headers=headers or {})
                with urllib.request.urlopen(req, timeout=20) as r:
                    return r.status, r.headers, r.read()
            status, _, body = get("/api/status")
            self.assertEqual(json.loads(body)["tool"], "agentplay")
            segs = json.loads(get("/api/knowledge")[2])["segments"]
            self.assertEqual(len(segs), 3)
            full = json.loads(get("/api/knowledge/fake%2Fs0")[2])
            self.assertTrue(full["routeList"])
            players = json.loads(get("/api/players")[2])["players"]
            self.assertEqual(players[0]["state"], "running")
            req = urllib.request.Request(base + "/api/players/bot/send", data=json.dumps({"op": "status"}).encode(), method="POST")
            with urllib.request.urlopen(req, timeout=20) as r:
                self.assertIn("stage", json.loads(r.read()))
            self.assertIn(b"<html", get("/")[2][:200].lower())
            vid = getattr(type(self), "playtest_video", None)
            if vid:
                meta = json.loads(get("/api/videos/" + vid)[2])
                self.assertTrue(meta["keyframes"][0]["url"].startswith("/media/videos/"))
                if meta["videoUrl"]:
                    status, headers, body = get(meta["videoUrl"], {"Range": "bytes=0-99"})
                    self.assertEqual(status, 206)
                    self.assertEqual(len(body), 100)
                self.assertTrue(json.loads(get("/api/playtests")[2])["playtests"])
            with self.assertRaises(urllib.error.HTTPError) as caught:
                get("/media/videos/../../secret.txt")
            self.assertIn(caught.exception.code, (403, 404))
            with self.assertRaises(urllib.error.HTTPError) as caught:
                get("/api/status", {"Host": "evil.example"})
            self.assertEqual(caught.exception.code, 403)
        finally:
            server.shutdown()

    def test_31_share_link_is_keyed_and_view_only(self):
        """Through a tunnel (another Host, forwarding headers) the key is needed once, then a cookie; POST stays local."""
        from http.server import ThreadingHTTPServer
        import http.client
        web = self.ap.web
        web.SHARE.update(token="s3cret-key", control=False)
        server = ThreadingHTTPServer(("127.0.0.1", 0), web.Handler)
        threading.Thread(target=server.serve_forever, daemon=True).start()
        port = server.server_address[1]
        remote = {"Host": "abc.trycloudflare.com", "Cf-Connecting-Ip": "203.0.113.9", "Cf-Visitor": '{"scheme":"https"}'}

        def call(method, path, headers, body=None):
            c = http.client.HTTPConnection("127.0.0.1", port, timeout=20)
            c.request(method, path, body=body, headers=headers)
            r = c.getresponse()
            data = r.read()
            c.close()
            return r.status, r.getheader("Set-Cookie"), r.getheader("Location"), data
        try:
            self.assertEqual(call("GET", "/api/status", remote)[0], 401, "no key, no entry")
            self.assertEqual(call("GET", "/?key=wrong", remote)[0], 401)
            status, cookie, location, _ = call("GET", "/media/x?key=s3cret-key&t=1", remote)
            self.assertEqual(status, 302)
            self.assertEqual(location, "/media/x?t=1", "the key leaves the address bar")
            self.assertIn("HttpOnly", cookie)
            self.assertIn("Secure", cookie)
            keyed = dict(remote, Cookie=cookie.split(";")[0])
            status, _, _, body = call("GET", "/api/status", keyed)
            self.assertEqual((status, json.loads(body)["tool"]), (200, "agentplay"))
            status, _, _, body = call("POST", "/api/players/bot/send", dict(keyed, **{"Content-Type": "application/json"}), b'{"op":"status"}')
            self.assertEqual(status, 403, "a share link is view only")
            self.assertEqual(json.loads(call("GET", "/api/status", keyed)[3])["viewer"], "remote")
            local = {"Host": "127.0.0.1:%d" % port}
            self.assertEqual(json.loads(call("GET", "/api/status", local)[3])["viewer"], "local")
            phone = dict(keyed, **{"User-Agent": "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) Mobile/15E148 Safari/604.1"})
            self.assertIn(b'The phone app of the agentplay dashboard', call("GET", "/", phone)[3], "a phone gets the phone app")
            self.assertNotIn(b'The phone app of the agentplay dashboard', call("GET", "/?desktop=1", phone)[3], "?desktop=1 asks for the desktop page")
            self.assertIn(b'The phone app of the agentplay dashboard', call("GET", "/m", keyed)[3])
            self.assertEqual(call("GET", "/api/status", local)[0], 200, "the local user needs no key")
            web.SHARE["token"] = None
            self.assertEqual(call("GET", "/api/status", remote)[0], 403, "without sharing, remote requests are refused")
        finally:
            web.SHARE.update(token=None, control=False)
            server.shutdown()


if __name__ == "__main__":
    unittest.main(verbosity=2)
