"""Deep tests for agentplay.py: try everything a section offers, to find what breaks.

A playtest finishes the game the clean way; a deep test does not care about finishing. In each section (segment) it tries,
with the same real inputs a player has:
- every interactable in reach: walks there, aims, presses (or holds) the interact key, and sees what changed (its prompt,
  its position, what is held, new text, the story, errors in the log, a death, a hitch);
- with an item in hand: that item on the other interactables, then the other moves;
- every move of the project's input map (Assets/**/*.inputactions: jump, crouch, sprint, attack, drop, item keys, pause...):
  a tap each, and held while walking for modifiers (sprint, crouch);
- walking probes in 8 directions (blocks, falls through the floor, getting stuck).
After anything that changes the story, what is held or the player's life it resets to the section's start. When the story
moved in a way the learned route does not, it checks the section can still be finished (a softlock check).
It is smart about what it tries (`--exhaustive` turns that off): moves for an item (drop, next, use...) only with an item
in hand; a move that did nothing in a context twice, or the same harmless thing in three sections, is not repeated; of
look-alike objects (a dresser's drawers) two are tried and the rest only if those differ; kinds that behaved the same three
times elsewhere are not retried; objects the running story points at go first; areas already walked around are not probed
again. Every skip is listed with its reason. The effects memory is AgentPlayKnowledge/coverage/_effects.json.
Results are shared: AgentPlayKnowledge/coverage/<segment>.json keeps every action's outcome with its build, so the next
agent skips what was tested on the same build (`--retest` to redo) and retests after a new build. The report (with one
video, a caption per action) is in Builds/AgentPlay/playtests/<id>-deep/ and on the dashboard and phone.

  agentplay.py deeptest NAME --scope S [--states start|all] [--radius 12] [--max 80] [--retest] [--no-record]
  agentplay.py deeptest NAME --scope S --only door          every door (name, path or prompt matching), nothing else
"""
import json
import math
import re
import time
from pathlib import Path

ap = None
learn = None
video = None
COVERAGE = None

# Input actions that are not moves to try on their own (walking and looking are the probes; interact is the object tests).
NOT_MOVES = {"move", "look", "interact", "point", "click", "scrollwheel", "navigate", "rightclick", "middleclick", "submit",
             "cancel", "trackeddeviceposition", "trackeddeviceorientation"}
HOLD_MOVES = re.compile(r"sprint|run|crouch|walk|aim|block|slide", re.I)
FAR_MOVES = 3.5          # metres of a walking probe
SETTLE = 1.2             # seconds an action is given to show its effect
SEVERITY = {"error": "BUG", "fell": "BUG", "paused": "BUG", "softlock": "WARN", "death": "SOFT", "failed": "WARN", "hitch": "WARN"}
# The game took the controls during the action (a scene started on the way): not a failure of what was tried.
GAME_HAS_IT = re.compile(r"does not follow the mouse|controls (are )?off|the game has|frozen|look off", re.I)


def log(text):
    learn.log(text, "deep")


# ------------------------------------------------------------------ what can be tried

def key_command(binding):
    """An input binding path -> a harness command ({"op": "key"} or {"op": "click"}), or None."""
    m = re.match(r"<(Keyboard|Mouse)>/(\w+)$", binding or "")
    if not m:
        return None
    device, control = m.groups()
    if device == "Mouse":
        return {"op": "click", "button": {"leftButton": 0, "rightButton": 1}[control]} if control in ("leftButton", "rightButton") else None
    if re.fullmatch(r"\d", control):
        return {"op": "key", "key": "Digit" + control}
    if len(control) == 1:
        return {"op": "key", "key": control.upper()}
    return {"op": "key", "key": control[0].upper() + control[1:]}


def input_moves():
    """Every keyboard/mouse action of the project's input maps (first binding each), plus agentplay.json "deepMoves"."""
    moves, seen = [], set()
    for path in sorted((ap.ROOT / "Assets").rglob("*.inputactions")):
        try:
            data = json.loads(path.read_text(encoding="utf-8-sig"))
        except (OSError, ValueError):
            continue
        for amap in data.get("maps", []):
            for b in amap.get("bindings", []):
                action = b.get("action") or ""
                if not action or b.get("isComposite") or b.get("isPartOfComposite") or action.lower() in NOT_MOVES:
                    continue
                cmd = key_command(b.get("path"))
                if cmd is None or action.lower() in seen:
                    continue
                seen.add(action.lower())
                moves.append({"action": action, "map": amap.get("name"), "binding": b.get("path"), "cmd": cmd})
    for extra in ap.CONFIG.get("deepMoves", []):
        moves.append(extra)
    return moves


def move_actions(moves):
    out = []
    for m in moves:
        cmd = dict(m["cmd"])
        if cmd["op"] == "key":
            cmd["s"] = 0.15
        out.append({"key": "move:%s" % m["action"], "kind": "move", "label": "%s (%s)" % (m["action"], m["binding"].split("/")[-1]),
                    "cmds": [cmd]})
        if m["action"].lower() == "pause" or cmd.get("key") == "Escape":
            out[-1]["cmds"] = [cmd, {"sleep": 1.0}, dict(cmd)]  # open and close again (the wait on this side: game time stops)
            out[-1]["label"] = m["action"] + " (open and close)"
        elif HOLD_MOVES.search(m["action"]) and cmd["op"] == "key":
            out.append({"key": "move:%s+walk" % m["action"], "kind": "move", "label": "%s held while walking" % m["action"],
                        "cmds": [{"op": "hold", "key": cmd["key"]}, {"op": "key", "key": "W", "s": 1.2},
                                 {"op": "release", "key": cmd["key"]}]})
    return out


