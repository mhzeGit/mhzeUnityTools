"""The board service: lifecycle, scheduling, ownership, leases, events, archive."""

import json
import unittest

from helpers import CLAUDE, KILO, USER, BoardTestCase

from taskboard.errors import (
    NotClaimableError, NotFoundError, OwnershipError, ValidationError, WorkflowError,
)
from taskboard.model import Link


class LifecycleTests(BoardTestCase, unittest.TestCase):
    def test_full_agent_cycle(self):
        task = self.make("Fix hinge", acceptance="Door opens")
        claimed, _ = self.board.claim_next(CLAUDE, project=None)
        self.assertEqual(claimed.id, task.id)
        self.assertEqual(claimed.status, "in_progress")
        self.assertEqual(claimed.assignee, "Claude Code")
        self.board.note(task.id, CLAUDE, "halfway")
        done = self.board.submit_review(task.id, CLAUDE, "Fixed", [Link("virtual_commit", "37", repo="/r")])
        self.assertEqual(done.status, "review")
        self.assertEqual(done.result, "Fixed")
        self.assertIsNone(done.claim)
        final = self.board.move(task.id, "done", USER)
        self.assertEqual(final.status, "done")
        kinds = [a.kind for a in final.activity]
        self.assertEqual(kinds, ["created", "status", "note", "status", "status"])

    def test_agent_tasks_start_in_inbox_and_user_tasks_in_queue(self):
        self.assertEqual(self.board.create({"title": "idea"}, CLAUDE).status, "proposed")
        self.assertEqual(self.board.create({"title": "work"}, USER).status, "ready")
        with self.assertRaises(WorkflowError):
            self.board.create({"title": "x"}, USER, status="done")

    def test_ids_are_sequential_and_never_reused(self):
        a, b = self.make("a"), self.make("b")
        self.assertEqual((a.id, b.id), (1, 2))
        self.board.cancel(b.id, USER)
        self.clock.advance(days=30)
        self.board.archive_finished(days=1)
        self.assertEqual(self.make("c").id, 3)

    def test_unknown_task(self):
        with self.assertRaises(NotFoundError):
            self.board.get(99)


class SchedulingTests(BoardTestCase, unittest.TestCase):
    def test_priority_then_rank_order(self):
        low = self.make("low", priority="low")
        normal = self.make("normal")
        urgent = self.make("urgent", priority="urgent")
        order = []
        for agent in range(3):
            task, _ = self.board.claim_next(CLAUDE)
            order.append(task.id)
        self.assertEqual(order, [urgent.id, normal.id, low.id])
        self.assertEqual(self.board.claim_next(CLAUDE)[0], None)

    def test_dependencies_wait_until_done(self):
        first = self.make("first")
        second = self.make("second", depends_on=[first.id], priority="urgent")
        task, skipped = self.board.claim_next(CLAUDE)
        self.assertEqual(task.id, first.id)
        self.assertIn(second.id, skipped)
        self.assertEqual(self.board.claim_next(KILO)[0], None)
        self.board.complete(first.id, CLAUDE, "ok")
        self.assertEqual(self.board.claim_next(KILO)[0].id, second.id)

    def test_dependency_cycles_and_unknown_ids_are_rejected(self):
        a = self.make("a")
        b = self.make("b", depends_on=[a.id])
        with self.assertRaises(ValidationError):
            self.board.update(a.id, {"depends_on": [b.id]}, USER)
        with self.assertRaises(ValidationError):
            self.make("c", depends_on=[42])

    def test_touches_conflict_within_project_only(self):
        self.make("doors", project="Game", touches="Assets/Doors")
        hinge = self.make("hinge", project="Game", touches="Assets/Doors/Hinge.cs")
        other = self.make("tool", project="Tool", touches="Assets/Doors")
        self.board.claim_next(CLAUDE, project="Game")
        with self.assertRaises(NotClaimableError):
            self.board.claim(hinge.id, KILO)
        self.assertEqual(self.board.claim_next(KILO, project="Tool")[0].id, other.id)
        # Review still holds the files: the edits are uncommitted in the shared tree.
        self.board.submit_review(1, CLAUDE, "done")
        with self.assertRaises(NotClaimableError):
            self.board.claim(hinge.id, KILO)
        self.board.move(1, "done", USER)
        self.assertEqual(self.board.claim(hinge.id, KILO).status, "in_progress")

    def test_exclusive_resources(self):
        self.make("a", needs="unity-editor")
        b = self.make("b", needs="unity-editor")
        c = self.make("c")
        self.board.claim_next(CLAUDE)
        task, skipped = self.board.claim_next(KILO)
        self.assertEqual(task.id, c.id)
        self.assertIn("unity-editor is in use", skipped[b.id][0])

    def test_project_filter_includes_tasks_without_project(self):
        self.make("graphmat", project="Graphmat")
        anywhere = self.make("anywhere", project="")
        self.make("game", project="Game", priority="urgent")
        self.assertEqual(self.board.claim_next(CLAUDE, project="Graphmat")[0].title, "graphmat")
        self.assertEqual(self.board.claim_next(CLAUDE, project="Graphmat")[0].id, anywhere.id)
        self.assertIsNone(self.board.claim_next(CLAUDE, project="Graphmat")[0])

    def test_place_reorders_and_adopts_neighbor_priority(self):
        a, b, c = self.make("a"), self.make("b"), self.make("c", priority="low")
        self.board.place(c.id, USER, above=a.id, below=b.id)
        moved = self.board.get(c.id)
        self.assertEqual(moved.priority, "normal")
        self.assertTrue(a.rank < moved.rank < b.rank)
        self.assertEqual([self.board.claim_next(CLAUDE)[0].id for _ in range(3)], [a.id, c.id, b.id])


