"""Learning playtests for agentplay.py: bots that learn to play the game by trial and error, keep what they learned in a
shared knowledge base (so the next agent starts from it instead of learning again), and play whole chunks of the game
with what they mastered while recording a video and writing a quality report. Docs: AGENT_PLAY.md, skill agent-playtest.

  agentplay.py kb brief [--scope S]                 START HERE: what is mastered, what is not, lessons, what to do next
  agentplay.py learn NAME --scope S [--workers 3]   learn segments until mastered (explore, replay, repair, optimize)
  agentplay.py playtest NAME --scope S [--learn]    play the scope with the learned routes, record it, write a report
  agentplay.py playtest NAME --scope S --until-perfect 8   whole chained runs, learning between them, until one is perfect
  agentplay.py observe NAME                         what the bot sees, what the story waits for, suggested actions
  agentplay.py step NAME '{"op":"interact","target":"Kettle"}' [--why ...]   one exploration step, kept in the draft
  agentplay.py learn NAME --segment ID --from-draft turn your draft into a verified route
  agentplay.py kb status|show|lesson|route|retire|reset ...

How it learns. The game is cut into segments (a project gives them: id, setup commands that put the game at the
segment's start, a goal condition, a time budget). A route is a list of harness commands that plays one segment.
- Explore: with no route, the autopilot reads `observe` (what the running story steps reference, usable objects near,
  text on screen) and tries actions, scored by a value table per story state (state = the stage and the running story
  steps). An action that moves the story on is kept, gated on the state it worked in ("when"); one that does nothing or
  kills the player is remembered as bad for that state. Values persist, so later explorations skip known mistakes.
- Replay: a route is replayed from the segment's start, with human-like noise (reaction delays, look jitter) after its
  first run, so only robust routes count. The route to replay is chosen by UCB1 among the route variants.
- Repair: a failed step makes variants (detour around a block, open the door in the way, approach first, wait for the
  object to become usable, longer timeouts, drop the step, wait out a hazard); the bad ones retire, the good ones win.
- Mastery: a route that wins `--streak` times in a row (default 3) masters the segment; then optimization variants
  (no fixed waits, one step fewer) compete to make it faster. A mastered route that later fails marks the segment
  "stale": a regression in the game, or a route the game has outgrown.
- Until perfect: a segment mastered alone can still fail when the run before it leaves the game a little differently
  (an NPC a step further, a door already open). `playtest --until-perfect N` plays the whole scope chained (no story
  jumps), again and again: every segment picks its route by win-stay/UCB, a chained failure adds a lesson and repair
  variants (or explores on from where the run left the game), the run carries on by jumping, and the next run tries the
  repairs. It stops at the first perfect run: every segment passed, chained, no deaths, no new errors.
- What the autopilot cannot solve is marked needs-agent with a diagnosis; an agent then plays it by hand with
  observe/step and saves the route with --from-draft. Every outcome, lesson and route is shared through the knowledge
  base (agentplay.json "knowledgeDir", default AgentPlayKnowledge/, meant to be committed).
Installed from the Agentic Tools package (Install~/files/tools/agentplay): change it there, not in a project.
"""
import json
import math
import os
import queue
import random
import re
import shutil
import sys
import threading
import time
import uuid
from contextlib import contextmanager
from pathlib import Path

ap = None     # the agentplay module, set by register()
video = None  # agentplay_video
KB = None
EPISODES = PLAYTESTS = None
MASTER_STREAK = 3
UNLEARNED_WAIT = 60  # a playtest waits this long for a segment nobody has learned before it jumps on
MAX_LIVE_ROUTES = 8
MAX_ROUTES = 16      # live and retired; the oldest retired ones are dropped beyond this
REPAIR_BELOW = 6     # failed routes make repair variants only while fewer routes than this are live
HISTORY_KEEP = 40
SCHEMA = 1
STATE_FIELDS = ("scene", "stage", "running")
META_KEYS = {"why", "expect", "expect_timeout", "optional", "when", "when_timeout", "note"}
OP_TIMEOUT = {"walk": 45, "approach": 45, "interact": 50, "aim": 12, "key": 10, "stage": 60, "scene": 150, "teleport": 15,
              "look": 10, "click": 10, "observe": 30, "status": 30, "hold": 10, "release": 10, "point": 60}
WAIT_CHANGE = {"op": "until", "cond": {"any": [{"field": "running", "changed": True}, {"field": "stage", "changed": True},
                                               {"field": "held", "changed": True}]}, "timeout": 8, "soft": True}
HINT_FIELD = re.compile(r"target|zone|receiver|destination|dest|area|spot|point|door|item|pickup|interact|button|switch|"
                        r"lever|seat|bed|marker|goal|place|where|location|trigger|object|prop|use|give|take", re.I)
_print_lock = threading.Lock()
_provider = None
_scope_fn = None
_validate = None
_validated = set()


def log(text, who=""):
    with _print_lock:
        print(("[%s] " % who if who else "") + text, flush=True)


def now_text():
    return time.strftime("%Y-%m-%d %H:%M:%S")


# ------------------------------------------------------------------ segments (given by the project)

def set_segment_provider(segments, scope=None, validate=None):
    """A project's agentplay_project.py calls this in register(): `segments()` returns every segment in play order as
    dicts {id, label, order, setup:[commands], goal:condition, budget:seconds, at:condition (optional: "the game is
    at this segment's start", lets a playtest chain segments without jumping)}; `scope(text)` resolves a scope;
    `validate(status)` returns an error text when a running player does not match the segments (a stale export)."""
    global _provider, _scope_fn, _validate
    _provider, _scope_fn, _validate = segments, scope, validate


def all_segments():
    if _provider is not None:
        return _provider()
    path = Path(ap.__file__).resolve().parent / "segments.json"
    if path.exists():
        return json.loads(path.read_text(encoding="utf-8"))["segments"]
    ap.fail("this project defines no segments: add tools/agentplay/segments.json or set_segment_provider() in "
            "agentplay_project.py (see AGENT_PLAY.md, Learning playtests)", 2)


def resolve(scope):
    """Segments a scope names, in play order. Project resolver first; else ids, id prefixes, order numbers and a-b."""
    if _scope_fn is not None:
        return _scope_fn(scope)
    segs = all_segments()
    if scope in ("all", "*"):
        return segs
    chosen = []
    for part in [p.strip() for p in scope.split(",") if p.strip()]:
        m = re.fullmatch(r"(\d+)(?:-(\d+))?", part)
        if m:
            lo, hi = int(m.group(1)), int(m.group(2) or m.group(1))
            hits = [s for s in segs if lo <= s.get("order", -1) <= hi]
        else:
            hits = [s for s in segs if s["id"] == part] or [s for s in segs if s["id"].startswith(part.rstrip("/") + "/")]
        if not hits:
            ap.fail("scope item '%s' matches no segment (agentplay.py segments lists them)" % part)
        chosen += [s for s in hits if s not in chosen]
    return sorted(chosen, key=lambda s: s.get("order", 0))


def segment(seg_id):
    for s in all_segments():
        if s["id"] == seg_id:
            return s
    hits = [s for s in all_segments() if seg_id in s["id"]]
    if len(hits) == 1:
        return hits[0]
    ap.fail("segment '%s' %s" % (seg_id, "is ambiguous: " + ", ".join(s["id"] for s in hits[:8]) if hits else "does not exist"))


# ------------------------------------------------------------------ the knowledge base

class Knowledge:
    """One JSON file per segment under <knowledgeDir>/segments, plus global.json; every change happens under a file lock
    and re-reads the file first, so parallel agents and workers merge instead of overwriting each other."""

    def __init__(self, root):
        self.root = Path(root)
        self.seg_dir = self.root / "segments"

    @staticmethod
    def slug(seg_id):
        return re.sub(r"[^A-Za-z0-9_.-]+", "__", seg_id)

    def file(self, seg_id):
        return self.seg_dir / (self.slug(seg_id) + ".json")

    def blank(self, seg_id, seg=None):
        seg = seg or {}
        return {"schema": SCHEMA, "id": seg_id, "label": seg.get("label", seg_id), "order": seg.get("order", 0),
                "mastery": "unknown", "best": None, "routes": [], "q": {}, "lessons": [], "history": [],
                "stats": {"runs": 0, "wins": 0, "deaths": 0, "blocked": 0, "explorations": 0, "firstWinAfter": None},
                "needsAgent": None, "showcase": None, "updatedAt": None, "updatedBy": None}

    def load(self, seg_id, seg=None):
        try:
            rec = json.loads(self.file(seg_id).read_text(encoding="utf-8"))
        except (OSError, ValueError):
            return self.blank(seg_id, seg)
        base = self.blank(seg_id, seg)
        base.update(rec)
        if seg:
            base["label"], base["order"] = seg.get("label", base["label"]), seg.get("order", base["order"])
        return base

    def save(self, rec):
        self.seg_dir.mkdir(parents=True, exist_ok=True)
        self.readme()
        atomic_write(self.file(rec["id"]), json.dumps(rec, indent=1, ensure_ascii=False) + "\n")

    @contextmanager
    def lock(self, name):
        self.seg_dir.mkdir(parents=True, exist_ok=True)
        path = self.seg_dir / (self.slug(name) + ".lock")
        until = time.time() + 60
        while True:
            try:
                fd = os.open(str(path), os.O_CREAT | os.O_EXCL | os.O_WRONLY)
                os.write(fd, ("%d %d" % (os.getpid(), time.time())).encode())
                os.close(fd)
                break
            except FileExistsError:
                try:
                    if time.time() - path.stat().st_mtime > 60:
                        path.unlink(missing_ok=True)  # a crashed writer's lock
                        continue
                except OSError:
                    continue
                if time.time() > until:
                    ap.fail("knowledge base lock %s is held for over a minute" % path)
                time.sleep(0.05)
        try:
            yield
        finally:
            path.unlink(missing_ok=True)

    def update(self, seg_id, fn, seg=None, by=None):
        with self.lock(seg_id):
            rec = self.load(seg_id, seg)
            result = fn(rec)
            rec["mastery"] = mastery_of(rec)
            rec["best"] = (best_route(rec) or {}).get("id")
            rec["updatedAt"], rec["updatedBy"] = now_text(), by or rec.get("updatedBy")
            self.save(rec)
            return result

    def records(self):
        out = []
        for path in sorted(self.seg_dir.glob("*.json")) if self.seg_dir.exists() else []:
            try:
                out.append(json.loads(path.read_text(encoding="utf-8")))
            except (OSError, ValueError):
                pass
        return out

    def global_file(self):
        return self.root / "global.json"

    def load_global(self):
        try:
            return json.loads(self.global_file().read_text(encoding="utf-8"))
        except (OSError, ValueError):
            return {"schema": SCHEMA, "lessons": []}

    def update_global(self, fn):
        with self.lock("_global"):
            g = self.load_global()
            fn(g)
            self.root.mkdir(parents=True, exist_ok=True)
            atomic_write(self.global_file(), json.dumps(g, indent=1, ensure_ascii=False) + "\n")

    def readme(self):
        path = self.root / "README.md"
        if not path.exists():
            self.root.mkdir(parents=True, exist_ok=True)
            path.write_text(KB_README, encoding="utf-8")


KB_README = """# Agent play knowledge base

What agents learned about playing this game, shared by every agent and machine (commit it). Written by
`tools/agentplay/agentplay.py learn / playtest / kb` (Agentic Tools package); read it with `agentplay.py kb brief`.
Do not hand-edit while a learning run is going (files are locked per segment while they change).

- `segments/<segment>.json`: one per segment (a stretch of play with a start and a goal).
  - `routes`: replayable command lists that play the segment, with their runs, wins, current win streak and times.
    `best` is the one to use. A step may have `when` (wait until the story is in the state the step was learned in),
    `expect` (a condition that must hold after it), `optional`, `why`.
  - `mastery`: unknown, learning, competent (won at least once), mastered (best route won `streak` times in a row,
    with human-like noise), stale (a mastered route failed later: the game changed), needs-agent (the autopilot gave up;
    `needsAgent` holds the diagnosis).
  - `q`: action values per story state (+ moved the story on, - did nothing or killed the player). Explorations start
    from them, so a mistake is made once.
  - `lessons`: what agents and the learner noticed (tips, pitfalls, game bugs, fixes).
  - `history`: the latest runs (who, route, outcome, why it failed, time, video).
- `global.json`: lessons that hold for the whole game (controls, conventions).
"""