def probe_actions(base_pos):
    out = []
    for k in range(8):
        a = math.radians(k * 45)
        dest = [round(base_pos[0] + FAR_MOVES * math.sin(a), 2), base_pos[1], round(base_pos[2] + FAR_MOVES * math.cos(a), 2)]
        out.append({"key": "probe:%d" % (k * 45), "kind": "probe", "label": "walk %d deg, %.1f m" % (k * 45, FAR_MOVES),
                    "cmds": [{"op": "walk", "pos": dest, "timeout": 8, "direct": True},
                             {"op": "walk", "pos": base_pos, "timeout": 8, "direct": True}]})
    return out


def object_actions(objs, limit):
    out, seen = [], set()
    for o in sorted(objs, key=lambda o: o.get("dist", 99)):
        path = o.get("path") or o.get("name")
        if not o.get("usable") or path in seen:
            continue
        seen.add(path)
        parts = str(path).split("/")
        where = parts[-2] + "/" + parts[-1] if len(parts) >= 2 else str(o.get("name"))  # "Door01 (9) Hall1_Door01/Door"
        out.append({"key": "interact:%s" % path, "kind": "interact", "label": "interact %s%s" % (
            where, (" (" + o["prompt"].strip() + ")") if o.get("prompt") else ""), "target": path, "obj": o,
            "cmds": [{"op": "interact", "target": path}]})
        if len(out) >= limit:
            break
    return out


# ------------------------------------------------------------------ one action and what it changed

def snap(name, radius):
    o = learn.observe(name, radius)
    st = o.get("status") or learn.status(name)
    objs = {(x.get("path") or x.get("name")): x for x in o.get("objects") or []}
    try:
        issues = learn.issue_counts(name)
    except SystemExit:
        issues = {}
    return {"status": st, "objects": objs, "texts": list(o.get("texts") or []), "issues": issues, "beats": o.get("beats") or []}


def pos_of(st):
    return learn.parse_pos(st.get("player")) if st else None


def wait_for_control(name, seconds=8.0):
    """A player waits until the game gives them control back (a scripted look or placement after a reset)."""
    until = time.time() + seconds
    while time.time() < until:
        o = learn.observe(name, 2)
        if not o.get("playerFrozen") and (o.get("attention") or {}).get("focus") != "the game has the view":
            return True
        time.sleep(0.3)
    return False


def baseline(name, seconds=10.0):
    """What happens at this point when the player does nothing: the story steps it goes through by itself, and whether
    standing still kills. The control an action's effect is measured against."""
    st0 = learn.status(name)
    seen, died = [], False
    deaths0 = st0.get("deaths") or 0
    until = time.time() + seconds
    while time.time() < until:
        st = learn.status(name)
        key = learn.state_key(st)
        if key != learn.state_key(st0) and key not in seen:
            seen.append(key)
        if (st.get("deaths") or 0) > deaths0 or st.get("dying"):
            died = True
            break
        time.sleep(0.4)
    return {"states": seen, "died": died, "quiet": not seen and not died}


def settle(name, quiet=1.5, most=8.0):
    """Waits until the story has stopped changing for `quiet` seconds (a scene finishing), at most `most` seconds."""
    until, last, since = time.time() + most, None, time.time()
    while time.time() < until:
        key = learn.state_key(learn.status(name))
        if key != last:
            last, since = key, time.time()
        elif time.time() - since >= quiet:
            return True
        time.sleep(0.3)
    return False


def run_action(name, act, radius, recording=False):
    wait_for_control(name)
    settle(name)
    before = snap(name, radius)
    learn.send(name, {"op": "perf", "reset": True}, 30)
    if recording:
        video.mark(name, act["label"], "mark")
    answers, failed = [], None
    for cmd in act["cmds"]:
        if "sleep" in cmd:
            time.sleep(float(cmd["sleep"]))
            continue
        a = learn.send(name, cmd, learn.op_timeout(cmd))
        answers.append(a)
        if not a.get("ok") and failed is None:
            failed = a.get("error") or "failed"
    time.sleep(SETTLE)  # on this side: game time may be stopped (a pause menu left open)
    after = snap(name, radius)
    if (after["status"].get("timeScale") == 0) != (before["status"].get("timeScale") == 0):
        answers.append({"ok": True, "paused": after["status"].get("timeScale") == 0})
    perf = learn.send(name, {"op": "perf"}, 30)
    res = classify(act, before, after, answers, failed, perf)
    res["before"], res["after"] = before, after
    return res


