#!/usr/bin/env python3
"""Create, inspect and render Audacity .aup3 projects without Audacity.

Standard library only; ffmpeg (on PATH, or FFMPEG) decodes the audio a project is created from and encodes the
file a project is exported to. All output is JSON: {"ok": true, "data": ...} or {"ok": false, "error": ...}.

  inspect <project.aup3>
  create  <audio> <project.aup3> [--name NAME]      project holding the exact decoded samples
  render  <project.aup3> <out> [--like EXISTING]    mix the project to out; --like copies codec/bitrate of a file
  verify  <project.aup3> <audio>                    project samples == the decoded audio, bit for bit

Real-time effects are applied by Audacity only when it exports, so a project that has active ones cannot be
rendered here; render falls back to a running Audacity with mod-script-pipe enabled (see pipe_export).
"""
import argparse, array, base64, json, math, operator, os, shutil, sqlite3, struct, subprocess, sys, tempfile

BLOCK = 524288
I16, F32 = 0x20001, 0x4000F
FLT_MAX = 3.4028234663852886e+38
TYPE = {"push": 1, "pop": 2, "str": 3, "int": 4, "bool": 5, "long": 6, "ll": 7, "size": 8, "float": 9, "dbl": 10, "chars": 12}
_FMT = {4: "<i", 6: "<i", 7: "<q", 8: "<I"}

DICT = base64.b64decode("""
AAIPAAAOAHAAcgBvAGoAZQBjAHQADwEACgB4AG0AbABuAHMADwIADgB2AGUAcgBzAGkAbwBuAA8DAB4AYQB1AGQAYQBjAGkA
dAB5AHYAZQByAHMAaQBvAG4ADwQACAByAGEAdABlAA8FAB4AcwBlAGwAZQBjAHQAaQBvAG4AZgBvAHIAbQBhAHQADwYAHgBm
AHIAZQBxAHUAZQBuAGMAeQBmAG8AcgBtAGEAdAAPBwAeAGIAYQBuAGQAdwBpAGQAdABoAGYAbwByAG0AYQB0AA8IACgAdABp
AG0AZQBfAHMAaQBnAG4AYQB0AHUAcgBlAF8AdABlAG0AcABvAA8JACgAdABpAG0AZQBfAHMAaQBnAG4AYQB0AHUAcgBlAF8A
dQBwAHAAZQByAA8KACgAdABpAG0AZQBfAHMAaQBnAG4AYQB0AHUAcgBlAF8AbABvAHcAZQByAA8LAAwAcwBuAGEAcAB0AG8A
DwwACABzAGUAbAAwAA8NAAgAcwBlAGwAMQAPDgAIAHYAcABvAHMADw8AAgBoAA8QAAgAegBvAG8AbQAPEQAqAHAAcgBlAGYA
ZQByAHIAZQBkAF8AZQB4AHAAbwByAHQAXwByAGEAdABlAA8SAA4AZQBmAGYAZQBjAHQAcwAPEwAMAGEAYwB0AGkAdgBlAA8U
AAgAdABhAGcAcwAPFQASAHcAYQB2AGUAdAByAGEAYwBrAA8WAAgAbgBhAG0AZQAPFwAUAGkAcwBTAGUAbABlAGMAdABlAGQA
DxgAFABjAG8AbABvAHIAaQBuAGQAZQB4AA8ZAAwAaABlAGkAZwBoAHQADxoAEgBtAGkAbgBpAG0AaQB6AGUAZAAPGwAOAGgA
ZQBpAGcAaAB0ADEADxwAFABtAGkAbgBpAG0AaQB6AGUAZAAxAA8dAA4AYwBoAGEAbgBuAGUAbAAPHgAMAGwAaQBuAGsAZQBk
AA8fAAgAbQB1AHQAZQAPIAAIAHMAbwBsAG8ADyEACABnAGEAaQBuAA8iAAYAcABhAG4ADyMAGABzAGEAbQBwAGwAZQBmAG8A
cgBtAGEAdAAPJAAQAHcAYQB2AGUAYwBsAGkAcAAPJQAMAG8AZgBmAHMAZQB0AA8mABAAdAByAGkAbQBMAGUAZgB0AA8nABIA
dAByAGkAbQBSAGkAZwBoAHQADygAEgBjAGUAbgB0AFMAaABpAGYAdAAPKQAmAHAAaQB0AGMAaABBAG4AZABTAHAAZQBlAGQA
UAByAGUAcwBlAHQADyoAGgByAGEAdwBBAHUAZABpAG8AVABlAG0AcABvAA8rACAAYwBsAGkAcABTAHQAcgBlAHQAYwBoAFIA
YQB0AGkAbwAPLAAQAHMAZQBxAHUAZQBuAGMAZQAPLQAUAG0AYQB4AHMAYQBtAHAAbABlAHMADy4AKgBlAGYAZgBlAGMAdABp
AHYAZQBzAGEAbQBwAGwAZQBmAG8AcgBtAGEAdAAPLwAUAG4AdQBtAHMAYQBtAHAAbABlAHMADzAAEgB3AGEAdgBlAGIAbABv
AGMAawAPMQAKAHMAdABhAHIAdAAPMgAMAGwAZQBuAGcAdABoAA8zAA4AYgBsAG8AYwBrAGkAZAAPNAAQAGUAbgB2AGUAbABv
AHAAZQAPNQASAG4AdQBtAHAAbwBpAG4AdABzAA82AAwAZQBmAGYAZQBjAHQADzcABABpAGQADzgAFABwAGEAcgBhAG0AZQB0
AGUAcgBzAA85ABIAcABhAHIAYQBtAGUAdABlAHIADzoACgB2AGEAbAB1AGUA
""")
HEAD = base64.b64decode("""
DAwAAAA8AD8AeABtAGwAIAAMHAAAAHYAZQByAHMAaQBvAG4APQAiADEALgAwACIAIAAMIAAAAHMAdABhAG4AZABhAGwAbwBu
AGUAPQAiAG4AbwAiACAADAYAAAA/AD4ACgAMFAAAADwAIQBEAE8AQwBUAFkAUABFACAADBAAAABwAHIAbwBqAGUAYwB0ACAA
DA4AAABQAFUAQgBMAEkAQwAgAAxIAAAAIgAtAC8ALwBhAHUAZABhAGMAaQB0AHkAcAByAG8AagBlAGMAdAAtADEALgAzAC4A
MAAvAC8ARABUAEQALwAvAEUATgAiACAADIAAAAAiAGgAdAB0AHAAOgAvAC8AYQB1AGQAYQBjAGkAdAB5AC4AcwBvAHUAcgBj
AGUAZgBvAHIAZwBlAC4AbgBlAHQALwB4AG0AbAAvAGEAdQBkAGEAYwBpAHQAeQBwAHIAbwBqAGUAYwB0AC0AMQAuADMALgAw
AC4AZAB0AGQAIgAgAAwEAAAAPgAKAA==
""")