def atomic_write(path, text):
    path = Path(path)
    tmp = path.with_suffix(path.suffix + ".%s.tmp" % uuid.uuid4().hex[:6])
    tmp.write_text(text, encoding="utf-8")
    for _ in range(50):
        try:
            os.replace(tmp, path)
            return
        except PermissionError:  # a reader on Windows; try again
            time.sleep(0.05)
    os.replace(tmp, path)


# ------------------------------------------------------------------ routes, stats, mastery

def live_routes(rec):
    return [r for r in rec.get("routes", []) if not r.get("retired")]


def win_rate(r):
    return (r.get("wins", 0) + 1.0) / (r.get("runs", 0) + 2.0)


def best_route(rec):
    live = [r for r in live_routes(rec) if r.get("wins", 0) > 0] or live_routes(rec)
    if not live:
        return None
    def rank(r):
        if r.get("streak", 0) >= MASTER_STREAK:
            return (1, -(r.get("bestTime") or 1e9), win_rate(r))
        return (0, win_rate(r), r.get("streak", 0))
    return max(live, key=rank)


def mastery_of(rec):
    if not rec.get("routes"):
        return "needs-agent" if rec.get("needsAgent") else "unknown"
    best = best_route(rec)
    if best and best.get("streak", 0) >= MASTER_STREAK:
        return "mastered"
    if any(r.get("wins", 0) > 0 for r in rec["routes"]):
        if best and best.get("wasMastered") and best.get("lastOutcome") == "fail":
            return "stale"
        return "competent"
    return "needs-agent" if rec.get("needsAgent") else "learning"


def friction(rec):
    """0..1: how hard the segment was to learn (tries before the first win, deaths, blocked walks per run)."""
    st = rec.get("stats", {})
    runs = max(1, st.get("runs", 0))
    tries = st.get("firstWinAfter")
    if tries is None:
        tries = st.get("runs", 0) + st.get("explorations", 0)
    x = 0.35 * tries + 2.0 * st.get("deaths", 0) / runs + 0.5 * st.get("blocked", 0) / runs
    return round(1 - math.exp(-x), 3)


def steps_signature(steps):
    return json.dumps(steps, sort_keys=True)


def add_route(rec, steps, origin, by, parent=None, why=None):
    """Adds a route unless an identical one exists. Returns its id (the existing one's for a duplicate)."""
    sig = steps_signature(steps)
    for r in rec["routes"]:
        if steps_signature(r["steps"]) == sig:
            return r["id"]
    rid = "r%d" % (1 + max([int(r["id"][1:]) for r in rec["routes"] if r["id"][1:].isdigit()] + [0]))
    rec["routes"].append({"id": rid, "parent": parent, "origin": origin, "createdBy": by, "createdAt": now_text(),
                          "build": current_build(), "why": why, "steps": steps, "runs": 0, "wins": 0, "streak": 0,
                          "bestTime": None, "times": [], "lastOutcome": None, "retired": False})
    live = live_routes(rec)
    if len(live) > MAX_LIVE_ROUTES:  # retire the weakest tried one
        tried = [r for r in live if r.get("runs", 0) > 0 and r is not best_route(rec)]
        if tried:
            min(tried, key=win_rate)["retired"] = True
    retired = [r for r in rec["routes"] if r.get("retired")]
    for old in retired[:max(0, len(rec["routes"]) - MAX_ROUTES)]:  # forget the oldest losers; their lessons stay
        rec["routes"].remove(old)
    return rid


def apply_episode(rec, rid, ep):
    r = next((x for x in rec["routes"] if x["id"] == rid), None)
    win = ep["outcome"] == "win"
    if r is not None:
        r["runs"] = r.get("runs", 0) + 1
        r["lastOutcome"] = "win" if win else "fail"
        if win:
            r["wins"] = r.get("wins", 0) + 1
            r["streak"] = r.get("streak", 0) + 1
            r["times"] = (r.get("times", []) + [ep["time"]])[-10:]
            r["bestTime"] = min(r["times"])
            r["lastWinBuild"] = current_build()
            if r["streak"] >= MASTER_STREAK:
                r["wasMastered"] = True
        else:
            r["streak"] = 0
            r["lastFail"] = {"kind": ep.get("failKind"), "step": ep.get("failStep"), "detail": ep.get("failDetail", "")[:200]}
            if r["runs"] >= 4 and r.get("wins", 0) == 0:
                r["retired"] = True
    st = rec["stats"]
    st["runs"] = st.get("runs", 0) + 1
    st["deaths"] = st.get("deaths", 0) + ep.get("deaths", 0)
    st["blocked"] = st.get("blocked", 0) + sum(1 for s in ep.get("steps", []) if s.get("kind") == "blocked")
    if win:
        st["wins"] = st.get("wins", 0) + 1
        if st.get("firstWinAfter") is None:
            st["firstWinAfter"] = st["runs"] - 1 + st.get("explorations", 0)
        rec["needsAgent"] = None
        start = parse_pos(ep.get("startPlayer")) if ep.get("setup") else None
        if start:
            rec["startPos"] = start  # where the segment starts when played alone (and wins from there)
    rec["history"] = (rec.get("history", []) + [{
        "at": ep["at"], "by": ep["by"], "route": rid, "outcome": "win" if win else "fail", "failKind": ep.get("failKind"),
        "failStep": ep.get("failStep"), "time": ep["time"], "video": ep.get("video"), "build": current_build(),
        "noise": ep.get("noise", False), "episode": ep["id"]}])[-HISTORY_KEEP:]


def parse_pos(value):
    """[x, y, z] from a status position ("x,y,z" or a list), else None."""
    try:
        parts = value.split(",") if isinstance(value, str) else list(value)
        pos = [round(float(p), 2) for p in parts]
        return pos if len(pos) == 3 else None
    except (TypeError, ValueError, AttributeError):
        return None


def handoff_repair(rec, route, ep):
    """A segment that wins played alone can fail chained because the segment before leaves the player somewhere else
    (under the chandelier that falls next, in the girlfriend's way). The repair: first walk to where the segment starts
    when played alone. Tried before the other repairs (prior)."""
    start, here = rec.get("startPos"), parse_pos((ep.get("statusEnd") or {}).get("player"))
    if not start:
        return None
    first = route["steps"][0] if route["steps"] else {}
    if first.get("op") == "walk" and first.get("pos") == start:
        return None  # it already does
    if here and math.dist(start, here) < 0.6 and ep.get("failKind") != "death":
        return None  # it was already there
    walk = {"op": "walk", "pos": start, "tolerance": 0.5, "timeout": 15, "optional": True,
            "why": "start where this segment starts when played alone (it wins from there): the segment before left the "
                   "player elsewhere"}
    return ("repair:handoff:start-where-it-wins", [walk] + route["steps"], "start where it starts alone")


def make_room(rec, wanted, keep=None):
    """Retires live routes that never won after two runs or more (the most tried first), so new repairs fit."""
    losers = sorted([x for x in live_routes(rec) if x.get("runs", 0) >= 2 and not x.get("wins") and x["id"] != keep],
                    key=lambda x: -x.get("runs", 0))
    room = REPAIR_BELOW - len(live_routes(rec))
    for x in losers[:max(0, wanted - room)]:
        x["retired"] = True


def add_lesson(rec, text, kind, by, auto=False):
    for lesson in rec.setdefault("lessons", []):
        if lesson["text"] == text:
            lesson["seen"] = lesson.get("seen", 1) + 1
            lesson["at"] = now_text()
            return
    rec["lessons"].append({"text": text, "kind": kind, "by": by, "at": now_text(), "auto": auto, "seen": 1})
    rec["lessons"] = rec["lessons"][-60:]


def merge_q(rec, updates):
    """updates: [(state, action, reward, died)] -> running means per state and action."""
    q = rec.setdefault("q", {})
    for state, action, reward, died in updates:
        cell = q.setdefault(state, {}).setdefault(action, {"n": 0, "value": 0.0})
        cell["n"] += 1
        cell["value"] = round(cell["value"] + (reward - cell["value"]) / min(cell["n"], 5), 3)
        if died:
            cell["deaths"] = cell.get("deaths", 0) + 1
    if len(q) > 400:  # keep the most used states
        for key in sorted(q, key=lambda k: sum(c["n"] for c in q[k].values()))[:len(q) - 400]:
            del q[key]


_build_name = None


def current_build():
    global _build_name
    if _build_name is None:
        b = ap.latest_build()
        _build_name = b.name if b else "none"
    return _build_name


# ------------------------------------------------------------------ talking to the player

def send(name, cmd, timeout=60):
    return ap.send(name, cmd, timeout)[1]


def status(name):
    return send(name, {"op": "status"}, 30)


def check(name, cond, wait=0.0):
    """(met, status) for a condition, now or within `wait` seconds."""
    a = send(name, {"op": "until", "cond": cond, "timeout": wait, "soft": True, "poll": 0.2}, wait + 40)
    if not a.get("ok"):
        return False, a.get("status") or {}
    return bool(a.get("met")), a.get("status") or {}


def observe(name, radius=14):
    return send(name, {"op": "observe", "radius": radius, "max": 30}, 40)


def op_timeout(cmd):
    op = cmd.get("op")
    if op == "wait":
        return float(cmd.get("s", 1)) + 20
    if op == "until":
        return float(cmd.get("timeout", 10)) + 20
    return OP_TIMEOUT.get(op, 40) + float(cmd.get("timeout", 0) or 0)


def classify(cmd, ans):
    """(failure kind or None, detail) for one command's answer."""
    op = cmd.get("op")
    if not ans.get("ok"):
        err = ans.get("error") or ""
        e = err.lower()
        if "no answer within" in e:
            return "hang", err
        if "player exited" in e or "is not running" in e:
            return "crash", err
        if "unknown op" in e or "unknown key" in e or "unknown condition" in e or "needs a" in e:
            return "bad-step", err
        if op == "until" or "condition not met" in e:
            return "timeout", err
        if "crosshair" in e:
            return "no-target", err
        if "no active object" in e:
            return "missing", err
        if "does not follow" in e:
            return "no-control", err
        if "did not get there" in e or "blocked" in e:
            return "blocked", err
        return "error", err
    if op in ("walk", "approach") and ans.get("reached") is False:
        why = str(ans.get("why", ""))
        return ("blocked" if why.startswith("blocked") else "timeout"), why
    return None, ""


def trim(answer, limit=600):
    out = {k: v for k, v in answer.items() if k not in ("status", "id", "issues", "objects", "texts", "beats")}
    if isinstance(out.get("trail"), list):  # a walk: keep where it ended up (a death, a block) and the corners
        out["trail"] = out["trail"][-4:]
        if isinstance(out.get("path"), list):
            out["path"] = out["path"][:10]
    text = json.dumps(out)
    return out if len(text) <= limit else {"ok": answer.get("ok"), "truncated": text[:limit]}


def issue_counts(name):
    counts = {}
    for line in send(name, {"op": "errors"}, 30).get("issues") or []:
        m = re.match(r"(\w+) x(\d+) (.*)$", line)
        if m:
            counts[m.group(1) + " " + m.group(3)] = int(m.group(2))
    return counts


def new_issues(before, after):
    return [key for key, n in after.items() if n > before.get(key, 0) and key.split(" ", 1)[0] in ("FATAL", "ERR", "STALL")]


