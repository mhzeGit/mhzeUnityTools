#!/usr/bin/env python3
"""Drive standalone players of the game for agents, with no Unity editor open.

Several agents can each run their own player at once: every instance has its own folder and its
own save file. See Docs/AGENT_PLAY.md. Standard library only. Installed from the Agentic Tools package
(com.gigaduck.agentic-tools, Install~/files/tools/agentplay): change it there, not in a project.
Project settings: tools/agentplay/agentplay.json; project commands: tools/agentplay/agentplay_project.py.

  agentplay.py info [--files]                  newest build: age, commit, changes; exit 0 current, 3 stale, 2 none
  agentplay.py build [--force|--full] [--by NAME]  build the player if the sources changed (editor may stay open);
                                               script-only changes are patched in seconds without Unity
  agentplay.py start NAME [--headless] ...     start a player called NAME
  agentplay.py send NAME '{"op":"status"}'     send one command and print the answer
  agentplay.py shot NAME --pos 1,2,3 --look 4,5,6 [--as hall]
  agentplay.py sheet NAME                      pack the player's pictures into contact sheets
  agentplay.py stop NAME | stop --all          quit players
  agentplay.py list                            show builds and running players
  agentplay.py editor status|claim|release --by NAME   the one agent slot for editor Play Mode tests
"""
import argparse
import ctypes
import gzip
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import time
import uuid
from pathlib import Path

def find_root():
    """The Unity project this tool belongs to: the nearest folder above it with ProjectSettings/ProjectVersion.txt."""
    here = Path(__file__).resolve().parent
    for folder in [here, *here.parents]:
        if (folder / "ProjectSettings" / "ProjectVersion.txt").is_file():
            return folder
    return here.parents[1]


ROOT = find_root()
CONFIG_FILE = Path(__file__).resolve().parent / "agentplay.json"
PLUGIN_FILE = Path(__file__).resolve().parent / "agentplay_project.py"


def load_config():
    """Project settings (all optional): defaultScene, buildMethod, unityEditorRoot."""
    try:
        return json.loads(CONFIG_FILE.read_text(encoding="utf-8"))
    except FileNotFoundError:
        return {}
    except ValueError as error:
        print("ERROR: %s is not valid JSON: %s" % (CONFIG_FILE, error), file=sys.stderr)
        sys.exit(1)


CONFIG = load_config()
CLONE = ROOT.parent / (ROOT.name + "_AgentBuild")  # beside the project, so package paths and path lengths stay the same
SEEDED = "agentplay-seeded.txt"
HOME = ROOT / "Builds" / "AgentPlay"
STAGE = HOME / "_stage"
RUNS = HOME / "runs"
LATEST = HOME / "latest.txt"
BUILD_LOCK = HOME / "build.lock"
EXE = "AgentPlay.exe"
KEEP_BUILDS = 2
STALE_HEARTBEAT = 90  # seconds; a slow scene load can block the main thread for a while
BUILD_OPTIONS = ("-batchmode", "-buildTarget", "Win64", "-executeMethod",
                 CONFIG.get("buildMethod") or "Gigaduck.AgenticTools.AgentPlay.AgentPlayBuild.BuildFromCommandLine")
BUILD_ENV = {"AGENTPLAY_BUILD": "1"}
INPUT_FOLDERS = ("Assets", "Packages", "ProjectSettings")


def fail(message, code=1):
    print("ERROR: " + message, file=sys.stderr)
    sys.exit(code)


# ------------------------------------------------------------------ build

def scan_sources(root=None):
    """Scan the Unity inputs of the project or build copy. Returns (extras, files): `extras` are non-file inputs
    (build options, this tool, unresolved packages); `files` maps "Assets/x.cs" to "size|signature". The stamp is a
    hash of both; the file map is kept with each build so a later build can list exactly what changed."""
    root = ROOT if root is None else root
    extras = [repr((BUILD_OPTIONS, BUILD_ENV)), hashlib.sha256(Path(__file__).read_bytes()).hexdigest()]
    files = {}

    def walk(top, label):
        stack = [top]
        while stack:
            with os.scandir(stack.pop()) as it:
                for entry in it:
                    if entry.is_dir(follow_symlinks=False):
                        if entry.name != ".git":
                            stack.append(Path(entry.path))
                    else:
                        st = entry.stat(follow_symlinks=False)
                        path = Path(entry.path)
                        name = f"{label}/{path.relative_to(top).as_posix()}"
                        if path.suffix.lower() in {".cs", ".asmdef", ".asmref", ".json"} or label == "ProjectSettings":
                            signature = hashlib.sha256(path.read_bytes()).hexdigest()
                        else:
                            signature = str(st.st_mtime_ns)
                        files[name] = f"{st.st_size}|{signature}"

    for folder in INPUT_FOLDERS:
        walk(root / folder, folder)
    # Packages referenced with file: live outside the project but compile into the player.
    manifest = json.loads((root / "Packages" / "manifest.json").read_text(encoding="utf-8"))
    for name, reference in manifest.get("dependencies", {}).items():
        if reference.startswith("file:"):
            package = (root / "Packages" / reference[5:]).resolve()
            if package.is_dir():
                walk(package, f"package:{name}")
            else:
                extras.append(f"missing local package: {name}")
    return extras, files


def stamp_of(extras, files):
    entries = list(extras) + [f"{name}|{value}" for name, value in files.items()]
    entries.sort()
    return hashlib.sha256("\n".join(entries).encode("utf-8", "surrogateescape")).hexdigest()[:12]


def source_stamp(root=None):
    """Fingerprint Unity inputs and build options, relative to the project or build copy."""
    return stamp_of(*scan_sources(root))


def editor_is_open():
    """The editor keeps Temp/UnityLockfile open exclusively while it has this project."""
    lock = ROOT / "Temp" / "UnityLockfile"
    if not lock.exists():
        return False
    try:
        with open(lock, "r+b"):
            return False
    except OSError:
        return True


