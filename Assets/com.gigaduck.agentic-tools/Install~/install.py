#!/usr/bin/env python3
"""Install or update the Agentic Tools (com.gigaduck.agentic-tools) in a project, without Unity.

The Unity window (Tools > Agentic Tools) does the same with the same rules (Editor/Installer/AgenticInstaller.cs; keep the
two in step, test_install.py checks both); this script is for machines without Unity (cloud agents, CI) and for
repositories that are not Unity projects. Standard library only, Python 3.9+.

  install.py --status                      what is installed and what an update would change (writes nothing)
  install.py                               install or update the enabled modules (first run: the default modules)
  install.py --enable taskboard            also install a module; --disable removes one (unchanged files only)
  install.py --set RULES_DIR=Docs/rules    change a path variable (kept in .agentic-tools.json)
  install.py --force                       also replace files and rule blocks that were edited by hand
  install.py --dry-run                     print the plan only
  install.py --personal-skills             also copy the skills to this user's agent folders (~/.claude/skills, ...)
  install.py --uninstall                   remove every unchanged managed file and rule block

How it injects without overriding (the same in the Unity window):
  * Tools are copied to fixed paths. A path that already holds a different file nobody installed, or a managed file
    that was edited by hand since it was installed, is a CONFLICT: it is left alone and reported (--force replaces it).
  * Rules are blocks between `<!-- agentic-tools:begin ID -->` and `<!-- agentic-tools:end ID -->` (`# agentic-tools:...`
    lines in other text files). Only the inside of a block is ever written; everything around it stays the project's
    own. A missing file is created, an existing one gets the block appended (or placed after another block).
  * JSON settings (.mcp.json) are merged: missing keys are added, existing values are never changed.
  * .agentic-tools.json in the project root records the version, the modules, the variables and a hash of everything
    written, so an update can tell an untouched copy (safe to replace) from a hand edit (a conflict). Text is compared
    and hashed with LF line endings, and an updated file keeps its own line endings.
Exit codes: 0 done, 1 conflicts were left alone, 2 usage or package error.
"""
import argparse
import hashlib
import json
import os
import re
import shutil
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
PACKAGE_ROOT = HERE.parent
STATE_FILE = ".agentic-tools.json"
MARK = "agentic-tools"


class InstallError(Exception):
    pass


# ------------------------------------------------------------------ text, hashes, variables

def sha(data):
    return hashlib.sha256(data).hexdigest()


def is_text(data):
    return b"\0" not in data


def normalized(data):
    """Text compares and hashes with LF endings: Git checks a file out as CRLF on one machine and LF on another."""
    return data.replace(b"\r\n", b"\n") if is_text(data) else data


def file_hash(data):
    return sha(normalized(data))


def in_style_of(data, existing):
    """Content written over an existing text file keeps that file's line endings, so a diff shows only real changes."""
    if existing is not None and is_text(data) and b"\r\n" in existing:
        return normalized(data).replace(b"\n", b"\r\n")
    return data


def read_text_lf(path):
    return path.read_bytes().decode("utf-8-sig").replace("\r\n", "\n")


def rel_path(target, start):
    """Forward-slash relative path from folder `start` to folder `target` (both relative to the project root)."""
    t = [p for p in target.split("/") if p and p != "."]
    s = [p for p in start.split("/") if p and p != "."]
    while t and s and t[0] == s[0]:
        t.pop(0)
        s.pop(0)
    return "/".join([".."] * len(s) + t) or "."


def all_vars(manifest, state_vars, project_name):
    values = dict(manifest.get("vars", {}))
    values.update({k: v for k, v in (state_vars or {}).items() if k in values})
    values["PROJECT"] = project_name
    values["RULES_TO_ROOT"] = rel_path("", values["RULES_DIR"])
    values["RULES_TO_DOCS"] = rel_path(values["DOCS_DIR"], values["RULES_DIR"])
    values["DOCS_TO_ROOT"] = rel_path("", values["DOCS_DIR"])
    values["DOCS_TO_RULES"] = rel_path(values["RULES_DIR"], values["DOCS_DIR"])
    return values


def render(text, values, where):
    def one(match):
        if match.group(1) not in values:
            raise InstallError("%s uses an unknown variable {{%s}}" % (where, match.group(1)))
        return values[match.group(1)]
    return re.sub(r"\{\{([A-Z_]+)\}\}", one, text)


# ------------------------------------------------------------------ blocks