def state_key(st):
    parts = []
    for f in STATE_FIELDS:
        v = st.get(f)
        if v is None:
            continue
        if isinstance(v, list):
            v = "+".join(sorted(str(x) for x in v))
        parts.append("%s=%s" % (f, v))
    return "|".join(parts)


def micro_key(obs):
    """A finer state: also the held item and which nearby objects are usable (not screen text: interaction prompts
    change with every drawer opened, which is not progress)."""
    st = obs.get("status") or {}
    usable = sorted(o["name"] for o in obs.get("objects") or [] if o.get("usable") and o.get("dist", 99) < 8)[:12]
    return state_key(st) + "|held=%s|use=%s" % (st.get("held"), ",".join(usable))


def holds(cond, st):
    """Evaluates a condition on a status answer here, without the player: True, False, or None when it needs the player
    (object, text, log or project keys, `changed`)."""
    if isinstance(cond, list):
        parts = [holds(c, st) for c in cond]
        return None if None in parts else all(parts)
    if not isinstance(cond, dict):
        return cond if isinstance(cond, bool) else None
    result = True
    for key, arg in cond.items():
        if key in ("eq", "ne", "gt", "gte", "lt", "lte", "contains", "in", "exists", "within", "why", "label"):
            continue
        if key == "all":
            parts = [holds(c, st) for c in arg]
            if False in parts:
                return False
            if None in parts:
                return None
        elif key == "any":
            parts = [holds(c, st) for c in arg]
            if True not in parts:
                if None in parts:
                    return None
                result = False
        elif key == "not":
            inner = holds(arg, st)
            if inner is None:
                return None
            result = result and not inner
        elif key == "field":
            if "changed" in cond:
                return None
            v = st.get(arg)
            same = lambda a, b: (str(a).lower() == str(b).lower()) if not isinstance(a, (int, float)) or isinstance(a, bool) else a == b
            try:
                if "eq" in cond and not same(v, cond["eq"]):
                    result = False
                if "ne" in cond and same(v, cond["ne"]):
                    result = False
                for op, fn in (("gt", lambda x, y: x > y), ("gte", lambda x, y: x >= y), ("lt", lambda x, y: x < y), ("lte", lambda x, y: x <= y)):
                    if op in cond and not (isinstance(v, (int, float)) and fn(v, cond[op])):
                        result = False
            except TypeError:
                return None
            if "contains" in cond:
                needle = str(cond["contains"]).lower()
                if isinstance(v, list):
                    found = any(str(x).lower() == needle or needle in str(x).lower() for x in v)
                else:
                    found = v is not None and needle in str(v).lower()
                result = result and found
            if "in" in cond and not any(same(v, o) for o in cond["in"]):
                result = False
            if "exists" in cond and (v is not None) != bool(cond["exists"]):
                result = False
            if not any(k in cond for k in ("eq", "ne", "gt", "gte", "lt", "lte", "contains", "in", "exists")):
                result = result and bool(v) and v != "none"
        else:
            return None
    return result


def gate_for(st, previous_running):
    """The 'when' of a learned step: the story steps that appeared just before it worked (else all running), or the stage."""
    running = st.get("running") if isinstance(st.get("running"), list) else []
    fresh = sorted(set(running) - set(previous_running or []))
    picked = (fresh or sorted(running))[:2]
    conds = [{"field": "running", "contains": b} for b in picked]
    if not conds and "stage" in st:
        conds = [{"field": "stage", "eq": st["stage"]}]
    return {"all": conds} if conds else None


# ------------------------------------------------------------------ one episode: play a route once

def run_episode(name, seg, steps, setup=True, noise=False, rng=None, record=False, label=None, by="agent", budget=None,
                who="", recording_extra=None, final_wait=None):
    """Plays `steps` from the segment's start. Returns the episode record (also saved under Builds/AgentPlay/episodes)."""
    rng = rng or random.Random()
    budget = float(budget or seg.get("budget", 150))
    ep = {"id": time.strftime("%Y%m%d-%H%M%S-") + uuid.uuid4().hex[:4], "segment": seg["id"], "at": now_text(), "by": by,
          "player": name, "noise": noise, "outcome": "fail", "failKind": None, "failStep": None, "failDetail": "",
          "steps": [], "time": None, "deaths": 0, "errors": [], "video": None, "build": current_build()}
    if setup:
        for cmd in seg.get("setup", []):
            a = send(name, cmd, op_timeout(cmd))
            if not a.get("ok"):
                ep.update(failKind="setup", failDetail=a.get("error", "setup failed"))
                return save_episode(ep)
    before = issue_counts(name)
    send(name, {"op": "perf", "reset": True}, 30)
    st0 = status(name)
    deaths0 = st0.get("deaths", 0) or 0
    ep["setup"] = bool(setup and seg.get("setup"))
    ep["startPlayer"] = st0.get("player")
    if record:
        a = video.start(name, label or seg.get("label", seg["id"]))
        if not a.get("ok"):
            log("recording did not start: %s" % a.get("error"), who)
            record = False
    t0 = time.time()
    deadline = t0 + budget
    if noise:
        send(name, {"op": "look", "dx": rng.uniform(-60, 60), "dy": rng.uniform(-15, 15), "frames": 6}, 20)
    won, failed = False, False
    met, st = check(name, seg["goal"])
    won = met
    i = 0
    while i < len(steps) and not (won or failed) and time.time() < deadline:
        step = steps[i]
        if noise:
            time.sleep(rng.uniform(0.05, 0.6))
        row_t = round(time.time() - t0, 2)
        kind, detail, a = None, "", {}
        if step.get("when"):
            # A route is (story state -> action) pairs: wait for this step's state, but if the story is already in a later
            # step's state (an earlier action did more than expected), jump there instead of waiting for the past.
            ahead = [j for j in range(i, min(len(steps), i + 6)) if steps[j].get("when")]
            wait = min(float(step.get("when_timeout", 45)), max(1.0, deadline - time.time()))
            a = send(name, {"op": "until", "cond": {"any": [steps[j]["when"] for j in ahead] + [seg["goal"]]}, "timeout": wait,
                            "soft": True}, wait + 40)
            st = a.get("status") or st
            if not a.get("met"):
                kind, detail = "when-timeout", "the story never reached the state this step was learned in: %s" % json.dumps(step["when"])[:200]
            elif holds(seg["goal"], st) is not False and check(name, seg["goal"])[0]:
                won = True
                break
            else:
                hit = next((j for j in ahead if holds(steps[j]["when"], st)), i)
                for j in range(i, hit):
                    ep["steps"].append({"i": j, "op": steps[j].get("op"), "cmd": steps[j], "t": row_t, "ok": True,
                                        "kind": "skipped", "detail": "the story was already past this step's state"})
                i, step = hit, steps[hit]
        cmd = {k: v for k, v in step.items() if k not in META_KEYS}
        row = {"i": i, "op": cmd.get("op"), "cmd": cmd, "t": row_t, "ok": True, "kind": None}
        state_before = state_key(st)
        if kind is None:
            a = send(name, cmd, op_timeout(cmd))
            kind, detail = classify(cmd, a)
            if kind is None and step.get("expect"):
                e = send(name, {"op": "until", "cond": step["expect"], "timeout": float(step.get("expect_timeout", 4)), "soft": True}, 60)
                if not e.get("met"):
                    kind, detail = "expect", "expected %s" % json.dumps(step["expect"])[:200]
        met, st = check(name, seg["goal"])
        if kind in ("blocked", "timeout", "no-target", "expect") and state_key(st) != state_before:
            detail, kind = "%s, but the story moved on: counts as done" % kind, None  # e.g. walked into the NPC it had to reach
        row.update(ok=kind is None, kind=kind, detail=str(detail)[:300], dt=round(time.time() - t0 - row["t"], 2),
                   stage=st.get("stage"), state=state_key(st), result=trim(a))
        ep["steps"].append(row)
        if (st.get("deaths", deaths0) or 0) > deaths0 and not met:
            ep.update(failKind="death", failStep=i, failDetail="the player died during or after this step")
            failed = True
            break
        if met:
            won = True
            break
        if kind in ("crash", "hang", "bad-step") or (kind and not step.get("optional")):
            ep.update(failKind=kind, failStep=i, failDetail=str(detail)[:300])
            failed = True
        i += 1
    if not won and not failed:
        remaining = max(3.0, deadline - time.time())
        if final_wait is not None:
            remaining = min(remaining, final_wait)
        a = send(name, {"op": "until", "cond": seg["goal"], "timeout": remaining, "soft": True}, remaining + 40)
        st = a.get("status") or st
        if a.get("met"):
            won = True
        elif (st.get("deaths", deaths0) or 0) > deaths0:
            ep.update(failKind="death", failStep=len(steps), failDetail="the player died while waiting for the goal")
        else:
            ep.update(failKind="goal-timeout", failStep=len(steps),
                      failDetail="all %d steps ran; the goal did not happen within the budget (state: %s)" % (len(steps), state_key(st)))
    ep["time"] = round(time.time() - t0, 2)
    ep["outcome"] = "win" if won else "fail"
    ep["deaths"] = max(0, (st.get("deaths", deaths0) or 0) - deaths0)
    ep["statusEnd"] = {k: st.get(k) for k in ("stage", "chapter", "checkpoint", "running", "held", "deaths", "player") if k in st}
    ep["perf"] = trim(send(name, {"op": "perf"}, 30))
    try:
        ep["errors"] = new_issues(before, issue_counts(name))
    except SystemExit:
        pass
    if record:
        meta = video.stop(name, extra=dict({"segment": seg["id"], "episode": ep["id"]}, **(recording_extra or {})))
        ep["video"] = meta["id"]
    return save_episode(ep)


def save_episode(ep):
    folder = EPISODES / Knowledge.slug(ep["segment"])
    folder.mkdir(parents=True, exist_ok=True)
    atomic_write(folder / (ep["id"] + ".json"), json.dumps(ep, indent=1))
    return ep


# ------------------------------------------------------------------ exploring: the autopilot

class Act:
    def __init__(self, key, steps, prior, kind="act"):
        self.key, self.steps, self.prior, self.kind, self.score = key, steps, prior, kind, prior


def object_refs(value):
    if isinstance(value, dict) and "obj" in value:
        yield value
    elif isinstance(value, list):
        for item in value:
            yield from object_refs(item)


def candidates(obs, q_state=None, tried=(), rng=None):
    """Things to try now, best first: what the running story steps point at, usable objects near, or waiting."""
    rng = rng or random.Random()
    objects = obs.get("objects") or []
    by_name = {o["name"]: o for o in objects}
    beats = obs.get("beats") or []
    acts, hinted = {}, set()

    def add(act):
        if act.key not in acts or acts[act.key].prior < act.prior:
            acts[act.key] = act

    hint_count = 0
    for beat in beats:
        for field, value in (beat.get("fields") or {}).items():
            for ref in object_refs(value):
                if not ref.get("active", True):
                    continue
                name = ref["obj"]
                hinted.add(name)
                hint_count += 1
                weight = 1.0 if HINT_FIELD.search(field) else 0.55
                why = "%s.%s" % (beat.get("type", "beat"), field)
                usable = by_name.get(name) if by_name.get(name, {}).get("usable") else next(
                    (o for o in objects if o.get("usable") and name in o.get("path", "")), None)
                if usable:
                    add(Act("interact:" + usable["name"], [{"op": "interact", "target": usable["name"], "why": "story hint " + why}], 0.92 * weight))
                if ref.get("pos"):
                    add(Act("goto:" + name, [{"op": "walk", "pos": ref["pos"], "timeout": 40, "why": "story hint " + why}], 0.8 * weight))
    passive = obs.get("playerFrozen") or hint_count == 0
    add(Act("wait", [dict(WAIT_CHANGE)], 0.7 if passive else 0.3, kind="wait"))
    texts = " | ".join(obs.get("texts") or [])  # read the screen: "Click LMB to continue", "Press E to ..."
    if re.search(r"\b(click|LMB|left mouse)\b", texts, re.I):
        add(Act("click", [{"op": "click", "why": "the screen asks for a click"}], 0.86))
    for key in re.findall(r"\bpress\s+\[?([A-Za-z]{1,8})\]?", texts, re.I)[:2]:
        name = key.upper() if len(key) == 1 else key.capitalize()
        add(Act("key:" + name, [{"op": "key", "key": name, "why": "the screen says press " + key}], 0.8))
    for o in objects:
        if o.get("usable") and o["name"] not in hinted:
            prior = 0.45 * math.exp(-o.get("dist", 10) / 8.0) + (0.08 if o.get("inView") else 0) + (0.05 if o.get("prompt") else 0)
            add(Act("interact:" + o["name"], [{"op": "interact", "target": o["name"], "why": "usable object near"}], prior))
    for a in acts.values():
        cell = (q_state or {}).get(a.key)
        known = cell["value"] if cell and cell.get("n", 0) > 0 else None
        a.score = (known if known is not None else a.prior) + rng.uniform(0, 0.08) - (0.7 if a.key in tried else 0)
    return sorted(acts.values(), key=lambda a: -a.score)