def unity_exe():
    if os.environ.get("UNITY_EXE"):
        return Path(os.environ["UNITY_EXE"])
    text = (ROOT / "ProjectSettings" / "ProjectVersion.txt").read_text(encoding="utf-8")
    version = re.search(r"m_EditorVersion:\s*(\S+)", text).group(1)
    editors = Path(CONFIG.get("unityEditorRoot") or r"C:\Program Files\Unity\Hub\Editor")
    exe = editors / version / "Editor" / "Unity.exe"
    if not exe.exists():
        fail("Unity %s is not installed at %s (set unityEditorRoot in %s or the UNITY_EXE environment variable)"
             % (version, exe, CONFIG_FILE.name))
    return exe


def latest_build():
    if not LATEST.exists():
        return None
    path = HOME / LATEST.read_text(encoding="utf-8").strip()
    return path if (path / EXE).exists() else None


# ------------------------------------------------------------------ build records
# Every build writes build-info.json (read it, or Builds/AgentPlay/latest.json, to see when it was made, from which
# commit and what changed since the previous build) and source-manifest.json.gz (the file map the next build and
# `agentplay.py info` diff against). history.jsonl keeps one short line per build.

KINDS = {".cs": "code", ".asmdef": "code", ".asmref": "code", ".unity": "scene", ".prefab": "prefab",
         ".shader": "shader", ".shadergraph": "shader", ".hlsl": "shader", ".cginc": "shader"}
LISTED_FILES = 60  # changed files stored in the record; `info --files` prints them all


def kind_of(name):
    if name.startswith("ProjectSettings/"):
        return "settings"
    if name.startswith("Packages/") or name.startswith("package:"):
        return "packages"
    base = name[:-5] if name.endswith(".meta") else name
    return KINDS.get(os.path.splitext(base)[1].lower(), "assets")


def change_summary(old, new):
    """Diff two file maps from scan_sources. Pure and cheap; works on uncommitted work because it never asks Git."""
    added = sorted(set(new) - set(old))
    deleted = sorted(set(old) - set(new))
    modified = sorted(name for name in set(new) & set(old) if new[name] != old[name])
    by_kind = {}
    for letter, names in (("added", added), ("modified", modified), ("deleted", deleted)):
        for name in names:
            by_kind.setdefault(kind_of(name), {"added": 0, "modified": 0, "deleted": 0})[letter] += 1
    kinds = ", ".join("%s %d" % (kind, sum(counts.values())) for kind, counts in sorted(by_kind.items()))
    total = len(added) + len(modified) + len(deleted)
    text = ("%d file(s) changed (+%d ~%d -%d: %s)" % (total, len(added), len(modified), len(deleted), kinds)
            if total else "no source file differs")
    lines = ["A " + n for n in added] + ["M " + n for n in modified] + ["D " + n for n in deleted]
    return {"total": total, "added": len(added), "modified": len(modified), "deleted": len(deleted),
            "byKind": by_kind, "text": text, "files": lines}


def git_text(*args):
    """Read-only Git (the shared tree must never be changed). Empty text when Git is missing or slow."""
    try:
        proc = subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True, errors="replace", timeout=90,
                              creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        return proc.stdout.strip() if proc.returncode == 0 else ""
    except (OSError, subprocess.SubprocessError):
        return ""


def git_state():
    inputs = ["--", *INPUT_FOLDERS]
    dirty = git_text("status", "--porcelain", *inputs)
    return {"commit": git_text("rev-parse", "--short", "HEAD") or None,
            "branch": git_text("rev-parse", "--abbrev-ref", "HEAD") or None,
            "uncommittedPaths": len(dirty.splitlines()),
            "diffStatVsCommit": git_text("diff", "--shortstat", "HEAD", *inputs) or "none"}


