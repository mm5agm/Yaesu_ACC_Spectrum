"""Save a screenshot, and optionally the samples, from a Siglent SDS1000X-E scope.

Talks to the scope over its LAN port (SCPI on TCP port 5025). It never opens a
serial port.

Pin numbers are always the radio's ACC socket pins, as in the radio's manual,
never the breakout board's terminal numbers (those are GPIO numbers). Say which
ACC pin each channel's probe is on, and what the shot shows, and the script
names the picture from that, the timebase and the time it was taken:

    python tools/scope_shot.py --acc C1=12 --note burst
        -> acc-pin12-cnt-tx_burst_100us_2026-11-10_1432.png
    python tools/scope_shot.py --acc C1=6 C2=5 --note link-frame-start --samples C1 C2
        -> acc-pin6-cs_acc-pin5-clk_link-frame-start_2us_2026-11-10_1435.png

Only channels switched on at the scope go in the name. A channel switched on
without --acc is named by its channel, such as c3. Before saving, it prints
each channel's ACC pin and the breakout terminal it's on, to check against the
probes.

The screenshot goes to docs/images/scope/<name>.png and the samples, if asked
for, to captures/scope/<name>-C1.csv.gz and so on, as time in seconds and volts,
gzipped to keep the repository small.
An existing picture is never overwritten: a -2, -3 and so on is added.
Cursors are switched off first so they don't cover the trace.
"""

import argparse
import gzip
import re
import socket
import sys
from io import BytesIO
from pathlib import Path

from PIL import Image

REPO = Path(__file__).resolve().parent.parent
# ACC socket pin -> (name part, breakout terminal). Terminals are GPIO numbers.
SIGNALS = {
    "3": ("acc-pin3-data1", "14"),
    "4": ("acc-pin4-data2", "11"),
    "5": ("acc-pin5-clk", "10"),
    "6": ("acc-pin6-cs", "13"),
    "11": ("acc-pin11-cnt-rx", "39"),
    "12": ("acc-pin12-cnt-tx", "38"),
}
MAX_SAMPLES = 200_000
PREFIX = {"G": 1e9, "M": 1e6, "k": 1e3, "": 1.0, "m": 1e-3, "u": 1e-6, "n": 1e-9}


