"""Executed by Painter's documented remote scripting API; JOB is supplied as data."""
import os
import substance_painter.project as project
import substance_painter.textureset as textureset
import substance_painter.export as exporter

if not project.is_open() or os.path.normcase(os.path.realpath(project.file_path())) != os.path.normcase(os.path.realpath(JOB["source"])):
    raise ValueError("The linked Painter master must be the open project; no other project is exported.")
sets = textureset.all_texture_sets()
if len(sets) != 1:
    raise ValueError("Multiple Painter texture sets need an explicit output mapping; no asset was changed.")
channel = {"basecolor": "baseColor", "normal": "Normal_OpenGL", "roughness": "roughness",
           "metallic": "metallic", "ao": "AO_Mixed", "height": "height",
           "emission": "emissive", "alpha": "opacity"}[JOB["channel"]]
source_type = "virtualMap" if channel in {"Normal_OpenGL", "AO_Mixed"} else "documentMap"
components = "RGB" if JOB["channel"] in {"basecolor", "normal", "emission"} else "R"
config = {
    "exportPath": os.path.dirname(JOB["output"]),
    "exportShaderParams": False,
    "exportList": [{"rootPath": sets[0].name()}],
    "exportPresets": [{"name": "AssetSources", "maps": [{
        "fileName": "output", "channels": [{"destChannel": c,
        "srcChannel": c if components == "RGB" else "L", "srcMapType": source_type, "srcMapName": channel}
        for c in "RGB"]}]}],
    "defaultExportPreset": "AssetSources",
    "exportParameters": [{"parameters": {"fileFormat": "png", "bitDepth": "8", "dithering": False,
                                          "paddingAlgorithm": "infinite", "dilationDistance": 16}}]
}
result = exporter.export_project_textures(config)
if result.status != exporter.ExportStatus.Success:
    raise RuntimeError(str(result.message))