def read_info(build):
    try:
        return json.loads((build / "build-info.json").read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def read_manifest(build):
    try:
        with gzip.open(build / "source-manifest.json.gz", "rt", encoding="utf-8") as handle:
            return json.load(handle)
    except (OSError, ValueError):
        return None


def age_text(epoch):
    seconds = max(0, int(time.time() - epoch))
    if seconds < 90:
        return "%ds" % seconds
    if seconds < 5400:
        return "%d min" % round(seconds / 60)
    return "%.1f h" % (seconds / 3600)


def one_line(info):
    git = info.get("git") or {}
    since = info.get("sinceBuild")
    previous = since["text"] + " vs the previous build" if since else "no previous build to compare"
    return "%s built %s (%s ago, took %ds, by %s) from commit %s on %s with %d uncommitted path(s); %s" % (
        info.get("name"), info.get("builtAt"), age_text(info.get("builtAtEpoch", time.time())), info.get("durationSeconds", 0),
        info.get("builtBy") or "unknown", git.get("commit"), git.get("branch"), git.get("uncommittedPaths", 0), previous)


def make_info(stamp, summary, files, previous_build, began, attempts, built_by):
    old = read_manifest(previous_build) if previous_build is not None else None
    since = None
    if old is not None:
        since = change_summary(old, files)
        since = dict(since, previous=previous_build.name, files=since["files"][:LISTED_FILES],
                     filesTruncated=since["total"] > LISTED_FILES)
    now = time.time()
    return {"stamp": stamp, "builtAt": time.strftime("%Y-%m-%d %H:%M:%S"), "builtAtEpoch": int(now),
            "durationSeconds": int(now - began), "attempts": attempts, "builtBy": built_by, "unity": summary,
            "git": git_state(), "fileCount": len(files), "sinceBuild": since}


def write_text_atomic(path, text):
    temp = path.with_suffix(path.suffix + ".tmp")
    temp.write_text(text, encoding="utf-8")
    os.replace(temp, path)


def promote(stamp, info, files):
    """Move a finished _stage to its own folder and record it. Running players keep using their old one."""
    name = "%s-%s" % (time.strftime("%Y%m%d-%H%M%S"), stamp)
    info = dict(info, name=name)
    write_text_atomic(STAGE / "build-info.json", json.dumps(info, indent=1))
    with gzip.open(STAGE / "source-manifest.json.gz", "wt", encoding="utf-8") as handle:
        json.dump(files, handle)
    os.replace(STAGE, HOME / name)
    write_text_atomic(HOME / "latest.json", json.dumps(info, indent=1))
    short = {key: info[key] for key in ("name", "builtAt", "durationSeconds", "builtBy", "git")}
    short["changes"] = (info["sinceBuild"] or {}).get("text")
    with open(HOME / "history.jsonl", "a", encoding="utf-8") as handle:
        handle.write(json.dumps(short) + "\n")
    write_text_atomic(LATEST, name)
    prune(keep=name)
    return name, info


def prune(keep):
    builds = sorted(p for p in HOME.iterdir() if p.is_dir() and re.match(r"\d{8}-\d{6}-", p.name))
    for old in builds[:-KEEP_BUILDS]:
        if old.name != keep:
            shutil.rmtree(old, ignore_errors=True)  # a build a player still runs from stays locked and is kept


def acquire_build_lock():
    """Only one build at a time; a second caller waits for it, then usually finds the build it wanted."""
    HOME.mkdir(parents=True, exist_ok=True)
    told = False
    while True:
        try:
            fd = os.open(BUILD_LOCK, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            os.write(fd, str(os.getpid()).encode())
            os.close(fd)
            return
        except FileExistsError:
            try:
                owner = int(BUILD_LOCK.read_text() or "0")
            except (OSError, ValueError):
                owner = 0
            if owner and not pid_alive(owner, "python"):
                BUILD_LOCK.unlink(missing_ok=True)
                continue
            if not told:  # once: a line every few seconds only fills the waiting agent's context
                print("waiting for another agent's build (pid %s)..." % owner, flush=True)
                told = True
            time.sleep(5)


def robocopy(source, target, *extra):
    """Mirror a folder keeping file times, so Unity in the copy only re-imports what really changed."""
    proc = subprocess.run(["robocopy", str(source), str(target), "/MIR", "/COPY:DAT", "/DCOPY:T", "/MT:8", "/R:1", "/W:1",
                           "/NFL", "/NDL", "/NJH", "/NJS", "/NP", *extra],
                          capture_output=True, text=True, errors="replace", creationflags=subprocess.CREATE_NO_WINDOW)
    if proc.returncode >= 8:  # robocopy: below 8 means success
        fail("copying %s failed (robocopy %d): %s" % (source, proc.returncode, proc.stdout[-800:]))


def sync_clone():
    """The build copy has its own Library and is only opened in batch mode, so an open editor is never disturbed."""
    if not (CLONE / "Library").exists() or not (CLONE / SEEDED).exists():
        print("first build: seeding the build copy's Library (only happens once)...", flush=True)
        # A copied PID file can make the batch editor terminate another editor's compiler.
        robocopy(ROOT / "Library", CLONE / "Library", "/XF", "*-lock", "*.lock", "*.pid", "/XD", "Temp", "RunState")
        (CLONE / SEEDED).write_text(time.strftime("%Y-%m-%d %H:%M:%S"), encoding="utf-8")
    for folder in INPUT_FOLDERS:
        robocopy(ROOT / folder, CLONE / folder)


def compile_errors(log):
    try:
        text = log.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return []
    # A licensing timeout (several Unitys starting together) disables every package, which then shows up as
    # hundreds of false "namespace not found" errors. It passes by itself, so it counts as retryable.
    if "Unknown entitlement status" in text:
        return ["Unity licensing timed out while the machine was busy (not a code problem)"]
    seen = []
    for line in text.splitlines():
        if "error CS" in line and not line.startswith("[AgentPlayBuild]") and line not in seen:
            seen.append(line)
    return seen[:12]


def build_once(timeout):
    """One sync and batch-mode build. Returns (stamp, summary, compile errors, file map); a stamp of None means it failed."""
    for attempt in range(3):
        extras, files = scan_sources()
        stamp = stamp_of(extras, files)
        sync_clone()
        if stamp == source_stamp(CLONE) == source_stamp():
            break
        print("Source and build copy differ after sync; retrying.", flush=True)
    else:
        return None, "source changed during sync or the build copy resolves different local packages", [], None
    lock = CLONE / "Packages" / "packages-lock.json"
    package_resolution = lock.read_bytes()
    if STAGE.exists():
        shutil.rmtree(STAGE)
    STAGE.mkdir(parents=True)
    log = HOME / "build.log"
    print("building (log: %s)..." % log, flush=True)
    env = dict(os.environ, **BUILD_ENV)
    env.pop("UNITY_MCP_ALLOW_BATCH", None)  # the build copy must never register with the agents' MCP server
    try:
        proc = subprocess.run([str(unity_exe()), "-projectPath", str(CLONE), *BUILD_OPTIONS,
                               "-agentOut", str(STAGE), "-logFile", str(log)], timeout=timeout, env=env)
    except subprocess.TimeoutExpired:
        return None, "did not finish within %ds; see %s" % (timeout, log), [], None
    result = STAGE / "build-result.txt"
    summary = result.read_text(encoding="utf-8") if result.exists() else "no result written"
    if proc.returncode != 0 or not (STAGE / EXE).exists():
        return None, "exit %d: %s; see %s" % (proc.returncode, summary, log), compile_errors(log), None
    if lock.read_bytes() != package_resolution:
        return None, "Unity resolved different package revisions in the build copy; check Packages/packages-lock.json", [], None
    return stamp, summary, [], files


def cmd_shader_report(args):
    """Dry run: says how many shader variants a build would compile and which keywords cause them. Compiles none."""
    acquire_build_lock()
    try:
        report = HOME / "shader-report.txt"
        report.unlink(missing_ok=True)
        os.environ["AGENTPLAY_SHADER_REPORT"] = str(report)
        began = time.time()
        for attempt in range(args.retries + 1):
            stamp, summary, errors, _ = build_once(args.timeout)
            if not errors:
                break
            print("attempt %d: %s" % (attempt + 1, errors[0][:300]), flush=True)
            if attempt < args.retries:
                time.sleep(args.retry_wait)
        shutil.rmtree(STAGE, ignore_errors=True)  # a player without shaders must never be used
        if errors:
            fail("errors:\n  " + "\n  ".join(errors))
        if not report.exists():
            fail("no report written: " + summary)
        print("dry run took %.0fs, report: %s" % (time.time() - began, report))
        print(report.read_text(encoding="utf-8")[:6000])
    finally:
        os.environ.pop("AGENTPLAY_SHADER_REPORT", None)
        BUILD_LOCK.unlink(missing_ok=True)


# ------------------------------------------------------------------ patch (code-only changes, no Unity)
# When every change since the newest build is a script in Assets/, `build` recompiles only Assembly-CSharp with the
# same Roslyn command line Unity's player build used, and swaps that one DLL into a hardlinked copy of the build.
# No Unity process starts. Our assemblies are not changed by Unity's IL post-processing (checked 2026-11-08:
# only Unity.Collections and URP packages are), so the DLL equals what a full build would produce.
# DllCheck refuses a patch whose changes the built scene/prefab data or script list cannot follow; then `build`
# falls back to a full build by itself.

PLAYER_ASSEMBLY = "Assembly-CSharp"
COMPILE_DIR = "_compile"  # in each build: the player compile command it was made with
TOOLS = HOME / "_tools"


def dotnet_exe():
    exe = unity_exe().parent / "Data" / "DotNetSdk" / "dotnet.exe"
    if not exe.exists():
        fail("Unity's bundled .NET SDK is missing: " + str(exe))
    return exe


def csc_dll():
    found = sorted((dotnet_exe().parent / "sdk").glob("*/Roslyn/bincore/csc.dll"))
    if not found:
        fail("Roslyn csc.dll not found in Unity's .NET SDK")
    return found[-1]


def run_quiet(cmd, cwd=None, timeout=600):
    return subprocess.run([str(c) for c in cmd], cwd=cwd, capture_output=True, text=True, errors="replace",
                          timeout=timeout, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))


def dllcheck_tool():
    """Compile tools/agentplay/DllCheck.cs once (and again when it changes) with Unity's bundled .NET 8."""
    source = Path(__file__).with_name("DllCheck.cs")
    out = TOOLS / "DllCheck.dll"
    digest = hashlib.sha256(source.read_bytes()).hexdigest()[:12]
    marker = TOOLS / "DllCheck.stamp"
    if out.exists() and marker.exists() and marker.read_text() == digest:
        return out
    TOOLS.mkdir(parents=True, exist_ok=True)
    refs = sorted((dotnet_exe().parent / "packs" / "Microsoft.NETCore.App.Ref").glob("8.*/ref/net8.0/*.dll"))
    if not refs:
        fail(".NET 8 reference assemblies not found in Unity's .NET SDK")
    version = refs[0].parts[-4]
    proc = run_quiet([dotnet_exe(), "exec", csc_dll(), "/nologo", "/noconfig", "/nostdlib", "/target:exe", "/langversion:latest",
                      "/nullable:disable", "/out:" + str(out), *("/r:" + str(r) for r in refs), str(source)])
    if proc.returncode != 0:
        fail("DllCheck did not compile:\n" + proc.stdout[-2000:])
    (TOOLS / "DllCheck.runtimeconfig.json").write_text(json.dumps(
        {"runtimeOptions": {"tfm": "net8.0", "rollForward": "LatestMinor",
                            "framework": {"name": "Microsoft.NETCore.App", "version": version}}}), encoding="utf-8")
    marker.write_text(digest)
    return out


AGENT_DEFINE = "-define:AGENT_PLAY"


def is_agent_rsp(rsp):
    try:
        return AGENT_DEFINE in rsp.read_text(encoding="utf-8").splitlines()
    except OSError:
        return False


def player_dag():
    """The build copy's player compile folder (Library/Bee/artifacts/<hash>P.dag) that the agent build used: Bee also
    keeps graphs for other build kinds (PDev = Development, E = editor) with different defines, so pick by the define."""
    found = [p for p in (CLONE / "Library" / "Bee" / "artifacts").glob("*.dag") if is_agent_rsp(p / (PLAYER_ASSEMBLY + ".rsp"))]
    return max(found, key=lambda p: (p / (PLAYER_ASSEMBLY + ".dll")).stat().st_mtime if (p / (PLAYER_ASSEMBLY + ".dll")).exists() else 0) if found else None


def save_compile_command(stage):
    """Keep the player compile command beside a full build, so later patches use exactly its options."""
    dag = player_dag()
    if dag is None:
        return
    target = stage / COMPILE_DIR
    target.mkdir(exist_ok=True)
    for suffix in (".rsp", ".rsp2", ".UnityAdditionalFile.txt"):
        source = dag / (PLAYER_ASSEMBLY + suffix)
        if source.exists():
            shutil.copy2(source, target / source.name)


def player_sources():
    """Assets/ scripts that Unity compiles into Assembly-CSharp: not under an Editor folder, not in a folder
    with its own .asmdef/.asmref, not in Plugins/Standard Assets (firstpass). Matches Unity's list (checked 2026-11-08)."""
    owned, sources = [], []
    for folder, dirs, names in os.walk(ROOT / "Assets"):
        if any(n.endswith((".asmdef", ".asmref")) for n in names):
            owned.append(Path(folder))
    owned_set = {p.as_posix() for p in owned}
    for folder, dirs, names in os.walk(ROOT / "Assets"):
        rel = Path(folder).relative_to(ROOT)
        parts = rel.parts
        if any(p.lower() == "editor" for p in parts) or (len(parts) > 1 and parts[1] in ("Plugins", "Standard Assets", "Pro Standard Assets")):
            dirs[:] = []
            continue
        if Path(folder).as_posix() in owned_set:
            dirs[:] = []
            continue
        sources += [(rel / n).as_posix() for n in names if n.endswith(".cs")]
    return sorted(sources)


def patch_reason(change):
    """None when every changed input is a script under Assets/, else why a full build is needed."""
    for line in change["files"]:
        name = line[2:]
        if not (name.startswith("Assets/") and (name.endswith(".cs") or name.endswith(".cs.meta"))):
            return "%s is not a script" % name
    return None


def compile_patch(base, out_dir):
    """Recompile Assembly-CSharp from the working tree, referencing the base build's own DLLs. Returns (dll, errors)."""
    command = base / COMPILE_DIR
    rsp = command / (PLAYER_ASSEMBLY + ".rsp")
    if not is_agent_rsp(rsp):
        dag = player_dag()
        if dag is None:
            return None, ["no player compile command saved; a full build records one"]
        command, rsp = dag, dag / (PLAYER_ASSEMBLY + ".rsp")
    managed = base / "AgentPlay_Data" / "Managed"
    out_dir.mkdir(parents=True, exist_ok=True)
    dll = out_dir / (PLAYER_ASSEMBLY + ".dll")
    lines = []
    for line in rsp.read_text(encoding="utf-8").splitlines():
        if not line or line.endswith('.cs"') or line.startswith(("-out:", "-refout:")):
            continue
        match = re.match(r'^(-r:|-analyzer:|/additionalfile:)"(.*)"$', line)
        if match:
            flag, path = match.groups()
            if flag == "-r:" and "/Bee/artifacts/" in path:
                path = str(managed / (Path(path).name[:-len(".ref.dll")] + ".dll"))  # the build's own copy
                if not Path(path).exists():
                    return None, ["the build has no %s" % Path(path).name]
            elif flag == "/additionalfile:":
                path = str(command / Path(path).name)
            elif not Path(path).is_absolute():
                path = str(CLONE / path)
            line = '%s"%s"' % (flag, path)
        lines.append(line)
    lines.append('-out:"%s"' % dll)
    lines += ['"%s"' % s for s in player_sources()]
    patch_rsp = out_dir / "patch.rsp"
    patch_rsp.write_text("\n".join(lines) + "\n", encoding="utf-8")
    extra = [command / (PLAYER_ASSEMBLY + ".rsp2")]
    proc = run_quiet([dotnet_exe(), "exec", csc_dll(), "/nostdlib", "/noconfig", "/shared", "@" + str(patch_rsp),
                      *("@" + str(p) for p in extra if p.exists())], cwd=ROOT)
    if proc.returncode != 0 or not dll.exists():
        errors = [l.strip() for l in proc.stdout.splitlines() if "error CS" in l]
        return None, (errors or [proc.stdout[-1500:]])[:12]
    return dll, []


def link_tree(source, target):
    """Hardlink every file of a build into a new folder: instant and costs no disk space."""
    for folder, dirs, names in os.walk(source):
        rel = Path(folder).relative_to(source)
        if rel.parts and rel.parts[0] == COMPILE_DIR:
            continue
        (target / rel).mkdir(parents=True, exist_ok=True)
        for n in names:
            if not rel.parts and n in ("build-info.json", "source-manifest.json.gz", "build-result.txt"):
                continue
            os.link(Path(folder) / n, target / rel / n)


def replace_file(path, data):
    """Break the hardlink before writing, so the base build is never changed."""
    path.unlink(missing_ok=True)
    path.write_bytes(data)


def try_patch(base, files, stamp, by, began):
    """Make a new build from `base` by swapping in a recompiled Assembly-CSharp. Returns (name, None) or (None, reason)."""
    old = read_manifest(base)
    if old is None:
        return None, "the newest build has no file map"
    change = change_summary(old, files)
    reason = patch_reason(change)
    if reason:
        return None, reason
    work = HOME / "_patch"
    shutil.rmtree(work, ignore_errors=True)
    dll, errors = compile_patch(base, work)
    if dll is None:
        return None, "compile failed:\n  " + "\n  ".join(errors)
    managed = base / "AgentPlay_Data" / "Managed"
    proc = run_quiet([dotnet_exe(), dllcheck_tool(), managed / (PLAYER_ASSEMBLY + ".dll"), dll])
    if proc.returncode != 0:
        return None, "DllCheck failed: " + (proc.stderr or proc.stdout)[-800:]
    check = json.loads(proc.stdout)
    blockers = (["serialized fields changed in " + t for t in check["layout"]]
                + ["new Unity object type " + t for t in check["added"]]
                + ["removed Unity object type " + t for t in check["removed"]])
    if blockers:
        return None, "the built scene data cannot follow: " + "; ".join(blockers[:6])
    if STAGE.exists():
        shutil.rmtree(STAGE)
    link_tree(base, STAGE)
    replace_file(STAGE / "AgentPlay_Data" / "Managed" / (PLAYER_ASSEMBLY + ".dll"), dll.read_bytes())
    if check["runtimeInitChanged"]:
        init_path = STAGE / "AgentPlay_Data" / "RuntimeInitializeOnLoads.json"
        data = json.loads(init_path.read_text(encoding="utf-8"))
        kept = [e for e in data["root"] if e.get("assemblyName") != PLAYER_ASSEMBLY]
        data["root"] = kept + [dict(e, assemblyName=PLAYER_ASSEMBLY, isUnityClass=False) for e in check["runtimeInit"]]
        replace_file(init_path, json.dumps(data, separators=(",", ":")).encode("utf-8"))
    if is_agent_rsp(base / COMPILE_DIR / (PLAYER_ASSEMBLY + ".rsp")):
        shutil.copytree(base / COMPILE_DIR, STAGE / COMPILE_DIR)
    else:
        save_compile_command(STAGE)
    summary = "patched %s.dll onto %s (%s; no Unity)" % (PLAYER_ASSEMBLY, base.name, change["text"])
    (STAGE / "build-result.txt").write_text(summary, encoding="utf-8")
    info = make_info(stamp, summary, files, base, began, 1, by)
    info["patchedFrom"] = base.name
    name, info = promote(stamp, info, files)
    return name, None


def cmd_build(args):
    acquire_build_lock()
    try:
        current = latest_build()
        extras, files = scan_sources()
        stamp = stamp_of(extras, files)
        if current is not None and current.name.endswith("-" + stamp) and not args.force:
            recorded = read_info(current)
            print("UP TO DATE " + (one_line(recorded) if recorded else current.name))
            return
        began = time.time()
        if current is not None and not args.full and not args.force:
            name, reason = try_patch(current, files, stamp, args.by, began)
            if name is not None:
                print("PATCHED %s in %.1fs (code-only change: swapped Assembly-CSharp.dll, no Unity started)" % (name, time.time() - began))
                return
            if reason.startswith("compile failed"):
                fail("%s\nFix the compile errors (or wait for the other agent's edit to land) and run build again." % reason)
            print("full build needed: " + reason, flush=True)
        attempt = 0
        while True:
            attempt += 1
            stamp, summary, errors, files = build_once(args.timeout)
            if stamp is not None:
                break
            if not errors or attempt > args.retries:
                detail = ("\nCompile errors in the working tree:\n  " + "\n  ".join(errors)) if errors else ""
                note = "\nThe newest working build is still there: " + current.name if current is not None else ""
                fail("build failed after %.0fs: %s%s%s" % (time.time() - began, summary, detail, note))
            # Several agents share the tree, so these are usually somebody's edit in progress. Wait for it to land.
            print("build errors (attempt %d of %d), usually an edit in progress; retrying in %ds:\n  %s"
                  % (attempt, args.retries + 1, args.retry_wait, errors[0][:300]), flush=True)
            time.sleep(args.retry_wait)
        save_compile_command(STAGE)
        name, info = promote(stamp, make_info(stamp, summary, files, current, began, attempt, args.by), files)
        print("BUILT %s in %.0fs, %d attempt(s) (%s)" % (name, time.time() - began, attempt, summary))
        print(one_line(info))
    finally:
        BUILD_LOCK.unlink(missing_ok=True)


def cmd_info(args):
    """Is the newest build usable right now? Exit 0 = matches the working tree, 3 = stale (lists what differs), 2 = no build."""
    build = latest_build()
    if build is None:
        print("NO BUILD: run agentplay.py build")
        sys.exit(2)
    info = read_info(build)
    print(one_line(info) if info else "%s (made before build records existed)" % build.name)
    extras, files = scan_sources()
    if build.name.endswith("-" + stamp_of(extras, files)):
        print("STATUS: CURRENT - the build matches the working tree; start your own player from it, do not rebuild")
        return
    old = read_manifest(build)
    if old is None:
        print("STATUS: STALE - sources changed since this build (it has no file map to list them): agentplay.py build")
        sys.exit(3)
    change = change_summary(old, files)
    if not change["total"]:
        print("STATUS: STALE - no source file differs; the build tool or its options changed: agentplay.py build")
        sys.exit(3)
    print("STATUS: STALE - %s since this build:" % change["text"])
    shown = change["files"] if args.files else change["files"][:25]
    for line in shown:
        print("  " + line)
    if len(shown) < len(change["files"]):
        print("  ... %d more (info --files lists all)" % (len(change["files"]) - len(shown)))
    print("If none of these affect what you test, you may start without --require-current and say so in your report.")
    sys.exit(3)


# ------------------------------------------------------------------ instances

def pid_alive(pid, program=None):
    """Asks Windows directly: spawning tasklist in a poll loop is slow and flashes console windows.
    Windows reuses process ids, so with `program` (an exe name) the id must also belong to that program:
    a stale pid file must never make a long-dead player or build look alive."""
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.OpenProcess.restype = ctypes.c_void_p
    kernel32.OpenProcess.argtypes = [ctypes.c_ulong, ctypes.c_int, ctypes.c_ulong]
    kernel32.GetExitCodeProcess.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_ulong)]
    kernel32.QueryFullProcessImageNameW.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_wchar_p, ctypes.POINTER(ctypes.c_ulong)]
    kernel32.CloseHandle.argtypes = [ctypes.c_void_p]
    handle = kernel32.OpenProcess(0x1000, 0, pid)  # PROCESS_QUERY_LIMITED_INFORMATION
    if not handle:
        return False
    try:
        code = ctypes.c_ulong()
        if not (kernel32.GetExitCodeProcess(handle, ctypes.byref(code)) and code.value == 259):  # STILL_ACTIVE
            return False
        if program is None:
            return True
        size = ctypes.c_ulong(1024)
        name = ctypes.create_unicode_buffer(size.value)
        if not kernel32.QueryFullProcessImageNameW(handle, 0, name, ctypes.byref(size)):
            return False
        exe = os.path.basename(name.value).lower()
        return exe == program.lower() or (not program.endswith(".exe") and exe.startswith(program.lower()))
    finally:
        kernel32.CloseHandle(handle)


