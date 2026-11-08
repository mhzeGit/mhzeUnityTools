"""Cross-process and front-end tests: concurrent claims, CLI, HTTP API + live stream,
and the virtual-commit plugin against a real git repository."""

import http.client
import io
import json
import shutil
import subprocess
import sys
import tempfile
import threading
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

from helpers import CLAUDE, ROOT, USER, BoardTestCase, deadline

from taskboard import cli
from taskboard.board import Board
from taskboard.integrations import virtual_commits
from taskboard.model import Link
from taskboard.server import make_server

SCRIPT = str(ROOT / "taskboard.py")


class ConcurrencyTests(unittest.TestCase):
    @deadline(120)
    def test_parallel_agents_never_claim_the_same_task(self):
        home = Path(tempfile.mkdtemp(prefix="taskboard-race-"))
        try:
            board = Board.open(home, plugins=False)
            for i in range(5):
                board.create({"title": f"task {i}"}, USER)
            agents = 10
            procs = [
                subprocess.Popen(
                    [sys.executable, SCRIPT, "--home", str(home), "next", "--any-project",
                     "--agent", f"agent-{i}", "--json"],
                    stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
                )
                for i in range(agents)
            ]
            claimed, empty = [], 0
            for proc in procs:
                out, err = proc.communicate(timeout=60)
                if proc.returncode == 0:
                    claimed.append(json.loads(out)["id"])
                else:
                    self.assertEqual(proc.returncode, 2, err)
                    empty += 1
            self.assertEqual(sorted(claimed), [1, 2, 3, 4, 5])
            self.assertEqual(empty, agents - 5)
            owners = {t.id: t.assignee for t in Board.open(home, plugins=False).tasks()}
            self.assertEqual(len(set(owners.values())), 5)
        finally:
            shutil.rmtree(home, ignore_errors=True)


def run_cli(*args):
    out, err = io.StringIO(), io.StringIO()
    with redirect_stdout(out), redirect_stderr(err):
        code = cli.main(list(args))
    return code, out.getvalue(), err.getvalue()


class CliTests(BoardTestCase, unittest.TestCase):
    def cli(self, *args):
        return run_cli("--home", str(self.home), "--project", "Game", *args)

    def test_agent_session_through_the_cli(self):
        self.assertEqual(self.cli("add", "Fix hinge", "--touches", "Assets/Doors", "-p", "high")[0], 0)
        code, out, _ = self.cli("add", "An idea", "--agent", "Claude Code")
        self.assertIn("Inbox", out)
        code, out, _ = self.cli("next", "--agent", "Claude Code", "--json")
        self.assertEqual((code, json.loads(out)["key"]), (0, "T-1"))
        self.assertEqual(self.cli("next", "--agent", "Kilo")[0], 2)
        code, out, err = self.cli("review", "T-1", "--agent", "Kilo", "-s", "x")
        self.assertEqual(code, 1)
        self.assertIn("being worked on by Claude Code", err)
        code, out, _ = self.cli("review", "1", "--agent", "Claude Code", "--commit", "37", "--commit",
                                "9f2c1e4ab", "-s", "Hinge fixed", "--json")
        view = json.loads(out)
        self.assertEqual(view["status"], "review")
        self.assertEqual([l["kind"] for l in view["links"]], ["virtual_commit", "commit"])
        code, out, _ = self.cli("list", "--flat")
        self.assertIn("T-1", out)

    def test_errors_are_reported_not_raised(self):
        code, _, err = self.cli("show", "T-404")
        self.assertEqual(code, 1)
        self.assertIn("No task T-404", err)
        code, out, _ = self.cli("show", "nope", "--json")
        self.assertEqual(json.loads(out)["type"], "ValidationError")

    def test_guide_needs_no_board(self):
        code, out, _ = run_cli("guide")
        self.assertEqual(code, 0)
        self.assertIn("taskboard next", out)


