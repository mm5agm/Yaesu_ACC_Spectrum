"""Show the FTdx101MP's scope live: a trace and a waterfall for Main and for Sub.

Reads the frames the firmware in firmware/ sends over the board's native USB
(the socket marked USB, 303A:1001), the same port tools/check_frames.py uses.
Never opens COM3 or COM4.

    python tools/live_view.py
    python tools/live_view.py --port COM6
    python tools/live_view.py --file captures/acc-20261111-113822-mode2.bin

What it draws, from what I've found so far (docs/plan.md, step 4):
  bytes 0-849     Main, 850 points across the scope's span
  bytes 850-1699  Sub, the same
Each byte is inverted: 0 is the strongest signal, so the viewer shows 255 minus
the byte. No frequency scale yet: centre and span come from CAT later (step 6).

Keys: Space pauses, A sets the colours again from what's on screen now, Esc quits.
"""

import argparse
import pathlib
import queue
import sys
import threading
import time
import tkinter as tk

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from check_frames import HEADER, find_port, open_port  # noqa: E402

try:
    from PIL import Image, ImageTk
except ImportError:
    sys.exit("Pillow isn't installed. Run: python -m pip install -r tools/requirements.txt")

POINTS = 850
RECEIVERS = (("Main", 0), ("Sub", POINTS))
WIDTH = 1700            # pixels across; each point is two pixels wide
TRACE_H = 160
FALL_H = 220


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


def read_port(device, out, stop):
    splitter = Frames()
    with open_port(device) as s:
        while not stop.is_set():
            for f in splitter.feed(s.read(65536)):
                out.put(f)


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
        self.count, self.rate_start = 0, time.monotonic()
        self.pal = palette()
        self.canvas = {}
        self.falls = {}
        self.photos = {}
        self.items = {}
        root.configure(bg="black")
        for name, _ in RECEIVERS:
            tk.Label(root, text=name, fg="white", bg="black", anchor="w",
                     font=("Segoe UI", 10, "bold")).pack(fill="x", padx=4)
            c = tk.Canvas(root, width=WIDTH, height=TRACE_H + FALL_H, bg="black",
                          highlightthickness=0)
            c.pack(padx=4, pady=(0, 6))
            for x in range(0, WIDTH + 1, WIDTH // 10):      # tenths of the span
                c.create_line(x, 0, x, TRACE_H, fill="#222")
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
        root.bind("<Escape>", lambda e: root.destroy())
        root.after(40, self.tick)

    def make_lut(self, name, values):
        """Stretch the colours from the noise floor to the strongest signal on screen."""
        ordered = sorted(values)
        lo = ordered[len(ordered) // 10]
        hi = max(ordered[-1], lo + 20)
        self.range[name] = (lo, hi)
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
        now = time.monotonic()
        if now - self.rate_start >= 1:
            rate = self.count / (now - self.rate_start)
            self.count, self.rate_start = 0, now
            levels = "   ".join(f"{n} {lo} to {hi}" for n, (lo, hi) in self.range.items())
            self.status.config(text=f"{rate:.1f} frames a second   {levels}"
                               + ("   PAUSED" if self.paused else "")
                               + "   Space pause, A colours, Esc quit")
        self.root.after(40, self.tick)

    def add_row(self, frame):
        inv = bytes(255 - b for b in frame[:2 * POINTS])
        for name, start in RECEIVERS:
            points = inv[start:start + POINTS]
            if name not in self.lut:
                self.make_lut(name, points)
            fall = self.falls[name]
            fall.paste(fall.crop((0, 0, POINTS, FALL_H - 1)), (0, 1))
            fall.paste(Image.frombytes("P", (POINTS, 1), points.translate(self.lut[name])), (0, 0))

    def draw_traces(self, frame):
        for name, start in RECEIVERS:
            lo, hi = self.range.get(name, (0, 255))
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
    args = parser.parse_args()

    frames, stop = queue.Queue(maxsize=200), threading.Event()
    if args.file:
        source, what = threading.Thread(target=read_file, args=(args.file, frames, stop), daemon=True), args.file
    else:
        device = find_port(args.port)
        source, what = threading.Thread(target=read_port, args=(device, frames, stop), daemon=True), device
    source.start()

    root = tk.Tk()
    root.title(f"FTdx101MP ACC scope - {what}")
    Viewer(root, frames)
    root.mainloop()
    stop.set()


if __name__ == "__main__":
    main()