def explore(name, seg, budget=240, prefix=None, by="agent", rng=None, who="", record=False, setup=True):
    """Plays the segment by itself from its start (after `prefix` steps), learning which actions move the story on;
    with setup=False from wherever the game is now (a chained run). Returns {"outcome", "steps" (the useful ones,
    gated), "reason", "actions", "q" (updates)}."""
    rng = rng or random.Random()
    rec = KB.load(seg["id"], seg)
    q = json.loads(json.dumps(rec.get("q", {})))
    updates = []
    for cmd in seg.get("setup", []) if setup else []:
        a = send(name, cmd, op_timeout(cmd))
        if not a.get("ok"):
            return {"outcome": "fail", "steps": [], "reason": "setup failed: " + str(a.get("error")), "actions": 0, "q": []}
    useful = []
    if prefix:
        ep = run_episode(name, seg, prefix, setup=False, by=by, budget=seg.get("budget", 150), who=who, final_wait=10)
        if ep["outcome"] == "win":
            return {"outcome": "win", "steps": list(prefix), "reason": "the prefix already wins", "actions": 0, "q": []}
        if ep["failKind"] != "goal-timeout":
            return {"outcome": "fail", "steps": [], "reason": "prefix failed: %s" % ep["failKind"], "actions": 0, "q": []}
        useful = list(prefix)
    t0 = time.time()
    tried = {}
    previous_running, last_state, last_change = [], None, time.time()
    st = status(name)
    deaths = st.get("deaths", 0) or 0
    actions, outcome, reason = 0, "fail", "budget used up"
    if record:
        video.start(name, "explore " + seg.get("label", seg["id"]))
    while time.time() - t0 < budget:
        met, _ = check(name, seg["goal"])
        if met:
            outcome, reason = "win", "goal reached"
            break
        obs = observe(name)
        st = obs.get("status") or {}
        if (st.get("deaths", deaths) or 0) > deaths:
            deaths = st.get("deaths", 0)
            useful = list(prefix or [])  # back at the checkpoint: what worked before the death still has to be redone
        state = state_key(st)
        if state != last_state:
            if last_state is not None:
                last_change = time.time()
            last_state = state
        cands = candidates(obs, q.get(state), tried.get(state, set()), rng)
        if all(c.key in tried.get(state, set()) for c in cands) and time.time() - last_change > 45:
            reason = "stuck: tried every candidate in state %s for 45 s" % state
            break
        act = cands[0]
        micro0 = micro_key(obs)
        ok = True
        for cmd in act.steps:
            a = send(name, {k: v for k, v in cmd.items() if k not in META_KEYS}, op_timeout(cmd))
            kind, _ = classify(cmd, a)
            if kind in ("crash", "hang"):
                return {"outcome": "fail", "steps": useful, "reason": "player " + kind, "actions": actions, "q": updates}
            if kind and act.kind != "wait":
                ok = False
                break
        actions += 1
        tried.setdefault(state, set()).add(act.key)
        met, after = check(name, seg["goal"])
        died = (after.get("deaths", deaths) or 0) > deaths
        if met:
            reward = 1.0
        elif died:
            reward = -1.0
        elif after.get("stage") != st.get("stage"):
            reward = 1.0 if (after.get("stage") or 0) > (st.get("stage") or 0) else -0.5
        elif state_key(after) != state:
            reward = 0.6
        elif act.kind != "wait" and ok and micro_key(observe(name)) != micro0:
            reward = 0.3
        else:
            reward = -0.4 if not ok else -0.15
        updates.append((state, act.key, reward, died))
        merge_q({"q": q}, [(state, act.key, reward, died)])
        if reward > 0 and act.kind != "wait":
            gate = gate_for(st, previous_running)
            for cmd in act.steps:
                step = dict(cmd)
                if gate:
                    step["when"] = gate
                if not ok or reward < 0.5:
                    step["optional"] = True  # it failed yet the story moved, or it only changed a detail: never fail a run on it
                step["why"] = "learned: %s moved the story on (%s)" % (act.key, cmd.get("why", ""))
                useful.append(step)
        if state_key(after) != state:
            previous_running = st.get("running") if isinstance(st.get("running"), list) else []
        if met:
            outcome, reason = "win", "goal reached"
            break
    if record:
        try:
            video.stop(name, extra={"segment": seg["id"], "explore": True})
        except SystemExit:
            pass
    return {"outcome": outcome, "steps": useful, "reason": reason, "actions": actions, "q": updates,
            "seconds": round(time.time() - t0, 1), "lastState": last_state}


# ------------------------------------------------------------------ repairs and optimizations (route variants)

def repairs(route, ep, obs):
    """Variants of a failed route that fix the failing step in different ways: [(origin, steps, why)]."""
    i, kind, steps = ep.get("failStep"), ep.get("failKind"), route["steps"]
    if i is None or kind in (None, "setup", "crash", "hang", "bad-step"):
        return []
    out = []
    step = steps[i] if i < len(steps) else {}
    gate = {"when": step["when"]} if step.get("when") else {}

    def insert(extra):
        return steps[:i] + [dict(e, **gate) for e in extra] + steps[i:]

    project_usable = any("usable" in o for o in (obs.get("objects") or []))
    if kind == "blocked":
        res = (ep["steps"][-1].get("result") if ep.get("steps") else {}) or {}
        stop, dest = res.get("stoppedAt"), step.get("pos")
        if stop and dest:
            dx, dz = dest[0] - stop[0], dest[2] - stop[2]
            length = math.hypot(dx, dz) or 1.0
            px, pz = -dz / length, dx / length
            for side, sign in (("left", 1), ("right", -1)):
                p = [round(stop[0] + px * 1.3 * sign + dx / length * 0.8, 2), stop[1], round(stop[2] + pz * 1.3 * sign + dz / length * 0.8, 2)]
                out.append(("repair:blocked:detour-" + side, insert([{"op": "walk", "pos": p, "timeout": 20, "optional": True,
                                                                     "why": "detour around what blocked the walk"}]), "detour " + side))
        doors = [o for o in obs.get("objects") or [] if o.get("usable") and "door" in (o.get("path", "") + o["name"]).lower() and o.get("dist", 99) < 4]
        if doors:
            out.append(("repair:blocked:door", insert([{"op": "interact", "target": doors[0]["name"], "optional": True,
                                                         "why": "open the door in the way"}]), "open " + doors[0]["name"]))
        out.append(("repair:blocked:wait", insert([{"op": "wait", "s": 3, "why": "let whatever blocked the way move"}]), "wait 3 s"))
    elif kind in ("no-target", "missing"):
        target = step.get("target", "")
        if project_usable and target:
            out.append(("repair:%s:until-usable" % kind, insert([{"op": "until", "cond": {"usable": target}, "timeout": 20,
                                                                  "why": "wait until it can be used"}]), "wait until usable"))
        if target:
            out.append(("repair:%s:approach" % kind, insert([{"op": "approach", "target": target, "dist": 0.9, "soft": True,
                                                              "why": "get closer first"}]), "approach first"))
        out.append(("repair:%s:wait" % kind, insert([{"op": "wait", "s": 3}]), "wait 3 s"))
    elif kind in ("timeout", "expect", "when-timeout"):
        longer = dict(step)
        for key, default in (("timeout", 15), ("expect_timeout", 4), ("when_timeout", 45)):
            if key in step or (kind == "when-timeout" and key == "when_timeout") or (kind == "timeout" and key == "timeout"):
                longer[key] = min(180, float(step.get(key, default)) * 2)
        out.append(("repair:%s:longer" % kind, steps[:i] + [longer] + steps[i + 1:], "longer timeouts"))
        out.append(("repair:%s:skip" % kind, steps[:i] + steps[i + 1:], "drop the step"))
    elif kind == "death":
        if i < len(steps) and step.get("op") == "walk" and not step.get("direct"):
            # The shortest path can cut through the danger a player would walk around (keep to the wall, not the middle).
            out.append(("repair:death:direct", steps[:i] + [dict(step, direct=True)] + steps[i + 1:], "walk the straight line"))
        if i < len(steps):
            out.append(("repair:death:later", insert([{"op": "wait", "s": 2.5, "why": "a death followed this step: try it later"}]), "wait before"))
            out.append(("repair:death:skip", steps[:i] + steps[i + 1:], "drop the step"))
    elif kind == "no-control":
        out.append(("repair:no-control:wait", insert([{"op": "wait", "s": 4, "why": "the game had the controls off"}]), "wait for control"))
    if kind in ("blocked", "no-target", "missing", "no-control", "error") and i < len(steps):
        # Ablation: an explored route can carry a step that only looked useful (it happened to coincide with progress).
        out.append(("repair:%s:skip" % kind, steps[:i] + steps[i + 1:], "drop the step"))
    return out


def optimizations(route):
    """Faster variants of a winning route: no fixed waits; without one step (later steps first)."""
    steps = route["steps"]
    out = []
    if any(s.get("op") == "wait" for s in steps):
        out.append(("optimize:no-waits", [s for s in steps if s.get("op") != "wait"]))
    for i in reversed(range(len(steps))):
        if steps[i].get("op") != "wait" and len(steps) > 1:
            out.append(("optimize:drop-%d" % i, steps[:i] + steps[i + 1:]))
        if len(out) >= 3:
            break
    return out


def pick_route(rec, rng, prefer_untried=False):
    """Win-stay, lose-shift: replay the route on a winning streak until it is mastered; after a loss (or when faster
    variants should be tried) choose by UCB1, untried routes first, then the best mean plus an exploration bonus."""
    live = live_routes(rec)
    hot = [r for r in live if r.get("streak", 0) > 0]
    if hot and not prefer_untried:
        return max(hot, key=lambda r: (r["streak"], win_rate(r), -(r.get("bestTime") or 1e9)))
    total = sum(r.get("runs", 0) for r in live) + 1

    def ucb(r):
        if r.get("runs", 0) == 0:
            return 1e9 + r.get("prior", 0) + 0.1 * rng.random()
        return r["wins"] / r["runs"] + 0.6 * math.sqrt(math.log(total) / r["runs"]) + 0.05 * min(r.get("streak", 0), 5)
    return max(live, key=ucb)


# ------------------------------------------------------------------ learning one segment

