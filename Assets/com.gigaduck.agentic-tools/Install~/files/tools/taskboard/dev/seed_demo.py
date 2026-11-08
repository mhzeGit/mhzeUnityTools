"""Fill a throwaway board with realistic demo tasks (for screenshots and UI work).

    python tools/taskboard/dev/seed_demo.py --home %TEMP%\\taskboard-demo
    python tools/taskboard/taskboard.py --home %TEMP%\\taskboard-demo serve --open

Refuses to touch a board folder that already has tasks, so it can never
pollute your real board.
"""

from __future__ import annotations

import argparse
import sys
from datetime import timedelta
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from taskboard.board import Board  # noqa: E402
from taskboard.clock import utc_now  # noqa: E402
from taskboard.model import ACTOR_AGENT, ACTOR_USER, Actor, Link  # noqa: E402


class MovableClock:
    def __init__(self, start):
        self.now = start

    def __call__(self):
        return self.now

    def advance(self, minutes: float) -> None:
        self.now += timedelta(minutes=minutes)


def seed(home: Path) -> None:
    if (home / "tasks").exists() and any((home / "tasks").glob("*.json")):
        raise SystemExit(f"{home} already has tasks; use an empty folder for the demo")
    clock = MovableClock(utc_now() - timedelta(hours=30))
    board = Board.open(home, plugins=False, now=clock)
    you = Actor("you", ACTOR_USER)
    claude, kilo, gemini = (Actor(n, ACTOR_AGENT) for n in ("Claude Code", "Kilo Code", "Antigravity"))
    G, B = "Graphmat", "BlahBlahFamilyV6"

    def add(title, project, actor=you, **fields):
        clock.advance(2)
        return board.create({"title": title, "project": project, **fields}, actor)

    done1 = add("Kitchen fridge door opens the wrong way", B, priority="high",
                touches=["Assets/_BlahBlahFamilyV6/Content/Environments/Kitchen"], labels=["bug"])
    board.claim(done1.id, claude); clock.advance(40)
    board.note(done1.id, claude, "Hinge pivot was on the left; mirrored the prefab variant.")
    board.submit_review(done1.id, claude, "Swapped the hinge side and re-baked the door collider.",
                        [Link("virtual_commit", "41")])
    clock.advance(90)
    board.complete(done1.id, you, "Tested in Chapter 3, works.", [Link("commit", "9f2c1e4ab")])

    done2 = add("Add Ctrl+D duplicate to the node graph", G, labels=["ui"],
                touches=["src/GraphMat.Avalonia/Controls/GraphCanvas*.cs"])
    board.claim(done2.id, kilo); clock.advance(25)
    board.complete(done2.id, kilo, "Duplicate keeps wires between the copied nodes.")

    review = add("Girlfriend slap animation clips into the camera", B, priority="high",
                 description="During the slap the hand passes through the near plane. Pull the camera "
                             "back 8 cm for the duration of the clip.",
                 acceptance=["No clipping at 60 and 90 FOV", "Rewind restores the camera offset"],
                 touches=["Assets/_BlahBlahFamilyV6/Scripts/Girlfriend"], labels=["animation"])
    board.claim(review.id, claude); clock.advance(30)
    board.note(review.id, claude, "Added a curve profile for the camera push, per PROCEDURAL_ANIMATION.md.")
    board.submit_review(review.id, claude, "Camera pushes back along an editable curve during the slap; "
                        "rewind resets it.", [Link("virtual_commit", "44")])

    blocked = add("Main menu: choose the music track", B,
                  description="Three candidate tracks are in Content/UI/Audio.", labels=["audio"])
    board.claim(blocked.id, gemini); clock.advance(12)
    board.block(blocked.id, gemini, "Which of the three menu tracks should play? (calm_01, eerie_02, toybox_03)")

    clock.now = utc_now() - timedelta(minutes=50)
    running1 = add("Blur node: GPU path for radius above 64", G, priority="urgent",
                   description="Large radii fall back to the CPU and stall the preview.",
                   acceptance=["2K preview under 16 ms at radius 128", "Node audit images unchanged"],
                   touches=["src/GraphMat.Core/Nodes/Filters/Blur*.cs", "src/GraphMat.Avalonia/Rendering/GpuNodeShaders.cs"],
                   needs=["gpu-benchmark"], labels=["perf"])
    board.claim(running1.id, claude)
    clock.advance(31)
    board.note(running1.id, claude, "Separable two-pass kernel works; now checking the audit images.")
    clock.advance(4)

    clock.now = utc_now() - timedelta(minutes=7)
    running2 = add("Pantry shelves: replace TEMP props with the new kit", B,
                   touches=["Assets/_BlahBlahFamilyV6/Content/Modules/Pantry"], needs=["unity-editor"])
    board.claim(running2.id, kilo)

    clock.now = utc_now() - timedelta(minutes=40)

    stale = add("Export window: remember the last preset per project", G,
                touches=["src/GraphMat.Avalonia/Views/ExportWindow*.cs"], labels=["ui"])
    clock.now = utc_now() - timedelta(minutes=34)
    board.claim(stale.id, gemini)
    clock.now = utc_now() - timedelta(minutes=30)

    add("Chapter 5 checkpoint skips the basement beat", B, priority="urgent",
        acceptance=["Continue from checkpoint 5 lands in the basement", "Sweep and rewind clean for chapter 5"],
        labels=["story", "bug"])
    add("Album page for the third accident", B, priority="high", labels=["story"],
        depends_on=[blocked.id])
    add("Pantry: hide the cookie jar behind the cereal", B,
        touches=["Assets/_BlahBlahFamilyV6/Content/Modules/Pantry/Props"], needs=["unity-editor"])
    add("Tile Generator: random rotation per tile", G, labels=["nodes"])
    add("Write the node page for Height To Normal", G, priority="low", labels=["docs"])
    add("Live Link: reconnect Unity after domain reload", G, priority="high",
        description="The link drops after every script compile in Unity.")

    add("Idea: auto-generate item icons when a prop prefab changes", B, claude, labels=["tools"])
    add("Idea: cache the shader permutations between sessions", G, kilo, labels=["perf"])

    cancelled = add("Old patrol waypoints cleanup", B)
    board.cancel(cancelled.id, you, "The patrol waypoints are still used by dialog beats.")
    print(f"Seeded {home}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--home", type=Path, required=True)
    seed(parser.parse_args().home)
