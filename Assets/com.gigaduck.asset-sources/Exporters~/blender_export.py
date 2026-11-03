"""Export a confirmed Unity mesh set or embedded image from a saved Blender master."""
import json
import os
import sys
import bpy

# Optional per-master export overrides live next to the master as <name>.assetsources.json:
#   { "*": { "fbx": { "apply_unit_scale": false } }, "PlayerArm2.fbx": { "fbx": { ... } } }
# Keys are Unity target file names (case-insensitive) or "*"; "fbx" holds bpy.ops.export_scene.fbx keyword arguments.
OPTIONS_SUFFIX = ".assetsources.json"
_RESERVED_FBX = {"filepath", "use_selection", "check_existing"}
_TRANSFORM_PROPS = ("rotation_mode", "location", "rotation_euler", "rotation_quaternion", "rotation_axis_angle", "scale")


def _snapshot(target):
    values = {}
    for name in _TRANSFORM_PROPS:
        value = getattr(target, name)
        values[name] = value if isinstance(value, str) else tuple(value)
    return values


def _apply(target, values):
    for name in _TRANSFORM_PROPS:
        setattr(target, name, values[name])


def capture_pose_state():
    """Everything an animation re-evaluation (a frame change) would overwrite: object and pose-bone transforms, shape
    keys and the assigned action. Exporting changes the frame, so a manual, un-keyed pose must be put back afterwards."""
    state = []
    for obj in bpy.data.objects:
        entry = dict(obj=obj, transform=_snapshot(obj), bones=[], keys=[], action=None)
        if obj.pose:
            entry["bones"] = [(bone, _snapshot(bone)) for bone in obj.pose.bones]
        shape_keys = getattr(obj.data, "shape_keys", None) if obj.data else None
        if shape_keys:
            entry["keys"] = [(block, block.value) for block in shape_keys.key_blocks]
        animation = obj.animation_data
        if animation:
            entry["action"] = (animation.action, getattr(animation, "action_slot", None))
        state.append(entry)
    return state


def restore_pose_state(state):
    for entry in state:
        obj = entry["obj"]
        animation = obj.animation_data
        if animation and entry["action"] is not None and animation.action != entry["action"][0]:
            animation.action = entry["action"][0]
            if entry["action"][1] is not None and hasattr(animation, "action_slot"):
                animation.action_slot = entry["action"][1]
        _apply(obj, entry["transform"])
        for bone, values in entry["bones"]:
            _apply(bone, values)
        for block, value in entry["keys"]:
            block.value = value


def export_options(job):
    """The FBX keyword overrides a master's sidecar file declares for this job's Unity target."""
    path = os.path.splitext(job["source"])[0] + OPTIONS_SUFFIX if job.get("source") else ""
    if not path or not os.path.exists(path):
        return {}
    with open(path, encoding="utf-8-sig") as stream:
        data = json.load(stream)
    merged = {}
    target = os.path.basename(job["target"]).casefold()
    for key, section in data.items():
        if key == "*" or key.casefold() == target:
            merged.update(section.get("fbx", {}))
    return {name: value for name, value in merged.items() if name not in _RESERVED_FBX}


def mesh_objects(job, scene=None):
    objects = [obj for obj in (scene or bpy.context.scene).objects if obj.type == "MESH"]
    names = set(job["meshes"])
    # A confirmed one-mesh source/target pair remains unambiguous after a rename.
    if len(names) == 1 and len(objects) == 1:
        return objects
    meshes = [obj for obj in objects if obj.name in names or obj.data.name in names]
    found = {name for name in names if any(obj.name == name or obj.data.name == name for obj in meshes)}
    if not names or found != names:
        raise ValueError("Linked export mesh names do not uniquely identify this master's objects: "
                         + ", ".join(sorted(names - found)))
    for name in names:
        if sum(obj.name == name or obj.data.name == name for obj in meshes) != 1:
            raise ValueError("Ambiguous Blender mesh: " + name)
    return meshes


def export(job):
    active = bpy.context.view_layer.objects.active
    mode = active.mode if active else "OBJECT"
    state = [(obj, obj.name, obj.select_get(), obj.hide_get(), obj.hide_viewport, obj.hide_select)
             for obj in bpy.context.view_layer.objects]
    frame = bpy.context.scene.frame_current
    subframe = bpy.context.scene.frame_subframe
    mesh_names = [(mesh, mesh.name) for mesh in bpy.data.meshes]
    pose_state = capture_pose_state()
    try:
        _export(job)
    finally:
        for mesh, name in mesh_names:
            mesh.name = name
        if bpy.context.object and bpy.context.object.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")
        for obj, name, selected, hidden, viewport, locked in state:
            obj.name = name
            obj.hide_viewport = viewport
            obj.hide_select = locked
            obj.hide_set(hidden)
            obj.select_set(selected)
        bpy.context.view_layer.objects.active = active
        bpy.context.scene.frame_set(frame, subframe=subframe)
        # The frame change re-evaluated every action; put back what the artist had posed but not keyed.
        restore_pose_state(pose_state)
        bpy.context.view_layer.update()
        if active and mode != "OBJECT":
            bpy.ops.object.mode_set(mode=mode)


