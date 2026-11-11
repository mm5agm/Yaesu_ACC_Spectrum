"""Show the FTdx101MP's scope live: a trace and a waterfall for Main and for Sub.

Reads the frames the firmware in firmware/ sends over the board's native USB
(the socket marked USB, 303A:1001), the same port tools/check_frames.py uses.
Never opens COM3 or COM4.

    python tools/live_view.py
    python tools/live_view.py --port COM6
    python tools/live_view.py --file captures/acc-20261111-113822-mode2.bin
    python tools/live_view.py --dump captures/latest.bin   # for decoding, see below
    python tools/live_view.py --3dss                       # start in 3DSS

What it draws, from what I've found so far (docs/plan.md, step 4):
  bytes 0-849     Main, 850 points across the scope's span
  bytes 850-1699  Sub, the same
Each byte is inverted: 0 is the strongest signal, so the viewer shows 255 minus
the byte. The frequency scale comes from the frame too: each receiver's VFO and
span, and for Cursor and Fix the left edge (step 5). The red dashed line is the VFO.

--dump writes the last second's frames to a file every second, back to back, so
captures can be compared while the viewer stays open (step 5 of docs/plan.md).

Keys: Space pauses, A sets the colours again from what's on screen now, D switches
between waterfall and 3DSS, Esc quits.

3DSS draws the way the radio's 3DSS mode does: the last 40 traces stacked going
back into the screen in perspective, newest at the front.
"""

import argparse
import collections
import os
import pathlib
import queue
import sys
import threading
import time
import tkinter as tk

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from check_frames import HEADER, find_port, open_port  # noqa: E402

try:
    from PIL import Image, ImageDraw, ImageTk
except ImportError:
    sys.exit("Pillow isn't installed. Run: python -m pip install -r tools/requirements.txt")

POINTS = 850
RECEIVERS = (("Main", 0), ("Sub", POINTS))
# Where each receiver's tuning sits in the frame (docs/plan.md, step 5). The VFO
# and the left edge are in Hz, 4 bytes big-endian. The span byte holds the span
# in its low 4 bits, counting up the radio's span list, and the placement in its
# top 2: 1 Center, 0 Cursor, 2 Fix. In Center the scope is centred on the VFO
# and the left-edge field isn't kept up to date; in Cursor and Fix it is.
VFO_AT = {"Main": 3068, "Sub": 3094}
LEFT_AT = {"Main": 3076, "Sub": 3098}
SPAN_AT = {"Main": 3062, "Sub": 3084}
PLACEMENTS = {0: "Cursor", 1: "Center", 2: "Fix"}
SPANS = (1_000, 2_000, 5_000, 10_000, 20_000, 50_000, 100_000, 200_000, 500_000, 1_000_000)
# The 850 points cover a little more than the span: the span itself is the middle
# 791, with about 30 points beyond each edge. Measured at every span from 1k to
# 1M by tuning a known distance and seeing how far the trace moved: 788 to 795,
# 791 on average (docs/plan.md, step 5).
SPAN_POINTS = 791


def tuning(frame, name):
    """(centre Hz, span Hz, VFO Hz, placement) for a receiver, or None if the
    bytes don't make sense."""
    if len(frame) < 3102:
        return None
    word = lambda at: int.from_bytes(frame[at:at + 4], "big")
    vfo, left = word(VFO_AT[name]), word(LEFT_AT[name])
    index, placement = frame[SPAN_AT[name]] & 0x0F, PLACEMENTS.get(frame[SPAN_AT[name]] >> 6)
    if not 30_000 <= vfo <= 75_000_000 or index >= len(SPANS) or placement is None:
        return None
    span = SPANS[index]
    centre = vfo if placement == "Center" else left + span // 2
    return centre, span, vfo, placement


def khz(hz):
    return f"{hz / 1000:,.3f}".rstrip("0").rstrip(".")
WIDTH = 1700            # pixels across; each point is two pixels wide
TRACE_H = 160
FALL_H = 220
DEPTH = 40              # traces in the 3DSS picture