class Scope:
    def __init__(self, host, port=5025, timeout=20.0):
        self.sock = socket.create_connection((host, port), timeout=timeout)

    def send(self, command):
        self.sock.sendall((command + "\n").encode("ascii"))

    def read_exact(self, count):
        data = bytearray()
        while len(data) < count:
            chunk = self.sock.recv(min(65536, count - len(data)))
            if not chunk:
                raise ConnectionError("scope closed the connection")
            data += chunk
        return bytes(data)

    def query(self, command):
        self.send(command)
        while True:  # skip blank lines, such as the line feed after a screenshot
            line = bytearray()
            while not line.endswith(b"\n"):
                line += self.read_exact(1)
            text = line.decode("ascii", "replace").strip()
            if text:
                return text

    def number(self, command):
        """Read a value like 'C1:VDIV 1.00E+00V' or 'SARA 1.00GSa/s'."""
        reply = self.query(command)
        match = re.search(r"(-?[\d.]+(?:E[+-]?\d+)?)\s*([GMkmun]?)", reply.split(" ", 1)[-1])
        if not match:
            raise ValueError(f"can't read a number from {reply!r}")
        return float(match.group(1)) * PREFIX[match.group(2)]

    def channel_on(self, channel):
        return self.query(f"{channel}:TRA?").upper().endswith("ON")

    def screenshot(self):
        self.send("SCDP")
        header = self.read_exact(14)
        if header[:2] != b"BM":
            raise ValueError("screenshot isn't a BMP")
        size = int.from_bytes(header[2:6], "little")
        return header + self.read_exact(size - 14)

    def samples(self, channel, step):
        vdiv = self.number(f"{channel}:VDIV?")
        ofst = self.number(f"{channel}:OFST?")
        sara = self.number("SARA?")
        self.send("WFSU SP,0,NP,0,FP,0")  # every point; this scope ignores SP
        self.send(f"{channel}:WF? DAT2")
        head = bytearray()
        while not head.endswith(b"#9"):
            head += self.read_exact(1)
        length = int(self.read_exact(9))
        raw = self.read_exact(length)
        self.read_exact(2)  # the two line feeds after the block
        if not step:
            step = max(1, -(-len(raw) // MAX_SAMPLES))
        raw = raw[::step]
        volts = [(b - 256 if b > 127 else b) / 25.0 * vdiv - ofst for b in raw]
        return [i * step / sara for i in range(len(volts))], volts, step


def timebase_label(seconds):
    """1e-4 -> '100us', 0.002 -> '2ms', 2e-7 -> '200ns', 1.0 -> '1s'."""
    for unit, scale in (("s", 1.0), ("ms", 1e-3), ("us", 1e-6), ("ns", 1e-9)):
        if seconds >= scale * 0.999:
            return f"{seconds / scale:.3g}{unit}"
    return f"{seconds * 1e12:.3g}ps"


def parse_pins(items):
    pins = {}
    for item in items:
        match = re.fullmatch(r"([Cc][1-4])=(\d+)", item)
        if not match:
            sys.exit(f"{item!r}: write it as C1=12, with the ACC socket pin")
        pin = match.group(2)
        if pin not in SIGNALS:
            sys.exit(f"ACC pin {pin} isn't one of the probed ACC pins "
                     f"({', '.join(sorted(SIGNALS, key=int))})")
        pins[match.group(1).upper()] = pin
    return pins


def auto_name(scope, pins, note):
    from datetime import datetime
    parts = []
    for channel in ("C1", "C2", "C3", "C4"):
        if scope.channel_on(channel):
            pin = pins.get(channel)
            if pin:
                part, terminal = SIGNALS[pin]
                print(f"{channel}: ACC pin {pin}, breakout terminal {terminal}")
                parts.append(part)
            else:
                print(f"{channel}: on, no --acc pin given")
                parts.append(channel.lower())
        elif channel in pins:
            sys.exit(f"{channel} has --acc {channel}={pins[channel]} but it's off at the scope")
    if not parts:
        sys.exit("no channel is switched on at the scope")
    if note:
        parts.append(note)
    parts.append(timebase_label(scope.number("TDIV?")))
    parts.append(datetime.now().strftime("%Y-%m-%d_%H%M"))
    return "_".join(parts)


def unused(name):
    folder = REPO / "docs" / "images" / "scope"
    candidate, n = name, 1
    while (folder / f"{candidate}.png").exists():
        n += 1
        candidate = f"{name}-{n}"
    return candidate


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--acc", nargs="*", default=[], metavar="CH=PIN",
                        help="the ACC socket pin (not the terminal) on each channel, "
                             "e.g. C1=6 C2=5")
    parser.add_argument("--note", help="what the shot shows, e.g. burst or link-frame-start")
    parser.add_argument("--host", default="192.168.68.68",
                        help="the scope's address, from Utility > I/O > LAN on the scope")
    parser.add_argument("--samples", nargs="*", default=[], metavar="CH",
                        help="channels to save as CSV, e.g. C1 C2")
    parser.add_argument("--step", type=int, default=0,
                        help="keep every Nth sample; 1 keeps all of them. By default it "
                             f"keeps enough to make at most {MAX_SAMPLES:,}")
    args = parser.parse_args()

    pins = parse_pins(args.acc)
    if args.note is not None and not re.fullmatch(r"[a-z0-9-]+", args.note):
        sys.exit("use lower case letters, digits and hyphens in --note")

    scope = Scope(args.host)
    scope.send("CRMS OFF")
    args.name = unused(auto_name(scope, pins, args.note))

    png = REPO / "docs" / "images" / "scope" / f"{args.name}.png"
    png.parent.mkdir(parents=True, exist_ok=True)
    Image.open(BytesIO(scope.screenshot())).save(png)
    print(f"saved {png.relative_to(REPO)}")

    for channel in args.samples:
        channel = channel.upper()
        if not re.fullmatch(r"C[1-4]", channel):
            sys.exit(f"{channel} isn't a channel (C1 to C4)")
        times, volts, step = scope.samples(channel, args.step)
        csv = REPO / "captures" / "scope" / f"{args.name}-{channel}.csv.gz"
        csv.parent.mkdir(parents=True, exist_ok=True)
        with gzip.open(csv, "wt", encoding="ascii") as f:
            f.write("seconds,volts\n")
            for t, v in zip(times, volts):
                f.write(f"{t:.9g},{v:.4f}\n")
        print(f"saved {csv.relative_to(REPO)}, {len(volts):,} samples (every {step}), "
              f"{min(volts):.2f} to {max(volts):.2f} V")


if __name__ == "__main__":
    main()