class ServerTests(BoardTestCase, unittest.TestCase):
    def setUp(self):
        super().setUp()
        self.server = make_server(self.board, 0)
        self.server.hub.poll_seconds = 0.05
        self.server.start_background()

    def tearDown(self):
        self.server.close()
        super().tearDown()

    def request(self, method, path, body=None, headers=None):
        conn = http.client.HTTPConnection("127.0.0.1", self.server.port, timeout=10)
        hdrs = {"Host": f"127.0.0.1:{self.server.port}"}
        if body is not None:
            hdrs["Content-Type"] = "application/json"
        hdrs.update(headers or {})
        conn.request(method, path, body=json.dumps(body) if body is not None else None, headers=hdrs)
        response = conn.getresponse()
        data = response.read()
        conn.close()
        return response.status, json.loads(data) if data and data[:1] in b"{[" else data

    @deadline(60)
    def test_api_round_trip(self):
        status, created = self.request("POST", "/api/tasks", {"title": "From the web", "touches": "a/b\nc"})
        self.assertEqual((status, created["status"], created["touches"]), (201, "ready", ["a/b", "c"]))
        status, board = self.request("GET", "/api/board")
        self.assertEqual([t["title"] for t in board["tasks"]], ["From the web"])
        self.assertEqual(board["statuses"][0]["key"], "proposed")
        status, moved = self.request("POST", "/api/tasks/1/move", {"status": "review"})
        self.assertEqual(moved["status"], "review")  # people may move anywhere
        status, noted = self.request("POST", "/api/tasks/1/notes", {"text": "looks good"})
        self.assertEqual(noted["activity"][-1]["text"], "looks good")
        status, error = self.request("PATCH", "/api/tasks/1", {"priority": "someday"})
        self.assertEqual(status, 400)
        self.assertIn("Priority", error["error"])
        status, _ = self.request("GET", "/api/tasks/99")
        self.assertEqual(status, 404)
        status, page = self.request("GET", "/")
        self.assertEqual(status, 200)
        self.assertIn(b"<title>", page)

    @deadline(60)
    def test_rejects_foreign_hosts_origins_and_form_posts(self):
        self.assertEqual(self.request("GET", "/api/board", headers={"Host": "evil.example"})[0], 403)
        self.assertEqual(self.request("POST", "/api/tasks", {"title": "x"},
                                      headers={"Origin": "http://evil.example"})[0], 403)
        self.assertEqual(self.request("POST", "/api/tasks", None,
                                      headers={"Content-Type": "text/plain"})[0], 403)
        self.assertEqual(self.request("GET", "/../../config.json")[0], 404)

    @deadline(60)
    def test_stream_delivers_changes_from_other_writers(self):
        conn = http.client.HTTPConnection("127.0.0.1", self.server.port, timeout=10)
        conn.request("GET", "/api/stream", headers={"Host": f"127.0.0.1:{self.server.port}"})
        response = conn.getresponse()
        self.assertEqual(response.getheader("Content-Type"), "text/event-stream")
        self.assertEqual(response.readline(), b"retry: 2000\n")
        # A separate Board instance stands in for an agent in another process.
        other = Board.open(self.home, plugins=False)
        holder = {}

        def read_event():
            for raw in iter(response.readline, b""):
                if raw.startswith(b"data: "):
                    holder["event"] = json.loads(raw[6:])
                    return

        reader = threading.Thread(target=read_event, daemon=True)
        reader.start()
        other.create({"title": "agent made this"}, CLAUDE)
        reader.join(timeout=10)
        conn.close()
        self.assertEqual(holder.get("event", {}).get("type"), "task.created")


class VirtualCommitPluginTests(BoardTestCase, unittest.TestCase):
    def setUp(self):
        super().setUp()
        self.repo = Path(tempfile.mkdtemp(prefix="taskboard-repo-"))
        subprocess.run(["git", "init", "-q", str(self.repo)], check=True, timeout=30)
        self.records = self.repo / ".git" / "agent-commits" / "records"
        self.records.mkdir(parents=True)
        self.board.hooks.on("board.sync", virtual_commits.sync)

    def tearDown(self):
        shutil.rmtree(self.repo, ignore_errors=True)
        super().tearDown()

    def record(self, number, status, commit=""):
        (self.records / f"{number:04d}.json").write_text(
            json.dumps({"id": number, "status": status, "commit": commit}), encoding="utf-8")

    def in_review(self, record_id):
        task = self.make("x")
        self.board.claim(task.id, CLAUDE)
        return self.board.submit_review(task.id, CLAUDE, "agent summary",
                                        [Link("virtual_commit", str(record_id), repo=str(self.repo))])

    @deadline(60)
    def test_applied_record_moves_task_to_done(self):
        task = self.in_review(7)
        self.record(7, "pending")
        self.board.sync()
        self.assertEqual(self.board.get(task.id).status, "review")
        self.record(7, "applied", "abcdef1234567")
        self.board.sync()
        done = self.board.get(task.id)
        self.assertEqual(done.status, "done")
        self.assertEqual(done.result, "agent summary")
        self.assertIn(("commit", "abcdef1234567"), [(l.kind, l.value) for l in done.links])

    @deadline(60)
    def test_discarded_record_blocks_task(self):
        task = self.in_review(8)
        self.record(8, "discarded")
        self.board.sync()
        blocked = self.board.get(task.id)
        self.assertEqual(blocked.status, "blocked")
        self.assertIn("#8 was discarded", blocked.blocked_reason)


if __name__ == "__main__":
    unittest.main()
