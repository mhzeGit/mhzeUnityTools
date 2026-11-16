"""Content Studio, Blender side (run by studio.py in background Blender 4.4+): builds video-editing projects and pieces.

  blender --background --factory-startup --python studio_blender.py -- job.json

Modes (job["mode"]):
  piece     build one modular piece (outro, sting, intro, lower third) as its own scene and render it to a video file
  assemble  build the edit project (.blend): takes as layered strips (clean picture, UI with alpha, game audio, stems),
            pieces as editable scenes placed by their role, captions and markers from the take's events
  render    render an edit project as it is (hand edits included) to an MP4

Writes {"ok": true, ...} or {"ok": false, "error": ...} to job["answer"].
Channel layout of an edit (bottom to top): 1 picture, 2 UI (alpha over), 3 video files, 4 pieces in the sequence,
6 overlays and lower thirds, 8 game audio, 9-12 stems (muted unless chosen), 14 captions (muted unless shown).
"""
import json
import os
import sys
import traceback
from pathlib import Path

import bpy

CH_PICTURE, CH_UI, CH_VIDEO, CH_PIECE, CH_OVERLAY, CH_AUDIO, CH_STEM, CH_PIECE_AUDIO, CH_CAPTIONS = 1, 2, 3, 4, 6, 8, 9, 13, 14


def strips(scene):
    se = scene.sequence_editor_create()
    return se.strips if hasattr(se, "strips") else se.sequences


def hex_rgba(value, alpha=1.0):
    v = (value or "#ffffff").lstrip("#")
    if len(v) == 3:
        v = "".join(c * 2 for c in v)
    r, g, b = (int(v[i:i + 2], 16) / 255.0 for i in (0, 2, 4))
    a = int(v[6:8], 16) / 255.0 if len(v) >= 8 else alpha
    # sequencer colors are linear; the hex values are sRGB
    lin = lambda c: c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    return (lin(r), lin(g), lin(b), a)


def setup_scene(scene, w, h, fps):
    scene.render.resolution_x = w
    scene.render.resolution_y = h
    scene.render.resolution_percentage = 100
    scene.render.fps = int(fps)
    scene.render.fps_base = 1.0
    scene.view_settings.view_transform = "Standard"  # video in = video out, no film look
    scene.view_settings.look = "None"
    scene.render.use_sequencer = True
    try:
        scene.render.film_transparent = False
    except AttributeError:
        pass


def video_output(scene, path):
    s = scene.render.image_settings
    if hasattr(s, "media_type"):
        s.media_type = "VIDEO"
    s.file_format = "FFMPEG"
    ff = scene.render.ffmpeg
    ff.format = "MPEG4"
    ff.codec = "H264"
    ff.constant_rate_factor = "HIGH"
    ff.ffmpeg_preset = "GOOD"
    ff.audio_codec = "AAC"
    ff.audio_bitrate = 192
    ff.audio_mixrate = 48000
    ff.audio_channels = "STEREO"
    scene.render.filepath = str(path)
    scene.render.use_file_extension = True


def key(obj, prop, frame, value):
    setattr(obj, prop, value)
    obj.keyframe_insert(prop, frame=frame)


def font_of(path):
    if path and os.path.exists(path):
        for f in bpy.data.fonts:
            if f.filepath and os.path.normcase(bpy.path.abspath(f.filepath)) == os.path.normcase(path):
                return f
        return bpy.data.fonts.load(path)
    return None


# ------------------------------------------------------------------ pieces

