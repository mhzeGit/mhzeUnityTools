"""Plugin loading.

A plugin is any importable Python module with a ``register(context)``
function. List it in ``plugins`` in ``<home>/config.json``. The context gives
the plugin the hook bus, the board and the settings::

    def register(context):
        def on_moved(board, task, actor, from_status, to_status):
            if to_status == "done":
                ...  # e.g. post to Discord, play a sound, update a sheet
        context.hooks.on("task.moved", on_moved)

To load a plugin from a folder that is not on the Python path, add that
folder to ``plugin_paths`` in config.json.
"""

from __future__ import annotations

import importlib
import sys
from dataclasses import dataclass
from typing import TYPE_CHECKING, List

from .config import Config
from .hooks import HookBus

if TYPE_CHECKING:  # pragma: no cover
    from .board import Board


@dataclass
class PluginContext:
    board: "Board"
    hooks: HookBus
    config: Config


def load_plugins(context: PluginContext) -> List[str]:
    """Import and register every configured plugin. Returns the loaded names."""
    for folder in context.config.get("plugin_paths", []) or []:
        if folder not in sys.path:
            sys.path.append(folder)
    loaded = []
    for name in context.config.get("plugins", []) or []:
        try:
            module = importlib.import_module(name)
            register = getattr(module, "register")
            register(context)
            loaded.append(name)
        except Exception as exc:  # noqa: BLE001 - a broken plugin must not stop the board
            print(f"taskboard: plugin {name} not loaded: {exc}", file=sys.stderr)
    return loaded