def classify(act, before, after, answers, failed, perf):
    sb, sa = before["status"], after["status"]
    notes, outcome = [], None
    errors = learn.new_issues(before["issues"], after["issues"])
    pb, pa = pos_of(sb), pos_of(sa)
    if sa.get("timeScale") == 0 and sb.get("timeScale") != 0:
        outcome = "paused"
        notes.append("the game is still paused afterwards (time scale 0)")
    elif errors:
        outcome = "error"
        notes.append("; ".join(errors)[:300])
    elif (sa.get("deaths") or 0) > (sb.get("deaths") or 0) or sa.get("dying"):
        outcome = "death"
        notes.append(str(sa.get("lastDeath") or "the player died"))
    elif pb and pa and pa[1] < pb[1] - 3.0:
        outcome = "fell"
        notes.append("the player dropped %.1f m (%s -> %s)" % (pb[1] - pa[1], pb, pa))
    elif sa.get("scene") != sb.get("scene") or learn.state_key(sa) != learn.state_key(sb):
        outcome = "story"
        notes.append("story: %s -> %s" % (learn.state_key(sb).split("|", 1)[-1], learn.state_key(sa).split("|", 1)[-1]))
    elif sa.get("held") != sb.get("held"):
        outcome = "held"
        notes.append("held: %s -> %s" % (sb.get("held"), sa.get("held")))
    elif failed and GAME_HAS_IT.search(str(failed)):
        outcome = "busy"
        notes.append("the game had the controls: " + str(failed)[:160])
    elif failed:
        outcome = "failed"
        notes.append(str(failed)[:200])
    else:
        changed = []
        target = act.get("target")
        for path, o in after["objects"].items():
            b = before["objects"].get(path)
            if b is None or (path != target and not (b.get("usable") or o.get("usable"))):
                continue  # what the player carries or cannot use sways along: not an effect
            if (b.get("prompt") or "") != (o.get("prompt") or ""):
                changed.append("%s: '%s' -> '%s'" % (o.get("name"), (b.get("prompt") or "").strip(), (o.get("prompt") or "").strip()))
            elif b.get("pos") and o.get("pos") and math.dist(b["pos"], o["pos"]) > 0.05 and (path == target or len(changed) < 2):
                changed.append("%s moved %.2f m" % (o.get("name"), math.dist(b["pos"], o["pos"])))
        new_texts = [t for t in after["texts"] if t not in before["texts"]]
        if new_texts:
            changed.append("text: " + " | ".join(new_texts)[:160])
        moved = [a for a in answers if isinstance(a.get("player"), str)]
        if act["kind"] == "move" and moved and pb:
            mid = learn.parse_pos(moved[0]["player"])
            if mid and math.dist(mid, pb) > 0.08:
                changed.append("the player moved %.2f m" % math.dist(mid, pb))
        if act["kind"] == "probe":
            walk = answers[0] if answers else {}
            changed.append("reached" if walk.get("reached") else "stopped: %s" % walk.get("why"))
            outcome = "moved" if walk.get("reached") else "blocked"
        if outcome is None:
            outcome = "changed" if changed else "nothing"
        notes += changed
    hitch = (perf.get("worstMs") or 0) > 250
    if hitch:
        notes.append("a %.0f ms frame" % perf["worstMs"])
    return {"outcome": outcome, "detail": "; ".join(notes)[:400], "hitch": hitch, "worstMs": perf.get("worstMs")}


# ------------------------------------------------------------------ shared coverage

def coverage_file(seg_id):
    return COVERAGE / (learn.Knowledge.slug(seg_id) + ".json")


def load_coverage(seg_id):
    path = coverage_file(seg_id)
    if path.exists():
        try:
            return json.loads(path.read_text(encoding="utf-8"))
        except ValueError:
            pass
    return {"segment": seg_id, "actions": {}}


def save_result(seg_id, key, result):
    COVERAGE.mkdir(parents=True, exist_ok=True)
    with learn.KB.lock(seg_id + ".coverage"):
        cov = load_coverage(seg_id)
        cov["actions"][key] = result
        cov["updated"] = learn.now_text()
        learn.atomic_write(coverage_file(seg_id), json.dumps(cov, indent=1, sort_keys=True))


# ------------------------------------------------------------------ being smart: what is still worth trying

ITEM_MOVES = re.compile(r"\b(drop|throw|use|next|previous|prev|item|inventory|equip|switch)\b", re.I)  # whole words: not "pause"
HARMLESS = {"nothing", "changed", "moved", "blocked"}
SIBLINGS_FIRST = 2   # look-alike objects tested before the rest are judged by them


def effects_file():
    return COVERAGE / "_effects.json"