def palette():
    """Black, blue, cyan, yellow, red, white: weak to strong."""
    stops = [(0, (0, 0, 0)), (60, (0, 0, 140)), (120, (0, 170, 220)),
             (170, (240, 230, 0)), (215, (230, 30, 0)), (255, (255, 255, 255))]
    out = []
    for v in range(256):
        for (a, ca), (b, cb) in zip(stops, stops[1:]):
            if a <= v <= b:
                t = (v - a) / (b - a)
                out += [round(ca[i] + (cb[i] - ca[i]) * t) for i in range(3)]
                break
    return out


class Frames:
    """Splits the byte stream into frames. Keeps whatever's left for next time."""

    def __init__(self):
        self.buf = bytearray()

    def feed(self, data):
        self.buf += data
        frames = []
        while True:
            j = self.buf.find(b"YA")
            if j < 0:
                del self.buf[:-1]
                break
            if len(self.buf) - j < HEADER.size:
                del self.buf[:j]
                break
            _, kind, _, length, _ = HEADER.unpack_from(self.buf, j)
            if chr(kind) not in "FT":
                del self.buf[:j + 1]
                continue
            end = j + HEADER.size + length
            if len(self.buf) < end:
                del self.buf[:j]
                break
            if chr(kind) == "F" and length >= 2 * POINTS:
                frames.append(bytes(self.buf[j + HEADER.size:end]))
            del self.buf[:end]
        return frames


def read_port(device, out, stop, dump=None):
    splitter = Frames()
    recent = collections.deque(maxlen=25)
    last_dump = time.monotonic()
    with open_port(device) as s:
        while not stop.is_set():
            for f in splitter.feed(s.read(65536)):
                out.put(f)
                recent.append(f)
            if dump and time.monotonic() - last_dump >= 1:
                # The last second of frames, raw and back to back, for decoding
                # while the viewer stays open. Replaced whole, so a reader never
                # sees half a file.
                last_dump = time.monotonic()
                tmp = dump.with_suffix(".tmp")
                tmp.write_bytes(b"".join(recent))
                try:
                    os.replace(tmp, dump)
                except PermissionError:
                    pass        # someone is reading it this instant; next second


def read_file(path, out, stop):
    splitter = Frames()
    frames = splitter.feed(pathlib.Path(path).read_bytes())
    while not stop.is_set():
        for f in frames:
            if stop.is_set():
                return
            out.put(f)
            time.sleep(0.04)