def learn_segment(name, seg, attempts=10, streak=None, record="none", optimize=True, explore_budget=240, by="agent",
                  rng=None, who="", force=False):
    """The learning loop for one segment (see the module doc). Returns a summary dict."""
    global MASTER_STREAK
    if streak:
        MASTER_STREAK = streak
    rng = rng or random.Random()
    sid = seg["id"]
    began = time.time()
    rec = KB.load(sid, seg)
    explorations = 0
    optimizing = False
    if mastery_of(rec) == "mastered" and not force:
        best = best_route(rec)
        ep = run_episode(name, seg, best["steps"], noise=True, rng=rng, by=by, who=who)
        KB.update(sid, lambda r: apply_episode(r, best["id"], ep), seg, by)
        log("%s: already mastered; check run: %s (%.0fs)" % (sid, ep["outcome"], ep["time"]), who)
        if ep["outcome"] == "win":
            return summary(sid, began)
    for attempt in range(attempts):
        rec = KB.load(sid, seg)
        if not live_routes(rec):
            if explorations >= 2:
                break
            explorations += 1
            log("%s: exploring (no route yet, %ds budget)" % (sid, explore_budget), who)
            res = explore(name, seg, explore_budget, by=by, rng=rng, who=who)
            record_exploration(sid, seg, res, by)
            log("%s: exploration %s after %d actions: %s" % (sid, res["outcome"], res["actions"], res["reason"]), who)
            if res["outcome"] == "win":
                KB.update(sid, lambda r: add_route(r, res["steps"], "explore", by, why=res["reason"]), seg, by)
            elif res["reason"].startswith("player "):
                break
            continue
        route = pick_route(rec, rng, prefer_untried=optimizing)
        ep = run_episode(name, seg, route["steps"], noise=route.get("runs", 0) > 0, rng=rng, record=(record == "all"),
                         by=by, who=who)
        KB.update(sid, lambda r: apply_episode(r, route["id"], ep), seg, by)
        rec = KB.load(sid, seg)
        now = next(r for r in rec["routes"] if r["id"] == route["id"])
        log("%s: route %s (%s) %s in %.0fs%s; streak %d" % (
            sid, route["id"], route.get("origin"), ep["outcome"].upper(), ep["time"],
            "" if ep["outcome"] == "win" else " at step %s: %s %s" % (ep["failStep"], ep["failKind"], ep["failDetail"][:90]),
            now.get("streak", 0)), who)
        if ep["failKind"] in ("crash", "hang"):
            break
        if ep["outcome"] == "win":
            if now.get("streak", 0) >= MASTER_STREAK:
                if optimize and not now.get("optimized") and attempt < attempts - 2:
                    def add_variants(r):
                        target = next(x for x in r["routes"] if x["id"] == route["id"])
                        target["optimized"] = True
                        for origin, steps in optimizations(target):
                            add_route(r, steps, origin, by, parent=route["id"], why="faster variant")
                    KB.update(sid, add_variants, seg, by)
                    optimizing = True
                    continue
                if not any(r.get("runs", 0) == 0 for r in live_routes(rec)):
                    break
            continue
        if str(route.get("origin", "")).startswith("optimize:"):
            continue  # a faster variant that does not work simply loses; nothing to repair
        detail = "route %s step %s (%s) failed: %s %s" % (route["id"], ep["failStep"], json.dumps(
            route["steps"][ep["failStep"]] if ep["failStep"] is not None and ep["failStep"] < len(route["steps"]) else {})[:120],
            ep["failKind"], ep["failDetail"][:160])
        obs = observe(name) if ep["failKind"] not in ("setup",) else {}
        variants = repairs(route, ep, obs)

        def add_repairs(r):
            add_lesson(r, detail, "pitfall", by, auto=True)
            make_room(r, len(variants), keep=route["id"])
            for origin, steps, why in variants:
                if len(live_routes(r)) >= REPAIR_BELOW:
                    break
                rid = add_route(r, steps, origin, by, parent=route["id"], why=why)
                if origin.startswith("repair:death:direct"):
                    next(x for x in r["routes"] if x["id"] == rid)["prior"] = 0.4
        KB.update(sid, add_repairs, seg, by)
        if ep["failKind"] == "goal-timeout" and explorations < 2:
            explorations += 1
            log("%s: route %s ran out of steps; exploring on from its end" % (sid, route["id"]), who)
            res = explore(name, seg, explore_budget, prefix=route["steps"], by=by, rng=rng, who=who)
            record_exploration(sid, seg, res, by)
            if res["outcome"] == "win":
                KB.update(sid, lambda r: add_route(r, res["steps"], "explore-on", by, parent=route["id"], why=res["reason"]), seg, by)
    rec = KB.load(sid, seg)
    m = mastery_of(rec)
    if m in ("unknown", "learning", "needs-agent"):
        diagnosis = diagnose(name, seg, rec)
        KB.update(sid, lambda r: r.update(needsAgent=diagnosis), seg, by)
        log("%s: NEEDS AN AGENT: %s" % (sid, diagnosis["summary"]), who)
    elif record == "best":
        best = best_route(rec)
        ep = run_episode(name, seg, best["steps"], record=True, by=by, who=who, label="mastered " + seg.get("label", sid))
        KB.update(sid, lambda r: (apply_episode(r, best["id"], ep), r.update(showcase=ep.get("video"))), seg, by)
    return summary(sid, began)


def record_exploration(sid, seg, res, by):
    def apply(r):
        r["stats"]["explorations"] = r["stats"].get("explorations", 0) + 1
        merge_q(r, res.get("q") or [])
        if res["outcome"] != "win":
            add_lesson(r, "exploration stalled (%s) after %d actions" % (res["reason"], res["actions"]), "pitfall", by, auto=True)
    KB.update(sid, apply, seg, by)


def diagnose(name, seg, rec):
    """What an agent needs to take over: the last failure, what the story waits for, what is near, what was tried."""
    try:
        obs = observe(name)
    except SystemExit:
        obs = {}
    st = obs.get("status") or {}
    beats = ["%s (%s) %s" % (b.get("name"), b.get("type"), json.dumps(b.get("fields"))[:200]) for b in obs.get("beats") or []]
    q = rec.get("q", {}).get(state_key(st), {})
    tried = sorted(q.items(), key=lambda kv: kv[1]["value"])
    last = rec["history"][-1] if rec.get("history") else None
    text = "autopilot could not play %s. Last: %s. Story waits on: %s" % (
        seg["id"], ("%s at step %s" % (last["failKind"], last["failStep"])) if last else "never won",
        "; ".join(b.split(" {")[0] for b in beats[:3]) or "nothing visible")
    return {"summary": text, "at": now_text(), "state": state_key(st), "beats": beats[:6],
            "near": [o["name"] for o in (obs.get("objects") or [])[:10]], "texts": obs.get("texts"),
            "tried": ["%s=%+.2f" % (k, v["value"]) for k, v in tried[:12]],
            "next": "agentplay.py observe NAME; try actions with agentplay.py step NAME '{...}'; then "
                    "agentplay.py learn NAME --segment %s --from-draft" % seg["id"]}


def summary(sid, began):
    rec = KB.load(sid)
    best = best_route(rec) or {}
    return {"segment": sid, "mastery": mastery_of(rec), "best": best.get("id"), "streak": best.get("streak", 0),
            "bestTime": best.get("bestTime"), "routes": len(rec.get("routes", [])), "seconds": round(time.time() - began),
            "needsAgent": (rec.get("needsAgent") or {}).get("summary")}


# ------------------------------------------------------------------ several segments, several players

def ensure_player(name, headless=False):
    """Starts player NAME unless it runs. Returns True when it started it (and so should stop it)."""
    folder = ap.run_dir(name)
    pid = ap.instance_pid(folder)
    started = False
    if not (pid and ap.pid_alive(pid, ap.EXE)):
        record = ap.start_player(name, scene=ap.CONFIG.get("defaultScene", ""), headless=headless, res="960x540")
        if not record["boot"].get("ok"):
            ap.fail("player %s did not boot: %s" % (name, record["boot"].get("error")))
        started = True
    if _validate is not None and name not in _validated:
        problem = _validate(status(name))
        if problem:
            if started:
                ap.stop_one(name)
            ap.fail(problem)
        _validated.add(name)
    return started


def learn_many(name, segs, workers=1, headless=False, **kw):
    """Learns segments with one player, or with N worker players (NAME-w1..) taking segments from a queue."""
    if workers <= 1:
        started = ensure_player(name, headless)
        results = []
        try:
            for seg in segs:
                results.append(guarded(name, seg, headless, **kw))
        finally:
            if started:
                ap.stop_one(name)
        return results
    jobs = queue.Queue()
    for seg in segs:
        jobs.put(seg)
    results, threads = [], []

    def worker(wname):
        try:
            ensure_player(wname, headless)
        except SystemExit as e:
            log("could not start %s: %s" % (wname, e), wname)
            return
        try:
            while True:
                try:
                    seg = jobs.get_nowait()
                except queue.Empty:
                    break
                results.append(guarded(wname, seg, headless, **kw))
        finally:
            try:
                ap.stop_one(wname)
            except SystemExit:
                pass
    for i in range(workers):
        t = threading.Thread(target=worker, args=("%s-w%d" % (name, i + 1),), daemon=True)
        t.start()
        threads.append(t)
        time.sleep(2)  # stagger the player starts
    for t in threads:
        t.join()
    return sorted(results, key=lambda r: r.get("order", 0))


def guarded(name, seg, headless, **kw):
    """learn_segment that survives a crashed player: restart it once and go on with the next segment."""
    for attempt in range(2):
        try:
            res = learn_segment(name, seg, who=name, **kw)
            res["order"] = seg.get("order", 0)
            return res
        except SystemExit as e:
            log("%s: player trouble (%s); restarting %s" % (seg["id"], e, name), name)
            try:
                ap.stop_one(name)
            except SystemExit:
                pass
            try:
                ensure_player(name, headless)
            except SystemExit:
                break
    return {"segment": seg["id"], "mastery": mastery_of(KB.load(seg["id"])), "error": "player kept failing", "order": seg.get("order", 0)}


# ------------------------------------------------------------------ playtest: play a chunk with what was learned

def playtest(name, segs, record=True, learn_first=False, label=None, by="agent", workers=1, headless_learning=False,
             learn_kw=None, adaptive=False, rng=None, series=None):
    """Plays the segments in order with their best routes, chaining them when the game is already at the next one,
    recording one video. Writes Builds/AgentPlay/playtests/<id>/report.json and report.html. Returns the report.
    adaptive (the until-perfect runs): routes are picked by win-stay/UCB and every failure is learned from."""
    if learn_first:
        todo = [s for s in segs if mastery_of(KB.load(s["id"], s)) != "mastered"]
        if todo:
            log("learning %d segment(s) first" % len(todo))
            learn_many(name, todo, workers=workers, headless=headless_learning, by=by, **(learn_kw or {}))
    started = ensure_player(name)
    pt_id = time.strftime("%Y%m%d-%H%M%S-") + name
    report = {"id": pt_id, "scope": label or "%s..%s" % (segs[0]["id"], segs[-1]["id"]), "started": now_text(), "player": name,
              "build": current_build(), "by": by, "segments": [], "findings": [], "videoId": None, "video": None}
    if series:
        report["series"] = series
    title = "playtest " + report["scope"] + (" run %d" % series["run"] if series else "")
    t_start = time.time()
    try:
        for cmd in segs[0].get("setup", []):
            send(name, cmd, op_timeout(cmd))
        recording = False
        if record:
            a = video.start(name, title, rec_id=pt_id, max_minutes=240)
            recording = bool(a.get("ok"))
            if not recording:
                report["findings"].append({"sev": "INFO", "text": "no video: " + str(a.get("error")), "segment": None, "t": None})
        for seg in segs:
            report["segments"].append(play_segment(name, seg, recording, report, by, adaptive, rng))
            row = report["segments"][-1]
            log("%-48s %-9s %5.0fs  %s%s" % (seg.get("label", seg["id"])[:48], row["outcome"].upper(), row["duration"],
                                            "chained" if row["chained"] else "jumped", ("  " + "; ".join(row["notes"])) if row["notes"] else ""))
        if recording:
            meta = video.stop(name, extra={"playtest": pt_id, "label": title})
            report["videoId"] = meta["id"]
    finally:
        if started:
            ap.stop_one(name)
    report["duration"] = round(time.time() - t_start, 1)
    score(report)
    write_report(report)
    return report


