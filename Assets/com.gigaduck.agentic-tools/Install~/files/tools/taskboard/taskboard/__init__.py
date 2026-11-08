"""Agent Task Board: a local kanban that people and AI agents share.

Layers (each module only depends on the ones above it):

    clock, model, workflow, paths   pure data and rules, no I/O
    config, project                 settings and repository detection
    locking, store                  persistence (one JSON file per task)
    scheduler                       which ready task an agent may take next
    hooks, plugins                  extension points
    board                           the application service (every mutation)
    cli, server                     the two front ends (terminal and web)

See ARCHITECTURE.md next to this package for the full design.
"""

__version__ = "1.0.0"