class Unsupported(Exception):
    pass


def names_of(dictionary):
    names, i = {}, 0
    while i < len(dictionary):
        if dictionary[i] == 0x0F:
            n = int.from_bytes(dictionary[i + 1:i + 3], "little")
            size = int.from_bytes(dictionary[i + 3:i + 5], "little")
            names[n] = dictionary[i + 5:i + 5 + size].decode("utf-16le")
            i += 5 + size
        else:
            i += 1
    return names


NAMES = names_of(DICT)
IDS = {v: k for k, v in NAMES.items()}


def tokens(doc):
    """Audacity's binary XML as (type, id, value[, digits]) tuples; emit() is the exact inverse."""
    p, out = 0, []
    while p < len(doc):
        t = doc[p]
        p += 1
        if t in (1, 2):
            out.append((t, int.from_bytes(doc[p:p + 2], "little")))
            p += 2
        elif t == 3:
            n = int.from_bytes(doc[p + 2:p + 6], "little")
            out.append((t, int.from_bytes(doc[p:p + 2], "little"), doc[p + 6:p + 6 + n].decode("utf-16le")))
            p += 6 + n
        elif t in _FMT:
            size = struct.calcsize(_FMT[t])
            out.append((t, int.from_bytes(doc[p:p + 2], "little"), struct.unpack(_FMT[t], doc[p + 2:p + 2 + size])[0]))
            p += 2 + size
        elif t == 5:
            out.append((t, int.from_bytes(doc[p:p + 2], "little"), doc[p + 2]))
            p += 3
        elif t == 9:
            out.append((t, int.from_bytes(doc[p:p + 2], "little"), *struct.unpack("<fi", doc[p + 2:p + 10])))
            p += 10
        elif t == 10:
            out.append((t, int.from_bytes(doc[p:p + 2], "little"), *struct.unpack("<di", doc[p + 2:p + 14])))
            p += 14
        elif t == 12:
            n = int.from_bytes(doc[p:p + 4], "little")
            out.append((t, doc[p + 4:p + 4 + n]))
            p += 4 + n
        else:
            raise Unsupported("unknown field type %d in the project document" % t)
    return out