def play_segment(name, seg, recording, report, by, adaptive=False, rng=None):
    rec = KB.load(seg["id"], seg)
    if adaptive:
        route = pick_route(rec, rng or random.Random()) if live_routes(rec) else None
    else:
        route = best_route(rec) if mastery_of(rec) in ("mastered", "competent", "stale") else None
    at = seg.get("at")
    chained = bool(at) and check(name, at)[0]
    through = False
    if not chained and report["segments"] and report["segments"][-1]["outcome"] == "pass":
        # The segment before ended already past this one's start: a checkpoint the story runs through by itself. Its goal
        # holding means it was played, chained; jumping back to its start would replay it out of order.
        through = chained = check(name, seg["goal"])[0]
    t_video = video.elapsed(name) if recording else None
    if recording:
        video.mark(name, seg.get("label", seg["id"]), "segment")
    budget = seg.get("budget", 150) if route else min(seg.get("budget", 150), UNLEARNED_WAIT)
    if adaptive and route is None:
        ep = explore_episode(name, seg, chained, by, rng)
    else:
        ep = run_episode(name, seg, route["steps"] if route else [], setup=not chained, by=by, budget=budget)
    t_end = video.elapsed(name) if recording else None
    perf = ep.get("perf") or {}
    passive = sum(s.get("dt", 0) for s in ep["steps"] if s.get("op") in ("wait", "until"))
    active_time = max(0.001, ep["time"] or 0.001)
    waited = active_time - sum(s.get("dt", 0) for s in ep["steps"])
    row = {"id": seg["id"], "label": seg.get("label", seg["id"]),
           "outcome": "pass" if ep["outcome"] == "win" else ("unlearned" if route is None else "fail"),
           "mastery": mastery_of(rec), "start": t_video, "end": t_end, "duration": ep["time"], "chained": chained,
           "route": route["id"] if route else None, "deaths": ep.get("deaths", 0), "errors": ep.get("errors", []),
           "worstFrameMs": perf.get("worstMs"), "p95FrameMs": perf.get("p95Ms"), "fpsAvg": perf.get("fps"),
           "blocked": sum(1 for s in ep["steps"] if s.get("kind") == "blocked"),
           "passiveRatio": round(min(1.0, (passive + max(0.0, waited)) / active_time), 2), "notes": [],
           "friction": friction(rec), "episode": ep["id"]}
    if through:
        row["notes"].append("the story went through it by itself (the segment before ended past its start)")
    if ep.get("explored"):
        row.update(outcome="pass" if ep["outcome"] == "win" else "fail", explored=True)
        row["notes"].append("no route yet: explored %s and %s" % ("on from the run" if chained else "from the start",
                                                                  "learned one" if ep["outcome"] == "win" else "did not finish"))
    elif route is None:
        row["notes"].append("no learned route: waited for the goal" + (" and it came" if ep["outcome"] == "win" else ""))
    if ep["outcome"] != "win":
        row["notes"].append("%s at step %s: %s" % (ep["failKind"], ep["failStep"], ep["failDetail"][:140]))
    for e in ep.get("errors", []):
        report["findings"].append({"sev": e.split(" ", 1)[0], "text": e, "segment": seg["id"], "t": t_video})
    if ep.get("deaths"):
        report["findings"].append({"sev": "SOFT", "text": "the player died %d time(s)" % ep["deaths"], "segment": seg["id"], "t": t_video})
    if (perf.get("worstMs") or 0) > 250:
        report["findings"].append({"sev": "WARN", "text": "a %.0f ms frame (hitch)" % perf["worstMs"], "segment": seg["id"], "t": t_video})
    if row["blocked"]:
        report["findings"].append({"sev": "WARN", "text": "the walk was blocked %d time(s)" % row["blocked"], "segment": seg["id"], "t": t_video})
    if ep["outcome"] != "win":
        report["findings"].append({"sev": "STALL", "text": "%s did not finish: %s" % (seg["id"], ep["failDetail"][:200]),
                                   "segment": seg["id"], "t": t_end})
    if route and row["passiveRatio"] > 0.85 and (ep["time"] or 0) > 40:  # an unlearned segment's wait says nothing about pacing
        report["findings"].append({"sev": "INFO", "text": "%.0f s mostly waiting (cutscene or dialog pacing?)" % ep["time"],
                                   "segment": seg["id"], "t": t_video})
    variants, lesson, follow = [], None, None
    clean = chained or not report["segments"] or report["segments"][-1]["outcome"] == "pass"
    if adaptive and route and clean and ep["outcome"] != "win" and ep["failKind"] not in ("setup", "crash", "hang"):
        step = route["steps"][ep["failStep"]] if ep["failStep"] is not None and ep["failStep"] < len(route["steps"]) else {}
        lesson = "%s: route %s step %s (%s) failed: %s %s" % (
            "chained after the segment before" if chained else "after a jump", route["id"], ep["failStep"],
            json.dumps(step)[:120], ep["failKind"], ep["failDetail"][:160])
        if not str(route.get("origin", "")).startswith("optimize:"):
            variants = repairs(route, ep, observe(name))
        if chained:
            fix = handoff_repair(KB.load(seg["id"], seg), route, ep)
            if fix:
                variants.insert(0, fix)
        if ep["failKind"] == "goal-timeout":  # the route ran out of steps here: explore on from where it left the game
            res = explore(name, seg, min(seg.get("budget", 150), 120), by=by, rng=rng, setup=False)
            record_exploration(seg["id"], seg, res, by)
            if res["outcome"] == "win":
                follow = route["steps"] + res["steps"]
                row["notes"].append("explored on from the route's end and learned the rest")

    def note(r):
        if route:
            apply_episode(r, route["id"], ep)
        if lesson:
            add_lesson(r, lesson, "pitfall", by, auto=True)
        if variants:
            make_room(r, len(variants), keep=route["id"])
        for origin, steps, why in variants:
            if len(live_routes(r)) >= MAX_LIVE_ROUTES - 1:
                break
            rid = add_route(r, steps, origin.replace("repair:", "repair-chained:"), by, parent=route["id"], why=why + " (chained)")
            if origin.startswith(("repair:handoff", "repair:death:direct")):
                next(x for x in r["routes"] if x["id"] == rid)["prior"] = 0.5 if origin.startswith("repair:handoff") else 0.4
        if follow:
            add_route(r, follow, "explore-on-chained", by, parent=route["id"], why="the route ran out of steps in a chained run")
    KB.update(seg["id"], note, seg, by)
    if variants:
        row["notes"].append("added %d repair variant(s) for the next run" % len(variants))
    row["routeOrigin"] = route.get("origin") if route else None
    return row


def explore_episode(name, seg, chained, by, rng):
    """A segment nobody has a route for, in an until-perfect run: explore it (on from the run when chained), keep what
    wins as a route, and report it like an episode."""
    before = issue_counts(name)
    deaths0 = status(name).get("deaths", 0) or 0
    res = explore(name, seg, seg.get("budget", 150), by=by, rng=rng, setup=not chained)
    record_exploration(seg["id"], seg, res, by)
    if res["outcome"] == "win":
        KB.update(seg["id"], lambda r: add_route(r, res["steps"], "explore-chained" if chained else "explore", by,
                                                 why=res["reason"]), seg, by)
    st = status(name)
    ep = {"id": time.strftime("%Y%m%d-%H%M%S-") + uuid.uuid4().hex[:4], "segment": seg["id"], "at": now_text(), "by": by,
          "player": name, "noise": False, "outcome": res["outcome"], "explored": True, "steps": [],
          "failKind": None if res["outcome"] == "win" else "explore", "failStep": None, "failDetail": res["reason"],
          "time": res.get("seconds", 0), "deaths": max(0, (st.get("deaths", 0) or 0) - deaths0),
          "perf": trim(send(name, {"op": "perf"}, 30)), "video": None, "build": current_build()}
    try:
        ep["errors"] = new_issues(before, issue_counts(name))
    except SystemExit:
        ep["errors"] = []
    return save_episode(ep)


def imperfections(report):
    """Why a playtest is not a perfect play-through ([] when it is): every segment passed on a learned route, each one
    chained from the one before (no story jump), no deaths, no new errors."""
    why = []
    for i, r in enumerate(report.get("segments", [])):
        if r["outcome"] != "pass":
            why.append("%s did not finish (%s)" % (r["id"], r["outcome"]))
        elif r.get("explored"):
            why.append("%s had no route yet (explored)" % r["id"])
        if i > 0 and not r.get("chained"):
            why.append("%s started from a story jump" % r["id"])
        if r.get("deaths"):
            why.append("%s: died %d time(s)" % (r["id"], r["deaths"]))
        if r.get("errors"):
            why.append("%s: %d new error(s)" % (r["id"], len(r["errors"])))
    return why


def until_perfect(name, segs, runs=8, record=True, label=None, by="agent", rng=None):
    """Whole chained runs of the scope, learning between them, until one is perfect (or `runs` are used up).
    Returns (perfect report or None, [reports])."""
    rng = rng or random.Random()
    started = ensure_player(name)
    reports, perfect = [], None
    series = time.strftime("%Y%m%d-%H%M%S-") + name
    try:
        for n in range(1, runs + 1):
            log("=== run %d of %d (%s) ===" % (n, runs, label or "scope"))
            try:
                report = playtest(name, segs, record=record, label=label, by=by, adaptive=True, rng=rng,
                                  series={"id": series, "run": n, "of": runs})
            except SystemExit as e:
                log("run %d: player trouble (%s); restarting it" % (n, e))
                try:
                    ap.stop_one(name)
                except SystemExit:
                    pass
                ensure_player(name)
                continue
            reports.append(report)
            log("run %d: %s" % (n, report["verdict"]))
            for why in report.get("imperfect", [])[:8]:
                log("    - " + why)
            if report.get("perfect"):
                perfect = report
                break
    finally:
        if started:
            ap.stop_one(name)
    return perfect, reports


def score(report):
    rows = report["segments"]
    n = max(1, len(rows))
    passed = sum(1 for r in rows if r["outcome"] == "pass")
    clean = sum(1 for r in rows if not r["errors"])
    smooth = sum(1 for r in rows if (r.get("p95FrameMs") or 0) < 50 and (r.get("worstFrameMs") or 0) < 250)
    played = [r for r in rows if r["outcome"] != "unlearned"]  # pacing only where the bot played, not where it waited
    pacing = 1 - sum(max(0.0, r["passiveRatio"] - 0.6) / 0.4 for r in played) / max(1, len(played))
    report["quality"] = {"completion": round(passed / n, 3), "stability": round(clean / n, 3),
                         "performance": round(smooth / n, 3), "pacing": round(max(0.0, pacing), 3),
                         "friction": round(sum(r.get("friction", 0) for r in rows) / n, 3)}
    fails = n - passed
    errs = sum(len(r["errors"]) for r in rows)
    report["passed"] = passed
    report["imperfect"] = imperfections(report)
    report["perfect"] = not report["imperfect"] and bool(rows)
    if report["perfect"]:
        report["verdict"] = "PERFECT: all %d segments in one chained play-through, no deaths, no errors" % n
        return
    report["verdict"] = ("PASS: all %d segments played through" % n if fails == 0 else
                         "ISSUES: %d of %d segments did not finish" % (fails, n)) + (", %d new error(s)" % errs if errs else ", no errors")


def write_report(report):
    folder = PLAYTESTS / report["id"]
    folder.mkdir(parents=True, exist_ok=True)
    meta = None
    if report.get("videoId"):
        try:
            meta = video.find(report["videoId"])
        except SystemExit:
            meta = None
    report["video"] = video.web_meta(meta, "../../videos/%s/" % meta["id"]) if meta else None
    atomic_write(folder / "report.json", json.dumps(report, indent=1))
    template = Path(__file__).with_name("agentplay_report.html")
    if template.exists():
        data = json.dumps(report).replace("</", "<\\/")
        html = template.read_text(encoding="utf-8").replace("/*__REPORT_JSON__*/null", data)
        (folder / "report.html").write_text(html, encoding="utf-8")
    return folder