def load_effects():
    try:
        return json.loads(effects_file().read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {"moves": {}, "kinds": {}, "probed": {}}


def seed_effects():
    """The first time: learn the effects memory from the coverage already recorded (so earlier deep tests count)."""
    if effects_file().exists():
        return
    eff = {"moves": {}, "kinds": {}, "probed": {}}
    seen = set()
    for report in learn.all_playtests():  # the deep tests' reports keep every action with its label
        if report.get("kind") != "deep":
            continue
        for a in report.get("actions", []):
            outcome, key = a.get("outcome"), a.get("key") or ""
            # Reports without a control credited actions with what happened by itself: their story and death outcomes
            # teach nothing about the action, so they are left out.
            if outcome in (None, "skipped", "busy", "anyway", "story", "death", "softlock") or a.get("known") \
                    or (a.get("segment"), a.get("state"), key) in seen:
                continue
            seen.add((a.get("segment"), a.get("state"), key))
            label = a.get("label") or ""
            if key.startswith("move:"):
                cell_key = "%s|%s" % (key.split("+with:")[0], "holding" if "+with:" in key else "empty-handed")
                table = eff["moves"]
            elif key.startswith("interact:"):
                name = label[len("interact "):].split(" (")[0].split(" holding ")[0]
                prompt = label[label.find("(") + 1:label.find(")")] if "(" in label else ""
                cell_key = kind_of({"name": name, "prompt": prompt})
                table = eff["kinds"]
            else:
                continue
            cell = table.setdefault(cell_key, {"tries": 0, "outcomes": {}, "sections": []})
            cell["tries"] += 1
            cell["outcomes"][outcome] = cell["outcomes"].get(outcome, 0) + 1
            if a.get("segment") and a["segment"] not in cell["sections"]:
                cell["sections"].append(a["segment"])
    COVERAGE.mkdir(parents=True, exist_ok=True)
    learn.atomic_write(effects_file(), json.dumps(eff, indent=1, sort_keys=True))


def note_effect(kind, key, outcome, seg_id):
    """Remembers what an action did (moves per context, objects per kind) across sections and agents. What happens anyway
    (the control showed it) or could not be tried (the game had the controls) teaches nothing about the action."""
    if outcome in ("anyway", "busy", "skipped"):
        return
    COVERAGE.mkdir(parents=True, exist_ok=True)
    with learn.KB.lock("coverage-effects"):
        eff = load_effects()
        cell = eff.setdefault(kind, {}).setdefault(key, {"tries": 0, "outcomes": {}, "sections": []})
        cell["tries"] += 1
        cell["outcomes"][outcome] = cell["outcomes"].get(outcome, 0) + 1
        if seg_id not in cell["sections"]:
            cell["sections"] = (cell["sections"] + [seg_id])[-12:]
        learn.atomic_write(effects_file(), json.dumps(eff, indent=1, sort_keys=True))


def note_probed(scene, pos):
    with learn.KB.lock("coverage-effects"):
        eff = load_effects()
        spots = eff.setdefault("probed", {}).setdefault(scene or "?", [])
        spots.append([round(pos[0], 1), round(pos[2], 1)])
        eff["probed"][scene or "?"] = spots[-200:]
        learn.atomic_write(effects_file(), json.dumps(eff, indent=1, sort_keys=True))


def kind_of(obj):
    """Look-alike objects share a kind: the name without numbers and copies, and the verb of the prompt ("to Open")."""
    name = re.sub(r"\(\d+\)|\.\d+|\d+", "", obj.get("name") or "")
    name = re.sub(r"[\s_]+", "", name).lower()
    words = (obj.get("prompt") or "").strip().split()
    verb = words[1].lower() if len(words) > 1 and words[0].lower() == "to" else (words[0].lower() if words else "")
    return name + "|" + verb


def story_objects(observation):
    """Names of the objects the running story steps point at (they matter most, and are always tried)."""
    names = set()

    def walk(v):
        if isinstance(v, dict):
            if v.get("obj"):
                names.add(str(v["obj"]))
            for x in v.values():
                walk(x)
        elif isinstance(v, list):
            for x in v:
                walk(x)
    for b in observation.get("beats") or []:
        walk(b.get("fields"))
    return names


def plan(queue, held, scene, base_pos, story, exhaustive):
    """Orders the candidate actions by what they can teach and drops what cannot teach anything new.
    Returns (actions to try, {kind: look-alike objects held back}, [(action, reason) skipped])."""
    if exhaustive:
        return queue, {}, []
    eff = load_effects()
    keep, skipped, siblings, per_kind = [], [], {}, {}
    ctx = "holding" if held else "empty-handed"
    for act in queue:
        if act["kind"] == "move":
            name = act["key"].split(":", 1)[1].split("+")[0]
            if ITEM_MOVES.search(re.sub(r"(?<=[a-z0-9])(?=[A-Z])", " ", name)) and not held:  # UseItem -> Use Item
                skipped.append((act, "needs something in hand"))
                continue
            cell = eff.get("moves", {}).get("%s|%s" % (act["key"].split("+with:")[0], ctx))
            outs = set((cell or {}).get("outcomes", {}))
            if cell and cell["tries"] >= 2 and outs <= {"nothing"}:
                skipped.append((act, "did nothing %s in %d earlier tries" % (ctx, cell["tries"])))
                continue
            if cell and len(cell.get("sections", [])) >= 3 and outs <= HARMLESS:
                skipped.append((act, "the same harmless effect in %d sections already (%s)" % (len(cell["sections"]), ", ".join(sorted(outs)))))
                continue
            keep.append((2, act))
        elif act["kind"] == "probe":
            spots = eff.get("probed", {}).get(scene or "?", [])
            if any(math.hypot(p[0] - base_pos[0], p[1] - base_pos[2]) < 3.0 for p in spots):
                skipped.append((act, "this area was walked around before"))
                continue
            keep.append((3, act))
        else:
            obj = act.get("obj") or {}
            k = kind_of(obj)
            act["kindKey"] = k
            if obj.get("name") in story or str(act.get("target", "")).split("/")[-1] in story:
                keep.append((0, dict(act, why="the story step points at it")))
                continue
            cell = eff.get("kinds", {}).get(k)
            outs = set((cell or {}).get("outcomes", {}))
            if cell and cell["tries"] >= 3 and len(outs) == 1 and outs <= HARMLESS:
                skipped.append((act, "behaved the same in %d earlier tries of its kind (%s)" % (cell["tries"], next(iter(outs)))))
                continue
            per_kind[k] = per_kind.get(k, 0) + 1
            if per_kind[k] > SIBLINGS_FIRST:
                siblings.setdefault(k, []).append(act)
                continue
            keep.append((1 if not cell else 2, act))
    keep.sort(key=lambda pa: pa[0])
    return [a for _, a in keep], siblings, skipped


# ------------------------------------------------------------------ a section

def reach_state(name, seg, prefix):
    """The section's start (its setup), then the learned route's steps up to a later story state, then the first quiet
    moment: a scene that plays by itself is let finish, as a player would, so actions are tried where the player acts."""
    if learn.run_setup(name, seg):
        return False
    learn.send(name, {"op": "wait", "s": 1.0}, 30)
    for step in prefix:
        if step.get("when"):
            learn.send(name, {"op": "until", "cond": step["when"], "timeout": 30, "soft": True}, 60)
        learn.send(name, {k: v for k, v in step.items() if k not in learn.META_KEYS}, learn.op_timeout(step))
    settle(name, quiet=3.0, most=25.0)
    return True


def states_of(seg, which):
    """[(label, route prefix)]: the start, and with which == "all" each story state the learned route waits for."""
    out = [("start", [])]
    if which != "all":
        return out
    route = learn.best_route(learn.KB.load(seg["id"], seg)) or {}
    steps = route.get("steps") or []
    for i, step in enumerate(steps):
        if step.get("when") and i > 0:
            gate = " + ".join(c.get("contains", "") for c in step["when"].get("all", []))
            out.append((gate or "step %d" % i, steps[:i]))
    return out


def normal_path(seg):
    """The story steps a section normally goes through: the learned routes' gates and what recent episodes saw."""
    beats = set()
    for r in learn.live_routes(learn.KB.load(seg["id"], seg)):
        for step in r.get("steps", []):
            for c in (step.get("when") or {}).get("all", []):
                if c.get("contains"):
                    beats.add(c["contains"])
    folder = learn.EPISODES / learn.Knowledge.slug(seg["id"])
    for path in sorted(folder.glob("*.json"))[-12:] if folder.exists() else []:
        try:
            ep = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            continue
        for row in ep.get("steps", []):
            beats.update(b for b in str(row.get("state", "")).split("running=")[-1].split("+") if b)
        for _, running in ep.get("states", []):
            beats.update(running or [])
    return beats


def softlock_check(name, seg, base_pos, rounds=4, quiet=30):
    """After the story moved on its own way: can the section still be finished? A softlock is a story that stops moving.
    Tries the learned route; while it fails but the story still moves (a slow scene, someone in the way), waits for the next
    change and tries again from there (and once from back where the section starts). Only a story that does not move for
    `quiet` seconds after a failed try is reported. Returns the last episode (outcome "win" when finished)."""
    route = learn.best_route(learn.KB.load(seg["id"], seg))
    if not route:
        return None
    ep = None
    for k in range(rounds):
        ep = learn.run_episode(name, seg, route["steps"], setup=False, by="deep", budget=min(seg.get("budget", 150), 90))
        if ep["outcome"] == "win":
            return ep
        if k == 0 and base_pos:
            learn.send(name, {"op": "walk", "pos": base_pos, "timeout": 25}, 60)
            continue
        moved = learn.send(name, {"op": "until", "cond": {"any": [seg["goal"], {"field": "running", "changed": True},
                                                                  {"field": "stage", "changed": True}]},
                                  "timeout": quiet, "soft": True}, quiet + 30)
        if learn.check(name, seg["goal"])[0]:
            return dict(ep, outcome="win")
        if not moved.get("met"):
            ep["failDetail"] = "the story did not move for %d s after the route failed (%s)" % (quiet, ep["failDetail"][:120])
            return ep
    return ep


def deep_segment(name, seg, report, args, recording):
    t_start = video.elapsed(name) if recording else None
    if recording:
        video.mark(name, "deep: " + seg.get("label", seg["id"]), "segment")
    cov = load_coverage(seg["id"])
    build = learn.build_of(name)
    only = re.compile(args.only, re.I) if getattr(args, "only", None) else None
    moves = [] if args.no_moves or (only and not args.moves) else move_actions(input_moves())
    row = {"id": seg["id"], "label": seg.get("label", seg["id"]), "outcome": "pass", "mastery": None, "start": t_start,
           "end": None, "duration": 0, "chained": True, "route": None, "deaths": 0, "errors": [], "worstFrameMs": None,
           "p95FrameMs": None, "fpsAvg": None, "blocked": 0, "passiveRatio": 0, "notes": [], "friction": 0, "episode": None,
           "coverage": {"tested": 0, "known": 0, "outcomes": {}}}
    t0 = time.time()
    if seg.get("deep") is False:  # the project says nothing a deep test can try is here (a menu without a player)
        row["notes"].append("not deep-tested: the segment says there is nothing to try (deep: false)")
        return row
    known_path = normal_path(seg)
    for state_label, prefix in states_of(seg, args.states):
        if not reach_state(name, seg, prefix):
            row["notes"].append("could not reach state %s" % state_label)
            continue
        if learn.check(name, seg["goal"])[0]:  # the story already left this section: testing here would test the next one
            row["notes"].append("at %s the section has already ended by itself (nothing of its own to try)" % state_label)
            continue
        control = baseline(name)
        if not control["quiet"]:
            row["notes"].append("at %s, doing nothing: %s" % (state_label, "the player dies" if control["died"] else
                                "the story moves on by itself (%s)" % " -> ".join(s.split("|")[-1] for s in control["states"][:3])))
            if recording:
                video.mark(name, "control: doing nothing here %s" % ("kills" if control["died"] else "moves the story"), "mark")
            if not reach_state(name, seg, prefix):
                row["notes"].append("could not reach state %s again" % state_label)
                continue
        base = snap(name, args.radius)
        base_pos = pos_of(base["status"]) or [0, 0, 0]
        state = learn.state_key(base["status"])
        objects = object_actions(list(base["objects"].values()), 400)
        if only:  # every matching interactable; one already tried in this run with the same prompt is not tried again
            seen = report.setdefault("onlySeen", {})
            picked = []
            for a in objects:
                o = a.get("obj") or {}
                if not only.search(" ".join(str(x) for x in (o.get("name"), o.get("path"), o.get("prompt")) if x)):
                    continue
                mark = (o.get("path") or o.get("name"), (o.get("prompt") or "").strip())
                if mark in seen:
                    report["actions"].append({"segment": seg["id"], "state": state_label, "key": a["key"], "label": a["label"],
                                              "outcome": "repeat", "detail": "the same object, same prompt, tried in %s in this run" % seen[mark]})
                    row["coverage"]["repeats"] = row["coverage"].get("repeats", 0) + 1
                    continue
                seen[mark] = seg["id"]
                picked.append(a)
            objects = picked
        candidates = moves + (probe_actions(base_pos) if not (args.no_moves or (only and not args.moves)) else []) + objects
        known_now = [a for a in candidates if not args.retest and (cov["actions"].get("%s|%s" % (state_label, a["key"])) or {}).get("build") == build]
        fresh = [a for a in candidates if a not in known_now]
        queue, siblings, skipped = plan(fresh, base["status"].get("held"), base["status"].get("scene"), base_pos,
                                        story_objects(base), args.exhaustive or bool(only))  # --only: every match, no look-alike skips
        queue = known_now + queue  # reported as known from earlier on this build
        sibling_results = {}
        for act, why in skipped:
            report["actions"].append({"segment": seg["id"], "state": state_label, "key": act["key"], "label": act["label"],
                                      "outcome": "skipped", "detail": why})
        row["coverage"]["skipped"] = row["coverage"].get("skipped", 0) + len(skipped)
        probed = False
        dirty = False
        done = 0
        while (queue or siblings) and done < args.max:
            if not queue:  # the look-alike objects held back: tried only when the ones tried behaved differently
                for k, rest in list(siblings.items()):
                    seen = sibling_results.get(k, set())
                    if len(seen) > 1 or (seen - HARMLESS):
                        queue += rest
                    else:
                        for act in rest:
                            report["actions"].append({"segment": seg["id"], "state": state_label, "key": act["key"],
                                                      "label": act["label"], "outcome": "skipped",
                                                      "detail": "same kind as the %d tried here, which all did the same (%s)" % (
                                                          SIBLINGS_FIRST, ", ".join(sorted(seen)) or "untried")})
                            row["coverage"]["skipped"] = row["coverage"].get("skipped", 0) + 1
                    del siblings[k]
                if not queue:
                    break
            act = queue.pop(0)
            key = "%s|%s" % (state_label, act["key"])
            old = cov["actions"].get(key)
            if old and old.get("build") == build and not args.retest:
                row["coverage"]["known"] += 1
                report["actions"].append(dict(old, segment=seg["id"], state=state_label, key=act["key"], label=act["label"], known=True))
                continue
            if dirty:
                if not reach_state(name, seg, prefix):
                    row["notes"].append("reset failed")
                    break
                dirty = False
            t_act = video.elapsed(name) if recording else None
            res = run_action(name, act, args.radius, recording)
            done += 1
            after = res.pop("after")
            res.pop("before")
            entry = {"outcome": res["outcome"], "detail": res["detail"], "build": build, "at": learn.now_text(), "by": args.by,
                     "video": report.get("videoId"), "t": t_act, "state": state, "label": act["label"]}
            # Credit the action only with what does not happen by itself (the control at this point).
            if res["outcome"] == "death" and control["died"]:
                entry["outcome"], entry["detail"] = "anyway", "dies here anyway (doing nothing dies too); " + entry["detail"]
            elif res["outcome"] == "story" and learn.state_key(after["status"]) in control["states"]:
                entry["outcome"], entry["detail"] = "anyway", "the story goes there by itself too; " + entry["detail"]
            if res["outcome"] == "paused":  # leave the game as a player could: close the menu before going on
                for _ in range(3):
                    learn.send(name, {"op": "key", "key": "Escape", "s": 0.15}, 20)
                    time.sleep(1.0)
                    if learn.status(name).get("timeScale") != 0:
                        break
                dirty = True
            off_path = [b for b in (after["status"].get("running") or []) if b not in known_path]
            if res["outcome"] == "story" and not off_path:
                entry["detail"] += "; along the section's normal path"
            if res["outcome"] == "story" and off_path and not args.no_softlock:
                ep = softlock_check(name, seg, base_pos)
                if ep is not None and ep["outcome"] != "win":
                    entry["outcome"] = "softlock"
                    entry["detail"] += "; possible softlock: the section could not be finished after it (%s: %s); watch the "                                        "video" % (ep["failKind"], ep["failDetail"][:160])
                elif ep is not None:
                    entry["detail"] += "; the section can still be finished"
            if res["hitch"]:
                report["findings"].append({"sev": "WARN", "text": "%s: a %.0f ms frame" % (act["label"], res["worstMs"] or 0),
                                           "segment": seg["id"], "t": t_act})
            sev = SEVERITY.get(entry["outcome"])
            if sev:
                report["findings"].append({"sev": sev, "text": "%s: %s (%s)" % (act["label"], entry["outcome"], entry["detail"][:220]),
                                           "segment": seg["id"], "t": t_act})
                if recording:
                    video.mark(name, "%s: %s" % (entry["outcome"], act["label"]), "finding")
            if entry["outcome"] in ("error",):
                row["errors"].append(entry["detail"][:200])
            if entry["outcome"] == "death":
                row["deaths"] += 1
            if entry["outcome"] == "blocked":
                row["blocked"] += 1
            save_result(seg["id"], key, entry)
            if act["kind"] == "move":
                note_effect("moves", "%s|%s" % (act["key"].split("+with:")[0], "holding" if "+with:" in act["key"] or base["status"].get("held")
                                                 else "empty-handed"), entry["outcome"], seg["id"])
            elif act["kind"] == "interact" and act.get("kindKey"):
                note_effect("kinds", act["kindKey"], entry["outcome"], seg["id"])
                sibling_results.setdefault(act["kindKey"], set()).add(entry["outcome"])
            elif act["kind"] == "probe" and not probed:
                probed = True
                note_probed(base["status"].get("scene"), base_pos)
            if act.get("why"):
                entry["why"] = act["why"]
            report["actions"].append(dict(entry, segment=seg["id"], state=state_label, key=act["key"], label=act["label"]))
            row["coverage"]["tested"] += 1
            row["coverage"]["outcomes"][entry["outcome"]] = row["coverage"]["outcomes"].get(entry["outcome"], 0) + 1
            log("%-28s %-62s %-8s %s" % (seg["id"].split("/")[-1][:28], act["label"][:62], entry["outcome"], entry["detail"][:110]))
            # With something in hand: that item on the other interactables (next), and the moves again.
            if entry["outcome"] == "held" and after["status"].get("held") and act["kind"] == "interact":
                held = after["status"]["held"]
                combos = [dict(a, key="%s+with:%s" % (a["key"], held), label="%s holding %s" % (a["label"], held))
                          for a in object_actions(list(after["objects"].values()), args.combos) if a.get("target") != act.get("target")]
                drops = [dict(m, key="%s+with:%s" % (m["key"], held), label="%s holding %s" % (m["label"], held))
                         for m in moves if m["key"].startswith("move:") and "+walk" not in m["key"]][:4]
                planned, _, skipped_c = plan(combos + drops, held, after["status"].get("scene"), base_pos, set(), args.exhaustive)
                for a, why in skipped_c:
                    report["actions"].append({"segment": seg["id"], "state": state_label, "key": a["key"], "label": a["label"],
                                              "outcome": "skipped", "detail": why})
                queue[:0] = planned
                continue
            if entry["outcome"] == "busy" and not act.get("retried"):
                queue.insert(0, dict(act, retried=True, label=act["label"] + " (again)"))  # once more from a clean start
                dirty = True
                continue
            if entry["outcome"] in ("story", "softlock", "held", "death", "fell", "busy") or after["status"].get("scene") != base["status"].get("scene")                     or (entry["outcome"] == "anyway" and (after["status"].get("deaths") or 0) > (base["status"].get("deaths") or 0)):
                dirty = True
    row["duration"] = round(time.time() - t0, 1)
    row["end"] = video.elapsed(name) if recording else None
    oc = row["coverage"]["outcomes"]
    row["notes"].insert(0, "%d action(s) tried, %d known from earlier on this build, %d held back (not worth it, or tried already in this run): %s" % (
        row["coverage"]["tested"], row["coverage"]["known"], row["coverage"].get("skipped", 0) + row["coverage"].get("repeats", 0),
        ", ".join("%s %d" % kv for kv in sorted(oc.items())) or "none"))
    if any(f["segment"] == seg["id"] and f["sev"] == "BUG" for f in report["findings"]):
        row["outcome"] = "fail"
    return row


def deeptest(name, segs, args):
    if getattr(args, "radius", None) is None:
        args.radius = 30.0 if getattr(args, "only", None) else 12.0
    seed_effects()
    started = learn.ensure_player(name)
    rid = time.strftime("%Y%m%d-%H%M%S-") + name + "-deep"
    report = {"id": rid, "kind": "deep", "scope": "deep " + (args.scope or args.segment or "?") + (" only " + args.only if getattr(args, "only", None) else ""),
              "started": learn.now_text(),
              "player": name, "build": None, "by": args.by, "segments": [], "findings": [], "actions": [],
              "videoId": None, "video": None}
    t0 = time.time()
    recording = False
    report["build"] = learn.build_of(name)
    try:
        if not args.no_record:
            a = video.start(name, "deep test " + (args.scope or args.segment or ""), rec_id=rid, max_minutes=240)
            recording = bool(a.get("ok"))
            report["videoId"] = rid if recording else None
        for seg in segs:
            report["segments"].append(deep_segment(name, seg, report, args, recording))
        if recording:
            meta = video.stop(name, extra={"playtest": rid, "label": "deep test"})
            report["videoId"] = meta["id"]
    finally:
        if started:
            ap.stop_one(name)
    for a in report["actions"]:
        if a.get("video") is None and not a.get("known"):
            a["video"] = report["videoId"]
    report["duration"] = round(time.time() - t0, 1)
    report.pop("onlySeen", None)  # the run's memory of what --only tried (not part of the report)
    learn.score(report)
    tried = sum(r["coverage"]["tested"] for r in report["segments"])
    known = sum(r["coverage"]["known"] for r in report["segments"])
    bugs = sum(1 for f in report["findings"] if f["sev"] == "BUG")
    deaths = sum(1 for f in report["findings"] if f["sev"] == "SOFT")
    warns = sum(1 for f in report["findings"] if f["sev"] == "WARN")
    report["perfect"] = None
    report["imperfect"] = []
    skipped = sum(r["coverage"].get("skipped", 0) for r in report["segments"])
    repeats = sum(r["coverage"].get("repeats", 0) for r in report["segments"])
    report["coverage"] = {"tried": tried, "known": known, "skipped": skipped, "repeats": repeats}
    report["verdict"] = "DEEP: %d action(s) in %d section(s)%s%s%s: %d bug(s), %d death(s), %d warning(s)" % (
        tried, len(report["segments"]), " (+%d known from this build)" % known if known else "",
        ", %d repeat(s) of an object already tried in this run" % repeats if repeats else "",
        ", %d skipped as not worth it" % skipped if skipped else "", bugs, deaths, warns)
    learn.write_report(report)
    return report


def cmd_deeptest(args):
    args.by = args.by or args.name
    segs = learn.scoped(args)
    report = deeptest(args.name, segs, args)
    report.pop("onlySeen", None)
    print("\n" + report["verdict"])
    for r in report["segments"]:
        print("  %-40s %s" % (r["id"], "; ".join(r.get("notes", []))[:200]))
    held_back = [a for a in report["actions"] if a.get("outcome") in ("skipped", "repeat")]
    for a in held_back[:40]:
        print("  %-7s %s (%s): %s" % (a["outcome"].upper(), a["label"][:70], a["segment"].split("/")[-1], a.get("detail", "")[:100]))
    for f in report["findings"][:40]:
        print("  %-5s %s%s" % (f["sev"], f["text"][:160], "  @%s" % video.clock(f["t"]) if f.get("t") is not None else ""))
    print("REPORT %s" % (learn.PLAYTESTS / report["id"] / "report.html"))
    if report.get("videoId"):
        print("VIDEO  %s" % report["videoId"])
    link = ap.web.view_link("playtests/" + report["id"]) if getattr(ap, "web", None) else None
    print("VIEW   %s" % link if link else "dashboard: agentplay.py serve --detach  (then open the Playtests tab)")
    print("coverage kept in %s (commit it)" % COVERAGE)


def register(sub, ap_module):
    global ap, learn, video, COVERAGE
    ap = ap_module
    import agentplay_learn
    import agentplay_video
    learn, video = agentplay_learn, agentplay_video
    COVERAGE = ap.ROOT / ap.CONFIG.get("knowledgeDir", "AgentPlayKnowledge") / "coverage"
    p = sub.add_parser("deeptest", help="try everything in a section (every interactable, item combination and move) to find "
                                        "what breaks; coverage is shared in the knowledge base")
    p.add_argument("name", help="your player's name (your own, e.g. your agent name and a number); started and stopped for you")
    p.add_argument("--scope", help="segments, as for learn and playtest")
    p.add_argument("--segment", help="one segment id")
    p.add_argument("--states", choices=("start", "all"), default="start",
                   help="test at the section start, or also at each story state the learned route goes through")
    p.add_argument("--only", metavar="REGEX", help="only interactables whose name, path or prompt matches (case-insensitive), "
                   "e.g. door or 'drawer|wardrobe': every match is tried (no look-alike skips), each once per prompt per run; "
                   "moves and walking probes are off unless --moves")
    p.add_argument("--moves", action="store_true", help="with --only: also try the moves and walking probes")
    p.add_argument("--radius", type=float, default=None, help="metres around the section start to look for interactables "
                   "(default 12; 30 with --only)")
    p.add_argument("--max", type=int, default=80, help="actions per test point at most (default 80)")
    p.add_argument("--combos", type=int, default=6, help="interactables to try an item in hand on")
    p.add_argument("--retest", action="store_true", help="redo actions already tested on this build")
    p.add_argument("--exhaustive", action="store_true", help="try everything, even what the effects memory says cannot "
                   "teach anything new (item moves with empty hands, look-alike objects, moves that did nothing before)")
    p.add_argument("--no-moves", action="store_true", help="interactables only")
    p.add_argument("--no-softlock", action="store_true", help="skip the can-it-still-be-finished check after story changes")
    p.add_argument("--no-record", action="store_true", help="no video (the default records one video of the whole run)")
    p.add_argument("--by", help="your agent name, kept with what you add")
    p.set_defaults(func=cmd_deeptest)
