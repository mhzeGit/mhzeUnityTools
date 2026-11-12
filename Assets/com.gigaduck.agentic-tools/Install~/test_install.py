#!/usr/bin/env python3
"""Tests for the Agentic Tools installer. Every scenario runs through install.py and, when the .NET SDK is installed, through
the Unity window's C# installer (Editor/Installer, built from tests/cs), and both must leave byte-identical projects.

  python test_install.py            (give it a deadline: timeout 600 python test_install.py)
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
PACKAGE = HERE.parent
sys.dont_write_bytecode = True
sys.path.insert(0, str(HERE))
import install  # noqa: E402

CS_BUILD = None


def build_cs():
    """Builds the C# installer once; None when there is no .NET SDK."""
    global CS_BUILD
    if CS_BUILD is not None:
        return CS_BUILD or None
    if shutil.which("dotnet") is None:
        CS_BUILD = ""
        return None
    out = Path(tempfile.mkdtemp(prefix="agentic-cs-"))
    obj = Path(tempfile.mkdtemp(prefix="agentic-cs-obj-"))
    proc = subprocess.run(["dotnet", "build", str(HERE / "tests" / "cs"), "-c", "Release", "-o", str(out), "-nologo",
                           "-p:BaseIntermediateOutputPath=%s/" % obj], capture_output=True, text=True, timeout=600)
    if proc.returncode != 0:
        raise AssertionError("C# installer does not build:\n" + proc.stdout[-3000:])
    CS_BUILD = str(out / "AgenticInstallerCli.dll")
    return CS_BUILD


def run_python(project, *args):
    proc = subprocess.run([sys.executable, "-B", str(HERE / "install.py"), "--project", str(project), *args],
                          capture_output=True, text=True, timeout=300)
    return proc.returncode, proc.stdout + proc.stderr


def run_cs(project, *args):
    dll = build_cs()
    env = dict(os.environ, AGENTIC_PACKAGE_ROOT=str(PACKAGE))
    proc = subprocess.run(["dotnet", dll, "--project", str(project), *args], capture_output=True, text=True, timeout=300, env=env)
    return proc.returncode, proc.stdout + proc.stderr


def tree(root):
    return {p.relative_to(root).as_posix(): p.read_bytes() for p in sorted(Path(root).rglob("*")) if p.is_file()}


class Scenario:
    """Runs the same steps in two copies of a project, one per installer, and compares them after each step."""

    def __init__(self, test, files=None):
        self.test = test
        self.roots = {"py": Path(tempfile.mkdtemp(prefix="agentic-py-"))}
        if build_cs():
            self.roots["cs"] = Path(tempfile.mkdtemp(prefix="agentic-cs-"))
        for root in self.roots.values():
            for rel, data in (files or {}).items():
                path = root / rel
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(data if isinstance(data, bytes) else data.encode("utf-8"))
        self.root = self.roots["py"]

    def run(self, *args, expect=0):
        codes = {}
        for name, root in self.roots.items():
            code, output = (run_python if name == "py" else run_cs)(root, *args)
            self.test.assertEqual(code, expect, "%s installer %s exited %d:\n%s" % (name, " ".join(args), code, output))
            codes[name] = output
        if "cs" in self.roots:
            a, b = tree(self.roots["py"]), tree(self.roots["cs"])
            self.test.assertEqual(sorted(a), sorted(b), "the two installers wrote different files")
            for rel in a:
                self.test.assertEqual(a[rel], b[rel], "the two installers wrote %s differently" % rel)
        return codes["py"]

    def edit(self, rel, change):
        for root in self.roots.values():
            path = root / rel
            path.write_bytes(change(path.read_bytes()))

    def read(self, rel):
        return (self.root / rel).read_bytes().decode("utf-8")

    def exists(self, rel):
        return (self.root / rel).exists()

    def state(self):
        return json.loads(self.read(".agentic-tools.json"))

    def cleanup(self):
        for root in self.roots.values():
            shutil.rmtree(root, ignore_errors=True)


