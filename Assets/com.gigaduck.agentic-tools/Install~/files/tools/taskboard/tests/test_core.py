"""Pure rules: model round-trip, workflow, path overlap, scheduler ordering."""

import unittest

import helpers  # puts the package on sys.path

from taskboard import paths, workflow
from taskboard.errors import ValidationError, WorkflowError
from taskboard.model import Actor, Task, normalize_fields, parse_task_id

del helpers


class ModelTests(unittest.TestCase):
    def test_round_trip_keeps_unknown_fields(self):
        raw = {"id": 3, "title": "A", "status": "ready", "created_at": "", "created_by": "you",
               "updated_at": "", "plugin_field": {"x": 1}}
        task = Task.from_dict(raw)
        self.assertEqual(task.extra, {"plugin_field": {"x": 1}})
        self.assertEqual(task.to_dict()["plugin_field"], {"x": 1})
        self.assertEqual(Task.from_dict(task.to_dict()).to_dict(), task.to_dict())

    def test_parse_task_id_forms(self):
        for text in ("12", "T-12", "t12", "#12", 12):
            self.assertEqual(parse_task_id(text), 12)
        with self.assertRaises(ValidationError):
            parse_task_id("twelve")

    def test_normalize_fields(self):
        clean = normalize_fields({
            "title": "  Fix   door ", "labels": "UI, bug,ui", "touches": "Assets\\Doors\nsrc/x.cs",
            "acceptance": "a, with comma\n\nb", "depends_on": "T-1, 2", "priority": "HIGH",
        })
        self.assertEqual(clean["title"], "Fix door")
        self.assertEqual(clean["labels"], ["ui", "bug"])
        self.assertEqual(clean["touches"], ["Assets/Doors", "src/x.cs"])
        self.assertEqual(clean["acceptance"], ["a, with comma", "b"])
        self.assertEqual(clean["depends_on"], [1, 2])
        self.assertEqual(clean["priority"], "high")
        with self.assertRaises(ValidationError):
            normalize_fields({"status": "done"})
        with self.assertRaises(ValidationError):
            normalize_fields({"priority": "whenever"})


class WorkflowTests(unittest.TestCase):
    def test_agents_follow_transitions_people_may_force(self):
        agent, user = Actor("A", "agent"), Actor("you", "user")
        self.assertFalse(workflow.check_transition("ready", "in_progress", agent))
        with self.assertRaises(WorkflowError):
            workflow.check_transition("proposed", "done", agent)
        self.assertTrue(workflow.check_transition("proposed", "done", user))
        with self.assertRaises(WorkflowError):
            workflow.check_transition("ready", "ready", user)

    def test_every_status_has_transitions_and_aliases_resolve(self):
        self.assertEqual(set(workflow.TRANSITIONS), set(workflow.STATUS_KEYS))
        self.assertEqual(workflow.require_status("Queue"), "ready")
        self.assertEqual(workflow.require_status("in-progress"), "in_progress")


class PathTests(unittest.TestCase):
    def test_overlap_cases(self):
        yes = [
            ("Assets/Doors", "assets/doors/Hinge.cs"),
            ("Assets\\Doors\\", "Assets/Doors"),
            ("src/Nodes/Blur*.cs", "src/Nodes/BlurNode.cs"),
            ("src/Nodes/*.cs", "src"),
            ("src/**/x.cs", "src/a/b/x.cs"),
            ("src/a/*.cs", "src/a/*.json"),
        ]
        no = [
            ("Assets/Doors", "Assets/DoorsOld"),
            ("src/Nodes/Blur*.cs", "src/Nodes/Sharpen.cs"),
            ("src/a/*.cs", "docs/*.md"),
            ("", "src"),
        ]
        for a, b in yes:
            self.assertTrue(paths.overlap(a, b), (a, b))
            self.assertTrue(paths.overlap(b, a), (b, a))
        for a, b in no:
            self.assertFalse(paths.overlap(a, b), (a, b))


if __name__ == "__main__":
    unittest.main()