def emit(toks):
    o = bytearray()
    for k in toks:
        t = k[0]
        o.append(t)
        if t in (1, 2):
            o += k[1].to_bytes(2, "little")
        elif t == 3:
            s = k[2].encode("utf-16le")
            o += k[1].to_bytes(2, "little") + len(s).to_bytes(4, "little") + s
        elif t in _FMT:
            o += k[1].to_bytes(2, "little") + struct.pack(_FMT[t], k[2])
        elif t == 5:
            o += k[1].to_bytes(2, "little") + bytes([k[2]])
        elif t == 9:
            o += k[1].to_bytes(2, "little") + struct.pack("<fi", k[2], k[3])
        elif t == 10:
            o += k[1].to_bytes(2, "little") + struct.pack("<di", k[2], k[3])
        elif t == 12:
            o += len(k[1]).to_bytes(4, "little") + k[1]
    return bytes(o)


def tree(doc, names):
    root, stack = None, []
    for k in tokens(doc):
        if k[0] == 1:
            node = {"tag": names[k[1]], "a": {}, "k": []}
            if stack:
                stack[-1]["k"].append(node)
            else:
                root = node
            stack.append(node)
        elif k[0] == 2:
            stack.pop()
        elif k[0] != 12:
            stack[-1]["a"][names[k[1]]] = k[2]
    return root


def find(node, tag):
    return [x for x in node["k"] if x["tag"] == tag]


# ---------------------------------------------------------------- reading

def open_project(path):
    # Not mode=ro: a read-only WAL connection cannot delete the -wal/-shm files it leaves beside the project.
    db = sqlite3.connect(os.path.abspath(path), timeout=30)
    db.execute("PRAGMA query_only=1")
    return db


def read_project(path):
    db = open_project(path)
    try:
        row = db.execute("select dict, doc from project").fetchone()
        root = tree(row[1], names_of(row[0]))
    finally:
        db.close()
    tracks, second = [], False
    for w in find(root, "wavetrack"):
        effects = [e["a"].get("id") for fx in find(w, "effects") for e in find(fx, "effect") if e["a"].get("active", 1)]
        clips = []
        for c in find(w, "waveclip"):
            seq = find(c, "sequence")[0]
            env = find(c, "envelope")
            clips.append({
                "offset": c["a"].get("offset", 0.0), "trimLeft": c["a"].get("trimLeft", 0.0), "trimRight": c["a"].get("trimRight", 0.0),
                "stretch": c["a"].get("clipStretchRatio", 1.0), "cents": c["a"].get("centShift", 0),
                "format": seq["a"]["sampleformat"], "samples": seq["a"]["numsamples"],
                "blocks": [(b["a"]["start"], b["a"]["length"], b["a"]["blockid"]) for b in find(seq, "waveblock")],
                "envelope": env[0]["a"].get("numpoints", 0) if env else 0})
        a = w["a"]
        # Audacity 3.7 writes a lone track as channel 0; only a linked pair is stereo: its first track is left, the next right.
        role = 1 if second else 0 if a.get("linked", 0) in (2, 3) else 2
        second = role == 0
        tracks.append({"name": a.get("name", ""), "channel": a.get("channel", 2), "role": role, "linked": a.get("linked", 0), "rate": a.get("rate", 44100.0),
                       "gain": a.get("gain", 1.0), "pan": a.get("pan", 0.0), "mute": bool(a.get("mute", 0)), "solo": bool(a.get("solo", 0)),
                       "effects": effects, "clips": clips})
    return {"rate": root["a"].get("rate", 44100.0), "tracks": tracks}


def inspect(path):
    p = read_project(path)
    return {"rate": p["rate"], "tracks": [{k: v for k, v in t.items() if k != "clips"} | {"clips": [
        {k: v for k, v in c.items() if k != "blocks"} | {"blocks": len(c["blocks"])} for c in t["clips"]]} for t in p["tracks"]]}