class InstallerTests(unittest.TestCase):
    def scenario(self, files=None):
        s = Scenario(self, files)
        self.addCleanup(s.cleanup)
        return s

    def test_fresh_install_and_idempotent_update(self):
        s = self.scenario()
        s.run()
        state = s.state()
        self.assertEqual(state["modules"], ["core", "virtual-commit", "agent-play", "mcp", "skills", "procedural-animation"])
        self.assertTrue(s.exists("tools/git/agent-commit.ps1"))
        self.assertTrue(s.exists(".claude/skills/unity-gameplay-workflow/SKILL.md"))
        self.assertTrue(s.exists(".agent/skills/environment-design/SKILL.md"))
        self.assertFalse(s.exists("tools/taskboard/taskboard.py"))
        agents = s.read("AGENTS.md")
        self.assertTrue(agents.startswith("# Project Rules for AI Agents"))
        self.assertLess(agents.index("agentic-tools:begin core"), agents.index("agentic-tools:begin git"))
        self.assertLess(agents.index("agentic-tools:begin git"), agents.index("agentic-tools:begin testing"))
        self.assertIn("](Docs/rules/GIT.md)", agents)
        self.assertIn("[AGENTS.md](../../AGENTS.md)", s.read("Docs/rules/GIT.md"))
        self.assertIn("(../AGENT_PLAY.md)", s.read("Docs/rules/TESTING.md"))
        self.assertNotIn("{{", agents + s.read("Docs/rules/AGENTIC_TOOLS.md") + s.read("Docs/AGENT_PLAY.md"))
        before = tree(s.root)
        output = s.run()
        self.assertIn("0 change(s)", output)
        self.assertEqual(before, tree(s.root), "a second run must change nothing")

    def test_injects_into_existing_rules_without_overriding(self):
        own_agents = "# My Game\r\n\r\nNever touch the boss fight.\r\n"
        own_claude = "# Claude notes\n\nUse tabs.\n"
        mcp = {"mcpServers": {"other": {"url": "http://x"}, "unityMCP": {"url": "http://custom"}}, "x": 1}
        mcp_text = json.dumps(mcp)
        s = self.scenario({"AGENTS.md": own_agents, "CLAUDE.md": own_claude, ".mcp.json": mcp_text,
                           ".gitattributes": "*.png lfs\n"})
        s.run()
        agents = s.read("AGENTS.md")
        self.assertTrue(agents.startswith(own_agents), "the project's own text stays first and unchanged")
        self.assertNotIn("\n", agents.replace("\r\n", ""), "a CRLF file stays CRLF")
        self.assertIn("agentic-tools:begin core", agents)
        claude = s.read("CLAUDE.md")
        self.assertTrue(claude.startswith(own_claude))
        self.assertIn("@AGENTS.md", claude)
        merged = json.loads(s.read(".mcp.json"))
        self.assertEqual(merged["mcpServers"]["unityMCP"], {"url": "http://custom"}, "existing values are never changed")
        self.assertEqual(merged["mcpServers"]["other"], {"url": "http://x"})
        self.assertEqual(merged["x"], 1)
        self.assertTrue(s.read(".gitattributes").startswith("*.png lfs\n"))
        s.run("--uninstall")
        self.assertEqual(s.read("AGENTS.md"), own_agents, "uninstall restores the project's own file exactly")
        self.assertEqual(s.read("CLAUDE.md"), own_claude)
        self.assertEqual(s.read(".mcp.json"), mcp_text, "nothing was merged, so the project's file was never rewritten")
        self.assertEqual(s.read(".gitattributes"), "*.png lfs\n")
        self.assertFalse(s.exists(".agentic-tools.json"))
        self.assertFalse(s.exists("tools"))

    def test_conflicts_are_left_alone_until_forced(self):
        s = self.scenario({"virtual-commit.bat": "@echo my own\n"})
        out = s.run(expect=1)
        self.assertIn("CONFLICT virtual-commit.bat", out)
        self.assertEqual(s.read("virtual-commit.bat"), "@echo my own\n")
        s.edit("tools/git/repair-index.ps1", lambda b: b + b"# my fix\n")
        s.edit("AGENTS.md", lambda b: b.replace(b"**Docs are part of the change**", b"**Docs matter.**"))
        out = s.run(expect=1)
        self.assertIn("CONFLICT tools/git/repair-index.ps1", out)
        self.assertIn("CONFLICT AGENTS.md#core", out)
        self.assertIn("# my fix", s.read("tools/git/repair-index.ps1"))
        self.assertIn("**Docs matter.**", s.read("AGENTS.md"))
        state = s.state()
        self.assertIn("tools/git/repair-index.ps1", state["files"], "a conflicting managed file stays managed")
        self.assertNotIn("virtual-commit.bat", state["files"], "a file nobody installed is not adopted")
        s.run("--force")
        self.assertNotIn("# my fix", s.read("tools/git/repair-index.ps1"))
        self.assertIn("**Docs are part of the change**", s.read("AGENTS.md"))
        self.assertNotEqual(s.read("virtual-commit.bat"), "@echo my own\n")

    def test_update_replaces_untouched_copies_and_keeps_line_endings(self):
        s = self.scenario()
        s.run()
        # Pretend the project holds an older version: change a file and a block and record those as installed.
        old_file = b"# old version\r\n"
        s.edit("tools/agentplay/agentplay.py", lambda b: old_file)
        s.edit("Docs/rules/GIT.md", lambda b: b.replace(b"# Git rules", b"# Old git rules"))
        for root in s.roots.values():
            state = json.loads((root / ".agentic-tools.json").read_text())
            state["files"]["tools/agentplay/agentplay.py"] = install.file_hash(old_file)
            text = (root / "Docs/rules/GIT.md").read_text()
            state["blocks"]["Docs/rules/GIT.md#git"] = install.sha(install.find_block(text, "html", "git")[2].encode())
            state["version"] = "0.9.0"
            (root / ".agentic-tools.json").write_text(json.dumps(state, indent=2) + "\n")
        out = s.run()
        self.assertIn("UPDATE   tools/agentplay/ (1 file)", out)
        self.assertIn("UPDATE   Docs/rules/GIT.md#git", out)
        data = (s.root / "tools/agentplay/agentplay.py").read_bytes()
        self.assertIn(b"\r\n", data, "an updated file keeps its CRLF endings")
        self.assertEqual(install.normalized(data), (HERE / "files/tools/agentplay/agentplay.py").read_bytes())
        self.assertEqual(s.state()["version"], json.loads((PACKAGE / "package.json").read_text())["version"])
        self.assertIn("0 change(s)", s.run())

    def test_modules_and_variables(self):
        s = self.scenario()
        s.run("--enable", "taskboard", "--disable", "skills")
        self.assertTrue(s.exists("tools/taskboard/taskboard.py"))
        self.assertFalse(s.exists(".claude/skills"))
        agents = s.read("AGENTS.md")
        self.assertLess(agents.index("begin testing"), agents.index("begin taskboard"))
        s.run("--set", "RULES_DIR=docs/agents", "--set", "DOCS_DIR=docs")
        self.assertTrue(s.exists("docs/agents/GIT.md"))
        self.assertFalse(s.exists("Docs/rules/GIT.md"), "rule docs move with the variable")
        self.assertIn("[AGENTS.md](../../AGENTS.md)", s.read("docs/agents/GIT.md"))
        self.assertIn("](docs/agents/TESTING.md)", s.read("AGENTS.md"))
        self.assertIn("(../AGENT_PLAY.md)", s.read("docs/agents/TESTING.md"))
        s.run("--disable", "taskboard")
        self.assertFalse(s.exists("tools/taskboard"))
        self.assertNotIn("begin taskboard", s.read("AGENTS.md"))
        s.run("--uninstall")
        self.assertEqual(tree(s.root), {}, "a project with nothing of its own is empty again")

    def test_planner_skill_shares_the_skill_folders(self):
        s = self.scenario()
        s.run()
        self.assertFalse(s.exists(".claude/skills/do-task"), "the planner module is opt-in")
        s.run("--enable", "planner")
        for root in (".claude/skills", ".agents/skills", ".kilo/skills", ".agent/skills"):
            self.assertTrue(s.exists(root + "/do-task/SKILL.md"))
            self.assertTrue(s.exists(root + "/unity-gameplay-workflow/SKILL.md"))
        s.run("--disable", "planner")
        self.assertFalse(s.exists(".claude/skills/do-task"))
        self.assertTrue(s.exists(".claude/skills/unity-gameplay-workflow/SKILL.md"), "removing one module keeps the other's skills")

    def test_block_found_only_on_its_own_line(self):
        text = "x <!-- agentic-tools:begin core --> y\n<!-- agentic-tools:begin core -->\nbody\n<!-- agentic-tools:end core -->\nafter\n"
        start, end, body = install.find_block(text, "html", "core")
        self.assertEqual(body, "body\n")
        self.assertEqual(text[end:], "after\n")
        self.assertIsNone(install.find_block("nothing", "html", "core"))


if __name__ == "__main__":
    unittest.main(verbosity=2)