def _export(job):
    output = job["output"]
    extension = os.path.splitext(output)[1].lower()
    if extension in {".png", ".tga", ".jpg", ".jpeg", ".tif", ".tiff", ".exr"}:
        name = os.path.basename(job["target"]).casefold()
        images = [image for image in bpy.data.images if image.name.casefold() == name
                  or os.path.basename(bpy.path.abspath(image.filepath)).casefold() == name]
        if len(images) != 1:
            raise ValueError("Expected one embedded image matching " + name + "; found " + str(len(images)))
        image = images[0].copy()
        try:
            image.filepath_raw = output
            image.file_format = {".png": "PNG", ".tga": "TARGA", ".jpg": "JPEG", ".jpeg": "JPEG",
                                 ".tif": "TIFF", ".tiff": "TIFF", ".exr": "OPEN_EXR"}[extension]
            image.save()
        finally:
            bpy.data.images.remove(image)
        return

    names = set(job["meshes"])
    meshes = mesh_objects(job)
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        obj.hide_viewport = False
        obj.hide_select = False
        obj.hide_set(False)
        obj.select_set(True)
        for modifier in obj.modifiers:
            if modifier.type == "ARMATURE" and modifier.object:
                modifier.object.hide_set(False)
                modifier.object.select_set(True)
        parent = obj.parent
        while parent:
            parent.hide_set(False)
            parent.select_set(True)
            parent = parent.parent
    # Empties parented to an exported rig (a bone-attached contact or socket marker) travel with it; Unity finds them by name.
    for armature in [obj for obj in bpy.context.selected_objects if obj.type == "ARMATURE"]:
        for child in armature.children_recursive:
            if child.type == "EMPTY":
                child.hide_viewport = False
                child.hide_select = False
                child.hide_set(False)
                child.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(names) == 1 and len(meshes) == 1:
        # Retain Unity's mesh subasset identity without renaming the editable master.
        meshes[0].name = meshes[0].data.name = next(iter(names))
        if meshes[0].name not in names or meshes[0].data.name not in names:
            raise ValueError("Unity mesh name is already used by another Blender object or datablock")
    if extension == ".fbx":
        animated = any(obj.type == "ARMATURE" or obj.animation_data for obj in bpy.context.selected_objects)
        options = dict(object_types={"MESH", "ARMATURE", "EMPTY"}, add_leaf_bones=False, axis_forward="-Z", axis_up="Y",
                       path_mode="STRIP", bake_space_transform=not animated, apply_scale_options="FBX_SCALE_UNITS", bake_anim=animated)
        overrides = export_options(job)
        # "Apply Unit" scaling wins over apply_unit_scale=False, so asking for metre units must drop it too.
        if overrides.get("apply_unit_scale") is False:
            options["apply_scale_options"] = "FBX_SCALE_NONE"
        options.update(overrides)
        bpy.ops.export_scene.fbx(filepath=output, use_selection=True, **options)
    elif extension == ".obj":
        bpy.ops.wm.obj_export(filepath=output, export_selected_objects=True, export_materials=False)
    elif extension in {".glb", ".gltf"}:
        if extension == ".gltf":
            raise ValueError("Multi-file glTF needs an explicit export recipe; use a linked GLB instead.")
        bpy.ops.export_scene.gltf(filepath=output, export_format="GLB", use_selection=True)
    else:
        raise ValueError("Unsupported Blender output: " + extension)


if __name__ == "__main__":
    with open(sys.argv[sys.argv.index("--") + 1], encoding="utf-8-sig") as config:
        data = json.load(config)
    if "jobs" in data:
        for job in data["jobs"]:
            try:
                if os.path.splitext(job["source"])[1].lower() == os.path.splitext(job["target"])[1].lower():
                    import shutil
                    shutil.copyfile(job["source"], job["output"])
                else:
                    export(job)
            except Exception as error:
                with open(job["output"] + ".error", "w", encoding="utf-8-sig") as diagnostic:
                    diagnostic.write(str(error))
    else:
        export(data)