def _samples(db, clip, start, count):
    """Samples [start, start+count) of a clip's sequence as array('h') or array('f')."""
    typecode = {I16: "h", F32: "f"}.get(clip["format"])
    if typecode is None:
        raise Unsupported("sample format 0x%x (24-bit integer) is not supported; the project must hold 16-bit or float samples" % clip["format"])
    out = array.array(typecode)
    for b_start, b_len, block in clip["blocks"]:
        lo, hi = max(start, b_start), min(start + count, b_start + b_len)
        if lo >= hi:
            continue
        row = db.execute("select sampleformat, samples from sampleblocks where blockid=?", (block,)).fetchone()
        if row is None or row[0] != clip["format"]:
            raise Unsupported("sample block %d is missing or has a different format" % block)
        part = array.array(typecode)
        part.frombytes(row[1])
        if sys.byteorder == "big":
            part.byteswap()
        out.extend(part[lo - b_start:hi - b_start])
    if len(out) != count:
        raise Unsupported("the project's sample blocks do not cover its clip")
    return out


def render(path):
    """Mix the project. Returns (channels, typecode, rate): one array per output channel, same length."""
    p = read_project(path)
    tracks = [t for t in p["tracks"] if t["clips"]]
    solo = any(t["solo"] for t in tracks)
    tracks = [t for t in tracks if not t["mute"] and (t["solo"] or not solo)]
    if not tracks:
        raise Unsupported("the project has no audible audio")
    if any(t["effects"] for t in tracks):
        raise Unsupported("a track has real-time effects")
    rate = tracks[0]["rate"]
    if any(t["rate"] != rate for t in tracks):
        raise Unsupported("tracks with different sample rates")
    stereo = any(t["role"] != 2 or t["pan"] != 0 for t in tracks)
    # (signal, left gain, right gain) contributions, each placed at an absolute sample position.
    parts, db = [], open_project(path)
    try:
        for t in tracks:
            pan = t["pan"]
            lg, rg = t["gain"] * (1 - pan if pan > 0 else 1), t["gain"] * (1 + pan if pan < 0 else 1)
            gains = {0: (lg, 0.0), 1: (0.0, rg), 2: (lg, rg)}[t["role"]]
            for c in t["clips"]:
                if c["stretch"] != 1.0 or c["cents"] or c["envelope"]:
                    raise Unsupported("clip stretching, pitch shift or envelopes")
                lo, hi = round(c["trimLeft"] * rate), c["samples"] - round(c["trimRight"] * rate)
                if hi > lo:
                    parts.append((_samples(db, c, lo, hi - lo), gains, round((c["offset"] + c["trimLeft"]) * rate)))
    finally:
        db.close()
    length = max(s + len(sig) for sig, _, s in parts)
    wanted = [[(sig, g[ch], s) for sig, g, s in parts if g[ch] != 0.0] for ch in range(2 if stereo else 1)]
    # A lone, unmodified clip per channel is passed through untouched, so 16-bit audio stays bit-exact.
    straight = all(len(m) == 1 and m[0][1] == 1.0 and m[0][2] == 0 for m in wanted) and len({m[0][0].typecode for m in wanted}) == 1
    typecode = wanted[0][0][0].typecode if straight else "f"
    channels = []
    for mine in wanted:
        if straight:
            out = array.array(typecode, mine[0][0])
        else:
            out = array.array("f", bytes(4 * length))
            for sig, g, s in mine:
                f = g / 32768 if sig.typecode == "h" else g
                for i, v in enumerate(sig, s):
                    out[i] += v * f
        channels.append(out)
    length = max(len(c) for c in channels)
    for c in channels:
        c.extend(array.array(typecode, [0] * (length - len(c))))
    return channels, typecode, rate


def interleave(channels):
    if len(channels) == 1:
        return channels[0]
    out = array.array(channels[0].typecode, bytes(channels[0].itemsize * len(channels[0]) * len(channels)))
    for i, c in enumerate(channels):
        out[i::len(channels)] = c
    return out


# ---------------------------------------------------------------- ffmpeg

def tool(name):
    found = os.environ.get(name.upper()) or shutil.which(name)
    if not found:
        raise FileNotFoundError("%s was not found on PATH (set %s to its full path)" % (name, name.upper()))
    return found


