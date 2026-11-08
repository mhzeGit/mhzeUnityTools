#!/usr/bin/env python3
"""Run the task board without installing it: `python tools/taskboard/taskboard.py <command>`.

Works from any folder, so agents in other repositories can call it by path.
`pip install -e tools/taskboard` instead puts a `taskboard` command on PATH.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from taskboard.cli import main  # noqa: E402

if __name__ == "__main__":
    sys.exit(main())