def run_dir(name):
    if not re.fullmatch(r"[A-Za-z0-9_-]+", name):
        fail("instance names may only use letters, digits, _ and -")
    return RUNS / name


def instance_pid(folder):
    try:
        return int((folder / "pid.txt").read_text())
    except (OSError, ValueError):
        return 0


def require_running(name):
    folder = run_dir(name)
    pid = instance_pid(folder)
    if not pid or not pid_alive(pid, EXE):
        fail("player '%s' is not running: agentplay.py start %s. Log: %s" % (name, name, folder / "player.log"), 2)
    return folder, pid


def cmd_start(args):
    build = latest_build()
    if build is None:
        fail("no build yet: run agentplay.py build", 2)
    current = build.name.endswith("-" + source_stamp())
    if args.require_current and not current:
        fail(f"build {build.name} is stale: run agentplay.py build", 2)
    folder = run_dir(args.name)
    pid = instance_pid(folder)
    if pid and pid_alive(pid, EXE):
        fail("player '%s' is already running (pid %d)" % (args.name, pid))
    if folder.exists():
        shutil.rmtree(folder)  # a run folder is one session: pictures from the last one are gone
    folder.mkdir(parents=True)
    width, height = args.res.lower().split("x")
    cmd = [str(build / EXE), "-agent", args.name, "-agent-out", str(folder),
           "-agent-fps", str(args.fps), "-agent-scale", str(args.scale),
           "-logFile", str(folder / "player.log"),
           "-screen-fullscreen", "0", "-screen-width", width, "-screen-height", height]
    if not args.window:
        cmd.append("-batchmode")
    if args.headless:
        cmd.append("-nographics")
    if args.scene:
        cmd += ["-agent-scene", args.scene]
    if args.script:
        cmd += ["-agent-script", str(Path(args.script).resolve())]
    proc = subprocess.Popen(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                            creationflags=subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP)
    (folder / "pid.txt").write_text(str(proc.pid))
    (folder / "build.txt").write_text(build.name)
    # The boot command loads the scene; wait for its answer so the first real command finds a ready game.
    answer = wait_answer(folder, proc.pid, "boot", args.timeout)
    print(json.dumps({"started": args.name, "pid": proc.pid, "build": build.name, "sourceMatches": current,
                      "folder": str(folder), "boot": answer}, indent=1))
    if not answer.get("ok"):
        sys.exit(1)