def probe(path):
    out = subprocess.run([tool("ffprobe"), "-v", "error", "-select_streams", "a:0", "-show_entries",
                          "stream=codec_name,sample_rate,channels,bit_rate", "-of", "json", path], capture_output=True, text=True)
    if out.returncode:
        raise IOError(out.stderr.strip() or "ffprobe failed on " + path)
    return json.loads(out.stdout)["streams"][0]


def decode(path):
    """(raw little-endian bytes, 'h' or 'f', rate, channels): 16-bit PCM stays 16-bit, everything else is 32-bit float."""
    info = probe(path)
    channels, rate = int(info["channels"]), int(info["sample_rate"])
    if channels not in (1, 2):
        raise Unsupported("only mono and stereo audio is supported (%d channels)" % channels)
    exact16 = info["codec_name"] == "pcm_s16le"
    out = subprocess.run([tool("ffmpeg"), "-v", "error", "-i", path, "-map", "0:a:0", "-f", "s16le" if exact16 else "f32le",
                          "-acodec", "pcm_s16le" if exact16 else "pcm_f32le", "-"], capture_output=True)
    if out.returncode:
        raise IOError(out.stderr.decode(errors="replace").strip())
    return out.stdout, "h" if exact16 else "f", rate, channels


def encode(raw, typecode, rate, channels, out_path, like=None):
    """Write interleaved raw samples to out_path, matching the codec of an existing file when one is given."""
    ext = os.path.splitext(out_path)[1].lower()
    info = probe(like) if like and os.path.exists(like) else {}
    codec, bitrate = info.get("codec_name"), info.get("bit_rate")
    args = ["-c:a", {".wav": codec if (codec or "").startswith("pcm_") else ("pcm_s16le" if typecode == "h" else "pcm_f32le"),
                     ".ogg": "libvorbis", ".mp3": "libmp3lame", ".flac": "flac", ".aif": "pcm_s16be", ".aiff": "pcm_s16be"}[ext]]
    if ext in (".ogg", ".mp3"):
        args += ["-b:a", str(bitrate)] if bitrate else ["-q:a", "5" if ext == ".ogg" else "2"]
    cmd = [tool("ffmpeg"), "-v", "error", "-y", "-f", "s16le" if typecode == "h" else "f32le", "-ar", str(rate), "-ac", str(channels),
           "-i", "-", "-map_metadata", "-1", "-fflags", "+bitexact", "-flags:a", "+bitexact"] + args + [out_path]
    done = subprocess.run(cmd, input=raw, capture_output=True)
    if done.returncode:
        raise IOError(done.stderr.decode(errors="replace").strip())


# ---------------------------------------------------------------- Audacity pipe

def pipe_export(path, out_path):
    """Ask a running Audacity (Preferences > Modules > mod-script-pipe = Enabled) to export; it applies real-time effects."""
    to, back = ((r"\\.\pipe\ToSrvPipe", r"\\.\pipe\FromSrvPipe") if os.name == "nt" else
                ("/tmp/audacity_script_pipe.to.%d" % os.getuid(), "/tmp/audacity_script_pipe.from.%d" % os.getuid()))
    try:
        writer, reader = open(to, "w", encoding="utf-8"), open(back, "rt", encoding="utf-8")
    except OSError:
        raise Unsupported("this project has real-time effects, which only Audacity can render. Enable Edit > Preferences > Modules > "
                          "mod-script-pipe and restart Audacity with this project open, or export it from Audacity (Ctrl+Shift+E)")
    eol = "\r\n\0" if os.name == "nt" else "\n"

    def ask(command):
        writer.write(command + eol)
        writer.flush()
        text = ""
        while True:
            line = reader.readline()
            if line == "\n" and text:
                return text
            text += line

    with writer, reader:
        project = read_project(path)
        listing = json.loads(ask("GetInfo: Type=Tracks Format=JSON").split("BatchCommand finished")[0])
        if sorted(t["name"] for t in listing) != sorted(t["name"] for t in project["tracks"] if t["clips"]):
            raise Unsupported("the project open in Audacity is not this one (track names differ)")
        ask("SelectAll:")
        channels = 1 if all(t["role"] == 2 for t in project["tracks"]) else 2
        reply = ask('Export2: Filename="%s" NumChannels=%d' % (out_path.replace("\\", "/"), channels))
        if "finished: OK" not in reply:
            raise IOError("Audacity could not export: " + reply.strip())


