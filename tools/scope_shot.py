"""Save a screenshot, and optionally the samples, from a Siglent SDS1000X-E scope.

Talks to the scope over its LAN port (SCPI on TCP port 5025). It never opens a
serial port.

    python tools/scope_shot.py pin12-burst
    python tools/scope_shot.py link-cs-clk --samples C1 C2

The screenshot goes to docs/images/scope/<name>.png and the samples, if asked
for, to captures/scope/<name>-C1.csv and so on, as time in seconds and volts.
Cursors are switched off first so they don't cover the trace.
"""

import argparse
import re
import socket
import sys
from io import BytesIO
from pathlib import Path

from PIL import Image

REPO = Path(__file__).resolve().parent.parent
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
        line = bytearray()
        while not line.endswith(b"\n"):
            line += self.read_exact(1)
        return line.decode("ascii", "replace").strip()

    def number(self, command):
        """Read a value like 'C1:VDIV 1.00E+00V' or 'SARA 1.00GSa/s'."""
        reply = self.query(command)
        match = re.search(r"(-?[\d.]+(?:E[+-]?\d+)?)\s*([GMkmun]?)", reply.split(" ", 1)[-1])
        if not match:
            raise ValueError(f"can't read a number from {reply!r}")
        return float(match.group(1)) * PREFIX[match.group(2)]

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
        self.send(f"WFSU SP,{step},NP,0,FP,0")
        self.send(f"{channel}:WF? DAT2")
        head = bytearray()
        while not head.endswith(b"#9"):
            head += self.read_exact(1)
        length = int(self.read_exact(9))
        raw = self.read_exact(length)
        self.read_exact(2)  # the two line feeds after the block
        codes = [b - 256 if b > 127 else b for b in raw]
        volts = [code / 25.0 * vdiv - ofst for code in codes]
        return [i * step / sara for i in range(len(volts))], volts


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("name", help="file name without extension, e.g. pin12-burst")
    parser.add_argument("--host", default="192.168.68.71")
    parser.add_argument("--samples", nargs="*", default=[], metavar="CH",
                        help="channels to save as CSV, e.g. C1 C2")
    parser.add_argument("--step", type=int, default=1,
                        help="keep every Nth sample (default every one)")
    args = parser.parse_args()

    if not re.fullmatch(r"[a-z0-9-]+", args.name):
        sys.exit("use lower case letters, digits and hyphens in the name")

    scope = Scope(args.host)
    scope.send("CRMS OFF")

    png = REPO / "docs" / "images" / "scope" / f"{args.name}.png"
    png.parent.mkdir(parents=True, exist_ok=True)
    Image.open(BytesIO(scope.screenshot())).save(png)
    print(f"saved {png.relative_to(REPO)}")

    for channel in args.samples:
        channel = channel.upper()
        if not re.fullmatch(r"C[1-4]", channel):
            sys.exit(f"{channel} isn't a channel (C1 to C4)")
        times, volts = scope.samples(channel, args.step)
        csv = REPO / "captures" / "scope" / f"{args.name}-{channel}.csv"
        csv.parent.mkdir(parents=True, exist_ok=True)
        with csv.open("w", encoding="ascii") as f:
            f.write("seconds,volts\n")
            for t, v in zip(times, volts):
                f.write(f"{t:.9g},{v:.4f}\n")
        print(f"saved {csv.relative_to(REPO)}, {len(volts)} samples, "
              f"{min(volts):.2f} to {max(volts):.2f} V")


if __name__ == "__main__":
    main()