def animate(strip, anim, f0, fps, w, h):
    """Entry animation of a piece element, as keyframes you can edit in Blender (Graph Editor)."""
    if anim in (None, "", "none"):
        return
    q = max(2, int(round(0.3 * fps)))
    if anim in ("fade", "pop", "slide"):
        key(strip, "blend_alpha", f0, 0.0)
        key(strip, "blend_alpha", f0 + (q if anim == "fade" else max(2, q // 2)), 1.0)
    t = getattr(strip, "transform", None)
    if t is None:
        return
    if anim == "pop":
        bx, by = t.scale_x, t.scale_y  # relative to the element's own size
        for frame, s in ((f0, 0.6), (f0 + q, 1.08), (f0 + q + max(2, q // 2), 1.0)):
            key(t, "scale_x", frame, bx * s)
            key(t, "scale_y", frame, by * s)
    elif anim == "slide":
        base = t.offset_y
        key(t, "offset_y", f0, base - 0.05 * h)
        key(t, "offset_y", f0 + q, base)


def build_piece(job, name=None):
    """A piece as its own scene whose sequencer holds background, text and image strips: editable, any aspect ratio."""
    w, h, fps = int(job["w"]), int(job["h"]), int(job["fps"])
    name = name or ("Piece %s %dx%d" % (job.get("name", "piece"), w, h))
    if job.get("kind") == "blend" and job.get("blend"):
        return load_blend_piece(job, name)
    scene = bpy.data.scenes.new(name)
    setup_scene(scene, w, h, fps)
    total = max(1, int(round(float(job.get("duration", 3)) * fps)))
    scene.frame_start, scene.frame_end = 1, total
    st = strips(scene)
    bg = job.get("background")
    ch = 1
    if isinstance(bg, str) and bg.startswith("#"):
        c = st.new_effect(name="background", type="COLOR", channel=ch, frame_start=1, length=total)
        c.color = hex_rgba(bg)[:3]
    elif isinstance(bg, str) and bg and bg != "none" and os.path.exists(bg):
        st.new_image(name="background", filepath=bg, channel=ch, frame_start=1, fit_method="FILL").frame_final_duration = total
    font = font_of(job.get("font"))
    short = min(w, h)
    for i, el in enumerate(job.get("elements") or []):
        ch = i + 2
        f0 = 1 + int(round(float(el.get("start", 0)) * fps))
        length = max(1, total - f0 + 1 - int(round(float(el.get("endEarly", 0)) * fps)))
        if el.get("type", "text") == "text":
            s = st.new_effect(name="text %d" % (i + 1), type="TEXT", channel=ch, frame_start=f0, length=length)
            s.text = str(el.get("text", ""))
            s.font_size = float(el.get("size", 0.06)) * short
            s.location = (float(el.get("x", 0.5)), float(el.get("y", 0.5)))
            s.color = hex_rgba(el.get("color"))
            s.alignment_x = el.get("align", "CENTER").upper()
            s.anchor_x, s.anchor_y = "CENTER", "CENTER"
            s.wrap_width = float(el.get("wrap", 0.9))
            s.use_bold = bool(el.get("bold", False))
            if el.get("outline"):
                s.use_outline = True
                s.outline_color = hex_rgba(el.get("outline") if isinstance(el.get("outline"), str) else "#000000")
                s.outline_width = float(el.get("outlineWidth", 0.08))
            if el.get("shadow"):
                s.use_shadow = True
            if el.get("box"):
                s.use_box = True
                s.box_color = hex_rgba(el.get("box") if isinstance(el.get("box"), str) else "#000000", 0.7)
            if font:
                s.font = font
            s.blend_type = "ALPHA_OVER"
        elif el.get("type") == "image" and el.get("src") and os.path.exists(el["src"]):
            s = st.new_image(name="image %d" % (i + 1), filepath=el["src"], channel=ch, frame_start=f0, fit_method="ORIGINAL")
            s.frame_final_duration = length
            img = bpy.data.images.load(el["src"], check_existing=True)
            iw = max(1, img.size[0])
            sc = float(el.get("width", 0.5)) * w / iw
            s.transform.scale_x = s.transform.scale_y = sc
            s.transform.offset_x = (float(el.get("x", 0.5)) - 0.5) * w
            s.transform.offset_y = (float(el.get("y", 0.5)) - 0.5) * h
            s.blend_type = "ALPHA_OVER"
        else:
            continue
        animate(s, el.get("anim"), f0, fps, w, h)
        if job.get("fadeOutElements"):
            fo = max(2, int(round(float(job["fadeOutElements"]) * fps)))
            key(s, "blend_alpha", total - fo, 1.0)
            key(s, "blend_alpha", total, 0.0)
    if job.get("audio") and os.path.exists(job["audio"]):
        a = st.new_sound(name="piece audio", filepath=job["audio"], channel=ch + 1, frame_start=1)
        a.volume = float(job.get("volume", 1.0))
    scene["studio_piece"] = job.get("name", "")
    return scene


def load_blend_piece(job, name):
    """A hand-made piece: the scene named like the format (vertical, wide...) or WxH, else the first scene, of the piece's
    .blend; {gameName}-style words in its text strips are replaced with the brand values."""
    want = [job.get("formatName", ""), "%dx%d" % (job["w"], job["h"])]
    with bpy.data.libraries.load(job["blend"], link=False) as (src, dst):
        pick = next((s for s in src.scenes if s in want), src.scenes[0] if src.scenes else None)
        dst.scenes = [pick] if pick else []
    if not dst.scenes:
        raise RuntimeError("no scene in " + job["blend"])
    scene = dst.scenes[0]
    scene.name = name
    variables = job.get("vars") or {}
    for s in strips(scene):
        if s.type == "TEXT":
            for k, v in variables.items():
                s.text = s.text.replace("{%s}" % k, str(v))
    return scene


def render_scene(scene, output):
    """Renders a scene's sequencer to output (an .mp4), wherever Blender puts the frame range in the name."""
    out = Path(output)
    out.parent.mkdir(parents=True, exist_ok=True)
    stem = out.with_suffix("")
    video_output(scene, str(stem) + "_")
    for old in out.parent.glob(stem.name + "_*"):
        old.unlink()
    bpy.ops.render.render(animation=True, scene=scene.name)
    made = sorted(out.parent.glob(stem.name + "_*"), key=lambda p: p.stat().st_mtime)
    if not made:
        raise RuntimeError("Blender rendered no file next to " + str(out))
    if out.exists():
        out.unlink()
    os.replace(made[-1], out)
    return out


def mode_piece(job):
    piece = job["piece"]
    scene = build_piece(piece)
    # render from a scene the window would use as well
    out = render_scene(scene, job["output"])
    return {"output": str(out), "frames": scene.frame_end}


# ------------------------------------------------------------------ assemble an edit

def frames(seconds, fps):
    return int(round(float(seconds) * fps))


def add_take(scene, e, cursor, fps, captions, show_captions, font):
    st = strips(scene)
    folder = Path(e["folder"])
    t = e["take"]
    tf = int(t.get("fps", fps))
    if tf != fps:
        raise RuntimeError("take %s is %d fps, the edit is %d fps" % (folder.name, tf, fps))
    cin, cout = frames(e["in"], fps), frames(e["out"], fps)
    total = int(t.get("frames") or cout)
    length = max(1, cout - cin)
    start = cursor - cin
    made = []

    def trim(s):
        s.frame_start = start
        s.frame_offset_start = cin
        s.frame_final_duration = length
        return s

    pic = st.new_movie(name="%s picture" % folder.name, filepath=str(folder / "clean.mp4"), channel=CH_PICTURE, frame_start=start, fit_method="FIT")
    made.append(trim(pic))
    ui_dir = folder / "ui"
    if t.get("ui") and ui_dir.exists():
        files = sorted(p.name for p in ui_dir.glob("ui_*.png"))
        if files:
            ui = st.new_image(name="%s UI" % folder.name, filepath=str(ui_dir / files[0]), channel=CH_UI, frame_start=start, fit_method="FIT")
            for f in files[1:]:
                ui.elements.append(f)
            ui.blend_type = "ALPHA_OVER"
            trim(ui)
            ui.mute = not e.get("ui", True)
            made.append(ui)
    audio = e.get("audio", "master")
    chosen = [] if audio in ("master", "none", None) else audio.split("+")
    if t.get("audio") and (folder / "audio.wav").exists():
        snd = st.new_sound(name="%s game audio" % folder.name, filepath=str(folder / "audio.wav"), channel=CH_AUDIO, frame_start=start)
        trim(snd)
        snd.volume = float(e.get("volume", 1.0))
        snd.mute = audio == "none" or bool(chosen)
        made.append(snd)
    for i, rel in enumerate(t.get("stemFiles") or []):
        name = Path(rel).stem
        sf = folder / rel
        if not sf.exists():
            continue
        s = st.new_sound(name="%s stem %s" % (folder.name, name), filepath=str(sf), channel=CH_STEM + min(i, 3), frame_start=start)
        trim(s)
        s.mute = name not in chosen
        made.append(s)
    fi, fo = frames(e.get("fadeIn", 0), fps), frames(e.get("fadeOut", 0), fps)
    if fi > 0:
        key(pic, "blend_alpha", cursor, 0.0)
        key(pic, "blend_alpha", cursor + fi, 1.0)
    if fo > 0:
        key(pic, "blend_alpha", cursor + length - fo, 1.0)
        key(pic, "blend_alpha", cursor + length, 0.0)
    # captions and markers from the take's events, on this clip's span
    evf = folder / "events.jsonl"
    if evf.exists():
        events = []
        for line in evf.read_text(encoding="utf-8").splitlines():
            try:
                events.append(json.loads(line))
            except ValueError:
                pass
        for t0, t1, text in e.get("captionLines") or []:  # cleaned by studio.py
            a, b = max(frames(t0, fps), cin), min(frames(t1, fps), cout)
            if b <= a:
                continue
            c = st.new_effect(name="caption", type="TEXT", channel=CH_CAPTIONS, frame_start=cursor + a - cin, length=b - a)
            c.text = text
            c.font_size = 0.045 * min(scene.render.resolution_x, scene.render.resolution_y)
            c.location = (0.5, 0.22)
            c.wrap_width = 0.85
            c.use_box = True
            c.box_color = (0, 0, 0, 0.6)
            c.anchor_x, c.anchor_y = "CENTER", "CENTER"
            if font:
                c.font = font
            c.blend_type = "ALPHA_OVER"
            c.mute = not show_captions
        for ev in events:
            if ev.get("kind") in ("label", "mark", "cut"):
                f = frames(ev["t"], fps)
                if cin <= f <= cout:
                    scene.timeline_markers.new(("%s: %s" % (ev["kind"], ev.get("text", "")))[:60], frame=cursor + f - cin)
    return cursor + length, made


def add_piece_strip(scene, e, start, channel):
    """A piece as a scene strip (its scene stays editable in the same file)."""
    st = strips(scene)
    job = dict(e["job"])
    piece_scene = build_piece(job, "Piece %s" % e["piece"])
    s = st.new_scene(name="piece %s" % e["piece"], scene=piece_scene, channel=channel, frame_start=start)
    if hasattr(s, "scene_input"):
        s.scene_input = "SEQUENCER"
    s.frame_final_duration = piece_scene.frame_end - piece_scene.frame_start + 1
    s.blend_type = "ALPHA_OVER"
    return s, piece_scene


def mode_assemble(job):
    plan, edit = job["plan"], job["edit"]
    w, h, fps = plan["w"], plan["h"], plan["fps"]
    scene = bpy.context.scene
    scene.name = "Edit %s" % edit["name"]
    setup_scene(scene, w, h, fps)
    st = strips(scene)
    for s in list(st):
        st.remove(s)
    captions = job.get("captions") or {}
    font = font_of(job.get("font"))
    cursor, count, end_of_last_clip = 1, 0, 1
    for e in plan["sequence"]:
        if e["kind"] == "take":
            cursor, made = add_take(scene, e, cursor, fps, captions, bool(captions.get("show")), font)
            count += len(made)
            end_of_last_clip = cursor
        elif e["kind"] == "video":
            m = st.new_movie(name=Path(e["file"]).stem, filepath=e["file"], channel=CH_VIDEO, frame_start=cursor, fit_method="FIT")
            if e.get("in"):
                m.frame_start = cursor - frames(e["in"], fps)
                m.frame_offset_start = frames(e["in"], fps)
            if e.get("out"):
                m.frame_final_duration = frames(float(e["out"]) - float(e.get("in") or 0), fps)
            cursor = m.frame_final_end
            count += 1
        elif e["kind"] == "piece":
            place = e.get("place") or {}
            fade_in = frames(place.get("fadeIn", 0), fps)
            start = max(1, cursor - fade_in) if cursor > 1 else 1
            s, ps = add_piece_strip(scene, e, start, CH_PIECE)
            if fade_in > 0 and start < cursor:
                key(s, "blend_alpha", start, 0.0)
                key(s, "blend_alpha", cursor, 1.0)
            fade_out = frames(place.get("fadeOut", 0), fps)
            if fade_out > 0:
                key(s, "blend_alpha", s.frame_final_end - fade_out, 1.0)
                key(s, "blend_alpha", s.frame_final_end, 0.0)
            scene.timeline_markers.new("%s: %s" % (e.get("role") or "piece", e["piece"]), frame=start)
            cursor = s.frame_final_end
            count += 1
    for e in plan["overlays"]:
        start = 1 + frames(e.get("at", 0), fps)
        add_piece_strip(scene, e, start, CH_OVERLAY)
        count += 1
    scene.frame_start, scene.frame_end = 1, max(1, cursor - 1)
    renders = Path(job["renders"])
    renders.mkdir(parents=True, exist_ok=True)
    video_output(scene, str(renders / (edit["name"] + "_")))
    scene["studio_edit"] = edit["name"]
    for extra in [s for s in bpy.data.scenes if s != scene and not s.get("studio_piece") and not s.name.startswith("Piece ")]:
        bpy.data.scenes.remove(extra)
    Path(job["blend"]).parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=job["blend"], compress=False)
    return {"blend": job["blend"], "frames": scene.frame_end, "seconds": round(scene.frame_end / fps, 2), "strips": count}


def mode_render(job):
    bpy.ops.wm.open_mainfile(filepath=job["blend"])
    edit_scenes = [s for s in bpy.data.scenes if s.get("studio_edit")]
    scene = edit_scenes[0] if edit_scenes else bpy.context.scene
    out = render_scene(scene, job["output"])
    return {"output": str(out), "frames": scene.frame_end, "scene": scene.name}


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    job = json.loads(Path(argv[0]).read_text(encoding="utf-8"))
    answer = {"ok": False}
    try:
        fn = {"piece": mode_piece, "assemble": mode_assemble, "render": mode_render}[job["mode"]]
        answer = dict(fn(job), ok=True)
    except Exception as e:
        answer = {"ok": False, "error": "%s: %s" % (type(e).__name__, e), "trace": traceback.format_exc()[-3000:]}
    Path(job["answer"]).write_text(json.dumps(answer, indent=1), encoding="utf-8")


main()