def cmd_playtest_report(args):
    report = json.loads((PLAYTESTS / args.id / "report.json").read_text(encoding="utf-8"))
    unlearned = {r["id"] for r in report["segments"] if r["outcome"] == "unlearned"}
    report["findings"] = [f for f in report["findings"]
                          if not (f["sev"] == "INFO" and f.get("segment") in unlearned and "mostly waiting" in f["text"])]
    score(report)
    print(write_report(report) / "report.html")


def all_playtests():
    out = []
    for path in PLAYTESTS.glob("*/report.json") if PLAYTESTS and PLAYTESTS.exists() else []:
        try:
            out.append(json.loads(path.read_text(encoding="utf-8")))
        except (OSError, ValueError):
            pass
    out.sort(key=lambda r: r.get("started", ""), reverse=True)
    return out


# ------------------------------------------------------------------ summaries for kb and the dashboard

def seg_summary(rec):
    best = best_route(rec) or {}
    last = rec["history"][-1] if rec.get("history") else {}
    return {"id": rec["id"], "label": rec.get("label", rec["id"]), "order": rec.get("order", 0), "mastery": mastery_of(rec),
            "routes": len(rec.get("routes", [])), "runs": rec.get("stats", {}).get("runs", 0),
            "wins": rec.get("stats", {}).get("wins", 0), "streak": best.get("streak", 0), "bestTime": best.get("bestTime"),
            "lessons": len(rec.get("lessons", [])), "friction": friction(rec), "lastRun": last.get("at"),
            "lastOutcome": last.get("outcome"), "needsAgent": (rec.get("needsAgent") or {}).get("summary")}


def seg_full(rec):
    full = seg_summary(rec)
    routes = sorted(rec.get("routes", []), key=lambda r: (r is not best_route(rec), r.get("retired", False), -win_rate(r)))
    full["routeList"] = [dict(r, meanTime=round(sum(r["times"]) / len(r["times"]), 2) if r.get("times") else None) for r in routes]
    full.update({"lessons": rec.get("lessons", []), "history": rec.get("history", [])[::-1], "q": rec.get("q", {}),
                 "needsAgentDetail": rec.get("needsAgent"), "showcase": rec.get("showcase")})
    return full


def table(segs):
    """Every segment of the scope with its knowledge, in play order."""
    by_id = {r["id"]: r for r in KB.records()}
    return [seg_summary(by_id.get(s["id"]) or KB.blank(s["id"], s)) for s in segs]


# ------------------------------------------------------------------ commands

def scoped(args):
    if getattr(args, "segment", None):
        return [segment(args.segment)]
    if getattr(args, "scope", None):
        return resolve(args.scope)
    ap.fail("give --scope (or --segment); `agentplay.py segments` lists them. A whole-game run (--scope all) only when "
            "the user asked for it.", 2)


def cmd_segments(args):
    segs = resolve(args.scope) if args.scope else all_segments()
    for s in segs:
        print("%3s  %-44s %s" % (s.get("order", ""), s["id"], s.get("label", "")))


def cmd_learn(args):
    by = args.by or args.name
    if args.from_draft:
        if not args.segment:
            ap.fail("--from-draft needs --segment")
        return learn_from_draft(args.name, segment(args.segment), by)
    segs = scoped(args)
    kw = dict(attempts=args.attempts, streak=args.streak, record=args.record, optimize=not args.no_optimize,
              explore_budget=args.explore_seconds, by=by, force=args.force)
    began = time.time()
    results = learn_many(args.name, segs, workers=args.workers, headless=args.headless, **kw)
    print("\nLEARNED %d segment(s) in %.0f min:" % (len(results), (time.time() - began) / 60))
    for r in sorted(results, key=lambda r: r.get("order", 0)):
        print("  %-44s %-11s best %-4s streak %s%s" % (r["segment"], r["mastery"], r.get("best") or "-", r.get("streak", 0),
                                                     "  NEEDS AGENT: " + r["needsAgent"] if r.get("needsAgent") else ""))
    print("Knowledge saved in %s (commit it). Next: agentplay.py playtest %s --scope ..." % (KB.root, args.name))


def cmd_playtest(args):
    segs = scoped(args)
    learn_kw = dict(attempts=args.attempts, streak=args.streak, explore_budget=args.explore_seconds)
    if args.until_perfect:
        if args.learn:
            todo = [s for s in segs if mastery_of(KB.load(s["id"], s)) != "mastered"]
            if todo:
                learn_many(args.name, todo, workers=args.workers, by=args.by or args.name, **learn_kw)
        began = time.time()
        perfect, reports = until_perfect(args.name, segs, runs=args.until_perfect, record=not args.no_record,
                                         label=args.scope or args.segment, by=args.by or args.name)
        print("\n%d run(s) in %.0f min:" % (len(reports), (time.time() - began) / 60))
        for r in reports:
            print("  run %-2s %-8s %d/%d passed  %5.0fs  %s" % (r["series"]["run"], "PERFECT" if r.get("perfect") else "-",
                                                               r["passed"], len(r["segments"]), r["duration"],
                                                               "; ".join(r.get("imperfect", [])[:3])[:150]))
        if not perfect:
            print("NO PERFECT RUN in %d tries: the learned lessons and repair variants are kept; run again, or teach the "
                  "segments that keep failing by hand (observe/step, learn --from-draft)." % len(reports))
            return
        report = perfect
    else:
        report = playtest(args.name, segs, record=not args.no_record, learn_first=args.learn, label=args.scope or args.segment,
                          by=args.by or args.name, workers=args.workers, learn_kw=learn_kw)
    folder = PLAYTESTS / report["id"]
    q = report["quality"]
    print("\n%s" % report["verdict"])
    print("quality: completion %.0f%%, stability %.0f%%, performance %.0f%%, pacing %.0f%%, friction %.0f%%" % tuple(
        100 * q[k] for k in ("completion", "stability", "performance", "pacing", "friction")))
    for f in report["findings"][:30]:
        print("  %-5s %s%s" % (f["sev"], f["text"][:150], "  @%s" % video.clock(f["t"]) if f.get("t") is not None else ""))
    print("REPORT %s" % (folder / "report.html"))
    if report.get("videoId"):
        print("VIDEO  %s  (agents: agentplay.py video sheet %s --from S --to S)" % (video.folder_of(video.find(report["videoId"])) / "video.mp4", report["videoId"]))
    print("dashboard: agentplay.py serve  (then open the Playtests tab)")


def cmd_observe(args):
    obs = observe(args.name, args.radius)
    if args.json:
        print(json.dumps(obs, indent=1))
        return
    if not obs.get("ok"):
        ap.finish(obs)
    print(render_observation(obs))


def render_observation(obs, seg_id=None):
    st = obs.get("status") or {}
    lines = ["STAGE %s  %s / %s   scene %s, deaths %s, held %s%s" % (
        st.get("stage"), st.get("chapter"), st.get("checkpoint"), st.get("scene"), st.get("deaths"), st.get("held"),
        ", PLAYER FROZEN (dialog)" if obs.get("playerFrozen") else "")]
    if obs.get("beats"):
        lines.append("STORY waits on:")
        for b in obs["beats"]:
            fields = ", ".join("%s=%s" % (k, (v.get("obj") + "@" + json.dumps(v.get("pos"))) if isinstance(v, dict) and "obj" in v
                                         else json.dumps(v)[:60]) for k, v in (b.get("fields") or {}).items())
            lines.append("  - %s (%s): %s" % (b.get("name"), b.get("type"), fields[:300]))
    lines.append("CROSSHAIR: %s" % obs.get("crosshair"))
    if obs.get("texts"):
        lines.append("SCREEN: " + " | ".join('"%s"' % t for t in obs["texts"][:8]))
    lines.append("NEAR:")
    for o in (obs.get("objects") or [])[:15]:
        lines.append("  %5.1fm  %-28s %s%s%s%s" % (o.get("dist", 0), o["name"][:28], "usable" if o.get("usable") else "-     ",
                                                 "  hold %.1fs" % o["hold"] if o.get("hold") else "",
                                                 '  "%s"' % o["prompt"] if o.get("prompt") else "",
                                                 "  in view" if o.get("inView") else ""))
    sid = seg_id or segment_at(st)
    q = KB.load(sid).get("q", {}).get(state_key(st), {}) if sid else {}
    lines.append("SUGGESTED (hints + knowledge%s):" % (" for " + sid if sid else ""))
    for a in candidates(obs, q, rng=random.Random(1))[:6]:
        known = q.get(a.key)
        lines.append("  %5.2f  %-30s %s%s" % (a.score, a.key[:30], json.dumps({k: v for k, v in a.steps[0].items() if k != "why"}),
                                            "   (known: %+.2f over %d)" % (known["value"], known["n"]) if known else ""))
    bad = sorted(((k, v) for k, v in q.items() if v["value"] < 0), key=lambda kv: kv[1]["value"])[:5]
    if bad:
        lines.append("KNOWN MISTAKES here: " + ", ".join("%s %+.2f" % (k, v["value"]) for k, v in bad))
    return "\n".join(lines)


def segment_at(st):
    """The segment whose 'at' condition matches this status (field conditions only), for hints."""
    try:
        for s in all_segments():
            at = s.get("at") or {}
            if at.get("field") and "eq" in at and st.get(at["field"]) == at["eq"]:
                return s["id"]
    except SystemExit:
        pass
    return None


def draft_file(name):
    return ap.run_dir(name) / "draft.jsonl"


def cmd_step(args):
    try:
        cmd = json.loads(args.json)
    except ValueError as e:
        ap.fail("bad JSON: %s" % e)
    before = observe(args.name)
    st0 = before.get("status") or {}
    t = time.time()
    a = send(args.name, cmd, op_timeout(cmd))
    kind, detail = classify(cmd, a)
    after = observe(args.name)
    st1 = after.get("status") or {}
    progressed = state_key(st1) != state_key(st0)
    micro = micro_key(after) != micro_key(before)
    entry = {"cmd": cmd, "why": args.why, "ok": kind is None, "kind": kind, "detail": detail[:200], "dt": round(time.time() - t, 2),
             "stateBefore": state_key(st0), "stateAfter": state_key(st1), "progressed": progressed, "micro": micro,
             "gate": gate_for(st0, []), "deathsBefore": st0.get("deaths"), "deathsAfter": st1.get("deaths")}
    with open(draft_file(args.name), "a", encoding="utf-8") as f:
        f.write(json.dumps(entry) + "\n")
    print(json.dumps(trim(a, 1500), indent=1))
    verdict = "STORY MOVED ON" if progressed else "something changed (held item, usable objects or text)" if micro else "no visible effect"
    if (st1.get("deaths") or 0) > (st0.get("deaths") or 0):
        verdict = "THE PLAYER DIED: a mistake; it is kept in the draft as one"
    print("-> %s%s. Draft: %d step(s) in %s" % ("FAILED (%s) " % kind if kind else "", verdict,
                                               len(draft_file(args.name).read_text(encoding="utf-8").splitlines()), draft_file(args.name)))
    if args.segment:
        met, _ = check(args.name, segment(args.segment)["goal"])
        if met:
            print("GOAL REACHED for %s: save it with agentplay.py learn %s --segment %s --from-draft" % (args.segment, args.name, args.segment))


def cmd_draft(args):
    path = draft_file(args.name)
    if args.clear:
        path.unlink(missing_ok=True)
        print("draft cleared")
        return
    if not path.exists():
        print("no draft: agentplay.py step %s '{...}' adds steps" % args.name)
        return
    for i, line in enumerate(path.read_text(encoding="utf-8").splitlines()):
        e = json.loads(line)
        print("%2d %-4s %-8s %s %s" % (i, "ok" if e["ok"] else "FAIL", "MOVED" if e["progressed"] else "micro" if e["micro"] else "-",
                                     json.dumps(e["cmd"])[:100], ("  # " + e["why"]) if e.get("why") else ""))