def wait_answer(folder, pid, command_id, timeout):
    final = folder / "outbox" / (command_id + ".json")
    until = time.time() + timeout
    checks = 0
    while time.time() < until:
        if final.exists():
            try:
                return json.loads(final.read_text(encoding="utf-8"))
            except (OSError, ValueError):
                pass  # being replaced; look again
        time.sleep(0.2)
        checks += 1
        if checks % 25 == 0 and not pid_alive(pid, EXE):
            return {"ok": False, "error": "the player exited; see " + str(folder / "player.log")}
    return {"ok": False, "error": "no answer within %ds (the command may still be running)" % timeout, "id": command_id}


def send(name, command, timeout):
    folder, pid = require_running(name)
    command = dict(command)
    command["id"] = command.get("id") or "c" + uuid.uuid4().hex[:10]
    # Numbered by time so commands from one agent keep their order; renamed so the player never reads half a file.
    stem = "%d_%s" % (time.time_ns(), command["id"])
    tmp = folder / "inbox" / (stem + ".tmp")
    tmp.write_text(json.dumps(command), encoding="utf-8")
    os.replace(tmp, folder / "inbox" / (stem + ".json"))
    return folder, wait_answer(folder, pid, command["id"], timeout)


def finish(answer):
    print(json.dumps(answer, indent=1))
    if not answer.get("ok"):
        sys.exit(1)


