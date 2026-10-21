"""Portable Asset Sources automation. Standard-library only; no Unity/MCP session required."""
import argparse
import json
import os
from pathlib import Path, PurePosixPath
import re
import sys
import tempfile

SCHEMA = 1
APPLICATIONS = {".blend": "blender", ".gmat": "graphmat", ".spp": "painter",
                ".sbs": "designer", ".psd": "photoshop", ".psb": "photoshop",
                ".afphoto": "affinity", ".afdesign": "affinity"}


def identifier(value):
    if not isinstance(value, str) or not re.fullmatch(r"[a-zA-Z0-9][a-zA-Z0-9._-]*", value):
        raise ValueError("IDs use letters, numbers, dots, underscores and hyphens.")
    return value


def relative_path(value):
    value = value.replace("\\", "/")
    if (not value.strip() or value.startswith("/") or ":" in value or "\0" in value
            or any(p in ("", ".", "..") for p in value.split("/"))):
        raise ValueError("Expected a library-relative path without traversal or empty segments.")
    return value


def read_json(path, default):
    if not path.exists():
        return default
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(data, dict) or data.get("schemaVersion") != SCHEMA:
        raise ValueError(f"Unsupported or missing schemaVersion in {path}")
    return data


def write_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=path.parent,
                                         prefix=path.name, suffix=".tmp", delete=False) as stream:
            temporary = Path(stream.name)
            json.dump(data, stream, indent=2, ensure_ascii=False)
            stream.write("\n")
        os.replace(temporary, path)
    finally:
        if temporary and temporary.exists():
            temporary.unlink()