class Viewer:
    def __init__(self, root, frames):
        self.root, self.frames = root, frames
        self.paused = False
        self.lut = {}           # per receiver: Main and Sub sit at different levels
        self.range = {}
        self.trace_lo = {}      # the trace keeps the noise in view, so it starts lower
        self.count, self.rate_start = 0, time.monotonic()
        self.pal = palette()
        self.canvas = {}
        self.falls = {}
        self.photos = {}
        self.items = {}
        self.titles = {}
        self.dss = False
        self.history = {n: collections.deque(maxlen=DEPTH) for n, _ in RECEIVERS}
        self.dss_colours = [self.depth_colour(k) for k in range(DEPTH)]
        self.scale_items = {}
        self.shown = {}         # the (centre, span) each scale is drawn for
        root.configure(bg="black")
        for name, _ in RECEIVERS:
            self.titles[name] = tk.Label(root, text=name, fg="white", bg="black", anchor="w",
                                         font=("Segoe UI", 10, "bold"))
            self.titles[name].pack(fill="x", padx=4)
            self.scale_items[name] = []
            c = tk.Canvas(root, width=WIDTH, height=TRACE_H + FALL_H, bg="black",
                          highlightthickness=0)
            c.pack(padx=4, pady=(0, 6))
            for k in range(-5, 6):                  # the radio's ten divisions
                x = self.x_at(k * SPAN_POINTS / 10)
                c.create_line(x, 0, x, TRACE_H, fill="#333" if k in (-5, 5) else "#222")
            for y in range(0, TRACE_H, TRACE_H // 4):
                c.create_line(0, y, WIDTH, y, fill="#222")
            fall = Image.new("P", (POINTS, FALL_H))
            fall.putpalette(self.pal)
            photo = ImageTk.PhotoImage(fall.resize((WIDTH, FALL_H)))
            self.items[name] = (c.create_line(0, TRACE_H, 1, TRACE_H, fill="#ffd400"),
                                c.create_image(0, TRACE_H, anchor="nw", image=photo))
            self.canvas[name], self.falls[name], self.photos[name] = c, fall, photo
        self.status = tk.Label(root, fg="#aaa", bg="black", anchor="w")
        self.status.pack(fill="x", padx=4)
        root.bind("<space>", lambda e: setattr(self, "paused", not self.paused))
        root.bind("<a>", lambda e: self.lut.clear())
        root.bind("<d>", lambda e: self.toggle_3dss())
        root.bind("<Escape>", lambda e: root.destroy())
        root.after(40, self.tick)

    @staticmethod
    def x_at(points_from_centre):
        return round((POINTS / 2 + points_from_centre) * WIDTH / POINTS)

    def make_lut(self, name, values):
        """Stretch the colours from the noise floor to the strongest signal on screen.

        Black is the median, about the noise floor, so the noise stays dark and
        weak signals such as FT8 show up against it."""
        ordered = sorted(values)
        lo = ordered[len(ordered) // 2]
        hi = max(ordered[-1], lo + 20)
        self.range[name] = (lo, hi)
        self.trace_lo[name] = ordered[len(ordered) // 20]
        self.lut[name] = bytes(0 if v <= lo else 255 if v >= hi else round((v - lo) * 255 / (hi - lo))
                               for v in range(256))

    def tick(self):
        latest = None
        while True:
            try:
                latest = self.frames.get_nowait()
            except queue.Empty:
                break
            self.count += 1
            if not self.paused:
                self.add_row(latest)
        if latest is not None and not self.paused:
            self.draw_traces(latest)
            self.draw_scales(latest)
        now = time.monotonic()
        if now - self.rate_start >= 1:
            rate = self.count / (now - self.rate_start)
            self.count, self.rate_start = 0, now
            levels = "   ".join(f"{n} {lo} to {hi}" for n, (lo, hi) in self.range.items())
            self.status.config(text=f"{rate:.1f} frames a second   {levels}"
                               + ("   PAUSED" if self.paused else "")
                               + "   Space pause, A colours, D 3DSS, Esc quit")
        self.root.after(40, self.tick)

    def draw_scales(self, frame):
        """Frequencies along the top of each trace, at every second grid line."""
        for name, _ in RECEIVERS:
            now = tuning(frame, name)
            if now == self.shown.get(name):
                continue
            self.shown[name] = now
            c = self.canvas[name]
            for item in self.scale_items[name]:
                c.delete(item)
            self.scale_items[name] = []
            if now is None:
                self.titles[name].config(text=f"{name}   (no frequency in the frame)")
                continue
            centre, span, vfo, placement = now
            self.titles[name].config(text=f"{name}   VFO {khz(vfo)} kHz   span {khz(span)} kHz"
                                          f"   {placement}")
            for k in range(-4, 5, 2):
                self.scale_items[name].append(
                    c.create_text(self.x_at(k * SPAN_POINTS / 10), 2, anchor="n",
                                  text=khz(round(centre + k * span / 10)), fill="#8aa",
                                  font=("Segoe UI", 8)))
            for k, anchor in ((-5, "nw"), (5, "ne")):   # the span's edges
                self.scale_items[name].append(
                    c.create_text(self.x_at(k * SPAN_POINTS / 10) + (3 if k < 0 else -3), 14,
                                  anchor=anchor, text=khz(round(centre + k * span / 10)),
                                  fill="#566", font=("Segoe UI", 8)))
            x = self.x_at((vfo - centre) * SPAN_POINTS / span)     # the VFO
            if 0 <= x <= WIDTH:
                self.scale_items[name].append(
                    c.create_line(x, 0, x, TRACE_H, fill="#a33", dash=(3, 3)))

    def add_row(self, frame):
        inv = bytes(255 - b for b in frame[:2 * POINTS])
        for name, start in RECEIVERS:
            points = inv[start:start + POINTS]
            if name not in self.lut:
                self.make_lut(name, points)
            fall = self.falls[name]
            fall.paste(fall.crop((0, 0, POINTS, FALL_H - 1)), (0, 1))
            fall.paste(Image.frombytes("P", (POINTS, 1), points.translate(self.lut[name])), (0, 0))

    @staticmethod
    def depth_colour(k):
        """Front (k=0) bright yellow, fading through green to dark blue at the back."""
        t = k / (DEPTH - 1)
        stops = [(0, (255, 230, 60)), (0.35, (60, 220, 120)), (0.7, (30, 110, 200)), (1, (20, 30, 90))]
        for (a, ca), (b, cb) in zip(stops, stops[1:]):
            if a <= t <= b:
                u = (t - a) / (b - a)
                return tuple(round(ca[i] + (cb[i] - ca[i]) * u) for i in range(3))

    def toggle_3dss(self):
        self.dss = not self.dss
        for name, _ in RECEIVERS:
            line, image = self.items[name]
            c = self.canvas[name]
            c.coords(image, 0, 0 if self.dss else TRACE_H)
            c.itemconfigure(line, state="hidden" if self.dss else "normal")
            c.tag_raise(image)
            for item in self.scale_items[name]:
                c.tag_raise(item)

    def draw_3dss(self, name):
        """The last DEPTH traces in perspective. The front one is full width at
        the bottom; each one further back sits higher, narrower and lower."""
        H = TRACE_H + FALL_H
        img = Image.new("RGB", (WIDTH, H))
        d = ImageDraw.Draw(img)
        lo, hi = self.trace_lo.get(name, 0), self.range.get(name, (0, 255))[1]
        span = max(hi - lo, 1)
        rows = list(self.history[name])
        step = 2                                     # every second point is plenty
        for k in range(len(rows) - 1, -1, -1):       # back to front, so front hides back
            depth = k / DEPTH
            scale = 1 - 0.45 * depth                 # width and height shrink going back
            base = H - 6 - depth * (H * 0.55)        # and the baseline rises
            x0 = WIDTH * (1 - scale) / 2 + depth * WIDTH * 0.08   # leans right, like the radio
            amp = (H * 0.42) * scale
            row = rows[k]
            pts = [(x0 + i * WIDTH * scale / POINTS,
                    base - min(max((row[i] - lo) / span, 0), 1) * amp)
                   for i in range(0, POINTS, step)]
            d.polygon([(pts[0][0], base)] + pts + [(pts[-1][0], base)], fill=(0, 0, 0))
            d.line(pts, fill=self.dss_colours[k], width=2 if k == 0 else 1)
        return img

    def draw_traces(self, frame):
        for name, start in RECEIVERS:
            self.history[name].appendleft([255 - b for b in frame[start:start + POINTS]])
            if self.dss:
                line, image = self.items[name]
                photo = ImageTk.PhotoImage(self.draw_3dss(name))
                self.canvas[name].itemconfig(image, image=photo)
                self.photos[name] = photo
                continue
            _, hi = self.range.get(name, (0, 255))
            lo = self.trace_lo.get(name, 0)
            span = max(hi - lo, 1)
            pts = []
            for i in range(POINTS):
                v = 255 - frame[start + i]
                y = TRACE_H - 1 - (v - lo) * (TRACE_H - 2) / span
                pts += [i * WIDTH / POINTS, min(max(y, 0), TRACE_H - 1)]
            line, image = self.items[name]
            self.canvas[name].coords(line, *pts)
            photo = ImageTk.PhotoImage(self.falls[name].resize((WIDTH, FALL_H), Image.NEAREST))
            self.canvas[name].itemconfig(image, image=photo)
            self.photos[name] = photo


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--port", help="the board's USB port, e.g. COM6")
    parser.add_argument("--file", help="play a saved capture instead of the board")
    parser.add_argument("--dump", type=pathlib.Path,
                        help="every second, write the last second's frames (4096 bytes each) here")
    parser.add_argument("--3dss", dest="dss", action="store_true", help="start in 3DSS (D switches)")
    args = parser.parse_args()

    frames, stop = queue.Queue(maxsize=200), threading.Event()
    if args.file:
        source, what = threading.Thread(target=read_file, args=(args.file, frames, stop), daemon=True), args.file
    else:
        device = find_port(args.port)
        source, what = threading.Thread(target=read_port, args=(device, frames, stop, args.dump), daemon=True), device
    source.start()

    root = tk.Tk()
    root.title(f"FTdx101MP ACC scope - {what}")
    viewer = Viewer(root, frames)
    if args.dss:
        viewer.toggle_3dss()
    root.mainloop()
    stop.set()


if __name__ == "__main__":
    main()