def marker_style(dst):
    return "html" if dst.lower().endswith((".md", ".mdc", ".html")) else "hash"


def markers(style, block_id):
    if style == "html":
        return "<!-- %s:begin %s -->" % (MARK, block_id), "<!-- %s:end %s -->" % (MARK, block_id)
    return "# %s:begin %s" % (MARK, block_id), "# %s:end %s" % (MARK, block_id)


def find_block(text, style, block_id):
    """(start, end, body) of a block in LF text; `end` is just past the end marker's line break. None when absent."""
    begin, end = markers(style, block_id)
    at = -1
    for match in re.finditer(re.escape(begin) + r"[ \t]*(?:\n|$)", text):
        if match.start() == 0 or text[match.start() - 1] == "\n":
            at = match.start()
            body_start = match.end()
            break
    if at < 0:
        return None
    close = re.compile(r"(?:^|\n)" + re.escape(end) + r"[ \t]*(?:\n|$)").search(text, body_start - 1)
    if close is None:
        raise InstallError("block '%s' has a begin marker but no end marker" % block_id)
    body_end = close.start() + (1 if text[close.start()] == "\n" else 0)
    return at, close.end(), text[body_start:body_end]


def block_text(style, block_id, body):
    begin, end = markers(style, block_id)
    return begin + "\n" + body + end + "\n"


def body_of(text):
    return text.rstrip("\n") + "\n"


# ------------------------------------------------------------------ plan

class Plan:
    def __init__(self, project, manifest, state, modules, values, force):
        self.project, self.manifest, self.state = project, manifest, state
        self.modules, self.values, self.force = modules, values, force
        self.actions = []      # [verb, path, detail, group]
        self.writes = {}       # path -> bytes, or None to delete
        self.new_files = {}    # managed file -> hash
        self.new_blocks = {}   # "path#id" -> hash
        self.texts = {}        # path -> LF text being built (files that get blocks)
        self.originals = {}    # path -> bytes on disk before this plan (None = missing)

    def note(self, verb, path, detail="", group=None):
        self.actions.append((verb, path, detail, group))

    @property
    def conflicts(self):
        return [a[1] for a in self.actions if a[0] == "CONFLICT"]

    @property
    def changes(self):
        return sum(1 for a in self.actions if a[0] in ("ADD", "UPDATE", "REMOVE"))

    def original(self, rel):
        if rel not in self.originals:
            path = self.project / rel
            self.originals[rel] = path.read_bytes() if path.is_file() else None
        return self.originals[rel]

    def text_of(self, rel):
        if rel not in self.texts:
            data = self.original(rel)
            self.texts[rel] = None if data is None else data.decode("utf-8-sig").replace("\r\n", "\n")
        return self.texts[rel]


def plan_copy(plan, dst, src, group):
    data = src.read_bytes()
    want = file_hash(data)
    current = plan.original(dst)
    recorded = plan.state.get("files", {}).get(dst)
    if current is not None and file_hash(current) == want:
        plan.new_files[dst] = want
        plan.note("same", dst, group=group)
    elif current is None:
        plan.new_files[dst] = want
        plan.writes[dst] = data
        plan.note("ADD", dst, group=group)
    elif recorded is not None and file_hash(current) == recorded:
        plan.new_files[dst] = want
        plan.writes[dst] = in_style_of(data, current)
        plan.note("UPDATE", dst, group=group)
    elif plan.force:
        plan.new_files[dst] = want
        plan.writes[dst] = in_style_of(data, current)
        plan.note("UPDATE", dst, "forced over a local edit")
    else:
        if recorded is not None:
            plan.new_files[dst] = recorded
        plan.note("CONFLICT", dst, "edited by hand since it was installed" if recorded else "a file nobody installed is already there")