def cmd_send(args):
    try:
        command = json.loads(args.json)
    except ValueError as error:
        fail("bad JSON: %s" % error)
    finish(send(args.name, command, args.timeout)[1])


def triple(text):
    parts = [float(x) for x in text.split(",")]
    if len(parts) != 3:
        fail("expected x,y,z but got " + text)
    return parts


def cmd_shot(args):
    command = {"op": "shot", "w": args.w, "h": args.h}
    if args.as_name:
        command["name"] = args.as_name
    if args.pos:
        command["pos"] = triple(args.pos)
    if args.look:
        command["look_at"] = triple(args.look)
    if args.euler:
        command["euler"] = triple(args.euler)
    if args.fov:
        command["fov"] = args.fov
    folder, answer = send(args.name, command, args.timeout)
    if answer.get("file"):
        answer["path"] = str(folder / answer["file"])
    finish(answer)


def cmd_sheet(args):
    """Packs a player's pictures into labelled contact sheets, cheap to look through."""
    try:
        from PIL import Image, ImageDraw
    except ImportError:
        fail("contact sheets need Pillow: python -m pip install pillow")
    folder = run_dir(args.name)
    shots = sorted((folder / "shots").glob("*.png"))
    if not shots:
        fail("no pictures in " + str(folder / "shots"))
    out = folder / "sheets"
    shutil.rmtree(out, ignore_errors=True)
    out.mkdir()
    cols, rows, width, height = 3, 4, 480, 270
    per = cols * rows
    for n in range(0, len(shots), per):
        sheet = Image.new("RGB", (cols * width, rows * height), (20, 20, 20))
        draw = ImageDraw.Draw(sheet)
        for i, path in enumerate(shots[n:n + per]):
            x, y = (i % cols) * width, (i // cols) * height
            sheet.paste(Image.open(path).convert("RGB").resize((width, height)), (x, y))
            draw.rectangle([x, y, x + width, y + 16], fill=(0, 0, 0))
            draw.text((x + 3, y + 2), path.stem[:72], fill=(255, 255, 0))
        target = out / ("sheet_%d.png" % (n // per + 1))
        sheet.save(target)
        print(target)


def stop_one(name):
    folder = run_dir(name)
    pid = instance_pid(folder)
    if not pid or not pid_alive(pid, EXE):
        return "%s: not running" % name
    send(name, {"op": "quit"}, 15)
    for _ in range(40):
        if not pid_alive(pid, EXE):
            return "%s: stopped" % name
        time.sleep(0.25)
    subprocess.run(["taskkill", "/PID", str(pid), "/F"], capture_output=True, creationflags=subprocess.CREATE_NO_WINDOW)
    return "%s: killed" % name


def cmd_stop(args):
    if not args.all and not args.name:
        fail("name a player or pass --all")
    names = [p.name for p in RUNS.iterdir() if p.is_dir()] if args.all and RUNS.exists() else [args.name]
    for name in names:
        print(stop_one(name))


def cmd_list(args):
    build = latest_build()
    print("latest build: " + (build.name if build else "none"))
    print("sources changed since: " + ("unknown" if not build else str(not build.name.endswith("-" + source_stamp()))))
    print("editor open: " + str(editor_is_open()))
    if RUNS.exists():
        for folder in sorted(RUNS.iterdir()):
            pid = instance_pid(folder)
            beat = folder / "heartbeat.txt"
            state = "stopped"
            if pid and pid_alive(pid, EXE):
                state = "running"
                if beat.exists():
                    parts = beat.read_text(encoding="utf-8").split(" ", 1)
                    age = time.time() - int(parts[0])
                    state = "%s, %s, heartbeat %ds ago%s" % (state, parts[1] if len(parts) > 1 else "", age, " (STUCK?)" if age > STALE_HEARTBEAT else "")
            print("%s: %s" % (folder.name, state))


# ------------------------------------------------------------------ editor Play Mode claim
# One agent at a time may test in the shared editor's Play Mode (Docs/rules/TESTING.md rule 1). The claim is a file with
# an expiry; renewing it is `claim` again by the same name. It is held only while an agent tests in the editor (released
# right after, expires if not renewed), so finished agents never block. Whether Unity is busy is checked by the agent first.

EDITOR_CLAIM = HOME / "editor-play.json"
EDITOR_CLAIM_MINUTES = 15


def read_claim():
    try:
        claim = json.loads(EDITOR_CLAIM.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None
    return claim if claim.get("until", 0) > time.time() else None


def cmd_editor(args):
    HOME.mkdir(parents=True, exist_ok=True)
    claim = read_claim()
    if args.action == "status":
        print("FREE" if claim is None else "CLAIMED by %s until %s (%s)" % (
            claim["by"], time.strftime("%H:%M:%S", time.localtime(claim["until"])), claim.get("why", "")))
        return
    if not args.by:
        fail("--by NAME is required")
    if args.action == "claim":
        if claim is not None and claim["by"] != args.by:
            fail("editor Play Mode is claimed by %s until %s: use the standalone player (agentplay.py build/start)"
                 % (claim["by"], time.strftime("%H:%M:%S", time.localtime(claim["until"]))), 4)
        until = time.time() + args.minutes * 60
        write_text_atomic(EDITOR_CLAIM, json.dumps({"by": args.by, "why": args.why or "", "since": int(time.time()), "until": int(until)}))
        print("CLAIMED editor Play Mode for %s until %s; renew with the same command, release when done"
              % (args.by, time.strftime("%H:%M:%S", time.localtime(until))))
    else:
        if claim is not None and claim["by"] != args.by:
            fail("not yours: claimed by " + claim["by"])
        EDITOR_CLAIM.unlink(missing_ok=True)
        print("RELEASED")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)

    p = sub.add_parser("build")
    p.add_argument("--force", action="store_true", help="full rebuild even when the build is current")
    p.add_argument("--full", action="store_true", help="never patch: always run the full Unity build")
    p.add_argument("--by", help="who asked for the build, kept in the build record (your agent name)")
    p.add_argument("--timeout", type=int, default=3600)
    p.add_argument("--retries", type=int, default=5, help="retries after compile errors (another agent's edit in progress)")
    p.add_argument("--retry-wait", type=int, default=120)
    p.set_defaults(func=cmd_build)

    p = sub.add_parser("shader-report", help="dry run: which keywords multiply the shader variants")
    p.add_argument("--timeout", type=int, default=3600)
    p.add_argument("--retries", type=int, default=5)
    p.add_argument("--retry-wait", type=int, default=120)
    p.set_defaults(func=cmd_shader_report)

    p = sub.add_parser("start")
    p.add_argument("name")
    p.add_argument("--require-current", action="store_true", help="refuse a build made from different inputs")
    p.add_argument("--scene", default=CONFIG.get("defaultScene", ""),
                   help="build scene to load at boot (default: agentplay.json defaultScene, else the first build scene), or 'none'")
    p.add_argument("--headless", action="store_true", help="no GPU: lightest, for logic checks, cannot take pictures")
    p.add_argument("--window", action="store_true", help="show a window instead of running hidden")
    p.add_argument("--res", default="640x360")
    p.add_argument("--fps", type=int, default=30)
    p.add_argument("--scale", type=float, default=1.0, help="Time.timeScale")
    p.add_argument("--script", help="JSON file with a list of commands to run after boot")
    p.add_argument("--timeout", type=int, default=180)
    p.set_defaults(func=cmd_start)

    p = sub.add_parser("send")
    p.add_argument("name")
    p.add_argument("json")
    p.add_argument("--timeout", type=int, default=120)
    p.set_defaults(func=cmd_send)

    p = sub.add_parser("shot")
    p.add_argument("name")
    p.add_argument("--as", dest="as_name")
    p.add_argument("--pos")
    p.add_argument("--look")
    p.add_argument("--euler")
    p.add_argument("--fov", type=float)
    p.add_argument("--w", type=int, default=1280)
    p.add_argument("--h", type=int, default=720)
    p.add_argument("--timeout", type=int, default=60)
    p.set_defaults(func=cmd_shot)

    p = sub.add_parser("sheet", help="pack a player's pictures into labelled contact sheets")
    p.add_argument("name")
    p.set_defaults(func=cmd_sheet)

    p = sub.add_parser("stop")
    p.add_argument("name", nargs="?")
    p.add_argument("--all", action="store_true")
    p.set_defaults(func=cmd_stop)

    sub.add_parser("list").set_defaults(func=cmd_list)

    p = sub.add_parser("editor", help="claim, release or check the one agent slot for editor Play Mode testing")
    p.add_argument("action", choices=("status", "claim", "release"))
    p.add_argument("--by", help="your agent name (the same name renews or releases)")
    p.add_argument("--why", help="what you test, shown to other agents")
    p.add_argument("--minutes", type=int, default=EDITOR_CLAIM_MINUTES)
    p.set_defaults(func=cmd_editor)

    p = sub.add_parser("info", help="when the newest build was made, from which commit, what changed, and whether it is current")
    p.add_argument("--files", action="store_true", help="list every changed file, not the first 25")
    p.set_defaults(func=cmd_info)

    load_plugin(sub)
    args = parser.parse_args()
    args.func(args)


def load_plugin(sub):
    """The project's own commands (tools/agentplay/agentplay_project.py): register(sub, ap) adds subcommands."""
    if not PLUGIN_FILE.is_file():
        return
    import importlib.util
    spec = importlib.util.spec_from_file_location("agentplay_project", PLUGIN_FILE)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.register(sub, sys.modules[__name__])


if __name__ == "__main__":
    main()