# ---------------------------------------------------------------- creating

def _doc(name, rate, channels, numsamples, ids, fmt):
    """The project document for one clip per channel; ids are the sample block ids of each channel."""
    I = IDS
    push, pop = (lambda n: (1, I[n])), (lambda n: (2, I[n]))
    s, i, b, d = (lambda n, v: (3, I[n], v)), (lambda n, v: (4, I[n], v)), (lambda n, v: (5, I[n], v)), (lambda n, v, g=-1: (10, I[n], v, g))
    t = [push("project"), s("xmlns", "http://audacity.sourceforge.net/xml/"), s("version", "1.3.0"), s("audacityversion", "3.7.9"), d("rate", rate),
         s("selectionformat", "hh:mm:ss + milliseconds"), s("frequencyformat", "Hz"), s("bandwidthformat", "octaves"),
         d("time_signature_tempo", 120.0), i("time_signature_upper", 4), i("time_signature_lower", 4), s("snapto", "off"),
         d("sel0", 0.0, 10), d("sel1", 0.0, 10), i("vpos", 0), d("h", 0.0, 10),
         d("zoom", min(max(900.0 / max(numsamples / rate, 1e-3), 0.01), 1e6), 10), d("preferred_export_rate", rate),
         push("effects"), b("active", 1), pop("effects"), push("tags"), pop("tags")]
    for ch in range(channels):
        left_right = (0, 3) if channels == 2 and ch == 0 else (1, 0) if channels == 2 else (0, 0)
        t += [push("wavetrack"), s("name", name), b("isSelected", 1), i("colorindex", 0), i("height", 150), b("minimized", 0),
              i("height1", 150), b("minimized1", 0), i("channel", left_right[0]), i("linked", left_right[1]), b("mute", 0), b("solo", 0),
              d("rate", rate), d("gain", 1.0), d("pan", 0.0), (6, I["sampleformat"], fmt),
              push("waveclip"), d("offset", 0.0, 8), d("trimLeft", 0.0, 8), d("trimRight", 0.0, 8), i("centShift", 0),
              (6, I["pitchAndSpeedPreset"], 0), d("rawAudioTempo", 120.0, 8), d("clipStretchRatio", 1.0, 8), s("name", name), i("colorindex", 0),
              push("sequence"), (8, I["maxsamples"], BLOCK), (8, I["sampleformat"], fmt), (8, I["effectivesampleformat"], fmt),
              (7, I["numsamples"], numsamples)]
        for n, block in enumerate(ids[ch]):
            t += [push("waveblock"), (7, I["start"], n * BLOCK), (7, I["length"], min(BLOCK, numsamples - n * BLOCK)), (7, I["blockid"], block), pop("waveblock")]
        t += [pop("sequence"), push("envelope"), (8, I["numpoints"], 0), pop("envelope"), pop("waveclip"), pop("wavetrack")]
    return HEAD + emit(t + [pop("project")])


def _summary(chunk, scale):
    n = len(chunk)
    return (min(chunk) * scale, max(chunk) * scale, math.sqrt(sum(map(operator.mul, chunk, chunk)) / n) * scale)