def plan_block(plan, item):
    dst = render(item["dst"], plan.values, item["src"]).strip("/")
    block_id = item["id"]
    style = marker_style(dst)
    body = body_of(render(read_text_lf(HERE / item["src"]), plan.values, item["src"]))
    key = dst + "#" + block_id
    want = sha(body.encode("utf-8"))
    text = plan.text_of(dst)
    if text is None:
        header = render(read_text_lf(HERE / item["header"]), plan.values, item["header"]) if item.get("header") else ""
        plan.texts[dst] = header + block_text(style, block_id, body)
        plan.new_blocks[key] = want
        plan.note("ADD", key, "new file")
        return
    found = find_block(text, style, block_id)
    if found is None:
        anchor = find_block(text, style, item["after"]) if item.get("after") else None
        if anchor is not None:
            plan.texts[dst] = text[:anchor[1]] + "\n" + block_text(style, block_id, body) + text[anchor[1]:]
        else:
            if text and not text.endswith("\n"):
                text += "\n"
            plan.texts[dst] = text + ("\n" if text else "") + block_text(style, block_id, body)
        plan.new_blocks[key] = want
        plan.note("ADD", key, "injected")
        return
    start, end, old = found
    recorded = plan.state.get("blocks", {}).get(key)
    if old == body:
        plan.new_blocks[key] = want
        plan.note("same", key)
    elif (recorded is not None and sha(old.encode("utf-8")) == recorded) or plan.force:
        plan.texts[dst] = text[:start] + block_text(style, block_id, body) + text[end:]
        plan.new_blocks[key] = want
        plan.note("UPDATE", key, "" if recorded is not None and sha(old.encode("utf-8")) == recorded else "forced over a local edit")
    else:
        if recorded is not None:
            plan.new_blocks[key] = recorded
        plan.note("CONFLICT", key, "the block was edited by hand")


def merge_missing(target, source, depth):
    """Adds the keys of `source` that `target` lacks, looking `depth` levels deep: with 2, a missing server is added to
    "mcpServers", but a server the project already defines is left exactly as it is."""
    changed = False
    for key, value in source.items():
        if key not in target:
            target[key] = value
            changed = True
        elif depth > 1 and isinstance(target[key], dict) and isinstance(value, dict):
            changed = merge_missing(target[key], value, depth - 1) or changed
    return changed


def plan_json(plan, item):
    dst = render(item["dst"], plan.values, item["src"]).strip("/")
    wanted = json.loads(read_text_lf(HERE / item["src"]))
    current = plan.original(dst)
    recorded = plan.state.get("files", {}).get(dst)
    if current is None:
        data = (json.dumps(wanted, indent=2) + "\n").encode("utf-8")
        plan.writes[dst] = data
        plan.new_files[dst] = file_hash(data)  # created here, so an uninstall may remove it again
        plan.note("ADD", dst, "new file")
        return
    created = recorded is not None and file_hash(current) == recorded
    try:
        data = json.loads(current.decode("utf-8-sig"))
        if not isinstance(data, dict):
            raise ValueError("not an object")
    except ValueError:
        plan.note("CONFLICT", dst, "not plain JSON, so the keys cannot be merged: add them by hand")
        return
    if merge_missing(data, wanted, 2):
        merged = in_style_of((json.dumps(data, indent=2) + "\n").encode("utf-8"), current)
        plan.writes[dst] = merged
        if created:
            plan.new_files[dst] = file_hash(merged)
        plan.note("UPDATE", dst, "added missing keys")
    else:
        if created:
            plan.new_files[dst] = recorded
        plan.note("same", dst)


def headers_by_block(plan):
    """path -> the header text the file was created with, for every block item in the manifest that has one."""
    headers = {}
    for module in plan.manifest["modules"]:
        for item in module["items"]:
            if item["kind"] == "block" and item.get("header"):
                dst = render(item["dst"], plan.values, item["src"]).strip("/")
                headers[dst] = render(read_text_lf(HERE / item["header"]), plan.values, item["header"])
    return headers


def plan_removals(plan):
    """Managed files and blocks this plan no longer installs are removed when unchanged (or with --force). A file left with
    nothing but the header it was created with is deleted."""
    headers = headers_by_block(plan)
    for rel, recorded in sorted(plan.state.get("files", {}).items()):
        if rel in plan.new_files:
            continue
        current = plan.original(rel)
        if current is None:
            continue
        if file_hash(current) == recorded or plan.force:
            plan.writes[rel] = None
            plan.note("REMOVE", rel)
        else:
            plan.note("KEEP", rel, "no longer installed, but edited by hand: left in place")
    for key, recorded in sorted(plan.state.get("blocks", {}).items()):
        if key in plan.new_blocks:
            continue
        rel, block_id = key.rsplit("#", 1)
        text = plan.text_of(rel)
        found = find_block(text, marker_style(rel), block_id) if text else None
        if not found:
            continue
        start, end, old = found
        if sha(old.encode("utf-8")) == recorded or plan.force:
            before, after = text[:start], text[end:]
            if before.endswith("\n\n") and (after.startswith("\n") or not after):
                before = before[:-1]
            rest = before + after
            if rest.strip() == headers.get(rel, "").strip():
                rest = ""
            plan.texts[rel] = rest
            plan.note("REMOVE", key)
        else:
            plan.note("KEEP", key, "no longer installed, but edited by hand: left in place")