class Registry:
    def __init__(self, project):
        self.project = Path(project).resolve()
        if not (self.project / "Assets").is_dir() or not (self.project / "ProjectSettings").is_dir():
            raise ValueError(f"Not a Unity project: {self.project}")
        self.settings = self.project / "ProjectSettings/AssetSources"
        self.local_path = self.project / "UserSettings/AssetSources.local.json"

    def libraries(self):
        data = read_json(self.settings / "Libraries.json", {"schemaVersion": SCHEMA, "libraries": []})
        ids = []
        for item in data["libraries"]:
            ids.append(identifier(item["id"]))
        if len(ids) != len(set(ids)):
            raise ValueError("Duplicate library IDs.")
        return data

    def local(self):
        return read_json(self.local_path, {"schemaVersion": SCHEMA, "libraries": [], "applications": []})

    def library(self, library_id):
        identifier(library_id)
        for library in self.libraries()["libraries"]:
            if library["id"] == library_id:
                return library
        raise ValueError(f"Define library '{library_id}' first.")

    def define_library(self, library_id, name=None, environment=None):
        identifier(library_id)
        data = self.libraries()
        existing = next((x for x in data["libraries"] if x["id"] == library_id), {})
        data["libraries"] = [x for x in data["libraries"] if x["id"] != library_id]
        data["libraries"].append({"id": library_id, "displayName": name or existing.get("displayName", library_id),
                                   "environmentVariable": environment if environment is not None else existing.get("environmentVariable", "")})
        data["libraries"].sort(key=lambda x: x["id"])
        write_json(self.settings / "Libraries.json", data)
        return data

    def set_local(self, kind, item_id, path):
        identifier(item_id)
        if kind == "libraries":
            self.library(item_id)
        data = self.local()
        data[kind] = [x for x in data[kind] if x["id"] != item_id]
        if path:
            full = Path(path).expanduser().resolve()
            if kind == "libraries":
                if not full.is_dir():
                    raise ValueError(f"Library directory missing: {full}")
                self.external(full)
            elif not full.is_file():
                raise ValueError(f"Application executable missing: {full}")
            data[kind].append({"id": item_id, "path": full.as_posix()})
        data[kind].sort(key=lambda x: x["id"])
        write_json(self.local_path, data)
        return data

    def external(self, path):
        if path == self.project or self.project in path.parents:
            raise ValueError("Source libraries must be outside the Unity project.")

    def root(self, library_id):
        definition = self.library(library_id)
        environment = definition.get("environmentVariable")
        root = os.environ.get(environment) if environment else None
        if not root:
            root = next((x["path"] for x in self.local()["libraries"] if x["id"] == library_id), None)
        if not root:
            raise ValueError(f"Locate library '{library_id}' on this PC first.")
        full = Path(root).resolve()
        self.external(full)
        return full

    def guid(self, asset):
        if re.fullmatch(r"[a-fA-F0-9]{32}", asset):
            return asset.lower()
        path = (self.project / asset).resolve()
        if self.project not in path.parents or not path.is_file():
            raise ValueError(f"Unity asset does not exist in this project: {asset}")
        meta = Path(str(path) + ".meta")
        if not meta.is_file():
            raise ValueError(f"Missing Unity .meta; let Unity import the asset first: {asset}")
        match = re.search(r"^guid:\s*([a-fA-F0-9]{32})\s*$", meta.read_text(encoding="utf-8-sig"), re.MULTILINE)
        if not match:
            raise ValueError(f"No Unity GUID in {meta}")
        return match.group(1).lower()

    def record(self, asset):
        guid = self.guid(asset)
        data = read_json(self.settings / "Links" / (guid + ".json"),
                         {"schemaVersion": SCHEMA, "unityGuid": guid, "sources": []})
        if data["unityGuid"].lower() != guid:
            raise ValueError("Record GUID does not match filename.")
        ids = []
        for source in data["sources"]:
            ids.append(identifier(source["id"]))
            identifier(source["libraryId"])
            identifier(source["applicationId"])
            relative_path(source["relativePath"])
        if len(ids) != len(set(ids)):
            raise ValueError("Duplicate source IDs.")
        return data

    def resolve(self, source):
        root = self.root(source["libraryId"])
        full = (root / relative_path(source["relativePath"])).resolve()
        if root not in full.parents:
            raise ValueError("Source escapes its library.")
        if not full.is_file():
            raise ValueError(f"Missing source: {full}")
        return full

    def link(self, asset, library_id, source, application=None, source_id="model", label=None):
        root = self.root(library_id)
        if Path(source).is_absolute():
            source = Path(source).resolve().relative_to(root).as_posix()
        source = relative_path(source)
        entry = {"id": identifier(source_id), "libraryId": identifier(library_id), "relativePath": source,
                 "applicationId": identifier(application or APPLICATIONS.get(PurePosixPath(source).suffix.lower(), "default")),
                 "label": label or ""}
        self.resolve(entry)
        data = self.record(asset)
        data["sources"] = sorted([x for x in data["sources"] if x["id"] != source_id] + [entry], key=lambda x: x["id"])
        write_json(self.settings / "Links" / (data["unityGuid"] + ".json"), data)
        return data

    def unlink(self, asset, source_id):
        data = self.record(asset)
        data["sources"] = [x for x in data["sources"] if x["id"] != source_id]
        write_json(self.settings / "Links" / (data["unityGuid"] + ".json"), data)
        return data

    def inspect(self, asset):
        data = self.record(asset)
        results = []
        for source in data["sources"]:
            try:
                results.append(dict(source, resolvedPath=str(self.resolve(source)), exists=True))
            except (ValueError, OSError, KeyError) as ex:
                results.append(dict(source, exists=False, error=str(ex)))
        return dict(data, sources=results)

    def validate(self):
        self.libraries()
        self.local()
        errors, count = [], 0
        for file in sorted((self.settings / "Links").glob("*.json")):
            try:
                data = self.record(file.stem)
                count += 1
                for source in data["sources"]:
                    try:
                        self.resolve(source)
                    except (ValueError, OSError, KeyError) as ex:
                        errors.append(f"{file.stem}/{source['id']}: {ex}")
            except (ValueError, OSError, KeyError, TypeError) as ex:
                errors.append(f"{file.name}: {ex}")
        return {"records": count, "errors": errors}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", default=".", help="Unity project root; defaults to current directory")
    commands = parser.add_subparsers(dest="command", required=True)
    library = commands.add_parser("library", help="Define a portable library; optionally locate it on this PC")
    library.add_argument("--id", required=True)
    library.add_argument("--name")
    library.add_argument("--env")
    library.add_argument("--root")
    root = commands.add_parser("root", help="Set a library root on this PC")
    root.add_argument("--library", required=True)
    root.add_argument("--path", required=True)
    app = commands.add_parser("application", help="Set an executable override; omit executable to clear it")
    app.add_argument("--id", required=True)
    app.add_argument("--executable")
    link = commands.add_parser("link", help="Add or replace one source ID; other sources are retained")
    link.add_argument("--asset", required=True, help="Unity asset path or 32-character GUID")
    link.add_argument("--library", required=True)
    link.add_argument("--source", required=True, help="Library-relative or absolute source filename")
    link.add_argument("--app")
    link.add_argument("--id", default="model")
    link.add_argument("--label")
    unlink = commands.add_parser("unlink", help="Remove a link; never deletes the art file")
    unlink.add_argument("--asset", required=True)
    unlink.add_argument("--id", required=True)
    inspect = commands.add_parser("inspect", help="Get links, resolved paths and missing-source diagnostics")
    inspect.add_argument("--asset", required=True)
    commands.add_parser("validate", help="Validate source paths; Unity API validation also checks asset GUID existence")
    args = parser.parse_args(argv)
    try:
        registry = Registry(args.project)
        if args.command == "library":
            result = registry.define_library(args.id, args.name, args.env)
            if args.root:
                registry.set_local("libraries", args.id, args.root)
        elif args.command == "root": result = registry.set_local("libraries", args.library, args.path)
        elif args.command == "application": result = registry.set_local("applications", args.id, args.executable)
        elif args.command == "link": result = registry.link(args.asset, args.library, args.source, args.app, args.id, args.label)
        elif args.command == "unlink": result = registry.unlink(args.asset, args.id)
        elif args.command == "inspect": result = registry.inspect(args.asset)
        else: result = registry.validate()
        ok = not result.get("errors")
        print(json.dumps({"ok": ok, "data": result}, indent=2))
        return 0 if ok else 1
    except (ValueError, OSError, KeyError, TypeError) as ex:
        print(json.dumps({"ok": False, "error": str(ex)}, indent=2))
        return 1


if __name__ == "__main__":
    sys.exit(main())
