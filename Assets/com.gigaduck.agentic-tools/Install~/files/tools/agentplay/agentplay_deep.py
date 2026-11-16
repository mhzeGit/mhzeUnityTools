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
Results are shared: AgentPlayKnowledge/coverage/<segment>.json keeps every action's outcome with its build, so the next
agent skips what was tested on the same build (`--retest` to redo) and retests after a new build. The report (with one
video, a caption per action) is in Builds/AgentPlay/playtests/<id>-deep/ and on the dashboard and phone.

  agentplay.py deeptest NAME --scope S [--states start|all] [--radius 12] [--max 80] [--retest] [--no-record]
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
SEVERITY = {"error": "BUG", "fell": "BUG", "softlock": "BUG", "paused": "BUG", "death": "SOFT", "failed": "WARN", "hitch": "WARN"}


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
        out.append({"key": "interact:%s" % path, "kind": "interact", "label": "interact %s%s" % (
            o.get("name"), (" (" + o["prompt"].strip() + ")") if o.get("prompt") else ""), "target": path,
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
    return {"status": st, "objects": objs, "texts": list(o.get("texts") or []), "issues": issues}


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


def run_action(name, act, radius, recording=False):
    wait_for_control(name)
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


# ------------------------------------------------------------------ a section

def reach_state(name, seg, prefix):
    """The section's start (its setup), then the learned route's steps up to a later story state."""
    for cmd in seg.get("setup", []):
        a = learn.send(name, cmd, learn.op_timeout(cmd))
        if not a.get("ok"):
            return False
    learn.send(name, {"op": "wait", "s": 1.0}, 30)
    for step in prefix:
        if step.get("when"):
            learn.send(name, {"op": "until", "cond": step["when"], "timeout": 30, "soft": True}, 60)
        learn.send(name, {k: v for k, v in step.items() if k not in learn.META_KEYS}, learn.op_timeout(step))
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


def softlock_check(name, seg):
    """After the story moved on its own way: can the learned route still finish the section from here?"""
    route = learn.best_route(learn.KB.load(seg["id"], seg))
    if not route:
        return None
    ep = learn.run_episode(name, seg, route["steps"], setup=False, by="deep", budget=min(seg.get("budget", 150), 90))
    return ep


def deep_segment(name, seg, report, args, recording):
    t_start = video.elapsed(name) if recording else None
    if recording:
        video.mark(name, "deep: " + seg.get("label", seg["id"]), "segment")
    cov = load_coverage(seg["id"])
    build = learn.current_build()
    moves = [] if args.no_moves else move_actions(input_moves())
    row = {"id": seg["id"], "label": seg.get("label", seg["id"]), "outcome": "pass", "mastery": None, "start": t_start,
           "end": None, "duration": 0, "chained": True, "route": None, "deaths": 0, "errors": [], "worstFrameMs": None,
           "p95FrameMs": None, "fpsAvg": None, "blocked": 0, "passiveRatio": 0, "notes": [], "friction": 0, "episode": None,
           "coverage": {"tested": 0, "known": 0, "outcomes": {}}}
    t0 = time.time()
    for state_label, prefix in states_of(seg, args.states):
        if not reach_state(name, seg, prefix):
            row["notes"].append("could not reach state %s" % state_label)
            continue
        base = snap(name, args.radius)
        base_pos = pos_of(base["status"]) or [0, 0, 0]
        state = learn.state_key(base["status"])
        queue = moves + (probe_actions(base_pos) if not args.no_moves else []) + object_actions(list(base["objects"].values()), args.max)
        dirty = False
        done = 0
        while queue and done < args.max:
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
                     "video": report.get("videoId"), "t": t_act, "state": state}
            if res["outcome"] == "paused":  # leave the game as a player could: close the menu before going on
                for _ in range(3):
                    learn.send(name, {"op": "key", "key": "Escape", "s": 0.15}, 20)
                    time.sleep(1.0)
                    if learn.status(name).get("timeScale") != 0:
                        break
                dirty = True
            if res["outcome"] == "story" and not args.no_softlock:
                ep = softlock_check(name, seg)
                if ep is not None and ep["outcome"] != "win":
                    entry["outcome"] = "softlock"
                    entry["detail"] += "; the learned route could not finish the section after it (%s: %s)" % (
                        ep["failKind"], ep["failDetail"][:160])
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
            report["actions"].append(dict(entry, segment=seg["id"], state=state_label, key=act["key"], label=act["label"]))
            row["coverage"]["tested"] += 1
            row["coverage"]["outcomes"][entry["outcome"]] = row["coverage"]["outcomes"].get(entry["outcome"], 0) + 1
            log("%-34s %-46s %-9s %s" % (seg["id"][-34:], act["label"][:46], entry["outcome"], entry["detail"][:90]))
            # With something in hand: that item on the other interactables (next), and the moves again.
            if entry["outcome"] == "held" and after["status"].get("held") and act["kind"] == "interact":
                held = after["status"]["held"]
                combos = [dict(a, key="%s+with:%s" % (a["key"], held), label="%s holding %s" % (a["label"], held))
                          for a in object_actions(list(after["objects"].values()), args.combos) if a.get("target") != act.get("target")]
                drops = [dict(m, key="%s+with:%s" % (m["key"], held), label="%s holding %s" % (m["label"], held))
                         for m in moves if m["key"].startswith("move:") and "+walk" not in m["key"]][:4]
                queue[:0] = combos + drops
                continue
            if entry["outcome"] in ("story", "softlock", "held", "death", "fell") or after["status"].get("scene") != base["status"].get("scene"):
                dirty = True
    row["duration"] = round(time.time() - t0, 1)
    row["end"] = video.elapsed(name) if recording else None
    oc = row["coverage"]["outcomes"]
    row["notes"].insert(0, "%d action(s) tried, %d known from earlier on this build: %s" % (
        row["coverage"]["tested"], row["coverage"]["known"], ", ".join("%s %d" % kv for kv in sorted(oc.items())) or "none"))
    if any(f["segment"] == seg["id"] and f["sev"] == "BUG" for f in report["findings"]):
        row["outcome"] = "fail"
    return row


def deeptest(name, segs, args):
    started = learn.ensure_player(name)
    rid = time.strftime("%Y%m%d-%H%M%S-") + name + "-deep"
    report = {"id": rid, "kind": "deep", "scope": "deep " + (args.scope or args.segment or "?"), "started": learn.now_text(),
              "player": name, "build": learn.current_build(), "by": args.by, "segments": [], "findings": [], "actions": [],
              "videoId": None, "video": None}
    t0 = time.time()
    recording = False
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
    learn.score(report)
    tried = sum(r["coverage"]["tested"] for r in report["segments"])
    known = sum(r["coverage"]["known"] for r in report["segments"])
    bugs = sum(1 for f in report["findings"] if f["sev"] == "BUG")
    deaths = sum(1 for f in report["findings"] if f["sev"] == "SOFT")
    warns = sum(1 for f in report["findings"] if f["sev"] == "WARN")
    report["perfect"] = None
    report["imperfect"] = []
    report["coverage"] = {"tried": tried, "known": known}
    report["verdict"] = "DEEP: %d action(s) in %d section(s)%s: %d bug(s), %d death(s), %d warning(s)" % (
        tried, len(report["segments"]), " (+%d known)" % known if known else "", bugs, deaths, warns)
    learn.write_report(report)
    return report


def cmd_deeptest(args):
    args.by = args.by or args.name
    segs = learn.scoped(args)
    report = deeptest(args.name, segs, args)
    print("\n" + report["verdict"])
    for f in report["findings"][:40]:
        print("  %-5s %s%s" % (f["sev"], f["text"][:160], "  @%s" % video.clock(f["t"]) if f.get("t") is not None else ""))
    print("REPORT %s" % (learn.PLAYTESTS / report["id"] / "report.html"))
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
    p.add_argument("name")
    p.add_argument("--scope", help="segments, as for learn and playtest")
    p.add_argument("--segment", help="one segment id")
    p.add_argument("--states", choices=("start", "all"), default="start",
                   help="test at the section start, or also at each story state the learned route goes through")
    p.add_argument("--radius", type=float, default=12, help="metres around the player to look for interactables")
    p.add_argument("--max", type=int, default=80, help="actions per state at most")
    p.add_argument("--combos", type=int, default=6, help="interactables to try an item in hand on")
    p.add_argument("--retest", action="store_true", help="redo actions already tested on this build")
    p.add_argument("--no-moves", action="store_true", help="interactables only")
    p.add_argument("--no-softlock", action="store_true", help="skip the can-it-still-be-finished check after story changes")
    p.add_argument("--no-record", action="store_true")
    p.add_argument("--by")
    p.set_defaults(func=cmd_deeptest)