def expand_copy(item, values):
    """[(dst file, src Path)] of a copy item; a folder copies every file in it."""
    src = HERE / item["src"]
    dst = render(item["dst"], values, item["src"]).strip("/")
    if src.is_file():
        return [(dst, src)]
    if not src.is_dir():
        raise InstallError("package file missing: " + item["src"])
    return [(dst + "/" + path.relative_to(src).as_posix(), path) for path in sorted(src.rglob("*"))
            if path.is_file() and "__pycache__" not in path.parts and not path.name.endswith(".pyc")]


def make_plan(project, manifest, modules, values, state, force):
    plan = Plan(project, manifest, state, modules, values, force)
    for module in manifest["modules"]:
        if module["id"] not in modules:
            continue
        for item in module["items"]:
            kind = item["kind"]
            if kind == "copy":
                pairs = expand_copy(item, values)
                group = render(item["dst"], values, item["src"]).strip("/") + "/" if len(pairs) > 1 else None
                for dst, src in pairs:
                    plan_copy(plan, dst, src, group)
            elif kind == "block":
                plan_block(plan, item)
            elif kind == "json-merge":
                plan_json(plan, item)
            else:
                raise InstallError("unknown item kind '%s' in manifest.json" % kind)
    plan_removals(plan)
    for rel, text in plan.texts.items():
        if text is None:
            continue
        original = plan.original(rel)
        data = in_style_of(text.encode("utf-8"), original)
        if original is not None and original.startswith(b"\xef\xbb\xbf"):
            data = b"\xef\xbb\xbf" + data
        if data != original:
            plan.writes[rel] = data if text.strip() else None
    return plan


def state_of(plan, package, version):
    return {
        "package": package,
        "version": version,
        "modules": [m["id"] for m in plan.manifest["modules"] if m["id"] in plan.modules],
        "vars": {k: plan.values[k] for k in plan.manifest.get("vars", {})},
        "files": dict(sorted(plan.new_files.items())),
        "blocks": dict(sorted(plan.new_blocks.items())),
    }


def remove_empty_folders(folder, stop):
    while folder != stop and stop in folder.parents:
        try:
            folder.rmdir()
        except OSError:
            return
        folder = folder.parent


def apply(plan, package, version):
    for rel, data in sorted(plan.writes.items()):
        path = plan.project / rel
        if data is None:
            if path.exists():
                path.unlink()
                remove_empty_folders(path.parent, plan.project)
            continue
        path.parent.mkdir(parents=True, exist_ok=True)
        temp = path.with_name(path.name + ".agentic-tmp")
        temp.write_bytes(data)
        os.replace(temp, path)
    state_path = plan.project / STATE_FILE
    if not plan.modules:
        if state_path.exists():
            state_path.unlink()
        return
    text = json.dumps(state_of(plan, package, version), indent=2) + "\n"
    state_path.write_bytes(in_style_of(text.encode("utf-8"), state_path.read_bytes() if state_path.exists() else None))


# ------------------------------------------------------------------ output, personal skills, main

def print_actions(actions, verbose):
    """One line per change; the files of a copied folder are counted per verb unless --verbose."""
    counted = {}
    for verb, path, detail, group in actions:
        if group and not verbose and verb in ("same", "ADD", "UPDATE"):
            counted.setdefault((group, verb), 0)
            counted[(group, verb)] += 1
            continue
        if verb == "same" and not verbose:
            continue
        print("  %-8s %s%s" % (verb, path, ("  (" + detail + ")") if detail else ""))
    for (group, verb), count in counted.items():
        if verb != "same":
            print("  %-8s %s (%d file%s)" % (verb, group, count, "" if count == 1 else "s"))


def personal_skill_roots():
    home = Path.home()
    codex = Path(os.environ["CODEX_HOME"]) / "skills" if os.environ.get("CODEX_HOME") else home / ".codex" / "skills"
    return [codex, home / ".claude" / "skills", home / ".config" / "kilo" / "skills", home / ".gemini" / "config" / "skills"]