def create(source, target, name=None):
    raw, typecode, rate, channels = decode(source)
    samples = array.array(typecode)
    samples.frombytes(raw)
    if sys.byteorder == "big":
        samples.byteswap()
    frames = len(samples) // channels
    if frames == 0:
        raise Unsupported("the audio file is empty")
    scale = 1 / 32768 if typecode == "h" else 1.0
    fmt = I16 if typecode == "h" else F32
    name = name or os.path.splitext(os.path.basename(source))[0]
    if os.path.exists(target):
        raise FileExistsError(target + " already exists; Audacity projects are never overwritten")
    os.makedirs(os.path.dirname(os.path.abspath(target)), exist_ok=True)
    staging = target + ".creating"
    for leftover in (staging, staging + "-wal", staging + "-shm"):
        if os.path.exists(leftover):
            os.remove(leftover)
    db = sqlite3.connect(staging)
    try:
        db.executescript("PRAGMA page_size=65536; PRAGMA journal_mode=WAL; PRAGMA application_id=1096107097; PRAGMA user_version=50790400;")
        db.executescript("""CREATE TABLE project(  id                   INTEGER PRIMARY KEY,  dict                 BLOB,  doc                  BLOB);
            CREATE TABLE autosave(  id                   INTEGER PRIMARY KEY,  dict                 BLOB,  doc                  BLOB);
            CREATE TABLE sampleblocks(  blockid              INTEGER PRIMARY KEY AUTOINCREMENT,  sampleformat         INTEGER,  summin               REAL,  summax               REAL,  sumrms               REAL,  summary256           BLOB,  summary64k           BLOB,  samples              BLOB);""")
        ids, next_id = [], 1
        for ch in range(channels):
            mine, signal = [], samples[ch::channels]
            for start in range(0, frames, BLOCK):
                chunk = signal[start:start + BLOCK]
                s256 = [_summary(chunk[k:k + 256], scale) for k in range(0, len(chunk), 256)]
                s256 += [(FLT_MAX, -FLT_MAX, 0.0)] * (max(256, len(s256)) - len(s256))
                s64k = [_summary(chunk[k:k + 65536], scale) for k in range(0, len(chunk), 65536)]
                lo, hi, rms = _summary(chunk, scale)
                body = chunk if sys.byteorder == "little" else array.array(typecode, chunk)
                db.execute("insert into sampleblocks values (?,?,?,?,?,?,?,?)", (next_id, fmt, lo, hi, rms,
                           struct.pack("<%df" % (3 * len(s256)), *[v for f in s256 for v in f]),
                           struct.pack("<%df" % (3 * len(s64k)), *[v for f in s64k for v in f]), body.tobytes()))
                mine.append(next_id)
                next_id += 1
            ids.append(mine)
        db.execute("insert into project values (1,?,?)", (DICT, _doc(name, float(rate), channels, frames, ids, fmt)))
        db.commit()
        db.execute("PRAGMA wal_checkpoint(TRUNCATE)")
        db.close()
        os.replace(staging, target)
    finally:
        try:
            db.close()
        except sqlite3.Error:
            pass
        for leftover in (staging, staging + "-wal", staging + "-shm"):
            if os.path.exists(leftover):
                os.remove(leftover)
    return {"project": target, "frames": frames, "channels": channels, "rate": rate, "format": "int16" if typecode == "h" else "float32"}


# ---------------------------------------------------------------- commands

def export(project, out, like=None):
    """The command Asset Sources runs after a save: render the project to out, replacing nothing else."""
    try:
        channels, typecode, rate = render(project)
    except Unsupported as why:
        if "real-time effects" not in str(why):
            raise
        pipe_export(project, out)
        return {"rendered": out, "by": "audacity"}
    encode(interleave(channels).tobytes(), typecode, rate, len(channels), out, like)
    return {"rendered": out, "by": "python", "channels": len(channels), "rate": rate}


def verify(project, audio):
    raw, typecode, rate, channels = decode(audio)
    out, out_type, out_rate = render(project)
    got = interleave(out)
    if out_type != typecode or out_rate != rate or len(out) != channels:
        raise AssertionError("format differs: project %s/%s/%d, audio %s/%s/%d" % (out_type, out_rate, len(out), typecode, rate, channels))
    if got.tobytes() != raw:
        raise AssertionError("samples differ")
    return {"identical": True, "frames": len(got) // channels, "channels": channels, "rate": rate}


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("inspect").add_argument("project")
    c = sub.add_parser("create")
    c.add_argument("audio"), c.add_argument("project"), c.add_argument("--name")
    r = sub.add_parser("render")
    r.add_argument("project"), r.add_argument("out"), r.add_argument("--like")
    v = sub.add_parser("verify")
    v.add_argument("project"), v.add_argument("audio")
    a = ap.parse_args(argv)
    try:
        data = {"inspect": lambda: inspect(a.project), "create": lambda: create(a.audio, a.project, a.name),
                "render": lambda: export(a.project, a.out, a.like), "verify": lambda: verify(a.project, a.audio)}[a.cmd]()
        print(json.dumps({"ok": True, "data": data}))
        return 0
    except Exception as ex:  # one JSON line is the contract; the message is what the Asset Sources window shows
        print(json.dumps({"ok": False, "error": "%s" % ex}))
        print(ex, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