def learn_from_draft(name, seg, by):
    """Your draft -> a route: steps that worked and changed something, gated on the story state they worked in; then
    verified from the segment's start and stored. Steps that failed or killed the player become lessons."""
    path = draft_file(name)
    if not path.exists():
        ap.fail("no draft for %s: explore with agentplay.py step %s '{...}' first" % (name, name))
    entries = [json.loads(l) for l in path.read_text(encoding="utf-8").splitlines() if l.strip()]
    steps, mistakes = [], []
    for e in entries:
        died = (e.get("deathsAfter") or 0) > (e.get("deathsBefore") or 0)
        if died or not e["ok"]:
            mistakes.append("%s %s%s" % ("killed the player:" if died else "failed (%s):" % e.get("kind"), json.dumps(e["cmd"])[:120],
                                          " (tried: %s)" % e["why"] if e.get("why") else ""))
            continue
        if e["cmd"].get("op") not in ("observe", "status", "screen", "perf", "errors", "find", "shot", "shots"):
            # an agent's step is deliberate: keep it even without a visible effect (stepping out of a hazard's line shows nothing)
            step = dict(e["cmd"])
            if e.get("gate") and e["progressed"]:
                step["when"] = e["gate"]
            if e.get("why"):
                step["why"] = e["why"]
            steps.append(step)
    if not steps:
        ap.fail("the draft has no step that changed anything")
    log("verifying the draft route (%d of %d steps) from the segment's start" % (len(steps), len(entries)))
    ep = run_episode(name, seg, steps, by=by)

    def apply(r):
        rid = add_route(r, steps, "agent-draft", by, why="distilled from %s's draft" % by)
        apply_episode(r, rid, ep)
        for m in mistakes:
            add_lesson(r, m, "pitfall", by, auto=True)
        return rid
    rid = KB.update(seg["id"], apply, seg, by)
    print("route %s: %s (%s)" % (rid, ep["outcome"].upper(), ep.get("failKind") or "%.0fs" % ep["time"]))
    if ep["outcome"] == "win":
        print("Saved. Let the learner make it robust: agentplay.py learn %s --segment %s" % (name, seg["id"]))
        path.unlink(missing_ok=True)
    else:
        print("It did not win from the start: fix the draft (agentplay.py draft %s), or keep exploring" % name)


def cmd_kb(args):
    if args.action == "status":
        segs = resolve(args.scope) if args.scope else all_segments()
        rows = table(segs)
        counts = {}
        for r in rows:
            counts[r["mastery"]] = counts.get(r["mastery"], 0) + 1
        print("%d segments: %s" % (len(rows), ", ".join("%s %d" % kv for kv in sorted(counts.items()))))
        for r in rows:
            print("  %3s %-44s %-11s routes %-2d runs %-3d streak %-2d best %6s  friction %.2f%s" % (
                r["order"], r["id"][:44], r["mastery"], r["routes"], r["runs"], r["streak"],
                "%.0fs" % r["bestTime"] if r["bestTime"] else "-", r["friction"], "  ! " + r["needsAgent"][:60] if r.get("needsAgent") else ""))
        return
    if args.action == "brief":
        return brief(args)
    if args.action in ("show", "lesson", "route", "retire", "reset") and not args.segment:
        ap.fail("name the segment (or 'global' for a lesson)")
    if args.action == "lesson":
        text = " ".join(args.text)
        if not text:
            ap.fail("write the lesson text")
        if args.segment == "global":
            KB.update_global(lambda g: add_lesson(g, text, args.kind, args.by or "agent"))
        else:
            seg = segment(args.segment)
            KB.update(seg["id"], lambda r: add_lesson(r, text, args.kind, args.by or "agent"), seg, args.by)
        print("lesson saved")
        return
    seg = segment(args.segment)
    if args.action == "show":
        rec = KB.load(seg["id"], seg)
        if args.json:
            print(json.dumps(seg_full(rec), indent=1))
            return
        s = seg_summary(rec)
        print("%s  %s  mastery %s, %d runs, %d wins, friction %.2f" % (s["id"], s["label"], s["mastery"], s["runs"], s["wins"], s["friction"]))
        if rec.get("needsAgent"):
            print("NEEDS AGENT: " + json.dumps(rec["needsAgent"], indent=1))
        for r in seg_full(rec)["routeList"][:6]:
            print("route %s%s (%s) %d/%d wins, streak %d, best %s" % (r["id"], " RETIRED" if r.get("retired") else "", r.get("origin"),
                                                                    r["wins"], r["runs"], r["streak"], r.get("bestTime")))
            for st in r["steps"]:
                print("    %s%s" % (json.dumps({k: v for k, v in st.items() if k != "why"})[:150], "   # " + st["why"][:70] if st.get("why") else ""))
        for lesson in rec.get("lessons", [])[-10:]:
            print("lesson [%s] %s" % (lesson["kind"], lesson["text"][:200]))
        return
    if args.action == "route":
        if not args.file:
            ap.fail("--file steps.json (a JSON list of commands) is required")
        steps = json.loads(Path(args.file).read_text(encoding="utf-8"))
        if not isinstance(steps, list):
            ap.fail("the file must hold a JSON list of commands")
        rid = KB.update(seg["id"], lambda r: add_route(r, steps, "agent", args.by or "agent", why=" ".join(args.text) or None), seg, args.by)
        print("route %s added; agentplay.py learn NAME --segment %s makes it robust" % (rid, seg["id"]))
    elif args.action == "retire":
        route_id = " ".join(args.text)

        def retire(r):
            for x in r["routes"]:
                if x["id"] == route_id:
                    x["retired"] = True
        KB.update(seg["id"], retire, seg, args.by)
        print("retired " + route_id)
    elif args.action == "reset":
        if not args.yes:
            ap.fail("reset forgets everything learned about %s: add --yes" % seg["id"])
        KB.file(seg["id"]).unlink(missing_ok=True)
        print("forgot " + seg["id"])


def brief(args):
    """The first thing an agent reads: the scope's mastery, what needs an agent, global and recent lessons, next steps."""
    segs = resolve(args.scope) if args.scope else all_segments()
    rows = table(segs)
    counts = {}
    for r in rows:
        counts[r["mastery"]] = counts.get(r["mastery"], 0) + 1
    print("KNOWLEDGE BASE %s" % KB.root)
    print("%d segments: %s" % (len(rows), ", ".join("%s %d" % kv for kv in sorted(counts.items()))))
    lessons = KB.load_global().get("lessons", [])
    if lessons:
        print("GAME-WIDE LESSONS:")
        for lesson in lessons[-10:]:
            print("  [%s] %s" % (lesson["kind"], lesson["text"][:200]))
    need = [r for r in rows if r["mastery"] in ("needs-agent", "stale")]
    if need:
        print("NEED AN AGENT / REGRESSED:")
        for r in need:
            print("  %-40s %s  %s" % (r["id"], r["mastery"], (r.get("needsAgent") or "")[:120]))
    by_id = {r["id"]: r for r in KB.records()}
    recent = []
    for s in segs:
        for lesson in (by_id.get(s["id"]) or {}).get("lessons", []):
            if not lesson.get("auto"):
                recent.append((lesson["at"], s["id"], lesson))
    if recent:
        print("AGENT LESSONS (newest):")
        for at, sid, lesson in sorted(recent, reverse=True)[:12]:
            print("  %s [%s] %s" % (sid, lesson["kind"], lesson["text"][:160]))
    todo = [r for r in rows if r["mastery"] not in ("mastered",)]
    print("NEXT:")
    if todo:
        print("  learn the rest: agentplay.py learn NAME --scope %s --workers 2" % (args.scope or "all"))
    print("  play it: agentplay.py playtest NAME --scope %s   (records a video and writes a report)" % (args.scope or "all"))
    print("  details: agentplay.py kb show SEGMENT;  add what you learned: agentplay.py kb lesson SEGMENT|global \"text\"")


def register(sub, ap_module):
    global ap, video, KB, EPISODES, PLAYTESTS
    ap = ap_module
    import agentplay_video
    video = agentplay_video
    KB = Knowledge(ap.ROOT / ap.CONFIG.get("knowledgeDir", "AgentPlayKnowledge"))
    EPISODES = ap.HOME / "episodes"
    PLAYTESTS = ap.HOME / "playtests"

    def scope_args(p, segment=True):
        p.add_argument("--scope", help="segments: ids, prefixes, numbers, a-b, comma separated (the project may add chapters)")
        if segment:
            p.add_argument("--segment", help="one segment id")

    def learn_args(p):
        p.add_argument("--attempts", type=int, default=10, help="runs per segment (explorations and replays)")
        p.add_argument("--streak", type=int, default=3, help="wins in a row that master a segment")
        p.add_argument("--explore-seconds", type=int, default=240, help="budget of one exploration")
        p.add_argument("--workers", type=int, default=1, help="parallel worker players NAME-w1.. (about 0.7 GB each)")
        p.add_argument("--by", help="your agent name, kept with what you add")

    p = sub.add_parser("segments", help="the segments the learner plays (project-defined)")
    p.add_argument("--scope")
    p.set_defaults(func=cmd_segments)

    p = sub.add_parser("learn", help="learn segments until mastered; the knowledge base keeps what is learned")
    p.add_argument("name")
    scope_args(p)
    learn_args(p)
    p.add_argument("--record", choices=("none", "best", "all"), default="none", help="video: none, the mastered run, every run")
    p.add_argument("--no-optimize", action="store_true", help="do not try faster variants once mastered")
    p.add_argument("--headless", action="store_true", help="worker players without GPU (lighter; no rendering, no video)")
    p.add_argument("--force", action="store_true", help="learn on even when already mastered")
    p.add_argument("--from-draft", action="store_true", help="turn your step draft into a verified route (with --segment)")
    p.set_defaults(func=cmd_learn)

    p = sub.add_parser("playtest", help="play a chunk of the game with the learned routes, record it, write a quality report")
    p.add_argument("name")
    scope_args(p)
    learn_args(p)
    p.add_argument("--learn", action="store_true", help="learn segments that are not mastered first")
    p.add_argument("--until-perfect", type=int, metavar="RUNS", help="repeat whole chained runs, learning between them, "
                   "until one is perfect (every segment passed, chained, no deaths or errors); at most RUNS runs")
    p.add_argument("--no-record", action="store_true")
    p.set_defaults(func=cmd_playtest)

    p = sub.add_parser("observe", help="what the bot sees and can use, what the story waits for, suggested actions")
    p.add_argument("name")
    p.add_argument("--radius", type=float, default=14)
    p.add_argument("--json", action="store_true")
    p.set_defaults(func=cmd_observe)

    p = sub.add_parser("step", help="send one command while exploring by hand; kept in the player's draft")
    p.add_argument("name")
    p.add_argument("json")
    p.add_argument("--why", help="what you are trying (kept in the route)")
    p.add_argument("--segment", help="say GOAL REACHED when this segment's goal happens")
    p.set_defaults(func=cmd_step)

    p = sub.add_parser("draft", help="show or clear a player's exploration draft")
    p.add_argument("name")
    p.add_argument("--clear", action="store_true")
    p.set_defaults(func=cmd_draft)

    p = sub.add_parser("kb", help="the shared knowledge base: status, brief, show, lesson, route, retire, reset")
    p.add_argument("action", choices=("status", "brief", "show", "lesson", "route", "retire", "reset"))
    p.add_argument("segment", nargs="?", help="segment id ('global' for a game-wide lesson)")
    p.add_argument("text", nargs="*", help="lesson text / route note / route id to retire")
    p.add_argument("--scope")
    p.add_argument("--kind", default="tip", choices=("tip", "pitfall", "bug", "fix"))
    p.add_argument("--file", help="route: JSON list of commands")
    p.add_argument("--json", action="store_true")
    p.add_argument("--by")
    p.add_argument("--yes", action="store_true")
    p.set_defaults(func=cmd_kb)

    p = sub.add_parser("playtest-report", help="rewrite a playtest's report (scores and report.html) from its report.json")
    p.add_argument("id")
    p.set_defaults(func=cmd_playtest_report)
