"""Export a confirmed Unity mesh set or embedded image from a saved Blender master."""
import json
import os
import sys
import bpy


def export(job):
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
    meshes = [obj for obj in bpy.context.scene.objects
              if obj.type == "MESH" and (obj.name in names or obj.data.name in names)]
    found = {name for name in names if any(obj.name == name or obj.data.name == name for obj in meshes)}
    if not names or found != names:
        raise ValueError("Linked export mesh names do not uniquely identify this master's objects: "
                         + ", ".join(sorted(names - found)))
    for name in names:
        if sum(obj.name == name or obj.data.name == name for obj in meshes) != 1:
            raise ValueError("Ambiguous Blender mesh: " + name)
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
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
    bpy.context.view_layer.objects.active = meshes[0]
    if extension == ".fbx":
        bpy.ops.export_scene.fbx(filepath=output, use_selection=True, object_types={"MESH", "ARMATURE", "EMPTY"},
                                 add_leaf_bones=False, axis_forward="-Z", axis_up="Y", path_mode="STRIP")
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