class OwnershipAndLeaseTests(BoardTestCase, unittest.TestCase):
    def test_agents_cannot_touch_each_others_work(self):
        task = self.make("x")
        self.board.claim(task.id, CLAUDE)
        for action in (
            lambda: self.board.submit_review(task.id, KILO, "mine"),
            lambda: self.board.block(task.id, KILO, "?"),
            lambda: self.board.release(task.id, KILO),
            lambda: self.board.update(task.id, {"title": "hijack"}, KILO),
        ):
            with self.assertRaises(OwnershipError):
                action()
        self.board.note(task.id, KILO, "FYI: related to T-2")  # notes are open to all
        self.board.release(task.id, USER, "people can always intervene")

    def test_agents_follow_the_workflow(self):
        task = self.board.create({"title": "idea"}, CLAUDE)
        with self.assertRaises(NotClaimableError):
            self.board.claim(task.id, CLAUDE)
        with self.assertRaises(WorkflowError):
            self.board.complete(task.id, CLAUDE)

    def test_require_review_setting(self):
        self.board.config.values["require_review"] = True
        task = self.make("x")
        self.board.claim(task.id, CLAUDE)
        with self.assertRaises(WorkflowError):
            self.board.complete(task.id, CLAUDE)
        self.assertEqual(self.board.submit_review(task.id, CLAUDE).status, "review")

    def test_stale_then_expired_lease_returns_task_to_queue(self):
        task = self.make("x")
        self.board.claim(task.id, CLAUDE)
        self.clock.advance(minutes=25)
        self.assertTrue(self.board.view(self.board.get(task.id))["lease"]["stale"])
        self.board.note(task.id, CLAUDE, "still here")
        self.assertFalse(self.board.view(self.board.get(task.id))["lease"]["stale"])
        self.clock.advance(hours=2, minutes=1)
        released = self.board.sync()
        self.assertEqual([t.id for t in released], [task.id])
        back = self.board.get(task.id)
        self.assertEqual((back.status, back.assignee, back.claim), ("ready", "", None))
        self.assertIn("lease expired", back.activity[-1].text)
        with self.assertRaises(WorkflowError):
            self.board.submit_review(task.id, CLAUDE)  # no longer in progress

    def test_block_needs_reason_and_unblock_returns_to_queue(self):
        task = self.make("x")
        self.board.claim(task.id, CLAUDE)
        with self.assertRaises(ValidationError):
            self.board.block(task.id, CLAUDE, " ")
        blocked = self.board.block(task.id, CLAUDE, "Which track?")
        self.assertEqual(blocked.blocked_reason, "Which track?")
        back = self.board.unblock(task.id, USER, "Use eerie_02")
        self.assertEqual((back.status, back.blocked_reason), ("ready", ""))


class EventAndArchiveTests(BoardTestCase, unittest.TestCase):
    def test_every_change_appends_an_event(self):
        task = self.make("x")
        self.board.claim(task.id, CLAUDE)
        self.board.heartbeat(task.id, CLAUDE)
        events = self.board.store.read_events()
        self.assertEqual([e["type"] for e in events], ["task.created", "task.moved", "task.heartbeat"])
        self.assertEqual([e["seq"] for e in events], [1, 2, 3])
        self.assertTrue(events[2]["data"]["quiet"])
        self.assertEqual(self.board.store.last_event_seq(), 3)

    def test_archive_and_reopen(self):
        task = self.make("x")
        self.board.complete(task.id, USER, "ok")
        self.clock.advance(days=15)
        self.assertEqual([t.id for t in self.board.archive_finished()], [task.id])
        self.assertEqual(self.board.tasks(), [])
        with self.assertRaises(NotFoundError):
            self.board.note(task.id, USER, "hi")
        self.assertEqual(self.board.get(task.id).status, "done")  # still readable
        reopened = self.board.reopen(task.id, USER)
        self.assertEqual(reopened.status, "ready")
        self.assertEqual(len(self.board.tasks()), 1)

    def test_hooks_run_after_changes(self):
        seen = []
        self.board.hooks.on("task.moved", lambda board, task, actor, from_status, to_status:
                            seen.append((task.id, from_status, to_status)))
        self.board.hooks.on("task.created", lambda board, task, actor: 1 / 0)  # must not break
        task = self.make("x")
        self.board.claim(task.id, CLAUDE)
        self.assertEqual(seen, [(task.id, "ready", "in_progress")])

    def test_task_files_are_plain_json(self):
        task = self.make("x", labels="a")
        data = json.loads((self.home / "tasks" / "00001.json").read_text(encoding="utf-8"))
        self.assertEqual((data["id"], data["title"], data["labels"]), (task.id, "x", ["a"]))


if __name__ == "__main__":
    unittest.main()