def install_personal_skills(dry_run):
    skills = sorted(p for p in (HERE / "skills").iterdir() if (p / "SKILL.md").is_file())
    for root in personal_skill_roots():
        print("%s %d skills to %s" % ("would copy" if dry_run else "copied", len(skills), root))
        if not dry_run:
            for skill in skills:
                shutil.copytree(skill, root / skill.name, dirs_exist_ok=True, ignore=shutil.ignore_patterns("__pycache__", "*.pyc"))


def find_project(start):
    path = Path(start).resolve()
    for candidate in [path, *path.parents]:
        if (candidate / STATE_FILE).exists() or (candidate / "ProjectSettings" / "ProjectVersion.txt").exists():
            return candidate
    return path


def choose_modules(manifest, state, args):
    known = [m["id"] for m in manifest["modules"]]
    if args.uninstall:
        return []
    if args.modules:
        modules = [m.strip() for m in args.modules.split(",") if m.strip()]
    elif state.get("modules") is not None:
        modules = [m for m in state["modules"] if m in known]
    else:
        modules = [m["id"] for m in manifest["modules"] if m.get("default")]
    modules += [m for m in args.enable if m not in modules]
    modules = [m for m in modules if m not in args.disable]
    for m in modules:
        if m not in known:
            raise InstallError("unknown module '%s' (modules: %s)" % (m, ", ".join(known)))
    modules += [m["id"] for m in manifest["modules"] if m.get("required") and m["id"] not in modules]
    return [m for m in known if m in modules]


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", help="project root (default: the Unity project or installed project around the current folder)")
    parser.add_argument("--modules", help="comma-separated: exactly these modules")
    parser.add_argument("--enable", action="append", default=[], help="add a module")
    parser.add_argument("--disable", action="append", default=[], help="remove a module")
    parser.add_argument("--set", action="append", default=[], metavar="VAR=VALUE", help="set a path variable")
    parser.add_argument("--force", action="store_true", help="replace hand-edited managed files and blocks too")
    parser.add_argument("--dry-run", action="store_true", help="print the plan, write nothing")
    parser.add_argument("--status", action="store_true", help="the module list and the plan, write nothing")
    parser.add_argument("--uninstall", action="store_true", help="remove every unchanged managed file and block")
    parser.add_argument("--personal-skills", action="store_true", help="also copy the skills to this user's agent folders")
    parser.add_argument("--verbose", action="store_true", help="list every file, also unchanged ones")
    args = parser.parse_args(argv)

    try:
        project = Path(args.project).resolve() if args.project else find_project(".")
        manifest = json.loads((HERE / "manifest.json").read_text(encoding="utf-8"))
        info = json.loads((PACKAGE_ROOT / "package.json").read_text(encoding="utf-8"))
        state_path = project / STATE_FILE
        state = json.loads(state_path.read_text(encoding="utf-8")) if state_path.exists() else {}
        modules = choose_modules(manifest, state, args)
        state_vars = dict(state.get("vars", {}))
        for pair in args.set:
            name, _, value = pair.partition("=")
            if name not in manifest.get("vars", {}):
                raise InstallError("unknown variable %s (variables: %s)" % (name, ", ".join(manifest.get("vars", {}))))
            state_vars[name] = value.strip().replace("\\", "/").strip("/")
        values = all_vars(manifest, state_vars, project.name)
        plan = make_plan(project, manifest, modules, values, state, args.force)
    except InstallError as error:
        print("ERROR: %s" % error, file=sys.stderr)
        return 2

    dry = args.dry_run or args.status
    print("Agentic Tools %s -> %s (installed: %s)" % (info["version"], project, state.get("version", "nothing")))
    print("modules: " + (", ".join(modules) if modules else "none (uninstall)"))
    if args.status:
        for module in manifest["modules"]:
            print("  [%s] %-21s %s" % ("x" if module["id"] in modules else " ", module["id"], module["title"]))
    print_actions(plan.actions, args.verbose)
    if dry:
        print("dry run: %d change(s), %d conflict(s); nothing written" % (plan.changes, len(plan.conflicts)))
    else:
        apply(plan, info["name"], info["version"])
        print("done: %d change(s), %d conflict(s)" % (plan.changes, len(plan.conflicts)))
    if plan.conflicts:
        print("CONFLICTS were left alone (a hand edit, or a file nobody installed). Put the change in the package source or in "
              "the project's own part of the file, then run again; --force replaces them.")
    if args.personal_skills:
        install_personal_skills(dry)
    return 1 if plan.conflicts else 0


if __name__ == "__main__":
    sys.exit(main())
